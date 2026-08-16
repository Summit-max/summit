[← Sumário](00-indice.md)

# Capítulo 11 — Services e Background Workers

Este capítulo explica **como** os processos de fundo da API são construídos (a mecânica de
código). O **porquê de produto** de cada um (que problema resolve) está nos capítulos de feature
correspondentes: [Capítulo 18](18-feature-bracket.md) para o `LifecycleWorker` e
[Capítulo 20](20-feature-pool-servidores.md) para `MatchServerService`/`PoolManagerService`/
`RconClient`/`ServerProvisionPoller`.

## 11.1 O molde comum de um `BackgroundService`

Os três workers (`LifecycleWorker`, `ServerProvisionPoller`, `PoolManagerService`) herdam
`Microsoft.Extensions.Hosting.BackgroundService` e seguem exatamente o mesmo esqueleto:

```csharp
public class NomeDoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NomeDoWorker> _log;

    public NomeDoWorker(IServiceScopeFactory scopes, ILogger<NomeDoWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
                await TickAsync(db);
            }
            catch (Exception ex) { _log.LogError(ex, "Erro no NomeDoWorker"); }
            await Task.Delay(TimeSpan.FromSeconds(N), ct);
        }
    }
}
```

Três decisões de design se repetem nos três e valem a pena destacar:

1. **`IServiceScopeFactory`, não `ApiDbContext` direto** — porque `BackgroundService` é
   registrado como singleton de longa duração (roda a vida inteira do processo), enquanto
   `ApiDbContext` é *scoped* (uma vida curta, tipicamente por requisição). Injetar o
   `ApiDbContext` direto no construtor do worker o prenderia à mesma instância para sempre — o
   que é errado para um `DbContext` (ele não foi desenhado para ser reusado indefinidamente
   através de muitos ciclos, é mais seguro pegar um novo por operação). Por isso todo tick cria
   seu próprio escopo (`_scopes.CreateScope()`) e pega um `ApiDbContext` novo, e o descarta
   (`using`) ao fim daquele tick.
2. **`try/catch` genérico ao redor de tudo** — nenhuma exceção deve nunca escapar do
   `ExecuteAsync` de um `BackgroundService`, porque se escapar, o host do ASP.NET Core considera
   o serviço "parado" e ele nunca mais roda de novo (nem tenta) pelo resto da vida do processo —
   um único erro transitório (ex. timeout de rede na AWS) mataria o worker inteiro para sempre.
   Capturar e logar, deixando o `while` continuar para o próximo tick, é o que torna esses workers
   resilientes a falhas pontuais.
3. **`Task.Delay(..., ct)`** — o `CancellationToken` é passado para o `Delay`, não só checado no
   `while`; isso garante que, quando o host pede para desligar (`ct` é cancelado), o worker acorda
   imediatamente do delay em vez de esperar o intervalo inteiro antes de perceber que devia parar.

## 11.2 `LifecycleWorker` — estrutura de código

Tick de 20 segundos. `ExecuteAsync` delega para `TickAsync(db)` (método `static`, o que facilita
testá-lo isoladamente se um dia houver testes automatizados — recebe tudo que precisa como
parâmetro, sem depender de campos de instância). A cada tick, ele:

1. Busca todos os campeonatos `Open` ou `Upcoming` (com toda a árvore de times/chave já incluída,
   para evitar N+1 dentro do loop).
2. Para cada um, checa duas condições de tempo independentes (`now >= t.CheckInClosesAt` e
   `now >= t.StartDate`) — **ambas podem disparar no mesmo tick** se o processo ficou parado por
   tempo suficiente para pular os dois marcos de uma vez (o que de fato aconteceu várias vezes
   durante o desenvolvimento deste projeto, dado os intervalos longos entre sessões).
3. Ao fim, chama `AutoVetoBotsAsync` — uma rotina separada que faz contas demo/bot jogarem o veto
   sozinhas (identificadas por `team.CaptainId.Length <= 12`, os ids curtos como `usr_ghost` do
   `SeedData`, versus os ids longos gerados por `Guid.NewGuid():N` para contas reais).

A lógica de negócio de cada uma dessas etapas (o que significa "fechar check-in", "gerar a
chave", "iniciar o campeonato") está detalhada no [Capítulo 16](16-feature-campeonatos-inscricao.md)
e [Capítulo 18](18-feature-bracket.md).

### 11.2.1 Por que `AutoVetoBotsAsync` chama a própria API via HTTP em vez de manipular o banco direto

```csharp
private static readonly HttpClient Http = new() { BaseAddress = new Uri("http://localhost:5180") };
// ...
await Http.PostAsJsonAsync($"/api/veto/{s.BracketMatchId}/action", new { teamTag = tag, map });
```

Isso parece redundante (o worker já tem acesso direto ao `ApiDbContext` — por que passar por
HTTP para si mesmo?), mas é uma escolha deliberada: a lógica de "qual é a próxima ação válida e o
que acontece ao executá-la" já existe inteira e testada dentro do endpoint
`POST /api/veto/{id}/action` (validação de turno, avanço de `StepIndex`, criação da sala ao
terminar). Reimplementar essa mesma lógica diretamente contra o banco dentro do worker duplicaria
uma regra inteira em dois lugares, com risco real de os dois divergirem no futuro. Chamar o
próprio endpoint HTTP é mais lento, mas garante que o bot segue **exatamente** o mesmo caminho de
código que um jogador humano clicando no cliente — "dogfooding" da própria API.

## 11.3 `MatchServerService` — a peça central da lógica de servidor

Registrado como `Singleton`. Não guarda estado próprio; todo estado (qual instância, qual
`PoolServer`, qual `Match`) vive no banco, lido/escrito a cada chamada através de um escopo criado
na hora (`_scopes.CreateScope()`).

Os métodos se dividem em três grupos:

- **Provisionamento** (`ProvisionAsync`, `ProvisionPoolServerAsync`, `LaunchInstanceAsync`,
  `LaunchBareInstanceAsync`) — chamam a AWS SDK (`RunInstancesAsync`) para criar instâncias EC2.
  Fora do escopo deste livro em termos de *configuração* (ver `docs/plano-aws.md`), mas a
  *lógica* de quando cada um é chamado é produto e está no
  [Capítulo 20](20-feature-pool-servidores.md).
- **Atribuição via RCON** (`TryAssignFromPoolAsync`, `ReleaseToPoolAsync`,
  `CheckPoolServerAliveAsync`, `GetHumanPlayerCountAsync`) — usam `RconClient` (ver 11.5) para
  trocar mapa/senha em um servidor já ligado, sem criar máquina nova.
- **Polling de estado AWS** (`PollAsync`, `PollPoolServerAsync`, `TerminateAsync`, `StopAsync`) —
  consultam `DescribeInstancesAsync` para saber se uma instância já tem IP público/está rodando.

### 11.3.1 `ToConsoleMapName` — um bug real, virado helper permanente

```csharp
private static string ToConsoleMapName(string map)
{
    if (string.IsNullOrWhiteSpace(map)) return "de_mirage";
    var trimmed = map.Trim().ToLowerInvariant();
    return trimmed.StartsWith("de_") ? trimmed : $"de_{trimmed}";
}
```

Vale a pena estudar este helper como estudo de caso de "por que um detalhe pequeno vira uma
função nomeada": o pool de mapas do veto guarda nomes de exibição (`"Nuke"`, `"Ancient"`), mas o
comando de console do CS2 (`changelevel`/`+map`) exige o nome real do arquivo do mapa
(`de_nuke`, `de_ancient`). Enviar `changelevel Nuke` sem o prefixo não gera erro nenhum visível —
o console do CS2 simplesmente ecoa `int(0=0x0)` e continua no mapa atual, silenciosamente. Esse
bug existiu nos dois caminhos (cold-boot e pool) até ser descoberto ao vivo; a correção virou este
helper único, chamado nos dois lugares (`BuildUserData` e `TryAssignFromPoolAsync`), justamente
para que a próxima pessoa que adicionar um terceiro caminho de troca de mapa não repita o mesmo
erro — qualquer string de mapa que vá para dentro de um comando de console **precisa** passar por
`ToConsoleMapName` primeiro.

## 11.4 `PoolManagerService` — três sub-rotinas por tick

Tick de 30 segundos, só roda de fato se `MatchServerService.IsConfigured` (AWS configurada) —
caso contrário, o loop gira vazio indefinidamente sem custo. A cada tick que roda:

```csharp
await TopUpAsync(db, server, ct);        // 1. repõe o pool até SUMMIT_POOL_SIZE
await ConfirmBootingAsync(db, server, ct); // 2. confirma via RCON que quem está "Booting" já responde
await ReleaseEmptyAsync(db, server, ct);   // 3. libera de volta ao pool quem ficou vazio
```

Essas três etapas rodam sempre em sequência, nunca em paralelo — isso é intencional: por exemplo,
um servidor recém-criado no passo 1 só vai aparecer como candidato do passo 2 no *próximo* tick
(30s depois), nunca no mesmo — dando tempo real para a instância AWS de fato existir antes de a
API tentar falar com ela.

### 11.4.1 Por que "instância rodando na AWS" ≠ "pronto para uso"

```csharp
if (await server.CheckPoolServerAliveAsync(p))
{
    p.State = PoolServerState.Idle;
    // ...
}
```

`ConfirmBootingAsync` só marca um `PoolServer` como `Idle` depois de uma resposta **RCON real**
(`status`), não apenas depois de a AWS reportar `State.Name == Running` com um IP público. Isso
existe por causa de um problema real encontrado durante o desenvolvimento (documentado em
`docs/plano-aws.md`): uma instância pode estar "Running" segundo a AWS enquanto o processo do CS2
dentro dela ainda está inicializando (ou travado por algum motivo), e tratar "Running" como
sinônimo de "pronto para receber jogadores" levaria a atribuir partidas reais a um servidor que
na verdade não está pronto. RCON respondendo é a única confirmação de que o CS2 de fato está de
pé e aceitando comandos.

### 11.4.2 A janela de graça antes de liberar um servidor `InUse`

```csharp
private static readonly TimeSpan AssignGrace = TimeSpan.FromMinutes(3);
private static readonly TimeSpan HardReleaseCeiling = TimeSpan.FromHours(3);
// ...
if (elapsed < AssignGrace) continue;                 // não checa logo de cara
if (elapsed > HardReleaseCeiling) { /* libera de qualquer jeito */ }
var humans = await server.GetHumanPlayerCountAsync(p);
if (humans == 0) { /* libera */ }
```

`AssignGrace` (3 minutos) evita liberar um servidor de volta ao pool só porque nenhum jogador
entrou *ainda* — dá tempo para os dois times realmente se conectarem depois do fim do veto antes
de começar a monitorar se está vazio. `HardReleaseCeiling` (3 horas) é uma rede de segurança: se
o RCON parar de responder por qualquer motivo (travou, perdeu rede) e `GetHumanPlayerCountAsync`
nunca mais conseguir confirmar "vazio" normalmente, o servidor ainda assim é liberado
incondicionalmente depois de 3 horas — para nunca prender um servidor do pool "preso" para
sempre por uma falha de leitura.

## 11.5 `RconClient` — implementação do protocolo Source RCON

Escrito à mão, sem NuGet, porque o protocolo é simples o suficiente (pacotes binários de tamanho
fixo de cabeçalho sobre TCP) para não justificar uma dependência externa. Estrutura de um pacote
Source RCON:

```
[4 bytes: Size (int32, little-endian, tamanho do resto do pacote)]
[4 bytes: Id (int32, id da requisição, ecoado na resposta)]
[4 bytes: Type (int32, SERVERDATA_AUTH=3 / SERVERDATA_EXECCOMMAND=2 / SERVERDATA_AUTH_RESPONSE=2)]
[N bytes: Body (string, terminada em \0)]
[1 byte: \0 extra de terminação do pacote]
```

`ConnectAndAuthAsync` manda um pacote tipo `3` (auth) com a senha como corpo, e então **lê até 3
pacotes de resposta**, procurando especificamente pelo tipo `SERVERDATA_AUTH_RESPONSE` (2) — isso
porque o protocolo manda um `SERVERDATA_RESPONSE_VALUE` vazio *antes* da resposta de autenticação
real, e um cliente ingênuo que olhasse só o primeiro pacote recebido concluiria erroneamente que
a autenticação falhou (corpo vazio) mesmo quando a senha estava correta.

### 11.5.1 Os três bugs reais encontrados ao validar isso ao vivo

Documentados também em `docs/plano-aws.md`, vale registrar aqui porque são o tipo de erro sutil
que pode voltar a acontecer se o código for reescrito sem atenção:

1. **Leitura de pacote desalinhada** — a versão inicial lia 4 bytes como todo o "cabeçalho"
   (achando que só existia o campo `Size`) e depois `Size` bytes como "o resto", mas isso
   descartava os 4 bytes do campo `Id`, desalinhando a leitura de tudo dali para frente. A versão
   corrigida (a que está no código hoje) lê `Size` sozinho primeiro, e então exatamente `Size`
   bytes de uma vez (que já incluem `Id` + `Type` + corpo + os dois bytes nulos finais):
   ```csharp
   var sizeBytes = await ReadExactAsync(4, timeoutMs);
   var size = BitConverter.ToInt32(sizeBytes, 0);
   var rest = await ReadExactAsync(size, timeoutMs);
   var id = BitConverter.ToInt32(rest, 0);
   var type = BitConverter.ToInt32(rest, 4);
   ```
2. **Consumo duplo de `ValueTask`** — `NetworkStream.ReadAsync` moderno devolve `ValueTask<int>`,
   que (ao contrário de `Task<T>`) **não pode ser aguardado (`await`) mais de uma vez**. Uma
   versão anterior chamava `.AsTask()` nele e depois dava `await` na `ValueTask` original de
   novo por engano, lançando `InvalidOperationException`. A correção guarda a conversão numa
   variável e reusa só essa:
   ```csharp
   var readTask = _stream!.ReadAsync(buf.AsMemory(offset, count - offset)).AsTask();
   if (await Task.WhenAny(readTask, Task.Delay(timeoutMs)) != readTask) throw new TimeoutException(...);
   var n = await readTask;   // reusa a MESMA Task já convertida, nunca a ValueTask original de novo
   ```
3. **Nome de mapa sem prefixo** — já coberto em [§11.3.1](#1131-toconsolemapname--um-bug-real-virado-helper-permanente).

## 11.6 `ServerProvisionPoller` — o irmão mais simples do `PoolManagerService`

Tick de 10 segundos (mais frequente que o pool, porque o cold-boot é um caminho de exceção onde
cada segundo a menos de espera importa mais para a experiência do jogador). Só faz uma coisa:
busca `Match`es com `ProvisionState == Booting` e chama `MatchServerService.PollAsync` para cada
uma, que verifica se a instância já tem IP público via `DescribeInstancesAsync` e, se sim, grava
`ServerIp`/`ProvisionState = Ready`/`Status = Live` na mesma passada.

## 11.7 `SeedData` — como os dados de demonstração são estruturados

`SeedData.EnsureSeededAsync` roda uma única vez (ver [§9.3](09-backend-api-program.md#93-criação-do-schema-e-seed--o-que-acontece-na-primeira-subida))
e monta, em ordem (cada bloco depende do anterior já ter sido salvo, por causa das chaves
estrangeiras):

1. 16 usuários mock (nomes inspirados em jogadores profissionais de CS, estatísticas variadas —
   de `usr_ghost` no topo do ranking até `usr_rookie` quase no fim).
2. 6 times, cada um com um `CaptainId` apontando para um dos usuários acima.
3. Atribuição de cada usuário a um time e cargo, via uma função local `Join(userId, teamId, role)`
   que centraliza a repetição.
4. 4 campeonatos em estados diferentes (`Open`, `InProgress`, `Upcoming`) para que a tela de
   Campeonatos mostre variedade logo de cara.
5. Inscrições (`TournamentTeam`) para dois desses campeonatos.
6. Uma chave pré-montada manualmente (não gerada pelo `LifecycleWorker`) para o campeonato "Cup
   #1" e outra, parcialmente com resultado, para o "Ranked Series" (uma partida já `Finished`,
   uma `Live`) — isso existe para que a tela de chave tenha algo interessante para mostrar sem
   precisar esperar o relógio do sistema atingir os marcos automáticos.
7. Partidas com scoreboard completo (`SeedMatchesAsync`/`BuildPlayerStat`, com números
   pseudo-aleatórios gerados a partir de uma seed determinística por partida —
   `new Random(m.id.GetHashCode())` — para que os números sejam sempre os mesmos entre uma
   recriação do banco e outra, facilitando comparar telas antes/depois de uma mudança de UI).
8. 8 badges de catálogo + desbloqueios manuais para alguns usuários.
9. Amizades (algumas aceitas, algumas pendentes) para popular a tela de Amigos.

Nenhuma dessas linhas de seed passa pelas mesmas rotas HTTP que um uso real passaria — o
`SeedData` escreve direto no `ApiDbContext` (`db.Users.AddRangeAsync(...)`), pulando toda
validação de negócio que os endpoints aplicariam. Isso é aceitável exatamente porque é dado de
demonstração controlado, não simula um usuário real interagindo com regras — mas serve de aviso:
não se deve inferir "quais validações existem" olhando o `SeedData`, porque ele deliberadamente
não passa por nenhuma.
