# Spec — Summit, Fase Final (Pós-Partida + Desacoplamento de AWS)

> Documento de especificação (SDD — Spec-Driven Development). Este arquivo descreve **o quê** e
> **por quê**, sem detalhe de implementação — isso vive em [`plan.md`](plan.md). A quebra em
> tarefas executáveis vive em [`tasks.md`](tasks.md). Contexto de arquitetura/código já existente:
> [`docs/book/`](../../book/00-indice.md) (o livro completo do sistema atual) e
> [`docs/pendencias.md`](../../pendencias.md) (lista viva de pendências, sendo fechada por esta spec).

## 1. Motivação

O Summit hoje cobre de ponta a ponta tudo **até o jogador entrar no servidor** (conta, time,
amizades, inscrição, escalação, chave, veto, sala com IP real via AWS). O que falta é
especificamente **o que acontece depois** — resultado, avanço de chave, badges, encerramento de
campeonato — e isso hoje trava o sistema inteiro num estado de demonstração: a chave nunca avança
além da primeira rodada, badges nunca são concedidas de verdade, um campeonato nunca termina
sozinho.

Duas exigências atravessam toda esta spec:

1. **Nada pode depender de AWS para ser construído, testado ou demonstrado.** O sistema precisa
   funcionar 100% localmente (client + API + MySQL/SQLite), com o caminho de servidor real na AWS
   continuando a existir e podendo ser "religado" depois sem reescrever nada — arquitetura
   plugável, hoje desconectada por padrão. Ver [RF-00](#rf-00--provider-de-servidor-plugável-e-desligado-por-padrão).
2. **Toda API precisa funcionar e toda tela precisa ter um fluxo completo e testável**, sem elo
   perdido entre o que o botão promete e o que a API de fato faz.

## 2. Escopo (decidido em conjunto, 26/jul/2026)

| Área | Está dentro? |
|---|---|
| Resultado de partida + avanço de chave (simples e dupla) + encerramento de campeonato | ✅ Sim |
| Provider de servidor plugável, com implementação local simulada (sem AWS) como padrão | ✅ Sim |
| Agregação de estatísticas (KD, winrate, Elo) a partir de resultado real | ✅ Sim |
| Motor de badges (concessão automática) | ✅ Sim |
| Sistema de notificação in-app | ✅ Sim |
| Formato suíço de campeonato | ✅ Sim |
| Denúncia de perfil + fila de moderação mínima | ✅ Sim |
| Tela de criação/edição de campeonato pelo organizador | ✅ Sim |
| Correção de regras de negócio identificadas como inconsistentes na Parte de revisão (§9) | ✅ Sim |
| Ações administrativas (editar placar manual, reabrir veto, W.O. manual, cancelar partida/campeonato) | ❌ Não — decisão explícita, ver §3 |
| Qualquer configuração/infraestrutura AWS nova | ❌ Não — fora do escopo por definição (§1) |
| No-show **dentro do servidor** (monitorar quem conectou de fato) | ❌ Não — depende do provider real de servidor (RCON de verdade); no provider local simulado isso não tem sinal real pra observar. Fica documentado como gap remanescente pós-AWS. |

## 3. Fora de escopo, explicitamente

- **Ações administrativas** (editar placar manualmente, reabrir veto, recriar sala, W.O. manual,
  cancelar partida/campeonato). Decisão consciente: o foco desta fase é o caminho automático
  funcionar sozinho; ferramentas de correção manual ficam para uma spec futura, quando houver uso
  real o suficiente para saber que tipo de intervenção realmente é necessária.
- **No-show dentro do servidor** (distinto do no-show do check-in, que já existe). Motivo: exige
  sinal real de jogadores conectados, que só existe com RCON contra um servidor de verdade — fora
  do que o provider local simulado consegue produzir de forma honesta.
- **Qualquer coisa em `docs/plano-aws.md`** — infraestrutura AWS não é tocada nesta spec.
- **Autenticação/autorização de administrador "de verdade"** (login separado, papéis de sistema).
  A denúncia (§8) usa um flag simples `User.IsModerator` setado manualmente no banco — não há
  fluxo de "virar moderador" pelo produto.

## 4. RF-00 — Provider de servidor plugável e desligado por padrão

**Por quê**: hoje `MatchServerService` fala direto com a AWS SDK — não dá pra testar nada do que
vem depois (resultado, avanço, badges) sem uma instância EC2 real rodando, e o objetivo desta fase
é justamente testar tudo isso sem gastar nada de AWS.

**User story**: Como desenvolvedor testando o sistema localmente, eu quero que uma partida
complete seu ciclo de vida inteiro (sala → resultado → avanço de chave) sem nenhuma chamada à
AWS, para poder validar toda a lógica de pós-partida offline.

**Critérios de aceite**:
- Existe uma abstração única que qualquer parte do sistema usa para "conseguir um servidor pra
  partida" — hoje é `MatchServerService`; depois desta spec, vira uma interface com duas
  implementações trocáveis.
- A implementação **local/simulada** é a **padrão** (não exige nenhuma variável de ambiente de
  AWS para funcionar) — "desplugado" por definição, não por acidente de configuração ausente
  (diferente do fallback silencioso que já existe hoje).
- A implementação **AWS real** continua existindo, inalterada em comportamento, e é escolhida
  só por configuração explícita — nunca é a padrão.
- No modo local, uma partida recebe uma "sala" com IP/senha simulados em poucos segundos, e o
  **resultado da partida chega automaticamente** depois de um tempo configurável — sem
  intervenção manual — para que o resto do pipeline (avanço de chave, badges, encerramento) seja
  exercitado de ponta a ponta sozinho.
- Existe também um caminho de **controle manual do resultado simulado** (endpoint de
  desenvolvimento), para que um teste dirigido consiga forçar quem vence uma partida específica —
  necessário pra andar a chave inteira de propósito durante testes/demonstração, não só
  aleatoriamente.

## 5. RF-01 — Resultado de partida

**User story**: Como sistema, eu preciso receber o resultado de uma partida terminada (placar +
estatísticas por jogador) e usar isso para atualizar tudo que depende dele.

**Critérios de aceite**:
- Existe um endpoint que recebe resultado de uma partida específica, uma única vez (uma segunda
  tentativa de enviar resultado pra uma partida já finalizada é rejeitada, não duplica nada).
- Ao receber o resultado: a partida (`Match`) vira `Finished` com placar e duração; cada jogador
  de cada lado recebe uma linha de estatística (`MatchPlayer`) com kills/deaths/assists/HS/ADR/
  rating/MVP.
- A `BracketMatch` correspondente também recebe o placar e vira `Finished`.
- Esse mesmo contrato de resultado é o que a implementação AWS real usaria (webhook do MatchZy) —
  ou seja, o "de onde vem o resultado" é plugável (provider local chama internamente; AWS
  chamaria via HTTP de fora), mas **o que acontece depois de receber é idêntico nos dois casos**.

## 6. RF-02 — Avanço de chave (simples e dupla)

**User story**: Como jogador, eu quero que, assim que minha partida termina, a próxima partida da
chave já apareça com os times certos preenchidos — sem alguém preencher isso à mão.

**Critérios de aceite — eliminação simples**:
- Toda `BracketMatch` sabe, desde a geração da chave, qual é a próxima partida que ela alimenta
  (e em qual lado, A ou B).
- Ao registrar um resultado, o vencedor é escrito automaticamente no lado certo da próxima
  partida. Quando essa próxima partida tem os dois lados preenchidos, ela fica pronta pra veto
  (o mesmo mecanismo que hoje só abre veto pra rodada 1 passa a valer pra qualquer rodada que
  acabou de ficar completa).
- O perdedor é marcado eliminado (`TournamentTeam.IsEliminated = true`).

**Critérios de aceite — eliminação dupla**:
- Além do "próxima partida se eu ganhar", toda `BracketMatch` da Upper sabe também "próxima
  partida (na Lower) se eu perder", incluindo o lado certo — seguindo o mapeamento padrão de
  eliminação dupla (perdedores da Upper descem pra Lower nas posições certas, sem colisão nem
  posição vazia sobrando).
- A Grande Final só fica pronta quando os dois campeões (Upper e Lower) estão definidos.
- Se `Tournament.BracketReset` estiver ativo e o time vindo da Lower vencer a primeira partida da
  Grande Final, uma segunda partida (reset) é criada automaticamente — só nesse caso.
- Times eliminados na Lower (perderam pela segunda vez) recebem `IsEliminated = true`
  definitivamente.

**Critérios de aceite — geral**:
- Nenhuma partida deveria conseguir "travar" a chave por falta de dado — todo caminho possível
  (incluindo BYE da primeira rodada) precisa levar a algum lugar definido.

## 7. RF-03 — Encerramento de campeonato

**User story**: Como organizador ou jogador, eu quero ver claramente quando um campeonato acabou
e quem foi campeão.

**Critérios de aceite**:
- Quando a última partida decisiva termina (final da simples; grande final — ou reset, se houver
  — da dupla; última rodada suíça, ver §11), o campeonato vira `TournamentStatus.Finished`
  automaticamente.
- O time campeão e o vice recebem `FinalPosition` (1 e 2). Terceiro/quarto lugar é um "bônus":
  implementar se for direto (perdedores da semifinal/penúltima rodada Lower), documentar como tal
  se não for.
- A tela de detalhes do campeonato mostra claramente "ENCERRADO — CAMPEÃO: [time]" quando
  aplicável.

## 8. RF-04 — Estatísticas e Elo

**User story**: Como jogador, eu quero que minhas estatísticas de perfil (KD, winrate, %HS, Elo)
reflitam partidas que eu realmente joguei, não só o valor fixo que veio do seed.

**Critérios de aceite**:
- Depois de cada resultado registrado, os campos agregados de `User` (KD, WinRate,
  HeadshotPercent, AvgDamagePerRound, TotalMatches/Wins/Kills/Deaths/Assists) são recalculados ou
  incrementados para cada jogador que participou.
- `Team` recebe o mesmo tratamento para seus agregados (`MatchesPlayed`, `MatchesWon`, `Elo`).
- Existe uma fórmula de Elo definida e documentada (ver `plan.md`) — não precisa ser
  competitivamente sofisticada, precisa ser **determinística, documentada, e sensata** (time/
  jogador que ganha de um adversário mais forte ganha mais pontos do que ganhar de um mais fraco).
- Ranking (`/api/ranking/*`) passa a refletir esses valores dinâmicos automaticamente — nenhuma
  mudança é necessária nesses endpoints, eles já leem os mesmos campos.

## 9. RF-05 — Badges automáticas

**User story**: Como jogador, eu quero desbloquear badges de verdade jogando, não só ver as que o
seed me deu.

**Critérios de aceite**:
- Depois de cada resultado, um conjunto de regras é avaliado para cada jogador envolvido; toda
  regra satisfeita e ainda não desbloqueada concede a badge (`UserBadge` nova, com
  `UnlockedAt = now`).
- Cobre, no mínimo: primeira vitória, MVP acumulado (10x), campeão de campeonato, HS% sustentado
  alto. Ver `plan.md` para a lista completa e a definição exata de cada critério (algumas badges
  do catálogo atual, como "Clutch King", exigem dado que o sistema não coleta hoje — a spec
  documenta isso como limitação conhecida em vez de fingir que funciona).

## 10. RF-06 — Notificações in-app

**User story**: Como jogador, eu quero saber que fui convidado, promovido, que meu check-in
abriu, ou que meu campeonato acabou, sem precisar ficar recarregando telas pra descobrir.

**Critérios de aceite**:
- Existe uma lista de notificações por usuário, com contagem de não-lidas visível no shell
  principal (ex. um sininho com número).
- No mínimo estes eventos geram notificação: convite de time recebido, solicitação de entrada
  aceita/recusada, promovido/rebaixado, dono transferido, check-in aberto, escalação alterada por
  outro membro, campeonato encerrado (pra todo participante), badge desbloqueada, denúncia
  resolvida (pro denunciante).
- Marcar como lida (individual e "marcar todas") funciona.
- **Não** é necessário e-mail/push — só in-app, como a espec original já limitava.

## 11. RF-07 — Formato suíço

**User story**: Como organizador, eu quero poder criar um campeonato em formato suíço e ele
funcionar de verdade (hoje cai silenciosamente em eliminação simples, o que é um bug de
comportamento, não uma feature ausente honesta).

**Critérios de aceite**:
- Escolher `TournamentFormat.Swiss` na criação realmente gera pareamento suíço, não mais um
  fallback silencioso pra eliminação simples.
- A cada rodada: times com a mesma campanha (mesmo número de vitórias-derrotas) se enfrentam,
  sem repetir confronto já ocorrido no campeonato; desempate por Buchholz quando necessário.
- O campeonato tem um critério claro de parar (X vitórias classifica, Y derrotas elimina —
  configurável, com um padrão sensato documentado em `plan.md`).
- A geração de cada rodada nova só acontece depois que todos os resultados da rodada anterior
  chegaram — isso é diferente de simples/dupla (que geram a chave inteira de uma vez);
  documentar essa diferença claramente.

## 12. RF-08 — Denúncia de perfil

**User story**: Como jogador, eu quero poder denunciar um comportamento problemático de outro
jogador, e como responsável pela plataforma eu quero uma lista simples pra revisar isso.

**Critérios de aceite**:
- Botão "Denunciar" no perfil de outro jogador (não aparece no próprio perfil), com motivo
  (texto livre curto ou categorias simples).
- Fila de denúncias pendentes, visível só para usuários com `IsModerator = true` (flag simples,
  setado manualmente no banco — sem fluxo de virar moderador pelo produto).
- Moderador consegue marcar como resolvida ou dispensada, com uma nota opcional; isso notifica
  quem denunciou (RF-06).
- **Não** inclui: banimento automático, restrição de conta, qualquer ação punitiva — só o
  registro e a fila. Ações punitivas ficam fora de escopo (não fazem sentido sem ação
  administrativa, que está explicitamente fora, §3).

## 13. RF-09 — Criação e edição de campeonato

**User story**: Como jogador organizador, eu quero criar um campeonato pelo próprio client, sem
precisar de acesso a SQL.

**Critérios de aceite**:
- Tela de criação: nome, descrição, região, data/hora de início, formato (simples/dupla/suíço),
  série (MD1/MD3/MD5, inclusive série final separada), map pool, mínimo/máximo de times, prêmio,
  entrada paga sim/não.
- Quem cria vira o "organizador" do campeonato — um vínculo real com o usuário (hoje
  `Tournament.Organizer` é só texto livre "Summit Staff"), não mais uma string solta.
- Edição é permitida só pelo organizador, e só enquanto o campeonato ainda não fechou inscrições
  (`RegistrationClosesAt` ainda não passou) — depois disso, os dados ficam congelados, consistente
  com a regra que já existe pra inscrição em si.
- Validação: datas coerentes (início no futuro), mínimo ≤ máximo de times, map pool não vazio.

## 14. Revisão de regras de negócio — correções propostas

Levantadas durante a auditoria do código atual (`docs/book/`). Cada uma é uma correção pequena e
isolada, não uma feature nova — listadas aqui porque o pedido explícito foi "revisar tudo e mudar
regra de negócio que não estiver boa".

1. **Convite de time — client permite o que o backend recusa.** `TeamService.InviteByNicknameAsync`
   habilita o botão de convite pra dono *ou* sublíder, mas a API só aceita dono. Um sublíder que
   tenta convidar recebe uma mensagem de erro genérica e confusa ("jogador não encontrado ou já
   tem time"), quando na verdade o problema é permissão. Correção: alinhar a checagem do client
   com a da API (só dono), e a API devolver uma mensagem específica de permissão em vez de genérica.
2. **Exclusão de time não valida campeonato ativo.** A especificação original (`espec-times.md
   §33`) previa bloquear exclusão de time com campeonato ativo/partida pendente; isso foi
   simplificado no MVP anterior. Agora que existe encerramento de campeonato de verdade (§7),
   faz sentido reativar essa validação: recusar exclusão se o time está inscrito em algum
   campeonato `Open`/`InProgress` não encerrado.
3. **Suíço caindo silenciosamente em eliminação simples.** Já é o motivo de existir do RF-07 —
   listado aqui também como "bug de comportamento" para deixar claro que não é só uma feature
   nova, é uma correção de um caminho de código que hoje mente sobre o que está fazendo.
4. **Cobertura de auditoria incompleta.** Aceitar/recusar convite de time e ações de amizade
   (pedido, aceite, bloqueio) não geram entrada de auditoria hoje, apesar de auditoria cobrir
   quase tudo mais nesse entorno. Estender para esses casos por consistência.
5. **`Tournament.Organizer` como string livre.** Vira vínculo real de usuário (RF-09) — isso
   também é uma correção de modelagem, não só um requisito da tela nova.
6. **`TournamentTeam.FinalPosition` nunca é usado.** Campo existe desde sempre no schema, nunca é
   escrito. RF-03 finalmente o usa — não é uma mudança de regra, é uma dívida sendo paga.

**Mantido de propósito (não é bug, é decisão que continua valendo)**: o "modo alpha" da escalação
(aceita elenco menor que 5 enquanto times de teste tiverem poucos membros) continua como está —
segue sendo a acomodação certa para o volume de usuários de teste atual.

## 15. Fluxo de ponta a ponta esperado ao final desta fase (critério de aceite geral)

Um teste manual único deveria conseguir, **inteiramente local, sem AWS**:
1. Criar dois usuários, dois times de 5 jogadores cada.
2. Um deles cria um campeonato (RF-09) em eliminação dupla, com 4 times (os 2 times reais +
   2 vindos do seed, por exemplo).
3. Inscrever os 4, fazer check-in dos 4.
4. Deixar a chave gerar, entrar em cada partida da primeira rodada, deixar o provider local
   simular o resultado (ou forçar via endpoint de dev quem ganha, pra guiar o teste).
5. Ver a chave avançar sozinha rodada após rodada, incluindo a Lower bracket recebendo os
   perdedores certos.
6. Ver o campeonato encerrar sozinho, campeão marcado, `FinalPosition` preenchido.
7. Ver as estatísticas de perfil e o ranking dos jogadores envolvidos mudarem de verdade.
8. Ver pelo menos uma badge nova ser concedida a alguém.
9. Ver notificações aparecerem pros jogadores certos em pelo menos 3 desses eventos.
10. Denunciar um perfil, ver a denúncia aparecer na fila de um usuário `IsModerator=true`,
    resolver, ver a notificação de resolução chegar pro denunciante.

Se esse roteiro completo funcionar sem nenhuma variável de ambiente de AWS definida, a fase está
pronta.
