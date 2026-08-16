# Pendências — Fluxo Completo (Login → Partida)

> Auditoria feita em 23/jul/2026 comparando o código real (client WPF + `Summit.Api`) contra
> `docs/espec-times.md` e `docs/espec-campeonatos.md`. Cada item marcado:
> **(não aplicado)** = não existe em lugar nenhum (nem client, nem API) — precisa criar do zero.
> **(precisa melhorar)** = existe mas está incompleto/parcial — geralmente API pronta e client faltando.

## 1. Conta / Login
- Login Steam real (OpenID) + sessão persistida — ✅ sólido, não mexer sem motivo.
- ✅ **Fechado em 23/jul/2026**: edição de perfil agora inclui avatar e país (`ProfileViewModel`,
  `UserService.UpdateAvatarUrlAsync/UpdateCountryAsync`) — API já aceitava esses campos, só
  faltava o client mandar. Onboarding mínimo: prompt de país + role no primeiro login
  (`OnboardingViewModel`, gate em `MainShellViewModel` quando `Country` vem vazio).
- **(não aplicado)** "Links" no perfil — não existe campo no modelo `User`, precisaria de schema novo.

## 2. Times
- Criar time, convidar, aceitar convite, sair (com transferência automática) — ✅ sólido.
- ✅ **Fechado em 23/jul/2026**: tela de solicitações de entrada (`JoinRequestsView`, dono
  aceitar/recusar), botão "solicitar entrada" no perfil de outro time (`TeamProfileView`),
  promover/rebaixar sublíder e transferir propriedade (botões por membro em `TeamView`,
  captain-only via `IsMyTeamCaptain`). Tudo ligado direto na API já existente.
- ✅ **Fechado em 23/jul/2026**: editar time (`PUT /api/teams/{id}`, form em `TeamView`) e
  excluir time (`DELETE /api/teams/{id}`, versão simples sem validação de campeonato ativo —
  decisão consciente, ver commit) — ambos owner-only. Remover jogador (kick) implementado de
  verdade (`POST /api/teams/{teamId}/kick`); o stub fake antigo (`RemoveMemberAsync` sempre
  `true`) foi removido junto com a declaração não-usada em `ITeamService`.

## 3. Amizades
- Pedido/aceite/recusa/remoção — ✅ sólido, fluxo mais completo do app.
- ✅ **Fechado em 23/jul/2026**: bloqueio de usuário (`POST /api/friends/block`, reaproveita o
  `FriendshipStatus.Blocked` que já existia no enum sem uso; desbloqueio reaproveita o
  `DELETE /api/friends` existente). Amigos em comum: interseção client-side das duas listas de
  amigos em `PlayerProfileViewModel`, sem endpoint novo.
- **(não aplicado)** Denúncia de perfil — deixado de fora conscientemente (decisão de escopo em
  23/jul); um sistema completo precisaria de fila de moderação/revisão por admin.

## 4. Campeonatos — inscrição
- Listar, filtrar, inscrever, check-in — ✅ sólido.
- ✅ **Fechado em 23/jul/2026**: tela de escalação (`LineupViewModel`/`LineupView`, acessível
  pelo botão "ESCALAÇÃO" em `TournamentDetailsView` quando `Tournament.CanEditLineup`) — captain
  seleciona os 5 jogadores + capitão da escalação, chama `PUT /api/tournaments/{id}/lineup`
  (que já validava tudo, só faltava o client mandar `PlayerIds`/`CaptainUserId`).

## 5. Chave (bracket)
- ✅ **Fechado em 23/jul/2026 (parcial — geração, não avanço)**: renderização agora é genérica
  (`BracketLayout.cs` — colunas por rodada com espaçamento padrão que dobra a cada rodada, sem
  linhas conectoras; substituiu o `Canvas`/`Path` hardcoded de 8 times em
  `TournamentDetailsView.xaml`). Eliminação dupla agora **gera** Upper + Lower + Grande Final
  (`LifecycleWorker.GenerateDoubleElimination`, campo novo `BracketRound.Side`) — testado com
  4 times (2+1 Upper, 1+1 Lower, 1 Grande Final = 6 partidas, bate com a fórmula `2N-2`) e 7 times
  na simples (BYE correto). **Sistema suíço continua sem implementar** (decisão de escopo). E,
  importante: isso é só geração da estrutura — **avanço automático de resultado continua sendo
  o gap grande da seção 7** (lower bracket nasce todo "TBD" porque não dá pra saber quem cai pra
  lá sem isso).

## 6. Veto → sala da partida
✅ **Fully wired** — pipeline validado ao vivo (incluindo pool quente de servidores AWS via RCON).

## 7. Pós-partida — MAIOR BURACO DO SISTEMA
- **(não aplicado)** Nenhum endpoint de resultado (`/api/matches/{id}/result` não existe em
  lugar nenhum). Nada atualiza placar, MVP, stats por jogador, nem marca `MatchStatus.Finished`.
- **(não aplicado)** Avanço de chave: ninguém lê o resultado de uma partida pra alimentar a
  próxima do bracket — a chave trava depois da primeira rodada.
- **(não aplicado)** No-show / W.O., monitoramento de jogadores conectados no servidor.
- **(não aplicado)** Badges: existe tela (`BadgesView`) e API de leitura, mas nenhuma lógica
  concede badge pra ninguém — hoje só existem as que vieram do `SeedData.cs`.
- **(não aplicado)** Encerramento de campeonato (campeão, vice, histórico, premiação) — nada
  seta `TournamentStatus.Finished`.
- **(não aplicado)** Ações administrativas da espec (editar placar, reabrir veto, recriar
  servidor, vitória administrativa, cancelar partida/campeonato) — nenhuma existe.

## 8. Auditoria
- ✅ **Fechado em 23/jul/2026**: tela de leitura (`AuditLogView`, `AuditLogViewModel`),
  acessível pelo botão "HISTÓRICO" em `TeamView`. Lista `AuditLog` (ação, data, old→new value,
  motivo) sem nenhuma ação interativa (só leitura, sem risco).

## 9. Notificações
- **(não aplicado)** Não existe sistema de notificação em lugar nenhum (client ou API).
  Convite, entrada, promoção, check-in — tudo só aparece se o usuário recarregar a tela na mão.

## MatchZy / CounterStrikeSharp
✅ **Desbloqueado em 23/jul/2026** — era instalação incompleta, não incompatibilidade real
(detalhes em `docs/plano-aws.md`). Reinstalação limpa da v1.0.371 carregou sem erro
(`CounterStrikeSharp.API Loaded Successfully.`), e MatchZy 0.8.15 por cima também
(`[MatchZy 0.8.15 LOADED]`). Ainda não integrado ao fluxo do Summit (webhook de resultado etc.)
— isso cai dentro do gap grande da seção 7.

## Resumo
Dá pra criar conta (com onboarding e perfil completo), montar/gerenciar time de ponta a ponta
(convite, solicitação, promoção, transferência, kick, edição, exclusão), bloquear/desbloquear
amigos, se inscrever, montar escalação, ver a chave (simples ou dupla, qualquer tamanho de
time), fazer o veto e entrar num servidor real com CS2+MatchZy prontos — tudo isso está testado
e funciona. O único buraco grande que sobra é **depois que a partida termina** (seção 7): o
sistema não sabe que ela acabou (sem resultado, sem chave avançando de verdade, sem badge, sem
notificação). Suíço e denúncia de perfil ficaram de fora por decisão consciente de escopo.
