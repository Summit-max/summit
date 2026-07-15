# Especificação Funcional — Times, Jogadores, Amizades e Perfis

> Mapeamento de nomenclatura no código: `TeamRole.Captain` = **Dono**, `TeamRole.ViceCaptain` = **Sublíder**, `TeamRole.Member` = **Membro comum**. O **capitão da escalação** é um conceito separado (por campeonato, campo `CaptainUserId` na inscrição).

## 2. Estrutura do time
1 dono (obrigatório e único), N sublíderes, N membros. **Sem limite de 5 jogadores no elenco** — o limite de 5 vale para a *escalação* de cada campeonato (padrão CS2; configurável por jogo no futuro).

## 3. Cargos
- **Dono (único):** convida jogadores; aceita/recusa solicitações de entrada; promove/rebaixa sublíderes; remove jogadores; transfere propriedade; edita o time; exclui/desativa; + tudo que o sublíder pode.
- **Sublíder:** inscreve em campeonatos; seleciona/altera escalação (até o bloqueio); check-in; cancela inscrição (quando permitido); representa em vetos. **Não pode:** convidar, aceitar/recusar solicitações, promover, remover, excluir, transferir.
- **Membro:** visualiza, participa de escalações, sai do time, recebe notificações. Sem poderes administrativos.

## 4-5. Criação e validação
Qualquer usuário elegível cria; vira dono automaticamente. Campos: nome (3-32), tag (2-8, única), escudo, descrição, país, links, privacidade, jogo. Validar caracteres, palavras proibidas, duplicidade.

## 6-9. Entrada de jogadores
Duas vias, **sempre com confirmação bilateral**:
1. **Convite** (só o dono envia): estados Pendente/Aceito/Recusado/Cancelado/Expirado. Aceitar → entra como membro comum, data registrada, notificações.
2. **Solicitação de entrada** (jogador → perfil do time, mensagem opcional): mesmos estados; só o dono aceita/recusa; jogador pode cancelar a própria enquanto pendente.

Validações antes de aceitar: já pertence? banido? time ativo? restrição regional? limite de times? suspenso? bloqueio entre jogador e dono? convite/solicitação ainda válido? Regra inicial: **1 time principal ativo por jogo**; nunca representar 2 times no mesmo campeonato.

## 10. Promoção a sublíder
Só o dono. Registrar: promovido, responsável, data/hora, cargo anterior e novo. **Antiguidade do cargo** (data da promoção) define a ordem de transferência automática. Rebaixar → volta a membro comum.

## 11-14. Saídas e transferência de propriedade
- Qualquer jogador sai a qualquer momento (alertar consequências: escalações, W.O. etc.).
- **Saída do dono:** o time nunca fica sem dono. Ordem automática: (1) sublíder há mais tempo no cargo → (2) membro mais antigo no time → (3) menor id (determinístico). Antigo dono é removido após transferir; todos notificados; log gerado.
- **Sem sublíderes:** se há outros membros, o dono deve escolher o novo dono antes de sair; se está sozinho, exclui/desativa o time; se banido/abandonou → transfere ao membro mais antigo.
- **Transferência voluntária:** dono escolhe → confirmação → alvo aceita/recusa → só transfere com aceite; antigo dono vira sublíder ou membro.

## 15. Remoção de jogadores
Só o dono. Removido perde permissões, sai de escalações editáveis, permanece nos históricos, é notificado; log gerado. Dono não remove a si mesmo (usa saída/transferência).

## 16-21. Escalação em campeonatos
- Só dono/sublíder inscreve. Seleciona **exatamente 5 elegíveis** (do elenco, ativos, não banidos, não inscritos por outro time no mesmo camp, integrações ok).
- **Capitão da escalação:** obrigatório, entre os 5; pode não ser dono/sublíder. Responsável na competição por: notificações, presença, vetos, dados do servidor, contato com admin, confirmação de resultados.
- **Alteração:** livre até o **início do check-in** → depois bloqueada (só admins, com justificativa + log). Toda alteração revalida os 5.
- Jogador sai antes do bloqueio → removido da escalação, inscrição incompleta, notificar. Depois do bloqueio → escalação irregular, substituição administrativa; sem 5 válidos → W.O. Partidas já disputadas preservam o jogador antigo.
- **Check-in do time:** dono, sublíder (ou capitão da escalação se habilitado no camp). Revalida os 5.
- Elenco geral ≠ escalação: escalações são independentes por competição.

## 22-26. Amizades e bloqueio
Solicitações bilaterais (nunca sem aceite): Pendente/Aceita/Recusada/Cancelada/Bloqueada. Remetente cancela enquanto pendente. Aceitar → amizade bidirecional. Remover: qualquer parte, sem aprovação, históricos preservados. **Bloqueio:** remove amizade, cancela pendências, impede novas solicitações/convites/mensagens, limita perfil; desbloqueável.

## 27-30. Perfis
- **Perfil do jogador** (clicando em nome/foto): foto, nick, país, descrição, links, Steam ID, time atual + cargo, amigos em comum, histórico de times, campeonatos, colocações, partidas, estatísticas, badges, punições públicas, status online, data da conta — respeitando privacidade. Ações contextuais: adicionar/aceitar/remover amizade, bloquear, **convidar pro time (só donos elegíveis)**, denunciar, ver time/estatísticas.
- **Perfil do time:** nome, tag, escudo, descrição, país, criação, dono, sublíderes, elenco, histórico de membros, campeonatos, títulos, partidas, estatísticas, ranking, recrutamento. Ações por papel: visitante (solicitar entrada, seguir, denunciar), membro (sair, área interna), sublíder (inscrever, escalação, check-in), dono (tudo).

## 31. Notificações
Convite, solicitação aceita/recusada, entrou/saiu/removido, promoção/rebaixamento, transferência de dono, inscrição, alteração de escalação, abertura de check-in, amizade, bloqueio, desclassificação. In-app agora; e-mail/push futuro.

## 32. Auditoria
Toda ação relevante gera log: ação, responsável, afetado, data/hora, valor anterior, valor novo, justificativa. Cobre: criação/edição do time, entradas/saídas/remoções, convites, promoções, troca de dono, inscrições, escalações, exclusão.

## 33-35. Estados
- **Time:** ativo, inativo, suspenso, bloqueado, excluído, em análise. Só ativo joga/inscreve/convida. Não exclui com camp ativo/partida pendente/punição/premiação/disputa — bloqueia até resolver. Exclusão preserva histórico; nome/tag reservados por período.
- **Membro-time:** ativo, convite pendente, solicitação pendente, removido, saiu, suspenso, bloqueado. Histórico nunca apagado.

## 36. Casos especiais
Dono banido → transfere ao sublíder mais antigo, senão membro mais antigo. Sublíder banido → perde permissões. Inscrito banido antes do camp → escalação inválida, corrigir no prazo; depois do bloqueio → decisão administrativa. Time sem membros não permanece ativo.

## 43. Regra central de segurança
**Todas as permissões validadas no backend.** A UI nunca é a única barreira: o servidor verifica identidade, cargo, vínculo, situação do time/competição, prazo, elegibilidade e permissão específica em toda rota.
