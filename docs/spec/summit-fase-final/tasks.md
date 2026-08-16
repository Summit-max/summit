# Tasks — Summit, Fase Final

> Checklist executável derivado de [`plan.md`](plan.md). Ordem = dependência (não pule fases).
> Cada tarefa tem um jeito de confirmar que funcionou antes de ir pra próxima — não é só "escrever
> o código", é "escrever e provar que funciona". Convenção de checkbox: `[ ]` pendente, marcar
> `[x]` ao concluir e verificar.

## Fase 0 — Preparação

- [x] Ler `spec.md` e `plan.md` inteiros antes de começar (evita retrabalho por perder contexto
      de uma decisão já tomada em outra seção).
- [x] Confirmar ambiente local funcionando **sem nenhuma variável de AWS definida** — se alguma
      estiver setada no processo atual (ex. de sessão anterior), desativar temporariamente pra
      essa fase inteira ser desenvolvida e testada 100% desplugada, por construção, não por
      disciplina.
- [x] `git status` limpo antes de começar; commits pequenos por fase, não um só gigante no final.
      *(nada commitado ainda — mudanças ficam no working tree até o usuário pedir commit)*

## Fase 1 — Modelo de dados ✅ concluída e verificada

- [x] Adicionar `NextMatchId`/`NextMatchSlot`/`LoserNextMatchId`/`LoserNextMatchSlot` em
      `Models/Bracket.cs` (`BracketMatch`).
- [x] Adicionar `OrganizerUserId`/`SwissTargetWins`/`SwissEliminationLosses` em
      `Models/Tournament.cs`.
- [x] Adicionar `IsModerator` em `Models/User.cs`.
- [x] Criar `Models/Notification.cs` (`NotificationType` enum + classe `Notification`).
- [x] Criar `Models/Report.cs` (`ReportStatus` enum + classe `Report`).
- [x] Mapear tudo isso em `ApiDbContext.OnModelCreating` (`DbSet`s novos, conversões de enum,
      índices) — ver `plan.md` seção de modelo de dados pro SQL exato.
- [x] Rodar os `ALTER TABLE`/`CREATE TABLE` listados no `plan.md` contra o MySQL local.
- [x] **Verificação**: `dotnet build` limpo nos dois projetos; API local (sem AWS) sobe e responde.

## Fase 2 — Provider desplugável (RF-00) ✅ concluída e verificada

- [x] Criar `Summit.Api/IMatchServerProvider.cs`.
- [x] Criar `Summit.Api/AwsMatchServerProvider.cs` (wrapper fino sobre `MatchServerService`
      existente — não alterar `MatchServerService.cs` em si nesta fase).
- [x] Criar `Summit.Api/LocalSimulatedMatchServerProvider.cs`: `ProvisionAsync` com delay
      configurável, `TryAssignFromPoolAsync` sempre `true`, disparo assíncrono do resultado
      simulado.
- [x] Endpoint de debug `POST /api/debug/simulate-result/{matchId}` (corpo `{ winner: "A"|"B" }`)
      pra forçar o resultado antes do delay automático.
- [x] Registrar em `Program.cs` via `SUMMIT_MATCH_PROVIDER` (padrão `local`).
- [x] Trocar o parâmetro de `MatchServerService` pra `IMatchServerProvider` no fechamento do veto
      em `CompetitionEndpoints.cs`.
- [x] **Correção encontrada durante o teste**: o fechamento do veto tinha um atalho antigo
      (`isAwsConfigured ? "" : "sv{n}.summit.gg..."`) que setava `ProvisionState=Ready` direto e
      pulava o provider inteiro quando AWS não estava configurada — removido; agora sempre
      `Requesting`, deixando o `IMatchServerProvider` decidir de verdade.
- [x] **Verificação**: subida sem nenhuma env var de AWS, veto real jogado do início ao fim via
      curl, sala apareceu com `sim.summit.local:27015` — zero chamada de rede pra AWS.

## Fase 3+4 — Resultado + avanço de chave (simples e dupla) + encerramento ✅ concluída e verificada

*(feitas juntas — o wiring de vencedor/perdedor é o mesmo código compartilhado entre os dois
formatos; separar teria duplicado trabalho à toa.)*

- [x] Endpoint `POST /api/matches/{id}/result` em `CompetitionEndpoints.cs` (grava `MatchPlayer`s,
      marca `Match`/`BracketMatch` como `Finished`, idempotente).
- [x] Estender `GenerateSingleElimination` pra popular `NextMatchId`/`NextMatchSlot` (halving) +
      resolver BYE da rodada 0 sozinho (sem veto fantasma).
- [x] Estender `GenerateDoubleElimination` pra popular `LoserNextMatchId`/`LoserNextMatchSlot` de
      toda partida Upper/Lower seguindo a tabela de mapeamento do `plan.md`.
- [x] Implementar `AdvanceBracketAsync` cobrindo Upper (com e sem rota pra Lower), Lower
      (eliminação definitiva), e `HandleGrandFinalResultAsync` (incluindo reset sob demanda).
- [x] Extrair `OpenVetoForMatchAsync` (compartilhado entre `LifecycleWorker` rodada 1 e
      `AdvanceBracketAsync` pra qualquer rodada seguinte).
- [x] Implementar `CloseTournamentAsync` (RF-03): `Status=Finished` + `FinalPosition` 1º/2º.
- [x] **Verificação real, ponta a ponta, sem AWS**: `trn_ranked` (4 times) virado dupla
      eliminação, chave gerada (Upper 2+1, Lower 1+1, Grande Final 1 = 6 partidas — bate com
      `2N-2`), wiring conferido linha a linha no MySQL (bate 100% com a tabela do `plan.md`),
      veto real jogado do início ao fim, resultado simulado automático (`NAVI 16x4 FAZE`),
      confirmado no banco: `FINAL.TeamATag=NAVI` (vencedor avançou), `LOWER 1.TeamATag=FAZE`
      (perdedor foi pra Lower, `IsEliminated=0` — corretíssimo, eliminação dupla de verdade).
      **Não verificado ainda**: o caso de 8 times (rodadas Lower alternando eliminação/colocação)
      e o encerramento completo (só testei 1 de 6 partidas — a chave inteira até o campeão não
      foi jogada nesta sessão).

### Série MD3/MD5 real (múltiplos mapas por confronto) — ✅ concluída e verificada

- [x] `Models/Match.cs`: `GameNumber` (1-based) — vários `Match` compartilham o mesmo
      `BracketMatchId`, um por mapa jogado.
- [x] `AdvanceSeriesAsync` (`CompetitionEndpoints.cs`) — conta vitórias por mapa via query
      derivada (sem campo de placar de série persistido), abre o mapa seguinte quando ninguém
      atingiu a maioria ainda, só chama `AdvanceBracketAsync` quando a série de fato terminou.
- [x] **Bug real encontrado e corrigido**: a query de contagem de vitórias rodava antes do
      `Match.Status = Finished` do mapa recém-fechado ser persistido (mesma classe de bug já visto
      na Fase 9) — o campeão saía errado numa série 2-1. Corrigido com `SaveChangesAsync()` antes
      da query.
- [x] `MatchRoomViewModel` (client): não trava mais depois do mapa 1 — detecta troca de sala
      (`Room.Id` mudou) e volta a mostrar "aguardando conexão" pro mapa seguinte.
- [x] **Verificação real via curl/MySQL**: três séries testadas (sweep 2-0, decisão 1-1→2-1 duas
      vezes) — campeão sempre correto após o fix, mapa 2/3 só abre quando necessário.

### W.O. / no-show em veto — ✅ concluída e verificada

- [x] `LifecycleWorker.CheckVetoNoShowsAsync` — usa `Tournament.NoShowMinutes` contra a última
      atividade do veto (`VetoStep.CreatedAt` mais recente, ou `VetoSession.CreatedAt` sem
      nenhuma ação ainda); força vitória por W.O. pro time adversário, audita, notifica os dois
      capitães (`NotificationType.MatchNoShow`), avança a chave via `AdvanceBracketAsync`.
- [x] **Verificação real**: veto abandonado propositalmente, aguardado o timeout configurado —
      W.O. disparou pro time certo, chave avançou, os dois capitães notificados.
- [ ] **Bug latente encontrado, não corrigido (fora de escopo)**: `AutoVetoBotsAsync` pula
      (não joga por) times cujo `CaptainId` tem mais de 12 caracteres — como os ids de time
      fantasma gerados (`usr_ghost_{hex}_{n}`) são justamente longos, isso está invertido: times
      fantasma são tratados como "humano" e times humanos reais como "bot". Não afeta o teste de
      W.O. em si, mas vale corrigir depois.

### Histórico completo da série em `MatchDetailsView` — ✅ concluída e verificada

- [x] Novo endpoint `GET /api/matches/by-bracket/{bracketMatchId}/all` — todos os `Match` de um
      confronto, ordenados por `GameNumber`.
- [x] `MatchRepository.GetSeriesAsync` (client) + `MatchDetailsViewModel.SeriesGames`/
      `SeriesSummary` — computa vencedor por `TeamId` (não por lado A/B, que pode differir por
      mapa) e monta "NAVI VENCEU A SÉRIE 2-1" com um chip por mapa.
- [x] `MatchDetailsView.xaml` — faixa de chips (um por mapa, o atual destacado) entre o placar e o
      scoreboard, só visível quando a série tem mais de 1 mapa (`HasSeries`).
- [x] **Verificação real via curl/MySQL**: série de 3 mapas forçada via `/api/debug/force-match-
      result` (`bm_0a48fd336dee4af4977eeccde0f6bb7a`, NAVI 16-10 / 8-16 / 16-10) — endpoint novo
      devolveu os 3 mapas na ordem certa, cálculo de vencedor bateu (`NAVI VENCEU A SÉRIE 2-1`).
      **Não visto na tela de verdade** (só via API) — o build do client passou limpo.

## Fase 4 — Avanço de chave dupla + Grande Final + reset

*(itens de N=8 e reset de grande final seguem pendentes de verificação — ver nota na Fase 3+4
acima; o mecanismo está implementado, só não foi exercitado nesses dois caminhos específicos.)*

## Fase 5 — Estatísticas e Elo (RF-04) ✅ implementada e verificada

- [x] Implementar `UpdateStatsAndEloAsync` (fórmula do `plan.md`, K=32) chamado de dentro do
      endpoint de resultado.
- [x] **Verificação**: partida simulada real, conferido no MySQL — `Team.Elo`/`MatchesPlayed`/
      `MatchesWon` e `User.TotalMatches`/`TotalWins`/`Elo`/`KD` de todos os 10 jogadores
      envolvidos mudaram de forma coerente (o time vencedor subiu Elo, o perdedor desceu).

## Fase 6 — Badges automáticas (RF-05) ✅ concluída

- [x] Implementar `EvaluateBadgesAsync` cobrindo `bd_firstwin`, `bd_mvp`, `bd_hunter` (critérios
      exatos no `plan.md`).
- [x] Implementar `bd_champion` dentro de `CloseTournamentAsync` (`CompetitionEndpoints.cs:552`) —
      concede pra todos os membros do time campeão ao fechar o campeonato.
- [x] Implementar a checagem diária de `bd_loyal` via `CheckLoyaltyBadgesAsync` dentro de
      `LifecycleWorker.TickAsync` (throttle `Hour==3`).
- [ ] **Verificação**: criar um jogador novo (sem badge nenhuma), fazer ele vencer uma partida
      simulada — confirmar que `bd_firstwin` aparece na tela de Badges dele sem nenhuma ação
      manual. *(rodei o teste com jogadores veteranos do seed, que já tinham `TotalWins > 1` — o
      código está lá e o critério é `TotalWins == 1`, mas ainda não vi disparar de verdade com um
      jogador novo.)*

## Fase 7 — Notificações (RF-06) ✅ implementada, backend verificado / client não testado visualmente

- [x] Criar `NotificationHelper.Notify(...)`.
- [x] Adicionar as chamadas de `Notify(...)` em **7 dos 8** pontos: convite, solicitação
      resolvida, cargo mudado (promover/rebaixar/transferir), check-in aberto, escalação
      alterada, campeonato encerrado, badge desbloqueada. **Faltou**: denúncia resolvida (o
      enum `ReportResolved` já existe, só não tem produtor ainda — depende da Fase 10, que ainda
      não foi implementada).
- [x] Endpoints `GET /api/notifications/{userId}` (com `?unreadOnly=`), `POST
      /api/notifications/{id}/read`, `POST /api/notifications/{userId}/read-all`.
- [x] `Data/NotificationRepository.cs` no client.
- [x] `ViewModels/NotificationsViewModel.cs` + `Views/NotificationsView.xaml` + registro em
      `App.xaml`.
- [x] Sininho com contador de não-lidas em `MainShellView.xaml`/`MainShellViewModel.cs` (polling
      15s, mesmo padrão do `DispatcherTimer` já usado em `MatchRoomViewModel`).
- [x] **Verificação do backend**: convite de time real via curl → notificação apareceu certa pro
      destinatário (`"Você recebeu um convite pra entrar no time NAVI Academy."`), `unreadOnly`
      filtrou certo, `mark-as-read` funcionou, sumiu do filtro depois. Confirmado também
      indiretamente que `BadgeUnlocked` dispara (via `GrantBadgeAsync`, testado na Fase 3/4/6).
- [ ] **Não verificado**: a tela `NotificationsView` e o sininho **no client de verdade** — o
      build passou e o padrão XAML é idêntico ao de `AuditLogView` (já provado funcionando), mas
      ninguém abriu a tela ainda pra confirmar visualmente. `CheckInOpened`,
      `OwnershipTransferred`, `RoleChanged` e `LineupChanged` também não foram exercitados nesta
      sessão (só o fluxo de convite foi testado ao vivo).

## Fase 8 — Criação de campeonato (RF-09) ✅ criação implementada e verificada / edição parcial

- [x] `POST /api/tournaments` em `Program.cs`, com as validações do `plan.md` (data futura,
      min≤max, mínimo 3 mapas, nome obrigatório).
- [x] `PUT /api/tournaments/{id}` em `Program.cs` (só organizador, só antes do fechamento de
      inscrições) — **implementado mas não testado nesta sessão** (só a criação foi exercitada
      de ponta a ponta).
- [x] `Data/TournamentRepository.cs`: `CreateTournamentAsync`/`UpdateTournamentAsync`; novo
      `ApiClient.PostWithMessageAsync<T>` (mesmo padrão de `PutWithMessageAsync`, mas devolve o
      objeto criado — precisou ser adicionado, não existia).
- [x] `ViewModels/CreateTournamentViewModel.cs` + `Views/CreateTournamentView.xaml` (formulário
      completo: nome, descrição, região, data, formato/série/série-final via seletores tipo chip,
      map pool, min/max times, premiação, entrada paga).
- [x] Botão "CRIAR CAMPEONATO" em `TournamentsView.xaml`, navega pra tela nova.
- [x] Botão "EDITAR" em `TournamentDetailsView.xaml` — reusa `CreateTournamentViewModel`/
      `CreateTournamentView` em modo edição (`CreateTournamentViewModel(Tournament existing)`),
      gated por `Tournament.CanEdit` (`IsOrganizer && DateTime.UtcNow < RegistrationClosesAt`).
      Testado via client de verdade.
- [x] **Verificação real, ponta a ponta, via API (equivalente ao que o client novo chama)**:
      criado um campeonato do zero (`POST /api/tournaments`, sem tocar em SQL/seed), registrados
      2 times reais (`team_navi`, `team_faze`), chave gerada, veto jogado até o fim — e nesse
      processo confirmou-se de bônus que a rodada final usa `FinalSeries` (MD3) corretamente, não
      `Series` (MD1) — resultado simulado automático, chave avançou, **campeonato encerrou
      sozinho** (`Status=Finished`, `FinalPosition` 1º/2º corretos), notificação de encerramento
      chegou pro organizador. **Não clicado na tela de verdade** (só via chamadas HTTP
      equivalentes ao que o formulário manda) — o build do client passou limpo, mas ninguém abriu
      a tela `CreateTournamentView` visualmente ainda.

## Fase 9 — Formato suíço (RF-07) ✅ concluída e verificada

- [x] `LifecycleWorker.GenerateBracket` (agora `async Task`): branch pra `TournamentFormat.Swiss`
      chama `GenerateSwissRoundAsync(db, t, teams, 0)` — gera só a primeira rodada (pareamento
      aleatório, sem campanha ainda).
- [x] `GenerateSwissRoundAsync` (pareamento por campanha via `GetSwissHistoryAsync` — derivada do
      histórico de `BracketMatch`, sem coluna nova; evita rematch via `HashSet` de pares já
      jogados; time ímpar empresta do grupo de campanha adjacente mais próximo).
- [x] Gatilho de rodada seguinte: `AdvanceSwissAsync` (chamado de dentro de `AdvanceBracketAsync`
      quando `bm.Round.Side == Upper` e `t.FormatType == Swiss`) — só avança quando a última
      partida pendente da rodada termina.
- [x] Critério de parada (`SwissTargetWins`/`SwissEliminationLosses`): classifica/elimina por
      `TournamentTeam`, encerra via `CloseTournamentAsync` quando não sobra ninguém ativo —
      campeão = melhor campanha entre os classificados, desempate Buchholz (`PickSwissFinalists`).
- [x] **Bug real encontrado e corrigido durante o teste**: `AdvanceSwissAsync` lia o histórico de
      vitórias/derrotas via uma query SQL (`GetSwissHistoryAsync`) que não enxergava a MUDANÇA da
      própria partida que acabou de fechar a rodada (ainda não commitada nesse ponto do request) —
      o pareamento da rodada seguinte ficava errado (dois vencedores 1-0 caíam contra perdedores
      em vez de um contra o outro). Corrigido com `await db.SaveChangesAsync()` antes da leitura.
- [x] **Verificação real via curl/MySQL direto** (2 campeonatos suíços de 4 times, sem client):
      confirmado pareamento correto por campanha rodada a rodada (vencedores 1-0 emparelhados
      entre si, não com perdedores), nenhum confronto repetido, classificação (`FinalPosition`)
      e eliminação (`IsEliminated`) disparando nos limiares certos (`SwissTargetWins=2`/
      `SwissEliminationLosses=2` de teste), encerramento automático com campeão de melhor campanha
      e `TournamentsWon` incrementado. **Não testado**: caso de número ímpar de times ativos com
      só 1 grupo de campanha restante (edge case documentado no código — time fica sem partida
      naquela rodada em vez de crashar) e o fluxo via client de verdade (só via API).

## Fase 10 — Denúncia e moderação (RF-08)

- [ ] `POST /api/reports`, `GET /api/reports?status=&moderatorUserId=` (com checagem de
      `IsModerator`), `POST /api/reports/{id}/resolve`.
- [ ] Botão "Denunciar" em `PlayerProfileView.xaml` + painel inline de motivo.
- [ ] `ViewModels/ModerationQueueViewModel.cs` + `Views/ModerationQueueView.xaml` (com a
      checagem de acesso do lado do client, além da checagem real no backend).
- [ ] Marcar manualmente `IsModerator = 1` pra um usuário de teste direto no MySQL (não existe
      fluxo de UI pra isso, por design — ver `plan.md`).
- [ ] **Verificação**: denunciar um perfil com um usuário comum, confirmar que ele NÃO acessa a
      fila de moderação; logar com o usuário `IsModerator`, resolver a denúncia, confirmar que o
      denunciante recebe a notificação de resolução (RF-06).

## Fase 11 — Correções de regra de negócio ✅ concluída e verificada

- [x] Corrigir `TeamService.InviteByNicknameAsync` (client) pra só permitir dono, alinhado com a
      API (`User.CanInvite` agora exige `IsCaptain`, sublíder não convida mais).
- [x] Mensagem de erro específica de permissão em `POST /api/teams/{teamId}/invite` — devolvida
      via `ApiClient.PostWithMessageAsync`/`TeamRepository.InviteAsync` até o `InviteMessage` da
      tela.
- [x] Bloqueio de exclusão de time com campeonato ativo em `DELETE /api/teams/{id}` — guarda por
      `TournamentTeams` não eliminado num torneio `Status != Finished`; mensagem real propagada
      via novo `ApiClient.DeleteWithMessageAsync`/`TeamRepository.DeleteAsync`/
      `TeamViewModel.DeleteErrorMessage`.
- [x] Auditoria em aceitar/recusar convite de time (`invite_accepted`/`invite_declined`) e em
      bloquear amizade (`friend_blocked`) via `CompetitionEndpoints.Audit(...)`.
- [x] **Verificação real via curl/MySQL direto**: exclusão de `team_faze` (inscrito e não
      eliminado em `trn_arena`) recusada com `"Não é possível excluir o time: ele está inscrito
      no campeonato ativo \"Summit Test Arena\"."`; convite como sublíder (`usr_shox`) recusado
      com `"Só o capitão do time pode convidar jogadores."`; convite como capitão aceito
      normalmente; `auditlogs` confirmou as 3 linhas novas (`invite_accepted`, `friend_blocked`)
      com ator/alvo corretos.

## Fase 12 — Verificação de ponta a ponta (critério final de "pronto")

- [ ] Rodar o roteiro completo descrito em `spec.md §15`, do zero, numa sessão só, **sem nenhuma
      variável de ambiente de AWS definida em nenhum momento**.
- [ ] Regenerar `database/schema.sql` (`mysqldump --no-data`) refletindo todas as tabelas/colunas
      novas desta fase.
- [ ] Atualizar `docs/pendencias.md` — mover pra "fechado" tudo que esta fase resolveu, e deixar
      claro o que ainda fica de fora (ações administrativas, no-show real de servidor — ambos já
      documentados como fora de escopo em `spec.md §3`).
- [ ] Atualizar `docs/book/21-feature-pos-partida-gaps.md` (ou substituir por um capítulo novo
      descrevendo como o pós-partida passou a funcionar) — o livro fica desatualizado assim que
      esta fase termina, e isso deveria ser corrigido antes de considerar a fase "concluída de
      verdade", não só "implementada".
