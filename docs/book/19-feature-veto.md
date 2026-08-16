[← Sumário](00-indice.md)

# Capítulo 19 — Veto de Mapas e Sala da Partida

## 19.1 Quando um veto começa

`LifecycleWorker`, no ramo `now >= t.StartDate`, cria uma `VetoSession` para toda partida da
**primeira rodada** que já tem os dois lados preenchidos (não é "TBD"/"BYE") e ainda não tem
sessão:

```csharp
var r1 = t.Bracket.OrderBy(r => r.RoundNumber).First();
foreach (var bm in r1.Matches.Where(m => m.TeamATag != "TBD" && m.TeamBTag != "TBD" && m.Status == BracketMatchStatus.Pending))
{
    var hasVeto = await db.VetoSessions.AnyAsync(v => v.BracketMatchId == bm.Id);
    if (hasVeto) continue;
    db.VetoSessions.Add(new VetoSession { /* ... */ });
    bm.Status = BracketMatchStatus.Veto;
}
```

Consistente com o que foi discutido no [Capítulo 18](18-feature-bracket.md): como só a primeira
rodada tem times reais preenchidos, só a primeira rodada consegue automaticamente abrir vetos
hoje. Existe também `POST /api/veto/{bracketMatchId}/start`, chamado pelo client
(`MatchRoomViewModel.RefreshAsync`, ver [§19.5](#195-a-tela-matchroomviewmodel)) como um
mecanismo idempotente de "garantir que a sessão existe" — se o `LifecycleWorker` já criou, ele só
devolve a existente; senão, cria uma na hora.

## 19.2 A sequência de bans/picks — `BuildSequence`

`docs/espec-campeonatos.md §8` define a sequência por formato de série:

- **MD1**: 6 bans alternados (A, B, A, B, A, B), o mapa restante é jogado.
- **MD3**: 2 bans, 2 picks, 2 bans, restante = decider.
- **MD5**: 2 bans, 4 picks, restante = decider (menos bans, porque mais mapas são jogados).

O código generaliza isso com uma fórmula em vez de listar cada caso manualmente:

```csharp
public static List<(VetoActionType action, int side)> BuildSequence(SeriesFormat series, int poolSize)
{
    int picks = series switch { SeriesFormat.MD3 => 2, SeriesFormat.MD5 => 4, _ => 0 };
    int totalSteps = Math.Max(poolSize - 1, 0);       // sempre sobra 1 mapa (o decider)
    int bansBefore = Math.Min(2, Math.Max(totalSteps - picks, 0));
    int bansAfter  = Math.Max(totalSteps - picks - bansBefore, 0);

    var steps = new List<(VetoActionType, int)>();
    int i = 0;
    for (int k = 0; k < bansBefore; k++) steps.Add((VetoActionType.Ban, i++ % 2));
    for (int k = 0; k < picks;      k++) steps.Add((VetoActionType.Pick, i++ % 2));
    for (int k = 0; k < bansAfter;  k++) steps.Add((VetoActionType.Ban, i++ % 2));
    return steps;
}
```

`side` alterna estritamente `0, 1, 0, 1, ...` ao longo de toda a sequência (`i++ % 2`) —
side 0 é sempre o Time A, side 1 o Time B, e a alternância nunca reinicia entre a fase de bans e
a fase de picks (ela continua de onde parou). `totalSteps = poolSize - 1` é a regra central:
**sempre sobra exatamente 1 mapa** no fim, que vira o decider automaticamente — nunca é um pick
explícito de ninguém.

O pool padrão do projeto tem 7 mapas (`docs/espec-campeonatos.md`: Mirage, Inferno, Nuke,
Ancient, Anubis, Dust2, Train) — para MD1 isso dá `totalSteps = 6`, todos bans
(`bansBefore = min(2, 6-0) = 2`... espera, isso daria só 2 bans antes e o resto depois: com
`picks=0`, `bansBefore = min(2, 6) = 2`, `bansAfter = 6-0-2 = 4`, total 6 bans — bate com a
especificação "6 bans alternados" para MD1 com pool de 7). Para MD3 com pool de 7:
`totalSteps=6`, `picks=2`, `bansBefore=min(2,4)=2`, `bansAfter=6-2-2=2` → 2 bans, 2 picks, 2 bans,
exatamente como a especificação descreve.

## 19.3 Executando uma ação: `POST /api/veto/{bracketMatchId}/action`

Cada chamada:

1. Recalcula a sequência esperada (`BuildSequence`) e confere que `body.TeamTag` é exatamente
   quem deveria agir no passo atual (`s.StepIndex`) — recusa com mensagem específica se não for
   ("Não é a vez de X — vez de Y.").
2. Confere que o mapa pedido está de fato disponível (não foi banido/escolhido antes, e está no
   pool) via `RemainingMaps`.
3. Grava o `VetoStep`, incrementa `s.StepIndex`.
4. **Se a sequência terminou**, cria automaticamente o passo final `Decider` com o único mapa
   restante, marca `s.IsComplete = true`, e — na mesma operação — **cria a sala da partida**
   (`Match`).

```csharp
public static List<string> RemainingMaps(VetoSession s)
{
    var used = s.Steps.Select(st => st.Map).ToHashSet(StringComparer.OrdinalIgnoreCase);
    return s.MapPool.Where(m => !used.Contains(m)).ToList();
}
```

## 19.4 O que acontece exatamente quando o veto termina

Este é um dos trechos mais densos do sistema — vale acompanhar linha a linha
(`CompetitionEndpoints.cs`, dentro do `if (s.StepIndex >= seq.Count)`):

```csharp
var playMaps = s.Steps.Where(x => x.Action != VetoActionType.Ban).OrderBy(x => x.Order).Select(x => x.Map).ToList();
var isAwsConfigured = MatchServerService.IsConfigured;
var room = new Match
{
    Id = $"m_{Guid.NewGuid():N}",
    Map = playMaps.First(),
    Status = MatchStatus.Scheduled,
    TeamATag = s.TeamATag, TeamBTag = s.TeamBTag,
    BracketMatchId = bm.Id,
    ServerIp = isAwsConfigured ? "" : $"sv{Random.Shared.Next(1,9)}.summit.gg:{27015 + Random.Shared.Next(0,4)}",
    ServerPassword = $"smt_{Guid.NewGuid().ToString("N")[..8]}",
    ProvisionState = isAwsConfigured ? ServerProvisionState.Requesting : ServerProvisionState.Ready
};
```

Repare no `if (isAwsConfigured)`: se a AWS **não** está configurada neste ambiente (ex.
desenvolvimento local, sem `AWS_ACCESS_KEY_ID`/`SUMMIT_AMI_ID` definidos), a sala nasce com um
**IP simulado** (`sv3.summit.gg:27017`, nunca um servidor real) e `ProvisionState = Ready`
imediatamente — isso existe puramente para que a UI da sala de partida tenha algo para mostrar e
testar (o botão "entrar no servidor", o fluxo completo até aqui) sem exigir AWS configurada
localmente. Quando a AWS está configurada, `ProvisionState = Requesting`, e o preenchimento do IP
real acontece depois, de forma assíncrona (ver [Capítulo 20](20-feature-pool-servidores.md)).

Logo depois, ainda na mesma requisição HTTP (fora do bloco `if`, já com `SaveChangesAsync` feito):

```csharp
var newRoom = await db.Matches.FirstOrDefaultAsync(m => m.BracketMatchId == bracketMatchId);
if (newRoom != null && newRoom.ProvisionState == ServerProvisionState.Requesting)
{
    var assignedFromPool = await server.TryAssignFromPoolAsync(newRoom.Id, newRoom.Map, newRoom.ServerPassword);
    if (!assignedFromPool)
        _ = server.ProvisionAsync(newRoom.Id);   // fire-and-forget: cold boot como fallback
}
```

Isso é o ponto de integração exato entre o veto e o [Capítulo 20](20-feature-pool-servidores.md):
assim que a sala nasce precisando de servidor real, a API **tenta primeiro** pegar um servidor já
quente do pool (via RCON, leva segundos); só cai para o cold-boot de uma EC2 nova (minutos) se o
pool não tiver nenhum servidor livre no momento. `_ = server.ProvisionAsync(...)` é
fire-and-forget deliberado — a resposta HTTP do `/action` não espera o cold-boot terminar (que
levaria minutos), ela devolve na hora e o `ServerProvisionPoller` (tick de 10s) acompanha o
resto em segundo plano.

## 19.5 A tela: `MatchRoomViewModel`

Um hub estilo FACEIT que mistura três estados (elencos, veto ao vivo, sala pronta) na mesma tela,
usando um `DispatcherTimer` de 3 segundos para simular tempo real (ver
[§5.6](05-client-mvvm.md#56-timers-para-simular-tempo-real-polling)). A cada `RefreshAsync`:

```csharp
var state = await _veto.GetStateAsync(_bracketMatchId) ?? await _veto.StartAsync(_bracketMatchId);
```

Note esse `??`: se ainda não existe sessão (`GET` devolve `null`/404), o client tenta **criar**
uma via `StartAsync` — isso é o que permite ao jogador abrir a sala de uma partida mesmo que o
`LifecycleWorker` ainda não tenha rodado o tick que criaria a sessão automaticamente (reduz a
espera percebida de até 20 segundos para instantâneo, do ponto de vista do jogador).

`MyTurn` decide se os mapas ficam clicáveis, comparando a tag do time do usuário atual com quem
o servidor diz que deve agir agora:

```csharp
var next = state.Next;
MyTurn = !s.IsComplete && next != null && string.Equals(next.Team, myTag, StringComparison.OrdinalIgnoreCase);
```

Cada mapa do pool vira um `VetoMapItem` (ver [§7.3](07-client-models.md#73-catálogo-dos-dtos-de-apresentação-locais))
com um rótulo textual calculado a partir do `VetoStep` correspondente, se houver:

```csharp
var label = step?.Action switch
{
    VetoActionType.Ban     => $"BAN {step.TeamTag}",
    VetoActionType.Pick    => $"PICK {step.TeamTag}",
    VetoActionType.Decider => "DECIDER",
    _                      => ""
};
IsClickable = available && MyTurn;
```

Quando `s.IsComplete`, a tela para de esperar ação de veto e passa a buscar a sala
(`_veto.GetRoomAsync`), mostrando `"PROVISIONANDO SERVIDOR NA AWS... ~90S"` enquanto
`HasRoom` (que checa `!string.IsNullOrWhiteSpace(_room.ServerIp)`) for falso, e parando o timer
assim que o IP aparecer:

```csharp
if (HasRoom) { StatusLine = "SERVIDOR PRONTO — BOA SORTE!"; _timer.Stop(); }
else         { StatusLine = "PROVISIONANDO SERVIDOR NA AWS... ~90S"; }
```

O botão "Entrar no servidor" (`ConnectCommand`, tanto aqui quanto em `MatchDetailsViewModel`) não
abre nenhum processo do CS2 diretamente — ele copia o comando de conexão para a área de
transferência, para o jogador colar no console do próprio jogo:

```csharp
System.Windows.Clipboard.SetText($"connect {_room.ServerIp}; password {_room.ServerPassword}");
```

## 19.6 Bots de veto para contas de demonstração

Já mencionado em [§11.2.1](11-backend-services-workers.md#1121-por-que-autovetobotsasync-chama-a-própria-api-via-http-em-vez-de-manipular-o-banco-direto):
`LifecycleWorker.AutoVetoBotsAsync` identifica contas "bot" pelo tamanho do id
(`team.CaptainId.Length <= 12` — os ids curtos como `usr_ghost` do `SeedData`, contra os ids
longos de 36+ caracteres gerados por `Guid.NewGuid():N` para contas reais criadas via login), e
faz uma jogada de veto por tick para essas contas, escolhendo aleatoriamente entre os mapas
disponíveis (`Random.Shared.Next(remaining.Count)`) — dando ao jogador humano do outro lado a
sensação de estar vetando contra um adversário real que também está agindo, em vez de o veto
ficar parado esperando indefinidamente porque "o outro lado" é só uma conta de seed sem ninguém
logado como ela.
