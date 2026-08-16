# Plan — Summit, Fase Final (Design Técnico)

> Como implementar o que [`spec.md`](spec.md) descreve. Cada seção usa o mesmo número de RF da
> spec para referência cruzada fácil. Convenções de código a seguir: as já estabelecidas no
> projeto — ver [`docs/book/03-padroes-projeto.md`](../../book/03-padroes-projeto.md) (Minimal
> API, `record` para DTOs, `Audit(...)` como efeito colateral, ids `prefixo_{Guid:N}`, enums com
> valor numérico explícito e nunca reordenados).

## RF-00 — Provider de servidor plugável

### Interface

Novo arquivo `Summit.Api/IMatchServerProvider.cs`:

```csharp
public interface IMatchServerProvider
{
    Task ProvisionAsync(string matchId);
    Task<bool> TryAssignFromPoolAsync(string matchId, string map, string password);
}
```

Essa assinatura é **exatamente** a superfície pública que `CompetitionEndpoints.cs` já chama
hoje em `MatchServerService` (ver `docs/book/19-feature-veto.md §19.4`) — trocar a implementação
não exige tocar no ponto de chamada do veto, só a resolução de DI.

### Implementações

- `AwsMatchServerProvider : IMatchServerProvider` — **wrapper fino** em volta da
  `MatchServerService` existente (não reescrever nada dela; ela continua com todos os métodos de
  RCON/AWS SDK como estão hoje, ver `docs/book/11-backend-services-workers.md §11.3`). Só existe
  pra satisfazer a interface nova.
- `LocalSimulatedMatchServerProvider : IMatchServerProvider` (novo, é o coração do RF-00):
  - `ProvisionAsync(matchId)`: espera um delay curto configurável (`SUMMIT_SIM_DELAY_SECONDS`,
    padrão 5), grava `Match.ServerIp = "sim.summit.local:27015"`,
    `Match.ServerPassword` mantém o que já foi gerado no veto, `ProvisionState = Ready`,
    `Status = Live`. Depois, agenda a simulação de resultado (ver abaixo).
  - `TryAssignFromPoolAsync(...)`: sempre devolve `true` imediatamente, populando o mesmo
    `ServerIp` simulado — não existe conceito de "pool" no modo local (não faz sentido simular
    fila de servidor quente; a v'antagem do pool é só custo de cold-boot real, que não existe
    aqui).
  - **Simulação automática de resultado**: depois do `ProvisionAsync`, um `Task.Delay` adicional
    configurável (`SUMMIT_SIM_RESULT_DELAY_SECONDS`, padrão 20) chama internamente o mesmo
    handler de `POST /api/matches/{id}/result` (RF-01) com placar/estatísticas **geradas
    proceduralmente** (vencedor aleatório 50/50 por padrão, ou determinístico se houver uma
    "instrução de teste" pendente — ver próximo parágrafo). Isso roda em um `Task.Run`
    fire-and-forget a partir de um escopo de DI próprio (mesmo padrão dos `BackgroundService`,
    ver `docs/book/11-backend-services-workers.md §11.1`), nunca bloqueando a resposta HTTP do
    veto.
  - **Controle determinístico pra teste dirigido**: novo endpoint de debug
    `POST /api/debug/simulate-result/{matchId}` com corpo `{ "winner": "A" | "B" }` — se chamado
    **antes** do delay automático disparar, o resultado simulado respeita o vencedor pedido em
    vez de sortear. Isso é o que permite a um QA "andar a chave inteira de propósito" (RF-02,
    critério de aceite do fluxo de ponta a ponta em `spec.md §15`).

### Registro em `Program.cs`

```csharp
var provider = Environment.GetEnvironmentVariable("SUMMIT_MATCH_PROVIDER") ?? "local";
if (provider == "aws")
    builder.Services.AddSingleton<IMatchServerProvider, AwsMatchServerProvider>();
else
    builder.Services.AddSingleton<IMatchServerProvider, LocalSimulatedMatchServerProvider>();
// MatchServerService continua registrado como está hoje — AwsMatchServerProvider depende dele.
```

`SUMMIT_MATCH_PROVIDER` ausente = `local` — desplugado por padrão, sem exceção (satisfaz o
critério de aceite de RF-00 na spec: "desligado por definição, não por acidente de configuração
ausente").

### Pontos de chamada a atualizar

`CompetitionEndpoints.cs`, dentro do fechamento do veto (`POST /api/veto/{id}/action`): trocar o
parâmetro do endpoint de `MatchServerService server` para `IMatchServerProvider server` — a
lógica ali (tentar pool, cair pro provision) não muda uma linha, só o tipo do parâmetro.

## Modelo de dados — mudanças

### `BracketMatch` (`Models/Bracket.cs`)

```csharp
public string? NextMatchId { get; set; }
public char? NextMatchSlot { get; set; }        // 'A' ou 'B' — onde o vencedor entra
public string? LoserNextMatchId { get; set; }    // só preenchido em BracketSide.Upper
public char? LoserNextMatchSlot { get; set; }
```

`ApiDbContext`: sem conversão especial necessária (strings/char nullable mapeiam direto).
MySQL: `ALTER TABLE bracketmatches ADD COLUMN NextMatchId VARCHAR(255) NULL, ADD COLUMN
NextMatchSlot CHAR(1) NULL, ADD COLUMN LoserNextMatchId VARCHAR(255) NULL, ADD COLUMN
LoserNextMatchSlot CHAR(1) NULL;`

### `Tournament` (`Models/Tournament.cs`)

```csharp
public string OrganizerUserId { get; set; } = string.Empty;   // substitui o uso de `Organizer` como identidade
public int SwissTargetWins { get; set; } = 3;      // RF-07 — X vitórias classifica
public int SwissEliminationLosses { get; set; } = 2; // RF-07 — Y derrotas elimina
```

`Organizer` (string livre) é **mantido** como campo de exibição (nome público do organizador,
pode divergir do nick por escolha de UX) — `OrganizerUserId` é o vínculo real usado pra
permissão de edição (RF-09). MySQL: `ALTER TABLE tournaments ADD COLUMN OrganizerUserId
VARCHAR(255) NOT NULL DEFAULT '', ADD COLUMN SwissTargetWins INT NOT NULL DEFAULT 3, ADD COLUMN
SwissEliminationLosses INT NOT NULL DEFAULT 2;`

### `User` (`Models/User.cs`)

```csharp
public bool IsModerator { get; set; }   // RF-08 — flag manual, sem fluxo de concessão pelo produto
```
MySQL: `ALTER TABLE users ADD COLUMN IsModerator TINYINT(1) NOT NULL DEFAULT 0;`

### Nova entidade `Notification` (RF-06) — novo arquivo `Models/Notification.cs`

```csharp
public enum NotificationType
{
    TeamInvite = 0, JoinRequestResolved = 1, RoleChanged = 2, OwnershipTransferred = 3,
    CheckInOpened = 4, LineupChanged = 5, TournamentFinished = 6, BadgeUnlocked = 7,
    ReportResolved = 8
}

public class Notification
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RelatedId { get; set; }     // teamId, tournamentId, badgeId, etc. — livre, sem FK (mesmo padrão do AuditLog)
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```
`ApiDbContext`: `notif.Property(x => x.Type).HasConversion<int>();`, índice em `(UserId,
IsRead)`. MySQL: `CREATE TABLE notifications (Id VARCHAR(255) PRIMARY KEY, UserId VARCHAR(255)
NOT NULL, Type INT NOT NULL, Message LONGTEXT NOT NULL, RelatedId VARCHAR(255) NULL, IsRead
TINYINT(1) NOT NULL DEFAULT 0, CreatedAt DATETIME(6) NOT NULL, KEY IX_Notifications_UserId_IsRead
(UserId, IsRead));`

### Nova entidade `Report` (RF-08) — novo arquivo `Models/Report.cs`

```csharp
public enum ReportStatus { Pending = 0, Resolved = 1, Dismissed = 2 }

public class Report
{
    public string Id { get; set; } = string.Empty;
    public string ReporterId { get; set; } = string.Empty;
    public string ReportedUserId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public ReportStatus Status { get; set; } = ReportStatus.Pending;
    public string? ResolutionNote { get; set; }
    public string? ResolvedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}
```
MySQL: tabela análoga, índice em `Status`.

## RF-01 — Endpoint de resultado

Novo em `CompetitionEndpoints.cs` (mesmo arquivo das regras de partida/veto):

```csharp
app.MapPost("/api/matches/{id}/result", async (ApiDbContext db, IMatchServerProvider server, string id, MatchResultBody body) =>
{
    var match = await db.Matches.FirstOrDefaultAsync(m => m.Id == id);
    if (match == null) return Results.NotFound();
    if (match.Status == MatchStatus.Finished) return Results.Ok(true); // idempotente

    match.Status = MatchStatus.Finished;
    match.ScoreA = body.ScoreA;
    match.ScoreB = body.ScoreB;
    match.DurationMinutes = body.DurationMinutes;

    foreach (var p in body.PlayersA) db.MatchPlayers.Add(ToMatchPlayer(id, p, "A"));
    foreach (var p in body.PlayersB) db.MatchPlayers.Add(ToMatchPlayer(id, p, "B"));

    if (match.BracketMatchId != null)
        await AdvanceBracketAsync(db, match.BracketMatchId, body.ScoreA > body.ScoreB); // RF-02

    await UpdateStatsAndEloAsync(db, match, body);   // RF-04
    await EvaluateBadgesAsync(db, body.PlayersA.Concat(body.PlayersB).Select(p => p.UserId));  // RF-05

    await Audit(db, "match_result_recorded", null, null, null, null, null, $"{body.ScoreA}-{body.ScoreB}", null);
    await db.SaveChangesAsync();
    return Results.Ok(true);
});

record MatchPlayerResultBody(string UserId, int Kills, int Deaths, int Assists, int HeadshotKills, double AvgDamagePerRound, double Rating, bool IsMvp);
record MatchResultBody(int ScoreA, int ScoreB, int DurationMinutes, List<MatchPlayerResultBody> PlayersA, List<MatchPlayerResultBody> PlayersB);
```

Esse é o **único** ponto de entrada de resultado — tanto o `LocalSimulatedMatchServerProvider`
(RF-00) quanto, no futuro, um webhook real do MatchZy chamariam esse mesmo endpoint. É isso que
torna o provider "plugável de verdade": nada rio abaixo (avanço de chave, stats, badges) sabe ou
se importa de onde o resultado veio.

## RF-02 — Avanço de chave

### Geração: wiring dos ponteiros (estende `LifecycleWorker.GenerateSingleElimination`/`GenerateDoubleElimination`)

**Regra geral de avanço "por vitória" (vale pra Upper inteira e pra simples)**: partida `p`
(0-indexed) da rodada `r` alimenta a partida `floor(p/2)` da rodada `r+1`, no slot `A` se `p` é
par, `B` se ímpar. Isso já é *implicitamente* a mesma regra que o seed da rodada 0 usa hoje
(`teams[p*2]`/`teams[p*2+1]`) — só está sendo tornada explícita e persistida.

**Regra de roteamento do perdedor da Upper pra Lower (só eliminação dupla)** — verificada à mão
contra o caso N=8 (k=3), documentado abaixo como referência de implementação:

| Rodada perdedora (Upper, r 0-indexed) | Vai pra Lower rodada `i` | Mapeamento de índice |
|---|---|---|
| `r = 0` (primeira rodada) | `i = 0` | `p` → Lower match `floor(p/2)`, slot par/ímpar (halving — Lower round 0 tem metade das partidas da Upper R1) |
| `r >= 1` | `i = 2r - 1` | `p` → Lower match `p` direto (1:1 — contagem de partidas bate exatamente, ver tabela de contagem em `docs/book/18-feature-bracket.md §18.3.1`), sempre no slot `B` (o slot `A` é do sobrevivente vindo da própria Lower) |

**Regra de avanço interno da Lower (sobrevivente, dentro da própria Lower)**:
- Se a rodada `i+1` é **de colocação** (`i+1` ímpar — recebe perdedor novo da Upper): mapeamento
  1:1, match `j` → match `j`, sempre no slot `A`.
- Se a rodada `i+1` é **de eliminação** (`i+1` par, `> 0` — só sobreviventes se enfrentam):
  mapeamento por halving igual à Upper, match `j` → match `floor(j/2)`, slot par/ímpar.

**Grande Final**: vencedor da última rodada Upper → slot `A` da Grande Final (`RoundNumber=200`).
Vencedor da última rodada Lower ("LOWER FINAL") → slot `B`. Isso já é coberto pelas regras
acima (última rodada Upper tem `r = k-1 >= 1`, logo o *perdedor* dela já vai pra Lower Final
pela regra de cima — só falta o *vencedor* apontar pra Grande Final via `NextMatchId`, mesma
regra de "vitória" só que sem halving porque só há 1 partida).

**Reset da Grande Final**: não é gerado na criação da chave (como já documentado em
`docs/book/18-feature-bracket.md §18.3.2`) — é criado sob demanda por `AdvanceBracketAsync`
(abaixo) só se `Tournament.BracketReset == true` e o time vindo da Lower vencer a primeira
Grande Final. Nova rodada `RoundNumber = 201`, side `GrandFinal`, 1 partida, `TeamATag`/
`TeamBTag` já preenchidos com os dois times da primeira Grande Final (sem "TBD" — já se sabe
quem joga).

### `AdvanceBracketAsync(db, bracketMatchId, aWon)` — lógica de execução

```csharp
async Task AdvanceBracketAsync(ApiDbContext db, string bracketMatchId, bool aWon)
{
    var bm = await db.BracketMatches.Include(m => m.Round).FirstOrDefaultAsync(m => m.Id == bracketMatchId);
    var winnerTag = aWon ? bm.TeamATag : bm.TeamBTag;
    var loserTag  = aWon ? bm.TeamBTag : bm.TeamATag;
    bm.Status = BracketMatchStatus.Finished;

    if (bm.NextMatchId != null) FillSlot(db, bm.NextMatchId, bm.NextMatchSlot!.Value, winnerTag);

    if (bm.Round!.Side == BracketSide.Upper)
    {
        // perdedor sai da Upper — vai pra Lower se houver LoserNextMatchId, senão está eliminado
        if (bm.LoserNextMatchId != null) FillSlot(db, bm.LoserNextMatchId, bm.LoserNextMatchSlot!.Value, loserTag);
        else MarkEliminated(db, loserTag, bm.Round.TournamentId);
    }
    else if (bm.Round.Side == BracketSide.Lower)
    {
        MarkEliminated(db, loserTag, bm.Round.TournamentId);  // perder na Lower = eliminado de vez
    }
    else // GrandFinal
    {
        await HandleGrandFinalResultAsync(db, bm, winnerTag, loserTag);  // pode gerar reset, ou encerrar torneio (RF-03)
    }

    // se a partida alimentada ficou com os dois lados preenchidos (nem TBD nem BYE), abre o veto —
    // generaliza o que hoje só acontece pra rodada 1 no tick do LifecycleWorker
    await OpenVetoIfReadyAsync(db, bm.NextMatchId);
    await OpenVetoIfReadyAsync(db, bm.LoserNextMatchId);
}
```

`FillSlot` escreve `TeamATag`/`TeamBTag` na posição certa. `OpenVetoIfReadyAsync` reaproveita
exatamente a mesma lógica de criação de `VetoSession` que `LifecycleWorker.TickAsync` já tem pra
rodada 1 (`docs/book/19-feature-veto.md §19.1`) — extrair pra um método compartilhado entre os
dois lugares (`LifecycleWorker` continua usando pra rodada 1 na abertura do campeonato;
`AdvanceBracketAsync` usa pra toda rodada seguinte).

## RF-03 — Encerramento de campeonato

`HandleGrandFinalResultAsync`: se não há reset pendente (ou o reset acabou de terminar), ou se é
eliminação simples e a última rodada terminou: marca `Tournament.Status = Finished`, seta
`FinalPosition = 1` no time campeão e `= 2` no vice (via `TournamentTeam` pelo `Tag`), dispara
notificação `TournamentFinished` (RF-06) pra todo `TournamentTeam` daquele campeonato. Terceiro/
quarto lugar (bônus, RF-03 marca como opcional): times perdedores da penúltima rodada Upper (ou
semifinal, no caso simples) recebem `FinalPosition = 3` (empatados, sem disputa de 3º lugar —
simplificação documentada).

## RF-04 — Estatísticas e Elo

Fórmula (documentada, deliberadamente simples — não é um sistema de rating competitivo sério):

```
E_time = 1 / (1 + 10^((Elo_adversário - Elo_time) / 400))
Δ = K * (S - E_time)        // K = 32, S = 1 se venceu, 0 se perdeu (nunca há empate)
Team.Elo += Δ (arredondado)
```

Cada jogador do time recebe o **mesmo `Δ`** aplicado ao `User.Elo` — decisão consciente de não
tentar isolar performance individual (isso é um problema de matchmaking sério, fora do escopo
razoável desta fase; documentado aqui pra não parecer descuido).

Os demais campos agregados de `User` (`TotalMatches`, `TotalWins`, `TotalKills`, `TotalDeaths`,
`TotalAssists`, `KD`, `WinRate`, `HeadshotPercent`, `AvgDamagePerRound`) são recalculados por
incremento simples a partir do novo `MatchPlayer` — `KD`/`WinRate`/`HeadshotPercent`/
`AvgDamagePerRound` recomputados como média sobre os totais atualizados (não como média móvel
separada). `Team.MatchesPlayed`/`MatchesWon` incrementam de forma equivalente.

## RF-05 — Badges automáticas

| Badge (`Id` do catálogo) | Critério exato | Fonte de dado |
|---|---|---|
| `bd_firstwin` | `User.TotalWins` era 0 antes deste resultado e o time do jogador venceu | `User` antes/depois do incremento |
| `bd_mvp` | `COUNT(MatchPlayers WHERE UserId=X AND IsMvp=true) >= 10` | consulta agregada em `MatchPlayers` |
| `bd_champion` | jogador pertence ao time que virou campeão (RF-03) no momento do encerramento | disparado em `HandleGrandFinalResultAsync`, não no resultado de partida comum |
| `bd_hunter` | média de `HSPercent` nas últimas 30 `MatchPlayers` do jogador `>= 0.50` | consulta com `OrderByDescending(PlayedAt).Take(30)` |
| `bd_loyal` | `DateTime.UtcNow - User.TeamJoinedAt >= 365 dias`, ainda no mesmo time | **não** disparado por resultado de partida — checado 1x por dia dentro do `LifecycleWorker.TickAsync` (throttle simples: só roda essa checagem se `now.Hour == 3`, por exemplo, pra não fazer isso a cada 20s) |
| `bd_founder` | sem critério automático — mantido como concessão manual/seed, documentado como decisão, não pendência |
| `bd_clutch`, `bd_ace` | **não implementável com o dado atual** (exigem estatística por round, que `MatchPlayer` não guarda). Documentado como limitação conhecida — permanece só concedida manualmente/seed até uma spec futura estender o contrato de resultado com detalhamento por round. |

`EvaluateBadgesAsync(db, userIds)`: pra cada `userId`, roda as regras acima que dependem só de
resultado de partida (`bd_firstwin`, `bd_mvp`, `bd_hunter`); grava `UserBadge` nova só se ainda
não existe (`db.UserBadges.AnyAsync(...)` antes de adicionar — idempotente).

## RF-06 — Notificações

`Summit.Api/NotificationHelper.cs` (mesmo estilo do `Audit` helper):

```csharp
public static Task Notify(ApiDbContext db, string userId, NotificationType type, string message, string? relatedId = null)
{
    db.Notifications.Add(new Notification { Id = $"ntf_{Guid.NewGuid():N}", UserId = userId, Type = type, Message = message, RelatedId = relatedId });
    return Task.CompletedTask;
}
```

**Pontos de chamada a adicionar** (todos já existentes, só ganham uma linha a mais antes do
`SaveChangesAsync` local):

| Evento | Arquivo/método | Notifica quem |
|---|---|---|
| Convite de time recebido | `Program.cs`, `POST /api/teams/{teamId}/invite` | `req.InvitedUserId` |
| Solicitação de entrada aceita/recusada | `CompetitionEndpoints.cs`, `.../join-requests/{id}/accept`/`decline` | `req.UserId` |
| Promovido/rebaixado/transferido | `CompetitionEndpoints.cs`, `/promote`, `/demote`, `/transfer-ownership` | `body.UserId` |
| Check-in aberto | `LifecycleWorker.TickAsync`, no instante `now >= CheckInOpensAt` (novo `if`) | capitão de cada `TournamentTeam` |
| Escalação alterada | `CompetitionEndpoints.cs`, `PUT /api/tournaments/{id}/lineup` | os 5 jogadores da escalação nova, exceto quem fez a alteração |
| Campeonato encerrado | `AdvanceBracketAsync` → `HandleGrandFinalResultAsync` (RF-03) | todo `TournamentTeam.CaptainUserId` do campeonato |
| Badge desbloqueada | `EvaluateBadgesAsync` (RF-05) | o próprio jogador |
| Denúncia resolvida | `POST /api/reports/{id}/resolve` (RF-08) | `report.ReporterId` |

Endpoints de leitura: `GET /api/notifications/{userId}?unreadOnly=`, `POST
/api/notifications/{id}/read`, `POST /api/notifications/{userId}/read-all`.

**Client**: `NotificationRepository` (padrão fino de sempre), `NotificationsViewModel`/`View`
novos; `MainShellViewModel` ganha `UnreadNotificationCount` (poll a cada ~15s via
`DispatcherTimer`, mesmo padrão de `MatchRoomViewModel` — ver `docs/book/05-client-mvvm.md §5.6`)
e um ícone de sino com badge numérico no XAML do shell, `RelayCommand` navegando pra
`NotificationsViewModel`.

## RF-07 — Formato suíço

Diferente de simples/dupla, o suíço **não gera a chave inteira de uma vez** — gera uma rodada,
espera os resultados, gera a próxima. Isso muda onde a lógica mora:

- `LifecycleWorker.GenerateBracket`: novo `if (t.FormatType == TournamentFormat.Swiss)` chama
  `GenerateSwissRound(db, t, teams, roundIndex: 0)` em vez de gerar tudo — cria só a primeira
  rodada (pareamento aleatório entre os inscritos, já que ninguém tem campanha ainda).
- **Pareamento de rodada seguinte** (chamado de dentro de `AdvanceBracketAsync` quando a última
  partida pendente de uma rodada suíça termina, não de `LifecycleWorker`):
  1. Agrupa `TournamentTeam`s ativos (não eliminados, não classificados) por campanha (vitórias -
     derrotas).
  2. Dentro de cada grupo de mesma campanha, pareia aleatoriamente evitando repetir um confronto
     já registrado no campeonato (mantém um `HashSet<(string,string)>` de pares já jogados,
     consultável via `AuditLog` action `"swiss_pairing"` ou uma tabela auxiliar simples — optar
     pela tabela simples: reaproveitar `BracketMatch` existente já registra isso, um `SELECT`
     nas partidas já geradas resolve sem tabela nova).
  3. Se um grupo tem número ímpar de times, um deles é emprestado do grupo adjacente (campanha
     mais próxima) — nunca dá "bye" grátis sem necessidade real.
  4. Desempate por Buchholz (soma das campanhas dos adversários já enfrentados) só é necessário
     na hora de definir classificação final, não no pareamento em si.
- **Critério de parada**: depois de cada rodada, checa por `TournamentTeam`:
  - `Wins >= Tournament.SwissTargetWins` → classificado, `FinalPosition` provisório = ordem de
    classificação; sai do pool de pareamento.
  - `Losses >= Tournament.SwissEliminationLosses` → eliminado, `IsEliminated = true`; sai do pool.
  - Quando não sobra ninguém "ativo" (todo mundo classificado ou eliminado), o campeonato encerra
    (RF-03) — o "campeão" é quem classificou com a melhor campanha (desempate Buchholz).
- `TournamentTeam` precisa de campos derivados de vitórias/derrotas suíças — reaproveitar
  contagem de `BracketMatch` vencidas/perdidas por `Tag` em vez de adicionar coluna nova (evita
  mais uma mudança de schema; é uma query, não um campo persistido).

## RF-08 — Denúncia e moderação

Endpoints (`CompetitionEndpoints.cs`, novo bloco):
- `POST /api/reports` — `record CreateReportBody(string ReporterId, string ReportedUserId,
  string Reason)`; recusa se `ReporterId == ReportedUserId`.
- `GET /api/reports?status=Pending` — sem checagem de `IsModerator` no backend? **Não** — mesmo
  sendo uma ferramenta simples, a regra central de segurança do projeto (`docs/book/03 §3.7`)
  exige checagem no servidor sempre: recebe `moderatorUserId` como query param, confere
  `db.Users.AnyAsync(u => u.Id == moderatorUserId && u.IsModerator)`, senão `Forbid()`.
- `POST /api/reports/{id}/resolve` — `record ResolveReportBody(string ModeratorUserId, bool
  Dismiss, string? Note)`; mesma checagem de `IsModerator`; dispara `Notify(...)` (RF-06) pro
  `ReporterId`.

Client: botão "Denunciar" em `PlayerProfileView` (ao lado de Bloquear, mesmo padrão de
visibilidade — `!IsSelf`), abre um painel inline simples (motivo em texto livre, igual ao padrão
de convite/edição já usado em `TeamView`). Novo `ModerationQueueViewModel`/`View`, só acessível
via navegação direta (sem item de menu — não é uma tela pra todo mundo; entrar por um link direto
é aceitável dado que não há sistema de papéis de UI ainda). Antes de mostrar qualquer dado, a
tela confere `App.UserService.CurrentUser?.IsModerator` e mostra uma mensagem de acesso negado
se não for — checagem de UI de conveniência, a de verdade já está no backend.

## RF-09 — Criação e edição de campeonato

Endpoints em `Program.cs`, bloco `TOURNAMENTS`:
- `POST /api/tournaments` — `record CreateTournamentRequest(string Name, string Description,
  string Region, DateTime StartDate, TournamentFormat FormatType, SeriesFormat Series,
  SeriesFormat FinalSeries, string MapPoolCsv, int MinTeams, int MaxTeams, string Prize, bool
  IsPaidEntry, string EntryFee, string OrganizerUserId)`. Validações: `StartDate > now`,
  `MinTeams <= MaxTeams`, `MinTeams >= 2`, `MapPoolCsv` não vazio (mínimo 3 mapas — regra mínima
  pra um MD1 fazer sentido, ver `BuildSequence` em `docs/book/19 §19.2`), `Status =
  TournamentStatus.Open` na criação.
- `PUT /api/tournaments/{id}` — exige `req.ByUserId == tournament.OrganizerUserId`; recusa se
  `now >= tournament.RegistrationClosesAt` ("dados congelados", igual à regra de inscrição).

Client: `CreateTournamentViewModel`/`View` novo — formulário com os campos acima (reaproveita
`DarkTextBox`/`SmallButton`/`PrimaryButton` do design system existente, mesmo padrão visual de
`TeamView`'s "editar time"). Botão "CRIAR CAMPEONATO" em `TournamentsView`. Edição: botão
"EDITAR" em `TournamentDetailsView`, visível só quando
`App.UserService.CurrentUser?.Id == Tournament.OrganizerUserId && Tournament.IsRegistrationOpen`.

## Correções de regra de negócio (mapeamento exato pro código)

1. **Convite — client vs. API divergentes**: `Services/TeamService.cs`,
   `InviteByNicknameAsync` — trocar `me.TeamRole != TeamRole.Captain && me.TeamRole !=
   TeamRole.ViceCaptain` por só `me.TeamRole != TeamRole.Captain`. `Program.cs`,
   `POST /api/teams/{teamId}/invite` — trocar o `return Results.BadRequest()` genérico por
   `Results.BadRequest("Só o dono pode convidar jogadores.")` quando a causa é cargo (distinto do
   caso "jogador já tem time").
2. **Exclusão de time sem validar campeonato ativo**: `Program.cs`, `DELETE /api/teams/{id}` —
   adicionar checagem `await db.TournamentTeams.AnyAsync(tt => tt.TeamId == id &&
   tt.Tournament!.Status != TournamentStatus.Finished)` antes de excluir; se `true`,
   `Results.BadRequest("Não é possível excluir um time inscrito em campeonato ainda não
   encerrado.")`.
3. **Suíço mentiroso**: `LifecycleWorker.GenerateBracket` — resolvido diretamente pela
   implementação de RF-07 (deixa de cair no `else` de eliminação simples).
4. **Auditoria incompleta**: `Program.cs`, `POST /api/teams/invitations/{id}/accept`/`decline` e
   `POST /api/friends/{id}/accept`/`block` — adicionar chamada a `Audit(...)` em cada um, mesmo
   padrão já usado em todo o resto do arquivo.
5. **`Organizer` como string livre**: resolvido por RF-09 (`OrganizerUserId` novo).
6. **`FinalPosition` nunca escrito**: resolvido por RF-03.

## Resumo de arquivos novos/alterados (visão geral pra `tasks.md`)

**Novos**: `IMatchServerProvider.cs`, `AwsMatchServerProvider.cs`,
`LocalSimulatedMatchServerProvider.cs`, `Models/Notification.cs`, `Models/Report.cs`,
`NotificationHelper.cs`, `Data/NotificationRepository.cs`, `Data/ReportRepository.cs`,
`ViewModels/NotificationsViewModel.cs` + `Views/NotificationsView.xaml`,
`ViewModels/ModerationQueueViewModel.cs` + `Views/ModerationQueueView.xaml`,
`ViewModels/CreateTournamentViewModel.cs` + `Views/CreateTournamentView.xaml`.

**Alterados**: `CompetitionEndpoints.cs` (resultado, avanço, suíço, reports), `LifecycleWorker.cs`
(geração suíça, checagem diária de badge `bd_loyal`, notificação de check-in aberto),
`Program.cs` (registro de DI do provider, endpoints de torneio/notificação/report),
`ApiDbContext.cs` (mapeamento das entidades novas + campos novos), `Models/Bracket.cs`,
`Models/Tournament.cs`, `Models/User.cs`, `Services/TeamService.cs`, `TeamView.xaml`/
`PlayerProfileView.xaml`/`TournamentsView.xaml`/`TournamentDetailsView.xaml`/`MainShellView.xaml`
(botões/telas novas), `App.xaml` (DataTemplates novos), `database/schema.sql` (regenerar dump ao
final).
