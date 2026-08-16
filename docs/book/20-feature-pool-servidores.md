[← Sumário](00-indice.md)

# Capítulo 20 — Pool de Servidores CS2 (a lógica, não a infraestrutura)

Este capítulo explica **por que** o sistema de pool existe e **como as peças se encaixam do
ponto de vista de produto**. A implementação mecânica de cada classe (`PoolManagerService`,
`MatchServerService`, `RconClient`) já foi coberta no
[Capítulo 11](11-backend-services-workers.md#113-matchserverservice--a-peça-central-da-lógica-de-servidor);
configuração específica de AWS (Security Groups, AMI, chaves) fica fora deste livro — ver
`docs/plano-aws.md`.

## 20.1 O problema que o pool resolve

Quando um veto termina (Capítulo 19), o jogador quer entrar no servidor **agora**. Criar uma
instância EC2 do zero ("cold boot") — mesmo com a AMI já pronta com CS2/Metamod instalados —
ainda leva de 60 a 120+ segundos: o boot da máquina virtual, a inicialização do sistema, e o
próprio CS2 carregando. Para um veto de partida competitiva, esperar dois minutos parado numa
tela de "preparando servidor" é uma experiência ruim.

A solução (o mesmo padrão usado por FACEIT/ESEA): manter um pequeno número de servidores **já
ligados e com CS2 já rodando**, parados num mapa neutro sem ninguém jogando, esperando serem
"chamados". Quando um veto termina, em vez de criar uma máquina nova, a API manda dois comandos
via **RCON** (protocolo de administração remota do próprio motor Source) para um desses
servidores ociosos: trocar o mapa e definir a senha da partida. Isso leva segundos, não minutos.

## 20.2 O ciclo de vida de um `PoolServer`

```csharp
public enum PoolServerState { Booting = 0, Idle = 1, InUse = 2, Unhealthy = 3 }
```

```
Booting ──(IP público aparece + RCON responde)──▶ Idle
   │                                                  │
   │ (falha ao autenticar/criar)                      │ (veto termina, TryAssignFromPoolAsync)
   ▼                                                  ▼
Unhealthy                                           InUse
                                                       │
                                    (vazio por >3min OU >3h sem confirmar) 
                                                       ▼
                                                 volta a Idle
```

- **`Booting`**: acabou de pedir a criação da instância EC2; ainda não tem IP público, ou tem IP
  mas o CS2 ainda não respondeu por RCON.
- **`Idle`**: confirmado utilizável — tem IP, e um comando RCON (`status`) foi respondido com
  sucesso. Só neste estado um servidor é elegível para receber uma partida.
- **`InUse`**: atribuído a uma partida específica (`CurrentMatchId`, `AssignedAt` preenchidos).
- **`Unhealthy`**: falhou em algum ponto (RCON não autenticou, instância terminou inesperadamente).
  Não participa mais do ciclo normal — conta como "faltando" para o `TopUpAsync` repor o pool.

Todo esse ciclo é orquestrado pelo `PoolManagerService` a cada 30 segundos (mecânica completa em
[§11.4](11-backend-services-workers.md#114-poolmanagerservice--três-sub-rotinas-por-tick)).

## 20.3 Por que "instância rodando" não é o mesmo que "pronta"

Este é o ponto mais importante de todo este capítulo, e vale repetir com ênfase de produto (a
mecânica de código já está em [§11.4.1](11-backend-services-workers.md#1141-por-que-instância-rodando-na-aws--pronto-para-uso)):
a AWS reportar que uma instância está `Running` só significa que o sistema operacional
terminou de inicializar — não diz nada sobre se o processo do CS2 dentro dela terminou de
carregar, autenticou na Steam, e está de fato aceitando conexões e comandos. Um servidor marcado
`Idle` prematuramente (só por estar "Running" na AWS) poderia receber uma partida atribuída e
falhar silenciosamente, deixando os dois times sem conseguir entrar. Por isso o único critério
aceito para marcar `Idle` é uma resposta RCON real e bem-sucedida — é a diferença entre "a
máquina ligou" e "o jogo está pronto para ser jogado".

## 20.4 O fallback: cold-boot como rede de segurança, não como caminho normal

```csharp
var assignedFromPool = await server.TryAssignFromPoolAsync(newRoom.Id, newRoom.Map, newRoom.ServerPassword);
if (!assignedFromPool)
    _ = server.ProvisionAsync(newRoom.Id);
```

Se **nenhum** `PoolServer` estiver `Idle` no momento exato em que um veto termina (por exemplo,
`SUMMIT_POOL_SIZE=1` e esse único servidor já está `InUse` numa outra partida simultânea), o
sistema não falha — ele automaticamente recorre ao caminho antigo de criar uma EC2 nova do zero.
O jogador nesse caso vê o aviso honesto "PREPARANDO SERVIDOR NA AWS... ~90S" em vez do
"SERVIDOR PRONTO" quase instantâneo — **nenhuma mudança de UI foi necessária** para suportar esse
caso de transbordo, porque a tela já lidava com o estado "aguardando provisionamento" desde antes
de o pool existir (ver [§19.5](19-feature-veto.md#195-a-tela-matchroomviewmodel)).

Isso significa que `SUMMIT_POOL_SIZE` é, na prática, uma escolha de trade-off entre custo
(servidores ociosos ligados 24/7 custam dinheiro continuamente, mesmo sem ninguém jogando neles)
e capacidade de absorver picos sem cair para o caminho lento — quanto maior o valor, menos vezes
o fallback de cold-boot é acionado, ao custo de mais instâncias sempre ligadas.

## 20.5 Liberação automática — como o sistema sabe que uma partida "acabou"

Aqui está um ponto sutil e importante: **o sistema não tem um evento de "partida terminou"** (ver
[Capítulo 21](21-feature-pos-partida-gaps.md) — esse é o gap central do produto). Então como o
`PoolManagerService` sabe quando pode liberar um servidor `InUse` de volta ao pool?

A resposta é: **não sabe, de verdade** — ele **infere** por ausência de jogadores, não por um
sinal explícito de fim de partida:

```csharp
var humans = await server.GetHumanPlayerCountAsync(p);   // RCON "status", regex sobre a contagem
if (humans == 0) await server.ReleaseToPoolAsync(db, p);
```

Isso é uma aproximação razoável (se ninguém está conectado, é seguro assumir que a partida
acabou ou nunca começou de verdade), mas é imperfeita por construção: um servidor entre o fim do
veto e a entrada efetiva dos 10 jogadores também mostraria zero humanos — por isso existe a
`AssignGrace` de 3 minutos antes de sequer começar a checar (dando tempo real para todos
entrarem). E o teto de segurança de 3 horas (`HardReleaseCeiling`) existe para o caso em que o
RCON simplesmente pare de responder (falha de rede, processo travado) e a contagem de humanos
nunca mais possa ser confirmada — sem esse teto, um servidor nessas condições ficaria "preso"
para sempre, nunca voltando ao pool mesmo que a partida real já tivesse terminado há muito tempo.

## 20.6 Onde este capítulo termina e o próximo começa

Note que nada neste capítulo depende de saber *quem venceu* a partida — o pool só cuida do ciclo
de vida da *infraestrutura* (servidor ligado/atribuído/liberado). O que acontece com o
**resultado** da partida (placar, estatísticas, quem avança na chave) é uma responsabilidade
completamente separada — e é exatamente a responsabilidade que **não existe ainda** no sistema,
coberta em detalhe no [Capítulo 21](21-feature-pos-partida-gaps.md).
