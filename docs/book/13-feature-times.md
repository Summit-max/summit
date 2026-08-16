[← Sumário](00-indice.md)

# Capítulo 13 — Times

Times são o agregado com mais regras de permissão do sistema. Este capítulo percorre cada
sub-fluxo (criação, entrada, cargos, edição/exclusão) mostrando como client e API se encaixam
para cada um. A tabela de rotas já está em
[§10.2](10-backend-endpoints.md#102-teams-programcs); aqui o foco é o fluxo completo.

## 13.1 Cargos — o vocabulário

```csharp
public enum TeamRole { Member = 0, ViceCaptain = 1, Captain = 2 }
```

Mapeamento para a linguagem de produto (`docs/espec-times.md`): `Captain` = **Dono** (único,
obrigatório), `ViceCaptain` = **Sublíder** (N por time), `Member` = **Membro comum**. Isso é
diferente do "capitão da escalação" (`TournamentTeam.CaptainUserId`), um papel *por campeonato*
que qualquer um dos 5 jogadores selecionados pode assumir, dono ou não — ver
[Capítulo 17](17-feature-escalacao.md). Confundir os dois é o erro conceitual mais fácil de
cometer lendo este código pela primeira vez.

## 13.2 Criação e entrada

Criar um time (`TeamViewModel.CreateTeamAsync` → `TeamService.CreateTeamAsync` →
`POST /api/teams`) promove automaticamente quem cria a `Captain` na mesma operação — não existe
um estado intermediário de "time sem dono".

Depois disso, há **duas vias simétricas** para um jogador entrar em um time existente, ambas
exigindo confirmação das duas partes (nunca automática):

| Via | Quem inicia | Quem aprova | Tela |
|---|---|---|---|
| Convite | Dono do time | O jogador convidado | `FriendsViewModel` (aba "Convites de Time") |
| Solicitação de entrada | O jogador | Dono do time | `TeamProfileViewModel.RequestJoinCommand` → `JoinRequestsViewModel` |

O convite só pode ser enviado pelo dono (`TeamService.InviteByNicknameAsync` checa
`me.TeamRole != TeamRole.Captain && me.TeamRole != TeamRole.ViceCaptain` — na verdade a checagem
do client permite dono *ou* sublíder tentar, mas o endpoint `POST /api/teams/{teamId}/invite`
recusa se `inviter.TeamRole != TeamRole.Captain`, ou seja, **só o dono de fato consegue**, mesmo
que o botão do client não distinga isso visualmente para o sublíder). Isso é um pequeno
descompasso entre a UI (que parece permitir ao sublíder abrir o painel de convite,
`CanInvite => TeamId != null && (IsCaptain || IsViceCaptain)`) e a regra real do backend (só
`Captain`) — na prática o sublíder veria a mensagem de erro genérica
`"Não foi possível convidar: jogador não encontrado ou já tem time."` mesmo com um nickname
válido, porque o `BadRequest()` do endpoint não distingue os dois motivos de falha para o client.

Aceitar um convite ou solicitação **cancela automaticamente** qualquer outra pendência do mesmo
tipo que o jogador tinha em aberto — impedindo o cenário de alguém aceitar dois convites de times
diferentes e acabar "em dois lugares".

## 13.3 Promover, rebaixar, transferir propriedade

Todas as três exigem `IsOwner` no backend (ver
[§10.8](10-backend-endpoints.md#108-competitionendpointscs--cargos)). No client, a visibilidade
de cada botão por linha de membro é controlada inteiramente em XAML, com `MultiDataTrigger`
combinando o cargo de quem está olhando com o cargo do membro daquela linha — vale a pena estudar
o trecho real (`Views/TeamView.xaml`) porque é o uso mais elaborado de `MultiDataTrigger` do
projeto:

```xml
<!-- Botão PROMOVER: só aparece se eu sou o dono E o alvo não é dono nem sublíder -->
<Style TargetType="Button">
    <Setter Property="Visibility" Value="Collapsed"/>
    <Style.Triggers>
        <MultiDataTrigger>
            <MultiDataTrigger.Conditions>
                <Condition Binding="{Binding DataContext.IsMyTeamCaptain, RelativeSource={RelativeSource AncestorType=UserControl}}" Value="True"/>
                <Condition Binding="{Binding IsCaptain}" Value="False"/>
                <Condition Binding="{Binding IsViceCaptain}" Value="False"/>
            </MultiDataTrigger.Conditions>
            <Setter Property="Visibility" Value="Visible"/>
        </MultiDataTrigger>
    </Style.Triggers>
</Style>
```

Note a mistura de dois contextos de binding na mesma condição: `IsMyTeamCaptain` vem do
`DataContext` do `UserControl` inteiro (o `TeamViewModel`, acessado via
`RelativeSource={RelativeSource AncestorType=UserControl}` porque dentro do `DataTemplate` de
cada membro o `DataContext` local já é o próprio `User` da linha, não o ViewModel da tela) — mas
`IsCaptain`/`IsViceCaptain` (sem esse `RelativeSource`) vêm do próprio `User` daquela linha
específica. Essa combinação de "uma condição olha para o ViewModel da tela, a outra olha para o
item da linha" é o que garante que os botões de ação **nunca aparecem na própria linha do dono**
(porque a segunda condição, `IsCaptain == False`, já exclui a linha dele) e **nunca aparecem para
quem não é dono** (a primeira condição). O botão REBAIXAR usa a mesma estrutura, trocando a
segunda condição para `IsViceCaptain == True` (só existe o que rebaixar se o alvo já é sublíder).

Do lado do backend, cada ação valida o estado atual do alvo antes de agir:

```csharp
// promover: só quem é Member vira ViceCaptain (recusa se já é ViceCaptain/Captain)
if (target == null || target.TeamRole != TeamRole.Member) return Results.BadRequest();

// rebaixar: só quem é ViceCaptain volta a Member
if (target == null || target.TeamRole != TeamRole.ViceCaptain) return Results.BadRequest();
```

Transferir propriedade é a única ação que muda **dois** usuários na mesma operação: o alvo vira
`Captain`, e quem era dono antes vira `ViceCaptain` (nunca `Member` — ele não perde todo o
privilégio, só desce um degrau):

```csharp
newOwner.TeamRole = TeamRole.Captain;
oldOwner.TeamRole = TeamRole.ViceCaptain;
team.CaptainId = newOwner.Id;
```

## 13.4 Saída do dono — a regra "o time nunca fica sem dono"

Já detalhada em [§10.2.1](10-backend-endpoints.md#1021-saída-do-dono-a-rota-mais-elaborada-do-domínio-de-times).
Vale reforçar aqui a hierarquia de decisão completa (`docs/espec-times.md §12-13`), porque é a
regra de negócio mais sofisticada de todo o domínio de Times:

1. Time tem outros membros? → o próximo dono é escolhido automaticamente, nesta ordem de
   prioridade: **sublíder há mais tempo no cargo** → **membro mais antigo no time** →
   **desempate por id** (determinístico, nunca aleatório).
2. Time não tem mais ninguém além do dono saindo? → o time inteiro é excluído.

Isso acontece **inteiramente no servidor**, dentro de `POST /api/teams/leave/{userId}` — o client
(`TeamViewModel.LeaveTeamAsync` → `TeamService.LeaveTeamAsync`) não participa dessa decisão, só
chama o endpoint e recarrega o estado depois.

## 13.5 Editar e excluir o time

`PUT /api/teams/{id}` (editar) e `DELETE /api/teams/{id}` (excluir) são ambos owner-only e
seguem o padrão de edição inline/confirmação em duas etapas já descrito em
[§5.5](05-client-mvvm.md#55-estado-de-edição-inline-padrão-editarsalvarcancelar) para editar, e um
padrão de **confirmação explícita separada** para excluir:

```csharp
DeleteTeamCommand        = new RelayCommand(_ => ConfirmingDelete = true, _ => IsMyTeamCaptain);
ConfirmDeleteTeamCommand = new RelayCommand(async _ => await DeleteTeamAsync());
CancelDeleteTeamCommand  = new RelayCommand(_ => ConfirmingDelete = false);
```

O primeiro clique só liga um booleano (`ConfirmingDelete = true`), que revela um painel de aviso
vermelho no XAML (`"EXCLUIR O TIME? ESSA AÇÃO NÃO PODE SER DESFEITA."`) com dois botões — só o
segundo clique de fato chama a API. Essa é a única ação destrutiva do domínio de Times que tem
essa confirmação de duas etapas na UI (comparado a "Sair do time" ou "Remover jogador", que
disparam direto no primeiro clique) — uma escolha consciente de UX proporcional ao dano
("excluir o time" afeta todos os membros e todo o histórico associado; "sair" afeta só quem
clicou).

A exclusão em si, do lado da API, é **simples e sem validação de estado do campeonato** — isto é
uma decisão de escopo documentada (`docs/pendencias.md`): um time pode ser excluído mesmo que
esteja inscrito ou disputando um campeonato ativo no momento. A especificação completa
(`docs/espec-times.md §33`) previa bloquear a exclusão nesse caso ("não exclui com campeonato
ativo/partida pendente"), mas essa validação não foi implementada — ficou marcada como
simplificação consciente para não expandir o escopo no momento em que este recurso foi
construído.

```csharp
app.MapDelete("/api/teams/{id}", async (ApiDbContext db, string id, string byUserId) =>
{
    if (!await CompetitionEndpoints.IsOwner(db, id, byUserId)) return Results.Forbid();
    var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id);
    if (team == null) return Results.NotFound();

    var members = await db.Users.Where(u => u.TeamId == id).ToListAsync();
    foreach (var m in members) { m.TeamId = null; m.TeamRole = TeamRole.Member; m.TeamJoinedAt = null; }
    db.Teams.Remove(team);
    // ...
});
```

## 13.6 Remover jogador (kick)

`POST /api/teams/{teamId}/kick`, owner-only, com uma checagem explícita para impedir o dono de
se auto-remover por esse caminho (ele precisa usar "sair do time" ou "transferir propriedade"
primeiro):

```csharp
if (body.UserId == body.ByUserId) return Results.BadRequest("O dono não pode remover a si mesmo.");
```

Esta rota substituiu um stub antigo que existia em `TeamService` (`RemoveMemberAsync` sempre
devolvia `true` sem fazer nada de verdade) — o stub foi removido junto com a implementação real
sendo adicionada, incluindo a declaração correspondente que havia ficado não-usada em
`ITeamService`.

## 13.7 Histórico de auditoria do time

O botão "HISTÓRICO" em `TeamView.xaml` (visível para qualquer membro, não só o dono) navega para
`AuditLogViewModel(teamId: Team.Id)`, que é somente leitura — ver [Capítulo 15](15-feature-auditoria.md)
para o mecanismo completo de auditoria. Toda ação deste capítulo (promover, rebaixar, transferir,
editar, excluir, kick, entrada por convite/solicitação) grava uma linha correspondente, com o
`action` como uma string livre e legível (`"member_promoted"`, `"ownership_transferred"`,
`"team_deleted"`, etc.) — ver a lista completa de ações auditadas no
[Capítulo 15](15-feature-auditoria.md#152-catálogo-de-ações-auditadas-hoje).
