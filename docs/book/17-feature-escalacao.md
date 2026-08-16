[← Sumário](00-indice.md)

# Capítulo 17 — Escalação (Lineup)

## 17.1 O que é a escalação e por que é separada do elenco do time

`docs/espec-times.md §2` é explícito: **não há limite de 5 jogadores no elenco** de um time — um
time pode ter 8, 10, 15 membros. O limite de 5 é uma regra *por campeonato*: a **escalação**
(`TournamentTeam.Lineup`, uma lista de `TournamentLineupPlayer`) é quem, dentre o elenco todo,
realmente representa o time naquela competição específica. Um jogador pode estar no elenco do
time e nunca ter sido escalado para um campeonato específico; times diferentes campeonatos podem
(em teoria, se o elenco for grande o suficiente) ter escalações completamente diferentes de
jogadores do mesmo elenco.

Junto disso vem o **capitão da escalação** (`TournamentTeam.CaptainUserId`) — um papel
*independente do cargo no time* (ver a distinção já estabelecida em
[§13.1](13-feature-times.md#131-cargos--o-vocabulário)). Qualquer um dos 5 selecionados pode ser
o capitão da escalação, mesmo que seja um `Member` comum sem nenhum privilégio administrativo no
time. Esse papel é quem, segundo `docs/espec-times.md §16`, responde pela equipe "na competição":
notificações, presença, vetos, dados do servidor, contato com admin, confirmação de resultados —
e, como visto em [§16.3](16-feature-campeonatos-inscricao.md#163-check-in), tem autorização
explícita para confirmar o check-in mesmo sem ser dono/sublíder.

## 17.2 Janela de edição: `CanEditLineup`

```csharp
// Models/Tournament.cs
public bool CanEditLineup => IsRegistered && DateTime.UtcNow < CheckInOpensAt;
```

A escalação pode ser alterada livremente **a qualquer momento entre a inscrição e a abertura do
check-in** (T-1h) — depois disso, fica bloqueada. `IsRegistered` (ver
[§7.5](07-client-models.md#75-isregistered-um-campo-calculado-à-mão-pelo-service-não-pela-api))
precisa estar `true` para o botão de escalação sequer aparecer — não faz sentido editar a
escalação de um campeonato em que o time nem está inscrito. O endpoint replica exatamente essa
mesma janela de tempo do lado do servidor (nunca confia só no botão estar desabilitado no client):

```csharp
// PUT /api/tournaments/{id}/lineup
if (DateTime.UtcNow >= t.CheckInOpensAt)
    return Results.BadRequest("Escalação bloqueada: o check-in já abriu.");
```

## 17.3 `ValidateLineupAsync` — a validação central, reusada em dois lugares

```csharp
// Summit.Api/CompetitionEndpoints.cs
public static async Task<string?> ValidateLineupAsync(
    ApiDbContext db, string tournamentId, string teamId,
    List<string> playerIds, string? captainUserId, string? ignoreTournamentTeamId,
    int requiredCount = 5)
{
    var ids = playerIds.Distinct().ToList();
    if (ids.Count != requiredCount) return $"A escalação precisa de exatamente {requiredCount} jogadores.";

    var inTeam = await db.Users.CountAsync(u => ids.Contains(u.Id) && u.TeamId == teamId);
    if (inTeam != requiredCount) return "Todos os jogadores da escalação precisam pertencer ao time.";

    if (string.IsNullOrEmpty(captainUserId) || !ids.Contains(captainUserId))
        return "O capitão da escalação deve estar entre os 5 selecionados.";

    // nenhum jogador pode representar outro time no mesmo campeonato
    var conflict = await db.TournamentLineupPlayers
        .Include(lp => lp.TournamentTeam)
        .AnyAsync(lp => ids.Contains(lp.UserId)
                     && lp.TournamentTeam!.TournamentId == tournamentId
                     && lp.TournamentTeamId != ignoreTournamentTeamId);
    if (conflict) return "Um dos jogadores já está inscrito por outro time neste campeonato.";

    return null;   // null = válido
}
```

Essa função é chamada de **dois lugares**: dentro de `POST /api/tournaments/{id}/register`
(validando a escalação inicial, com `ignoreTournamentTeamId = null` porque ainda não existe
`TournamentTeam` para esse time nesse campeonato) e dentro de `PUT /api/tournaments/{id}/lineup`
(validando uma troca posterior, passando o `TournamentTeam.Id` atual como
`ignoreTournamentTeamId` — para que a checagem de conflito não acuse o próprio time de estar "em
conflito consigo mesmo" ao comparar a escalação nova contra a lista de jogadores já escalados
*por qualquer time* naquele campeonato, incluindo os que o próprio time já tinha escalado antes).

A convenção de retorno (`string?` — `null` significa válido, qualquer outra coisa é a mensagem de
erro a mostrar) é o padrão que o `PutWithMessageAsync` do client foi desenhado para consumir (ver
[§3.6](03-padroes-projeto.md#36-cliente-http-tolerante-a-falha-client--api)) — a mensagem
retornada aqui é literalmente o texto que aparece na tela do usuário.

### 17.3.1 O "modo alpha": 5 é o alvo, mas times pequenos usam o elenco inteiro

```csharp
var required = Math.Min(5, team.Members.Count);
```

Times com menos de 5 membros no elenco (comum em ambientes de teste/desenvolvimento com poucos
usuários cadastrados) não ficam impedidos de se inscrever — a exigência cai para "o elenco
inteiro", não trava em "exatamente 5". Isso é chamado de "modo alpha" no comentário do código
(`CompetitionEndpoints.ValidateLineupAsync`), reconhecendo que é uma acomodação temporária para
o estágio atual de poucos usuários, não a regra final de produto (que é sempre 5, conforme
CS2 padrão).

## 17.4 A tela: `LineupViewModel` / `LineupView`

Carrega o elenco completo do time e pré-marca a seleção/capitão a partir do que **já veio
embutido** no `Tournament` carregado (`TournamentTeam.Lineup`/`CaptainUserId`) — não há um
endpoint de leitura dedicado para "buscar a escalação atual", porque essa informação já faz parte
da árvore que `GET /api/tournaments/{id}` devolve:

```csharp
private async Task LoadAsync()
{
    var team = await _teamRepo.GetByIdAsync(_teamId);
    var tournament = await _tourRepo.GetByIdAsync(_tournamentId);
    var tt = tournament?.TournamentTeams.FirstOrDefault(x => x.TeamId == _teamId);
    var currentLineupIds = tt?.Lineup.Select(l => l.UserId).ToHashSet() ?? new HashSet<string>();

    RequiredCount = Math.Min(5, team?.Members.Count ?? 0);
    Members = (team?.Members ?? new()).Select(u => new LineupMemberItem
    {
        User = u,
        IsSelected = currentLineupIds.Contains(u.Id),
        IsCaptainChoice = tt?.CaptainUserId == u.Id
    }).ToList();
}
```

Seleção é por clique/toggle (`ToggleSelect`), limitada a `RequiredCount` — tentar selecionar um
sexto jogador quando já há 5 simplesmente não faz nada (`if (!item.IsSelected && SelectedCount >=
RequiredCount) return;`). Desmarcar um jogador que era o capitão escolhido automaticamente também
limpa a escolha de capitão daquele jogador (`if (!item.IsSelected) item.IsCaptainChoice = false;`)
— evitando o estado inconsistente de "capitão escolhido que não está mais na escalação".
`SetCaptain` só permite escolher capitão entre quem já está selecionado
(`if (item == null || !item.IsSelected) return;`).

Salvar (`SaveCommand`) faz uma validação de UI rápida antes mesmo de chamar a API (contagem exata,
capitão escolhido) e só então chama `TournamentRepository.UpdateLineupAsync`, mostrando a
mensagem de erro exata que a API devolver via `PutWithMessageAsync` se algo passar da validação
local mas falhar no servidor (por exemplo, um conflito de "jogador já escalado por outro time" que
o client não tem como prever sozinho, porque não tem visibilidade sobre a escalação de outros
times).

## 17.5 Ligação com o resto do sistema

A escalação confirmada é o que a checagem de check-in revalida (ver
[§16.3](16-feature-campeonatos-inscricao.md#163-check-in)) e é também, semanticamente, quem
"representa o time" durante o veto e a partida (embora hoje o veto em si identifique os lados só
pela `Tag` do time, não pela lista de jogadores da escalação — ver
[Capítulo 19](19-feature-veto.md)). A escalação não tem nenhuma relação direta com a *geração* da
chave (Capítulo 18) — a chave é montada por time (via `Seed`), não por jogador.
