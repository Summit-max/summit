[← Sumário](00-indice.md)

# Capítulo 8 — Services e Repositórios do Client

Este capítulo cataloga cada repositório (`Data/*.cs`) e service (`Services/*.cs`) do client,
descrevendo a responsabilidade de cada método. O padrão geral (repositório = tradução HTTP fina,
service = regra de aplicação por cima) já foi explicado em
[§3.2-3.3](03-padroes-projeto.md#32-repository-pattern-client--uma-classe-http-por-área); aqui o
foco é o "o quê", não o "por quê".

## 8.1 Repositórios (`Data/`)

### `UserRepository`
| Método | Endpoint | Notas |
|---|---|---|
| `GetBySteamIdAsync(steamId)` | `GET /api/users/by-steam/{steamId}` | usado no login/restauração de sessão |
| `UpsertFromSteamAsync(steamId, nickname, avatarUrl)` | `POST /api/users/steam-login` | cria ou atualiza; usa `PostRequiredAsync` (falha é fatal para o login) |
| `UpdateAsync(user)` | `PUT /api/users/{id}` | manda o `User` inteiro — a API sobrescreve campo a campo |
| `SearchAsync(query)` | `GET /api/users/search?q=` | busca por substring de nickname, usado em convites/amizades |
| `GetByIdAsync(userId)` | `GET /api/users/{id}` | |
| `GetByNicknameAsync(nickname)` | `GET /api/users/by-nickname/{nickname}` | usado para convidar/adicionar por nome exato |

### `TeamRepository`
Cobre criação, convite, entrada por solicitação, cargos e edição/exclusão — é o repositório mais
extenso do projeto porque `Team` é o agregado com mais operações administrativas (ver
[Capítulo 13](13-feature-times.md) para o fluxo completo de cada uma). Métodos notáveis:
- `CreateAsync`/`InviteAsync`/`AcceptInvitationAsync`/`DeclineInvitationAsync`/`LeaveTeamAsync`
- `GetJoinRequestsAsync`/`CreateJoinRequestAsync`/`AcceptJoinRequestAsync`/`DeclineJoinRequestAsync`
- `PromoteAsync`/`DemoteAsync`/`TransferOwnershipAsync`
- `UpdateAsync`/`DeleteAsync`/`KickAsync`

Todos os métodos que mudam algo recebem um `byUserId`/`ownerId` explícito — o repositório não
sabe "quem é o usuário atual", isso é responsabilidade de quem chama (o `TeamService`, que lê
`App.UserService.CurrentUser`).

### `TournamentRepository`
| Método | Endpoint |
|---|---|
| `GetAllAsync()` | `GET /api/tournaments` |
| `GetByIdAsync(id)` | `GET /api/tournaments/{id}` |
| `RegisterTeamAsync(tournamentId, teamId)` | `POST /api/tournaments/{id}/register` |
| `IsTeamRegisteredAsync(tournamentId, teamId)` | `GET /api/tournaments/{id}/registered/{teamId}` |
| `CheckInAsync(tournamentId, teamId, byUserId)` | `POST /api/tournaments/{id}/checkin` |
| `UpdateLineupAsync(tournamentId, teamId, byUserId, playerIds, captainUserId)` | `PUT /api/tournaments/{id}/lineup` (via `PutWithMessageAsync`, ver [Capítulo 17](17-feature-escalacao.md)) |

### `FriendshipRepository`
Além do CRUD óbvio de amizade, define um enum local só para representar a relação entre dois
usuários vista do client:

```csharp
public enum RelationStatus { None, Friends, OutgoingPending, IncomingPending, Blocked }

public async Task<RelationStatus> GetRelationAsync(string viewerId, string otherId)
{
    var raw = await ApiClient.GetAsync<string>($"/api/friends/relation?viewerId={viewerId}&otherId={otherId}");
    return Enum.TryParse<RelationStatus>(raw, out var status) ? status : RelationStatus.None;
}
```

Repare que a API devolve a relação como **string simples** (`"Blocked"`, `"Friends"`, etc. — ver
`GET /api/friends/relation` em `Program.cs`), e o client faz `Enum.TryParse` sobre ela. Esse é um
dos poucos lugares do sistema onde o contrato entre client e API é uma string "mágica" em vez de
um tipo compartilhado — funciona porque os nomes dos valores são idênticos nos dois lados por
convenção, mas não há nada no compilador garantindo isso (se alguém renomeasse um valor só do
lado da API, o `TryParse` falharia silenciosamente e cairia no `RelationStatus.None` default, sem
erro nenhum). `BlockAsync`/`UnblockAsync` completam o conjunto (`UnblockAsync` reaproveita o mesmo
`DELETE /api/friends` do "remover amizade" — ver [Capítulo 14](14-feature-amizades.md)).

### `MatchRepository`, `VetoRepository`, `AuditRepository`, `BadgeRepository`
Repositórios pequenos e diretos — cada um cobre um único domínio de leitura (mais `VetoRepository.ActAsync`
para agir no veto). Sem lógica além da tradução para URL.

## 8.2 Services (`Services/`)

### `TeamService` (implementa `ITeamService`)
A camada de regra por cima de `TeamRepository`. Quase todo método começa checando
`App.UserService.CurrentUser` e seu cargo antes de delegar ao repositório — ver o exemplo completo
em [§3.3](03-padroes-projeto.md#33-service-layer-client--regra-de-aplicação-acima-do-repositório).
Um detalhe que se repete bastante: vários métodos terminam chamando `ReloadCurrentUserAsync()`:

```csharp
private async Task ReloadCurrentUserAsync()
{
    var me = App.UserService.CurrentUser;
    if (me == null) return;
    var fresh = await _userRepo.GetByIdAsync(me.Id);
    if (fresh != null) App.UserService.SetCurrentUser(fresh);
}
```

Isso existe porque `App.UserService.CurrentUser` é um **snapshot em memória** do usuário — depois
de uma ação que muda algo sobre o próprio usuário atual do ponto de vista do servidor (entrar em
um time, ser promovido, sair de um time, o time ser excluído), esse snapshot local fica
desatualizado até algo buscar o `User` de novo na API e substituir a referência com
`SetCurrentUser`. Qualquer método novo em `TeamService` que altere `TeamId`/`TeamRole` do usuário
atual **precisa** terminar chamando isso (ou equivalente), senão a UI continua mostrando o estado
antigo (ex. ainda mostrando o botão "criar time" depois de já ter entrado em um).

### `TournamentService` (implementa `ITournamentService`)
Pequeno — orquestra `TournamentRepository` e adiciona o preenchimento de `IsRegistered` via
`MarkRegisteredAsync` (ver [§7.5](07-client-models.md#75-isregistered-um-campo-calculado-à-mão-pelo-service-não-pela-api)).

### `UserService` (implementa `IUserService`)
O "estado de sessão" do app inteiro — guarda `CurrentUser` e expõe o evento
`CurrentUserChanged` que o resto da UI escuta (ver
[§5.4](05-client-mvvm.md#54-reatividade-a-eventos-globais-troca-de-usuário-navegação)). Os
métodos `UpdateBioAsync`/`UpdateRoleAsync`/`UpdateAvatarUrlAsync`/`UpdateCountryAsync` seguem
todos o mesmo padrão: mutam `CurrentUser` localmente **primeiro** (otimista) e só depois chamam
`App.UserRepository.UpdateAsync(CurrentUser)` para persistir — não há tratamento de erro nesse
caminho (se o `PUT` falhar silenciosamente, o client já mostra o valor novo mesmo assim, porque
`ApiClient.PutAsync` não lança exceção). Isso é aceitável para campos de perfil de baixo risco,
mas é bom saber que esse `Update*Async` **não confirma que a API de fato salvou** antes de a UI
já considerar a mudança como feita.

### `StatsService` (implementa `IStatsService`)
Combina dados de dois repositórios (`UserRepository` + `MatchRepository`) para montar um
`PlayerStats` — é o único service que faz uma "junção" client-side não trivial de duas fontes:

```csharp
public async Task<PlayerStats> GetStatsAsync(string userId)
{
    var user = await _userRepo.GetByIdAsync(userId);
    var matches = await _matchRepo.GetRecentForUserAsync(userId, 12);
    var recent = matches.Select(m => { /* extrai o MatchPlayer do userId dentro de cada Match */ }).ToList();
    return new PlayerStats { /* campos agregados de `user` + RecentPerformance = recent */ };
}
```

### `BadgeService` (implementa `IBadgeService`), `RankingService`
Pequenos, sem lógica de negócio própria além de decidir qual endpoint chamar
(`GetAllForCurrentUserAsync` chama a variante "com estado" quando há usuário logado, senão a
variante "catálogo puro"). `RankingService` não tem interface (ver nota em
[§3.3](03-padroes-projeto.md#33-service-layer-client--regra-de-aplicação-acima-do-repositório)
sobre a inconsistência de quais services têm interface).

## 8.3 Por que algumas interfaces existem e outras não — o que isso significa na prática

As interfaces em `Services/Interfaces/` (`ITeamService`, `ITournamentService`, `IUserService`,
`IStatsService`, `IBadgeService`) **não são usadas para injeção de dependência** (não há container
de DI no client, ver [§3.4](03-padroes-projeto.md#34-instâncias-estáticas-ao-invés-de-injeção-de-dependência-client)).
Elas existem hoje como documentação de contrato — e, notavelmente, **estão incompletas**: por
exemplo, `ITeamService` declara só `GetTeamAsync` e `CreateTeamAsync`, mas a classe `TeamService`
implementa mais de uma dezena de outros métodos públicos (`PromoteAsync`, `KickMemberAsync`, etc.)
que não estão na interface. Isso funciona porque em todo o código do projeto, o acesso é sempre
via `App.TeamService` (tipo concreto `TeamService`), nunca via uma variável do tipo `ITeamService`
— então o compilador nunca reclama dos métodos "extras" que a interface não lista. Se algum dia o
projeto passar a usar as interfaces de fato (por exemplo, para permitir testes com mock), esse
descompasso precisaria ser resolvido primeiro.
