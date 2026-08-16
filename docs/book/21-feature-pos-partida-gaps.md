[← Sumário](00-indice.md)

# Capítulo 21 — Pós-Partida: o Que Ainda Não Existe

Todo capítulo anterior da Parte V terminou apontando para este — este é, de longe, o maior
buraco conhecido do sistema hoje (`docs/pendencias.md §7` o chama literalmente de "MAIOR BURACO
DO SISTEMA"). Entender exatamente onde a corrente para é tão importante quanto entender o que já
funciona, porque explica por que várias telas do produto mostram dados que nunca mudam sozinhos.

## 21.1 O ponto exato onde tudo para

O sistema sabe fazer tudo até "o servidor está pronto, aqui está o IP" (fim do
[Capítulo 19](19-feature-veto.md)). A partir do momento em que os jogadores entram e jogam, **não
existe nenhum caminho de volta da partida real para a plataforma**:

```csharp
// Isto NÃO existe em lugar nenhum do código:
app.MapPost("/api/matches/{id}/result", ...) 
```

`docs/plano-aws.md` já havia planejado esse endpoint desde o início (Fase 4: "Endpoint
`POST /api/matches/{id}/result` (webhook MatchZy): placar + stats por player → vencedor avança na
chave → `TerminateInstances`") — o MatchZy (o plugin de partida instalado e validado com sucesso
no servidor CS2, ver `docs/plano-aws.md`) já é capaz de, tecnicamente, mandar um webhook de fim de
partida. O que falta é o lado que **recebe** esse webhook na Summit.Api e faz algo com ele.

## 21.2 As cinco consequências concretas dessa lacuna

### 21.2.1 Nada atualiza `MatchStatus.Finished` de verdade

Uma `Match` criada pelo veto nasce `Status = Scheduled` (ou, com a AWS não configurada,
tecnicamente pronta mas sem ninguém nunca marcando o fim). Nenhum processo do sistema muda esse
status para `Finished`, preenche `ScoreA`/`ScoreB`, ou grava `MatchPlayer`s com stats reais. Toda
`Match` com `Status = Finished` e scoreboard completo que existe no banco hoje **veio do
`SeedData`** (ver [§11.7](11-backend-services-workers.md#117-seeddata--como-os-dados-de-demonstração-são-estruturados))
— é dado de demonstração fixo, não histórico real de uso.

### 21.2.2 A chave trava depois da primeira rodada

Já visto em [§18.2](18-feature-bracket.md#182-geração--eliminação-simples): rodadas além da
primeira nascem `TeamATag = TeamBTag = "TBD"` e **permanecem assim para sempre**, porque nada
preenche "quem avançou" a partir do resultado de uma partida anterior. Isso significa que, na
prática, um campeonato de verdade jogado neste sistema hoje só teria sua primeira rodada
realmente disputável — a chave visualmente existe até a final, mas travada.

### 21.2.3 Sem monitoramento de no-show/W.O. dentro da partida

Existe um no-show do **check-in** (times que não confirmam presença 30min antes do início são
removidos automaticamente, ver [§16.4](16-feature-campeonatos-inscricao.md#164-fechamento-automático-do-check-in-t-30min))
— mas isso é diferente do no-show **dentro do servidor** (`docs/espec-campeonatos.md §10-11`:
"sistema monitora conectados/mínimo/tempo... após tempo configurado → W.O."). Não há nada que
observe quantos jogadores realmente entraram no servidor depois que a sala foi criada.

### 21.2.4 Badges nunca são concedidas por desempenho real

```csharp
// Existe a tela (BadgesView) e a API de leitura (GET /api/badges/user/{userId})...
// ...mas nenhuma lógica em lugar nenhum do código chama "conceder badge pra fulano".
```

As únicas linhas em `userbadges` vêm de `SeedData.EnsureSeededAsync` (uma lista fixa de
`(userId, badgeId)` escrita à mão). Não existe, por exemplo, um processo que detecte "5 kills
num round" e conceda automaticamente a badge "ACE!" — mesmo essa badge já existindo no catálogo.

### 21.2.5 Campeonato nunca "encerra" sozinho

`TournamentStatus.Finished` existe como valor de enum, mas nada no `LifecycleWorker` (ou em
qualquer outro lugar) faz a transição `InProgress → Finished`. `docs/espec-campeonatos.md §17`
descreve o encerramento como: registrar campeão/vice/3ºs, salvar histórico permanente, atualizar
ranking, distribuir premiação — nenhuma dessas etapas tem implementação hoje.

## 21.3 Por que isso não foi implementado ainda (não é um esquecimento)

Esse gap não é acidental — é uma sequência de decisões de escopo deliberadas ao longo do
desenvolvimento (documentadas explicitamente em `docs/pendencias.md`), priorizando primeiro
"conseguir chegar a uma partida real jogável de ponta a ponta" (login → time → inscrição →
escalação → check-in → chave → veto → servidor real com CS2+MatchZy) antes de fechar o círculo de
volta. Cada peça dessa cadeia foi testada ao vivo nesse processo — o [Capítulo 19](19-feature-veto.md)
e [Capítulo 20](20-feature-pool-servidores.md) descrevem infraestrutura genuinamente validada em
produção (uma partida real conectando a um servidor CS2 real na AWS). O que falta é
especificamente o **retorno** dessa informação — um problema de escopo isolado e bem
compreendido, não um problema de arquitetura.

## 21.4 O que precisaria existir (visão de implementação futura, não código atual)

Para deixar claro o tamanho real do trabalho pendente, uma lista concreta do que a próxima fase
precisaria construir — isto é análise, **não** uma descrição de código existente:

1. **`POST /api/matches/{id}/result`** — endpoint que recebe placar final + stats por jogador
   (formato ainda a definir, mas o webhook do MatchZy é o candidato natural de origem). Precisa
   validar que veio de uma fonte confiável (hoje nenhum endpoint da API tem autenticação/API key
   — isso também precisaria ser resolvido para um webhook exposto à internet, diferente dos
   endpoints de debug que só rodam localmente).
2. **Avanço de chave** — ao receber um resultado, localizar a próxima `BracketMatch` que deveria
   receber o vencedor (isso exige uma função que, dado um `BracketMatch` da Upper, saiba
   determinar sua "próxima" partida — hoje não existe nenhum link explícito entre partidas
   consecutivas de rodadas diferentes; a estrutura atual só sabe agrupar por `RoundId`/`Position`,
   não "esta partida alimenta aquela outra"). Para eliminação dupla, isso é ainda mais elaborado:
   o perdedor da Upper precisa ser roteado para a posição certa da Lower, seguindo o padrão
   específico de "descida" que a fórmula de [§18.3.1](18-feature-bracket.md#1831-a-fórmula-de-contagem-de-partidas-por-rodada-da-lower)
   já antecipa em quantidade, mas não em *roteamento* individual.
3. **Atualização de estatísticas do usuário** — recalcular (ou incrementar)
   `User.TotalKills/TotalDeaths/KD/WinRate/...` a partir do resultado — hoje esses campos só
   existem como valores estáticos do seed.
4. **Motor de badges** — um conjunto de regras ("5 kills num round → ACE", "MVP em 10 partidas →
   MVP", etc.) avaliadas a cada resultado recebido.
5. **Encerramento de campeonato** — quando a Grande Final (ou a final da eliminação simples) tem
   resultado, marcar `TournamentStatus.Finished`, gravar posições finais
   (`TournamentTeam.FinalPosition`).
6. **Monitoramento de conexão/no-show em servidor** — provavelmente via RCON `status` periódico
   (o mesmo mecanismo já usado por `MatchServerService.GetHumanPlayerCountAsync`, ver
   [§20.5](20-feature-pool-servidores.md#205-liberação-automática--como-o-sistema-sabe-que-uma-partida-acabou)),
   comparando contra os jogadores esperados da escalação de cada lado.

Nenhum desses itens é conceitualmente difícil isoladamente — a razão de listá-los juntos aqui é
mostrar que "fechar esse buraco" é, na prática, o tamanho de um novo capítulo de produto inteiro,
não um ajuste pequeno.

## 21.5 Como isso se conecta com o que os capítulos anteriores já entregaram

Vale fechar este capítulo — e o livro — lembrando o que **já** funciona de ponta a ponta, porque
é fácil, depois de uma lista de gaps, perder de vista que a maior parte do produto está
genuinamente pronta e testada:

- Conta, onboarding e perfil completo ([Capítulo 12](12-feature-conta-login.md)).
- Time de ponta a ponta: criar, convidar, solicitar entrada, promover, rebaixar, transferir,
  remover, editar, excluir ([Capítulo 13](13-feature-times.md)).
- Amizades e bloqueio ([Capítulo 14](14-feature-amizades.md)).
- Auditoria de leitura ([Capítulo 15](15-feature-auditoria.md)).
- Inscrição com escalação, check-in automático com remoção de ausentes
  ([Capítulo 16](16-feature-campeonatos-inscricao.md), [Capítulo 17](17-feature-escalacao.md)).
- Geração de chave flexível (qualquer tamanho, simples ou dupla) e renderização genérica
  ([Capítulo 18](18-feature-bracket.md)).
- Veto de mapas completo (MD1/MD3/MD5) com criação de sala
  ([Capítulo 19](19-feature-veto.md)).
- Pool de servidores CS2 reais na AWS, com fallback de cold-boot
  ([Capítulo 20](20-feature-pool-servidores.md)).

O que resta é especificamente **o que acontece depois de "os jogadores entraram no servidor"** —
um escopo isolado, bem delimitado, e agora, espera-se, bem compreendido por quem terminar de ler
este livro.
