[← Sumário](00-indice.md)

# Capítulo 10 — Endpoints por Domínio

Este capítulo é referência de consulta rápida: toda rota HTTP exposta pela API, o que ela faz e
qualquer regra de negócio não óbvia embutida nela. Para o *fluxo de produto* completo de cada
área (não só a rota isolada), veja o capítulo de feature correspondente na Parte V.

## 10.1 Users (`Program.cs`)

| Rota | Regra notável |
|---|---|
| `POST /api/users/steam-login` | upsert por `SteamId`; se não existe, cria com `Level=1`, `Rank="Unranked"`; se existe, só atualiza nick/avatar se vierem não-vazios e sempre atualiza `LastLoginAt` |
| `GET /api/users/{id}` | inclui `Team` + `Team.Members` |
| `GET /api/users/by-steam/{steamId}` | idem, usado no login |
| `GET /api/users/by-nickname/{nickname}` | comparação case-insensitive (`.ToLower()`) |
| `GET /api/users/search?q=` | substring case-insensitive no nickname, limita a 20 resultados |
| `PUT /api/users/{id}` | sobrescreve **todos** os campos com o que veio no corpo (não é um PATCH parcial — o client precisa mandar o objeto completo) |

## 10.2 Teams (`Program.cs`)

| Rota | Regra notável |
|---|---|
| `GET /api/teams` | ordenado por `Elo` decrescente |
| `GET /api/teams/{id}` / `GET /api/teams/by-tag/{tag}` | |
| `POST /api/teams` | cria o time e já promove o `captainId` a `TeamRole.Captain` na mesma transação |
| `PUT /api/teams/{id}` | exige `IsOwner`; audita `team_edited` |
| `DELETE /api/teams/{id}` | exige `IsOwner`; **libera todos os membros** (`TeamId=null`) antes de remover o time; audita `team_deleted` |
| `POST /api/teams/{teamId}/kick` | exige `IsOwner`; recusa se `UserId == ByUserId` ("o dono não pode remover a si mesmo"); audita `member_kicked` |
| `GET /api/teams/invitations/{userId}` | só convites `Pending`, inclui `Team.Members` e `InvitedBy` |
| `POST /api/teams/{teamId}/invite` | exige que quem convida seja `Captain` do próprio time; recusa se o alvo já tem time; se já existe convite pendente idêntico, devolve o existente em vez de duplicar |
| `POST /api/teams/invitations/{id}/accept` | ao aceitar, **cancela automaticamente** qualquer outro convite pendente que o mesmo jogador tinha de outros times |
| `POST /api/teams/invitations/{id}/decline` | |
| `POST /api/teams/leave/{userId}` | ver lógica completa de transferência automática em [§10.2.1](#1021-saída-do-dono-a-rota-mais-elaborada-do-domínio-de-times) |

### 10.2.1 Saída do dono: a rota mais elaborada do domínio de Times

`POST /api/teams/leave/{userId}` implementa a regra de "o time nunca fica sem dono"
(`docs/espec-times.md §12-13`, ver [Capítulo 13](13-feature-times.md)):

```csharp
if (user.TeamRole == TeamRole.Captain)
{
    var others = await db.Users.Where(u => u.TeamId == teamId && u.Id != userId).ToListAsync();
    if (others.Count == 0)
    {
        // último membro: o time inteiro é excluído
    }
    else
    {
        // ordem: sublíder mais antigo → membro mais antigo → id (desempate determinístico)
        var newOwner = others
            .OrderByDescending(u => u.TeamRole == TeamRole.ViceCaptain)
            .ThenBy(u => u.TeamJoinedAt ?? DateTime.MaxValue)
            .ThenBy(u => u.Id)
            .First();
        newOwner.TeamRole = TeamRole.Captain;
        team.CaptainId = newOwner.Id;
    }
}
```

O `.ThenBy(u => u.Id)` final existe puramente como **desempate determinístico** — se por algum
motivo dois candidatos empatarem em cargo e data de entrada, a ordenação por `Id` (string)
garante que a mesma entrada sempre produz o mesmo resultado, em vez de depender da ordem
não-garantida que o banco devolveria sem um `ORDER BY` completo.

## 10.3 Tournaments (`Program.cs`)

| Rota | Regra notável |
|---|---|
| `GET /api/tournaments` | ordenado por `Status`, depois `StartDate`; inclui times inscritos + membros de cada time + toda a chave |
| `GET /api/tournaments/{id}` | mesma inclusão completa |
| `POST /api/tournaments/{id}/register` | ver [§10.3.1](#1031-inscrição-a-rota-mais-densa-de-regras-do-projeto) |
| `GET /api/tournaments/{id}/registered/{teamId}` | usado por `TournamentService.MarkRegisteredAsync` (ver [§7.5](07-client-models.md)) |

### 10.3.1 Inscrição: a rota mais densa de regras do projeto

`POST /api/tournaments/{id}/register` encadeia, em ordem, todas estas checagens antes de gravar
qualquer coisa:

1. Idempotência: se o time já está inscrito, devolve `true` sem fazer nada de novo.
2. Campeonato existe.
3. Inscrições ainda abertas: `DateTime.UtcNow < t.RegistrationClosesAt` (fecha automaticamente
   12h antes do início — `docs/espec-campeonatos.md §3`) e `Status == Open`.
4. Ainda há vaga (`count < t.MaxTeams`).
5. Time existe.
6. Quem está inscrevendo é dono ou sublíder do time (`IsOwnerOrSub`) — **só se `ByUserId` for
   informado** (deixa margem para chamadas internas/de teste sem esse parâmetro).
7. Monta a escalação: usa os `PlayerIds` explícitos do corpo, ou — se nenhum vier — cai no
   fallback automático dos "5 membros mais antigos do time" (`OrderBy(TeamJoinedAt).Take(5)`).
8. Valida a escalação inteira (`CompetitionEndpoints.ValidateLineupAsync`, ver
   [Capítulo 17](17-feature-escalacao.md)).
9. Só então grava `TournamentTeam` + `TournamentLineupPlayer`s + audita `team_registered`.

Esse fallback do passo 7 é o que faz a inscrição funcionar mesmo sem nenhuma UI dedicada de
"escolher escalação na hora de inscrever" — o client de hoje sempre chama `register` sem
`PlayerIds` explícitos, deixando a API montar a escalação padrão; o ajuste fino de quem realmente
joga acontece depois, na tela de Escalação (ver [Capítulo 17](17-feature-escalacao.md)), até o
check-in abrir.

## 10.4 Matches (`Program.cs`)

Só leitura — não há criação/edição manual de partida via API pública (partidas nascem
internamente quando um veto termina, ver [Capítulo 19](19-feature-veto.md)).

| Rota | Notas |
|---|---|
| `GET /api/matches/recent?userId=&take=` | filtra por `Players.Any(p => p.UserId == userId)` |
| `GET /api/matches/team/{teamId}?take=` | filtra por `TeamAId` ou `TeamBId` |
| `GET /api/matches/{id}` | inclui `Players.User` (para nick/avatar/level no scoreboard) |

## 10.5 Friends (`Program.cs`)

| Rota | Regra notável |
|---|---|
| `GET /api/friends/{userId}` | união de "sou requester e foi aceito" com "sou addressee e foi aceito" — a direção original não importa mais depois de aceito |
| `GET /api/friends/{userId}/incoming` / `/outgoing` | só `Pending`, na direção certa |
| `GET /api/friends/relation` | devolve string (`"None"`/`"Friends"`/`"OutgoingPending"`/`"IncomingPending"`/`"Blocked"`), checando os dois sentidos do par |
| `POST /api/friends/block` | se já existe uma relação (de qualquer status), **sobrescreve** para `Blocked`; senão cria uma nova linha já `Blocked` |
| `POST /api/friends/request` | recusa se já existe qualquer relação entre os dois (em qualquer sentido) |
| `POST /api/friends/{id}/accept` / `/decline` | exige que quem responde seja o `AddresseeId` da linha |
| `DELETE /api/friends` | remove a linha inteira — usado tanto para "desfazer amizade" quanto para "desbloquear" |

## 10.6 Badges e Ranking (`Program.cs`)

| Rota | Notas |
|---|---|
| `GET /api/badges` | catálogo completo |
| `GET /api/badges/user/{userId}` | só as desbloqueadas, via `join` explícito `UserBadges ⋈ Badges` |
| `GET /api/badges/user/{userId}/all` | catálogo completo, com `IsUnlocked`/`UnlockedAt` preenchidos onde aplicável |
| `GET /api/ranking/players` | top 50 por `Elo`, monta `RankingPlayer` (DTO de ranking, não o `User` completo) |
| `GET /api/ranking/teams` | top 50 por `Elo`, monta `RankingTeam` |

## 10.7 `CompetitionEndpoints.cs` — Solicitações de Entrada

| Rota | Regra notável |
|---|---|
| `POST /api/teams/{teamId}/join-requests` | recusa se o jogador já tem time, ou já existe solicitação pendente dele para esse time |
| `GET /api/teams/{teamId}/join-requests?ownerId=` | exige `IsOwner`, senão `Forbid` |
| `POST /api/teams/join-requests/{id}/accept` | exige `IsOwner`; ao aceitar, cancela outras solicitações pendentes do mesmo jogador para outros times |
| `POST /api/teams/join-requests/{id}/decline` | exige `IsOwner` |
| `POST /api/teams/join-requests/{id}/cancel` | exige que quem cancela seja o próprio autor da solicitação (`req.UserId == body.ByUserId`) — **não usado hoje pelo client** (não existe botão "cancelar minha solicitação" na UI atual), mas a rota existe e funciona |

## 10.8 `CompetitionEndpoints.cs` — Cargos

| Rota | Regra notável |
|---|---|
| `POST /api/teams/{teamId}/promote` | exige `IsOwner`; só promove quem é `Member` (recusa se já é `ViceCaptain`/`Captain`) |
| `POST /api/teams/{teamId}/demote` | exige `IsOwner`; só rebaixa quem é `ViceCaptain` |
| `POST /api/teams/{teamId}/transfer-ownership` | exige `IsOwner`; o antigo dono vira `ViceCaptain` (nunca `Member`) automaticamente |

## 10.9 `CompetitionEndpoints.cs` — Check-in e Escalação

Ver [Capítulo 16](16-feature-campeonatos-inscricao.md) e [Capítulo 17](17-feature-escalacao.md)
para o fluxo completo; referência de rota:

| Rota | Regra notável |
|---|---|
| `POST /api/tournaments/{id}/checkin` | exige que a janela esteja aberta (`CheckInOpensAt <= now < StartDate`); exige que quem confirma seja dono/sublíder do time **ou** o capitão da escalação daquele campeonato especificamente; **revalida os 5 da escalação** antes de confirmar |
| `POST /api/tournaments/{id}/close-checkin` | remove (`NoShow` + `IsEliminated=true`) todo time que não confirmou; chamado hoje pelo `LifecycleWorker` no T-30min, não diretamente pelo client |
| `PUT /api/tournaments/{id}/lineup` | bloqueado a partir de `CheckInOpensAt`; exige dono/sublíder; roda `ValidateLineupAsync` completo antes de substituir a escalação |

## 10.10 `CompetitionEndpoints.cs` — Veto

Ver [Capítulo 19](19-feature-veto.md) para o fluxo completo.

| Rota | Regra notável |
|---|---|
| `POST /api/veto/{bracketMatchId}/start` | idempotente (devolve a sessão existente se já houver); decide `Series` vs `FinalSeries` checando se o nome da rodada contém "FINAL" |
| `GET /api/veto/{bracketMatchId}` | devolve sessão + mapas restantes + próxima ação esperada (`next`) |
| `POST /api/veto/{bracketMatchId}/action` | valida turno exato, valida que o mapa está disponível; ao completar a sequência, cria o "decider" automaticamente e dispara a criação da sala (`Match`) + tentativa de atribuição via pool |
| `GET /api/matches/by-bracket/{bracketMatchId}` | busca a sala pelo `BracketMatchId` |

## 10.11 `CompetitionEndpoints.cs` — Auditoria

| Rota | Notas |
|---|---|
| `GET /api/audit?teamId=&tournamentId=&take=` | filtro opcional por qualquer um dos dois; ordenado por `CreatedAt` decrescente |

## 10.12 Padrão de resposta HTTP usado nas rotas de ação

Três estilos coexistem deliberadamente, dependendo de quão informativa a falha precisa ser para
o usuário (ver a tabela de métodos do `ApiClient` em
[§6.3](06-client-navegacao-api.md#63-apiclient--referência-completa-dos-métodos) para o
espelho do lado client):

- **`Results.Ok(true/false)`** — quando "não deu" é um resultado normal do domínio, sem motivo
  específico a comunicar (ex. `RegisterTeamRequest` recusado por falta de vaga).
- **`Results.BadRequest(string)`** — quando existe um motivo específico que vale a pena mostrar
  ao usuário (ex. `"A escalação precisa de exatamente 5 jogadores."`). O client só consegue ler
  esse texto através de `PutWithMessageAsync`; se um endpoint novo precisar comunicar um motivo
  assim, ele deve ser chamado do client via esse método, não `PostBoolAsync`/`GetAsync`.
- **`Results.Forbid()`** — quando a falha é de permissão (não é dono, não é quem deveria agir).
  Vira um `403` HTTP puro, sem corpo — do lado do client, isso hoje aparece simplesmente como
  "falso"/falha genérica, porque nenhum dos métodos de `ApiClient` distingue um `403` de outro
  erro qualquer de forma especial.
