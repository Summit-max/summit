[← Sumário](00-indice.md)

# Capítulo 22 — Referência: Classes do Client

Dicionário de consulta rápida de toda classe do projeto `Summit.csproj`. Para o *porquê* de cada
uma, veja o capítulo de feature ou de fundação linkado na coluna "Ver também".

## 22.1 Models (`Models/*.cs`) — compartilhados com a API

| Classe/Enum | Arquivo | Papel | Ver também |
|---|---|---|---|
| `TeamRole`, `FriendshipStatus`, `TeamInvitationStatus`, `MatchStatus` | `Enums.cs` | Enums de estado usados em vários agregados | [Cap. 4](04-banco-dados.md), [Cap. 13](13-feature-times.md) |
| `User` | `User.cs` | Conta do jogador + estatísticas agregadas + vínculo com time | [Cap. 4](04-banco-dados.md#44-tabela-por-tabela) |
| `Team`, `TeamInvitation` | `Team.cs` | Time e convites pendentes | [Cap. 13](13-feature-times.md) |
| `TournamentFormat`, `SeriesFormat`, `CheckInStatus`, `VetoActionType`, `VetoSession`, `VetoStep`, `JoinRequestStatus`, `TeamJoinRequest`, `TournamentLineupPlayer`, `AuditLog`, `VetoState`, `VetoNext` | `Competition.cs` | O arquivo mais denso de enums/DTOs de regra — veto, escalação, solicitações, auditoria | [Cap. 15](15-feature-auditoria.md), [Cap. 17](17-feature-escalacao.md), [Cap. 19](19-feature-veto.md) |
| `TournamentStatus`, `Tournament`, `TournamentTeam`, `TournamentTeamEntry` | `Tournament.cs` | Configuração de campeonato, inscrição de time, DTO de listagem por time | [Cap. 16](16-feature-campeonatos-inscricao.md) |
| `BracketMatchStatus`, `BracketSide`, `BracketRound`, `BracketMatch`, `TournamentTeamEntry` | `Bracket.cs` | Estrutura da chave | [Cap. 18](18-feature-bracket.md) |
| `ServerProvisionState`, `Match`, `MatchPlayer` | `Match.cs` | Sala/resultado de partida + scoreboard | [Cap. 19](19-feature-veto.md), [Cap. 20](20-feature-pool-servidores.md) |
| `Friendship`, `UserBadge` | `Friendship.cs` | Amizade/bloqueio; junção usuário↔badge | [Cap. 14](14-feature-amizades.md) |
| `Badge` | `Badge.cs` | Catálogo de conquistas | [Cap. 21](21-feature-pos-partida-gaps.md) |
| `PlayerStats`, `RecentPerformance` | `PlayerStats.cs` | DTO agregado (não é tabela própria — montado a partir de `User`+`Match`) | [Cap. 8](08-client-services-repos.md) |
| `RankingPlayer`, `RankingTeam` | `RankingEntry.cs` | DTOs de ranking (não são `User`/`Team` completos) | [§10.6](10-backend-endpoints.md#106-badges-e-ranking-programcs) |
| `PoolServerState`, `PoolServer` | `PoolServer.cs` | Estado de um servidor CS2 do pool quente | [Cap. 20](20-feature-pool-servidores.md) |

## 22.2 Commands / Helpers / Components

| Classe | Arquivo | Papel |
|---|---|---|
| `RelayCommand` | `Commands/RelayCommand.cs` | Implementação de `ICommand` para binding de botões/itens |
| `BoolToVisibilityConverter`, `InverseBoolToVisibilityConverter`, `NullOrEmptyToVisibilityConverter`, `BoolToWinLossConverter`, `BoolToBrushConverter` | `Helpers/BoolToVisibilityConverter.cs` | `IValueConverter`s usados em bindings XAML |
| `LevelBadge` (+ `Tier` privado) | `Components/LevelBadge.xaml.cs` | `UserControl` reutilizável: anel colorido + rótulo de "tier" a partir do nível numérico |

## 22.3 Services (`Services/*.cs`)

| Classe | Interface | Papel | Ver também |
|---|---|---|---|
| `ApiClient` (estático) | — | Único ponto de saída HTTP do client | [§6.3](06-client-navegacao-api.md#63-apiclient--referência-completa-dos-métodos) |
| `NavigationService` | — | Pilha de histórico de ViewModels | [§6.1](06-client-navegacao-api.md#61-navigationservice--uma-pilha-não-um-frame) |
| `SessionStore` (estático) | — | Persiste/restaura o `SteamId` da sessão em disco | [§6.4.4](06-client-navegacao-api.md#644-sessionstore--persistência-local-da-sessão) |
| `SteamAuthService` | — | Fluxo de login OpenID, restauração de sessão, modo demo | [§6.4.1](06-client-navegacao-api.md#641-o-fluxo-openid-login-real-via-steam) |
| `SteamConfig` (estático) | — | Onde a chave da Steam Web API é lida (env var ou arquivo local) | [§6.4.3](06-client-navegacao-api.md#643-steamconfig--onde-a-chave-de-api-é-lida) |
| `SteamWebApiClient` | — | Busca nick/avatar reais da Steam, com fallback sem chave | [§6.4.2](06-client-navegacao-api.md#642-steamwebapiclient--nome-e-avatar-reais-com-fallback-sem-chave-de-api) |
| `TeamService` | `ITeamService` | Regra de aplicação sobre `TeamRepository` | [Cap. 13](13-feature-times.md), [§8.2](08-client-services-repos.md) |
| `TournamentService` | `ITournamentService` | Regra sobre `TournamentRepository` + preenche `IsRegistered` | [§7.5](07-client-models.md#75-isregistered-um-campo-calculado-à-mão-pelo-service-não-pela-api) |
| `UserService` | `IUserService` | Estado do usuário atual (`CurrentUser`) + evento de mudança | [§5.4](05-client-mvvm.md#54-reatividade-a-eventos-globais-troca-de-usuário-navegação) |
| `StatsService` | `IStatsService` | Monta `PlayerStats` combinando `User` + `Match` | [§8.2](08-client-services-repos.md) |
| `BadgeService` | `IBadgeService` | Catálogo de badges com/sem estado do usuário | [§8.2](08-client-services-repos.md) |
| `RankingService` | — (sem interface) | Top players/teams | [§8.2](08-client-services-repos.md) |

## 22.4 Data (`Data/*.cs`) — Repositórios

`UserRepository`, `TeamRepository`, `TournamentRepository`, `FriendshipRepository`,
`MatchRepository`, `VetoRepository`, `AuditRepository`, `BadgeRepository` — catalogados
método a método em [§8.1](08-client-services-repos.md#81-repositórios-data).

## 22.5 ViewModels (`ViewModels/*.cs`)

| Classe | Tela | Notas |
|---|---|---|
| `BaseViewModel` | (base) | `INotifyPropertyChanged` + `SetProperty` — ver [§3.1](03-padroes-projeto.md#31-mvvm-no-client-model-view-viewmodel) |
| `SidebarItem`, `MainShellViewModel` | Shell principal | Menu lateral + título dinâmico + minimizar/maximizar/fechar janela |
| `LoginViewModel` | Login | Steam real ou demo |
| `OnboardingViewModel` | Onboarding | País + role no primeiro login — [§12.2](12-feature-conta-login.md#122-onboarding--o-gate-do-primeiro-login) |
| `HomeViewModel` | Home | Partidas recentes + campeonatos em destaque |
| `ProfileViewModel` | Meu Perfil | Edição inline — [§12.3](12-feature-conta-login.md#123-a-tela-de-perfil-profileviewmodel) |
| `PlayerProfileViewModel` | Perfil de outro jogador | Relação de amizade, amigos em comum — [§12.4](12-feature-conta-login.md#124-perfil-de-outro-jogador-playerprofileviewmodel--o-mesmo-dado-contexto-diferente) |
| `SettingsViewModel` | Configurações | Só logout hoje |
| `FilterItem`, `TournamentsViewModel` | Lista de campeonatos | Filtro por status |
| `TournamentDetailsViewModel` | Detalhes do campeonato | Chave (`UpperColumns`/`LowerColumns`/`GrandFinalColumns`) — [Cap. 18](18-feature-bracket.md) |
| `BracketSlotViewModel`, `BracketColumnViewModel`, `BracketLayout` (estático) | (auxiliar da tela acima) | Algoritmo de espaçamento — [§18.4](18-feature-bracket.md#184-bracketlayout--o-algoritmo-de-desenho-genérico) |
| `LineupMemberItem`, `LineupViewModel` | Escalação | [Cap. 17](17-feature-escalacao.md) |
| `TeamViewModel` | Meu Time | O ViewModel mais extenso — [Cap. 13](13-feature-times.md) |
| `TeamProfileViewModel` | Perfil de time (de outro) | Solicitar entrada |
| `JoinRequestsViewModel` | Solicitações de entrada | Só para o dono |
| `FriendsViewModel` | Amigos | 4 abas: amigos/recebidos/enviados/convites de time — [§14.5](14-feature-amizades.md#145-o-fluxo-completo-em-friendsviewmodel) |
| `MatchListItem`, `MatchesViewModel` | Minhas partidas | |
| `ScoreboardRow`, `MatchDetailsViewModel` | Detalhes de partida | Placar + scoreboard dois lados |
| `VetoMapItem`, `MatchRoomViewModel` | Sala da partida | Elenco + veto ao vivo + servidor — [§19.5](19-feature-veto.md#195-a-tela-matchroomviewmodel) |
| `AuditLogViewModel` | Histórico | Somente leitura — [Cap. 15](15-feature-auditoria.md) |
| `BadgesViewModel` | Badges | Catálogo + desbloqueadas |
| `RankingViewModel` | Ranking | Pódio + resto, jogadores/times |
| `StatsViewModel` | Estatísticas | Gráficos normalizados (`KDNormalized`, `ADRNormalized`) |

## 22.6 Views (`Views/*.xaml` + `.xaml.cs`)

Uma View por ViewModel da lista acima (mesmo nome, sufixo `View` em vez de `ViewModel`), todas
registradas em `App.xaml` como `DataTemplate` (ver
[§6.2](06-client-navegacao-api.md#62-de-viewmodel-para-tela-datatemplate-implícito)). Todo
`.xaml.cs` é vazio além de `InitializeComponent()`, exceto onde já observado neste livro.

## 22.7 `App.xaml.cs` — o composition root

`App` expõe os services "globais" como propriedades estáticas, instanciadas uma única vez em
`OnStartup` — o "container de DI manual" do client (ver
[§3.4](03-padroes-projeto.md#34-instâncias-estáticas-ao-invés-de-injeção-de-dependência-client)).
Também registra um handler global de exceção não tratada
(`DispatcherUnhandledException`) que mostra um `MessageBox` com tipo/mensagem/stack trace em vez
de deixar o processo travar silenciosamente — a rede de segurança de último recurso do client
inteiro.
