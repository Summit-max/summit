[← Sumário](00-indice.md)

# Capítulo 23 — Referência: Classes da API

Dicionário de consulta rápida de toda classe do projeto `Summit.Api/Summit.Api.csproj`. Os
`Models/*.cs` são os mesmos catalogados em [§22.1](22-referencia-classes-client.md#221-models-modelscs--compartilhados-com-a-api)
— não repetidos aqui.

## 23.1 Arquivos e responsabilidades

| Arquivo | Papel | Ver também |
|---|---|---|
| `Program.cs` | Bootstrap (DI, DbContext, workers) + endpoints de Users/Teams/Tournaments/Matches/Friends/Badges/Ranking + todos os `/api/debug/*` | [Cap. 9](09-backend-api-program.md), [§10.1-10.6](10-backend-endpoints.md) |
| `ApiDbContext.cs` | Mapeamento EF Core completo — enums→int, `.Ignore()` de computed properties, `DeleteBehavior` por relacionamento | [Cap. 4](04-banco-dados.md#43-apidbcontext--como-o-mapeamento-funciona) |
| `CompetitionEndpoints.cs` | Endpoints das especificações funcionais: solicitações de entrada, cargos, check-in, escalação, veto, auditoria + helpers de regra (`IsOwner`, `ValidateLineupAsync`, `BuildSequence`, `Audit`) | [§10.7-10.11](10-backend-endpoints.md), Caps. 13, 16, 17, 19 |
| `LifecycleWorker.cs` | Motor do ciclo de vida do campeonato (tick 20s): fecha check-in, gera chave, inicia campeonato, abre vetos, roda bots | [§11.2](11-backend-services-workers.md#112-lifecycleworker--estrutura-de-código), Caps. 16, 18, 19 |
| `MatchServerService.cs` | Provisionamento AWS (cold-boot + pool) + atribuição via RCON de alto nível | [§11.3](11-backend-services-workers.md#113-matchserverservice--a-peça-central-da-lógica-de-servidor), Cap. 20 |
| `PoolManagerService.cs` | Mantém o pool de servidores quentes (tick 30s): repõe, confirma, libera | [§11.4](11-backend-services-workers.md#114-poolmanagerservice--três-sub-rotinas-por-tick), Cap. 20 |
| `RconClient.cs` | Cliente do protocolo Source RCON escrito à mão | [§11.5](11-backend-services-workers.md#115-rconclient--implementação-do-protocolo-source-rcon) |
| `ServerProvisionPoller.cs` | Acompanha instâncias de cold-boot (tick 10s) | [§11.6](11-backend-services-workers.md#116-serverprovisionpoller--o-irmão-mais-simples-do-poolmanagerservice) |
| `SeedData.cs` | Dados de demonstração (usuários, times, campeonatos, chave, partidas, badges, amizades) | [§11.7](11-backend-services-workers.md#117-seeddata--como-os-dados-de-demonstração-são-estruturados) |

## 23.2 `ApiDbContext` — `DbSet`s

```csharp
DbSet<User> Users; DbSet<Team> Teams; DbSet<TeamInvitation> TeamInvitations;
DbSet<Friendship> Friendships; DbSet<Tournament> Tournaments; DbSet<TournamentTeam> TournamentTeams;
DbSet<BracketRound> BracketRounds; DbSet<BracketMatch> BracketMatches;
DbSet<Match> Matches; DbSet<MatchPlayer> MatchPlayers;
DbSet<Badge> Badges; DbSet<UserBadge> UserBadges; DbSet<TeamJoinRequest> TeamJoinRequests;
DbSet<TournamentLineupPlayer> TournamentLineupPlayers;
DbSet<VetoSession> VetoSessions; DbSet<VetoStep> VetoSteps;
DbSet<AuditLog> AuditLogs; DbSet<PoolServer> PoolServers;
```

Praticamente um-para-um com as tabelas do [Capítulo 4](04-banco-dados.md#41-visão-geral-do-schema):
o dump `schema.sql` tem 17, e o único `DbSet` sem tabela correspondente nesse dump é
`PoolServers` — criada depois da última exportação do arquivo, via `CREATE TABLE` manual direto
no MySQL (o mesmo motivo, e o mesmo risco de desatualização, descrito na ressalva em
[§4.7](04-banco-dados.md#47-como-recriar-o-banco-do-zero-fluxo-de-dev) sobre esse
arquivo poder ficar desatualizado em relação ao `ApiDbContext` real).

## 23.3 Helpers estáticos de `CompetitionEndpoints` — referência rápida

| Método | Assinatura resumida | Uso |
|---|---|---|
| `IsOwner` | `(db, teamId, userId) → bool` | Confere se `userId` é `Captain` do `teamId` |
| `IsOwnerOrSub` | `(db, teamId, userId) → bool` | Confere se `userId` é `Captain` ou `ViceCaptain` |
| `ValidateLineupAsync` | `(db, tournamentId, teamId, playerIds, captainUserId, ignoreTournamentTeamId, requiredCount) → string?` | Valida escalação; `null` = válida — [Cap. 17](17-feature-escalacao.md#173-validatelineupasync--a-validação-central-reusada-em-dois-lugares) |
| `BuildSequence` | `(series, poolSize) → List<(VetoActionType, int side)>` | Sequência de bans/picks por formato — [Cap. 19](19-feature-veto.md#192-a-sequência-de-bansPicks--buildsequence) |
| `RemainingMaps` | `(session) → List<string>` | Mapas ainda não usados na sessão de veto |
| `Audit` | `(db, action, actor, target, teamId, tournamentId, oldValue, newValue, reason) → Task` | Grava log de auditoria (não salva sozinho) — [§3.8](03-padroes-projeto.md#38-auditoria-como-efeito-colateral-padronizado) |

## 23.4 Métodos públicos de `MatchServerService` — referência rápida

| Método | Categoria | Uso |
|---|---|---|
| `ProvisionAsync(matchId)` | Provisionamento cold-boot | Fallback quando o pool não tem servidor livre |
| `ProvisionPoolServerAsync()` | Provisionamento pool | Chamado por `PoolManagerService.TopUpAsync` |
| `LaunchBareInstanceAsync()` | Provisionamento manual | Sem User Data — para configuração manual via SSH (debug) |
| `TryAssignFromPoolAsync(matchId, map, password)` | RCON | Tenta atribuir servidor `Idle`; `false` se não houver nenhum |
| `ReleaseToPoolAsync(db, poolServer)` | RCON | Reseta mapa/senha, marca `Idle` |
| `CheckPoolServerAliveAsync(poolServer)` | RCON | Confirma que o CS2 responde (`status`) |
| `GetHumanPlayerCountAsync(poolServer)` | RCON | Extrai contagem de humanos via regex sobre `status` |
| `PollAsync(db, match)` | Polling AWS | Grava IP quando cold-boot fica `Running` |
| `PollPoolServerAsync(poolServer)` | Polling AWS | Idem, para instância do pool |
| `TerminateAsync(id)` / `StopAsync(id)` | Controle AWS | Termina/para uma instância manualmente |

## 23.5 Rotas — índice completo por prefixo

Para a tabela detalhada de cada rota (regra de negócio embutida), ver [Capítulo 10](10-backend-endpoints.md).
Índice de prefixos, para localizar rapidamente onde procurar:

| Prefixo | Arquivo | Seção |
|---|---|---|
| `/api/users/*` | `Program.cs` | [§10.1](10-backend-endpoints.md#101-users-programcs) |
| `/api/teams/*` (CRUD básico, convites, saída) | `Program.cs` | [§10.2](10-backend-endpoints.md#102-teams-programcs) |
| `/api/teams/*/join-requests`, `/promote`, `/demote`, `/transfer-ownership` | `CompetitionEndpoints.cs` | [§10.7](10-backend-endpoints.md#107-competitionendpointscs--solicitações-de-entrada), [§10.8](10-backend-endpoints.md#108-competitionendpointscs--cargos) |
| `/api/tournaments/*` (listagem, registro) | `Program.cs` | [§10.3](10-backend-endpoints.md#103-tournaments-programcs) |
| `/api/tournaments/*/checkin`, `/close-checkin`, `/lineup` | `CompetitionEndpoints.cs` | [§10.9](10-backend-endpoints.md#109-competitionendpointscs--check-in-e-escalação) |
| `/api/matches/*` (leitura) | `Program.cs` | [§10.4](10-backend-endpoints.md#104-matches-programcs) |
| `/api/matches/by-bracket/*` | `CompetitionEndpoints.cs` | [§10.10](10-backend-endpoints.md#1010-competitionendpointscs--veto) |
| `/api/friends/*` | `Program.cs` | [§10.5](10-backend-endpoints.md#105-friends-programcs) |
| `/api/badges/*`, `/api/ranking/*` | `Program.cs` | [§10.6](10-backend-endpoints.md#106-badges-e-ranking-programcs) |
| `/api/veto/*` | `CompetitionEndpoints.cs` | [§10.10](10-backend-endpoints.md#1010-competitionendpointscs--veto) |
| `/api/audit` | `CompetitionEndpoints.cs` | [§10.11](10-backend-endpoints.md#1011-competitionendpointscs--auditoria) |
| `/api/debug/*` | `Program.cs` | [§9.4](09-backend-api-program.md#94-os-endpoints-de-diagnóstico-apidebug) |
