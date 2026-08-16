[← Sumário](00-indice.md)

# Apêndices

## Apêndice A — Glossário

| Termo | Significado neste projeto |
|---|---|
| **Summit** | Nome do produto (a pasta no disco ainda se chama `Wallbang`, o nome anterior — ver [§1.2](01-visao-geral.md#12-de-onde-veio-o-nome)) |
| **Dono** (`TeamRole.Captain`) | Cargo único e obrigatório de um time — não confundir com "capitão da escalação" |
| **Sublíder** (`TeamRole.ViceCaptain`) | Cargo intermediário de um time, promovido/rebaixado pelo dono |
| **Capitão da escalação** (`TournamentTeam.CaptainUserId`) | Papel *por campeonato*, entre os 5 escalados — pode ser qualquer um deles, dono ou não |
| **Escalação / Lineup** | Os 5 jogadores que representam o time num campeonato específico ([Cap. 17](17-feature-escalacao.md)) |
| **Elenco** | Todos os membros do time, sem limite de 5 — distinto da escalação |
| **Chave / Bracket** | A estrutura eliminatória de um campeonato ([Cap. 18](18-feature-bracket.md)) |
| **Upper / Lower / Grande Final** | As três sub-chaves de uma eliminação dupla (`BracketSide`) |
| **Veto** | Sequência de bans/picks de mapa antes de uma partida ([Cap. 19](19-feature-veto.md)) |
| **Decider** | O mapa final de uma série, decidido por eliminação (nunca escolhido diretamente) |
| **Pool (de servidores)** | Servidores CS2 mantidos sempre ligados/prontos para atribuição rápida via RCON ([Cap. 20](20-feature-pool-servidores.md)) |
| **Cold-boot** | Criar uma instância EC2 nova do zero (60-120s+) — o caminho lento, usado como fallback do pool |
| **RCON** | Protocolo de administração remota do motor Source, usado para trocar mapa/senha sem recriar a instância |
| **GSLT** | Game Server Login Token — credencial exigida pela Steam para um servidor dedicado autenticar |
| **AMI** | Amazon Machine Image — o "molde" de disco usado para criar instâncias EC2 já com CS2 instalado |
| **MatchZy** | Plugin de partida (via CounterStrikeSharp) instalado no servidor CS2 — warmup, placar, webhook de resultado |
| **`EnsureCreated`** | Método do EF Core que cria o schema se não existir, sem suporte a migrations incrementais ([§4.2](04-banco-dados.md#42-a-decisão-consciente-de-não-usar-migrations)) |
| **DTO de apresentação local** | Classe auxiliar de uma tela específica (`MatchListItem`, `ScoreboardRow`, etc.), não compartilhada com a API ([§7.2](07-client-models.md#72-por-que-existem-dtos-de-apresentação-separados-dos-modelos-compartilhados)) |

## Apêndice B — Convenções de Código

- **Ids**: sempre `string`, gerados como `$"{prefixo}_{Guid.NewGuid():N}"` (ex. `usr_`, `team_`,
  `trn_`, `bm_`, `rnd_`, `m_`, `mp_`, `fr_`, `inv_`, `jrq_`, `lp_`, `veto_`, `vst_`, `aud_`,
  `pool_`). Nunca `int IDENTITY`.
- **Enums**: sempre com valores numéricos explícitos (`Pending = 0, Accepted = 1, ...`) e nunca
  reordenados depois de existir dado gravado (ver [§4.6](04-banco-dados.md#46-cuidado-ao-mexer-em-enums-persistidos)).
- **Repositórios do client**: um método por endpoint, sem lógica de negócio — só tradução para
  URL/corpo (ver [§3.2](03-padroes-projeto.md#32-repository-pattern-client--uma-classe-http-por-área)).
- **Services do client**: onde mora a checagem "isso faz sentido para o usuário atual?" antes de
  delegar ao repositório.
- **ViewModels**: sempre herdam `BaseViewModel`; construtor monta `RelayCommand`s e dispara
  `_ = LoadAsync()` sem esperar.
- **Endpoints da API**: `record` posicional para o corpo de requisição; `Results.Ok`/`BadRequest(string)`/
  `Forbid()` conforme o tipo de falha (ver [§10.12](10-backend-endpoints.md#1012-padrão-de-resposta-http-usado-nas-rotas-de-ação)).
- **Validação**: sempre repetida no backend, mesmo quando o client já checou (ver
  [§3.7](03-padroes-projeto.md#37-validação-sempre-no-backend-nunca-só-no-client)) — a única fonte de verdade é a API.
- **Auditoria**: toda ação administrativa relevante termina com `await CompetitionEndpoints.Audit(...)`,
  antes do `SaveChangesAsync()` final (não chama `SaveChangesAsync` sozinha).
- **Background workers**: sempre `IServiceScopeFactory` (nunca `ApiDbContext` direto no
  construtor), sempre `try/catch` ao redor do corpo do tick, sempre `Task.Delay(..., ct)`.
- **Comentários no código**: curtos, no topo de um método ou classe, explicando o *porquê* não
  óbvio (uma decisão de escopo, uma armadilha já encontrada) — não o *o quê* (o nome do método já
  diz isso).

## Apêndice C — Roadmap Consolidado

Resumo do estado de cada área, espelhando `docs/pendencias.md` no momento em que este livro foi
escrito:

### Completo e testado
- Login Steam real + sessão persistida; onboarding mínimo; edição de perfil completa.
- Time: criar, convidar, aceitar/recusar convite, sair (com transferência automática de
  propriedade), solicitação de entrada, promover/rebaixar/transferir, editar, excluir, kick.
- Amizades: pedido/aceite/recusa/remoção, bloqueio/desbloqueio, amigos em comum.
- Auditoria (somente leitura).
- Campeonato: inscrição com escalação, check-in automático com remoção de ausentes.
- Escalação: seleção de 5 + capitão, editável até o check-in abrir.
- Chave: geração flexível (qualquer tamanho de time), eliminação simples e dupla, renderização
  genérica sem linhas conectoras.
- Veto: MD1/MD3/MD5 completos, criação automática de sala.
- Servidor CS2 real (AWS): cold-boot funcional, pool de servidores quentes com RCON, fallback
  automático, CounterStrikeSharp + MatchZy validados ao vivo.

### Decisões conscientes de escopo (não é falta, é opção)
- Sistema Suíço: não implementado (só enum existe).
- Denúncia de perfil: fora de escopo (exigiria fila de moderação administrativa).
- Exclusão de time sem validar campeonato ativo: simplificação deliberada.
- "Modo alpha" da escalação (aceita elenco < 5): acomodação temporária para poucos usuários de
  teste, não a regra final de produto.

### O maior gap conhecido — pós-partida ([Capítulo 21](21-feature-pos-partida-gaps.md))
- Sem endpoint de resultado de partida.
- Chave não avança automaticamente além da primeira rodada.
- Sem no-show/W.O. monitorado dentro do servidor.
- Badges nunca concedidas automaticamente.
- Campeonato nunca encerra sozinho (sem campeão/vice registrados automaticamente).
- Notificações: não existe nenhum sistema de notificação (in-app ou outro) — toda mudança de
  estado só aparece se o usuário recarregar a tela manualmente.

---

*Fim do livro. Para o estado mais atual e vivo do roadmap, sempre prefira `docs/pendencias.md` no
repositório — este livro descreve a arquitetura e a lógica de forma duradoura, mas a lista de
pendências muda com o ritmo de desenvolvimento.*
