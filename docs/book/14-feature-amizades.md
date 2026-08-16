[← Sumário](00-indice.md)

# Capítulo 14 — Amizades

## 14.1 Uma tabela, um enum, quatro (na prática cinco) significados

Toda relação entre dois jogadores — pedido, aceite, recusa, bloqueio — vive na mesma tabela
`friendships` e no mesmo enum:

```csharp
public enum FriendshipStatus { Pending = 0, Accepted = 1, Declined = 2, Blocked = 3 }
```

Isso é uma decisão de modelagem deliberada: em vez de uma tabela separada `blockedusers`, o
bloqueio **reaproveita** a mesma linha de amizade (se já existir uma) ou cria uma nova já
diretamente com `Status = Blocked`:

```csharp
// Summit.Api/Program.cs — POST /api/friends/block
var existing = await db.Friendships.FirstOrDefaultAsync(x => /* nos dois sentidos */);
if (existing != null)
{
    existing.Status = FriendshipStatus.Blocked;
    existing.RespondedAt = DateTime.UtcNow;
}
else
{
    db.Friendships.Add(new Friendship { /* ... Status = FriendshipStatus.Blocked */ });
}
```

A vantagem prática: bloquear alguém que já era seu amigo **substitui** a amizade existente
automaticamente (não sobra uma linha "Accepted" e outra "Blocked" competindo) — é a mesma
linha que muda de status. A desvantagem: a tabela não guarda *quem* bloqueou quem de forma
inequívoca quando parte de um par pré-existente é reaproveitada — `RequesterId`/`AddresseeId`
continuam refletindo quem pediu a amizade *originalmente*, não necessariamente quem decidiu
bloquear. Isso não causa bug hoje porque nenhuma tela distingue "quem bloqueou" de "quem foi
bloqueado" (o efeito do bloqueio é simétrico em tudo que o sistema faz hoje), mas seria uma
limitação real se um dia o produto precisasse, por exemplo, mostrar "você bloqueou X" de forma
diferente de "X te bloqueou".

## 14.2 A consulta de relação: sempre nos dois sentidos

Como uma amizade pode ter sido originada por qualquer um dos dois lados, toda consulta que
pergunta "qual é a relação entre A e B" precisa checar as duas ordens possíveis:

```csharp
var f = await db.Friendships.FirstOrDefaultAsync(x =>
    (x.RequesterId == viewerId && x.AddresseeId == otherId) ||
    (x.RequesterId == otherId && x.AddresseeId == viewerId));
```

Esse padrão se repete em `GET /api/friends/relation`, `POST /api/friends/block`,
`POST /api/friends/request` (para recusar duplicata) e `DELETE /api/friends`. Se você for
escrever uma consulta nova sobre `friendships`, replique esse padrão de "OR nos dois sentidos" —
esquecê-lo é a fonte mais provável de um bug do tipo "amizade que devia aparecer bloqueada não
aparece" dependendo de quem pediu a amizade originalmente.

`GET /api/friends/relation` devolve a direção certa olhando quem é o `viewerId`:

```csharp
if (f.Status == FriendshipStatus.Pending)
    return Results.Ok(f.RequesterId == viewerId ? "OutgoingPending" : "IncomingPending");
```

Do lado do client, `FriendshipRepository.RelationStatus` (`None`/`Friends`/`OutgoingPending`/
`IncomingPending`/`Blocked`) espelha exatamente essas cinco strings possíveis via
`Enum.TryParse` — ver a ressalva sobre esse contrato "por nome de string" em
[§8.1](08-client-services-repos.md#81-repositórios-data).

## 14.3 Lista de amigos: união de dois sentidos, não um `JOIN` de tabela própria

```csharp
app.MapGet("/api/friends/{userId}", async (ApiDbContext db, string userId) =>
{
    var asRequester = db.Friendships.Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Accepted).Select(f => f.Addressee!);
    var asAddressee = db.Friendships.Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Accepted).Select(f => f.Requester!);
    return Results.Ok(await asRequester.Concat(asAddressee).OrderBy(u => u.Nickname).ToListAsync());
});
```

Duas subconsultas — "amizades aceitas onde eu sou quem pediu" (pega o `Addressee`, o outro lado)
e "amizades aceitas onde eu sou quem recebeu o pedido" (pega o `Requester`, o outro lado) — unidas
com `Concat`. Isso continua sendo traduzido para SQL pelo EF Core como uma única consulta (não
são duas idas ao banco), graças ao provider LINQ-to-Entities.

## 14.4 Amigos em comum: cálculo client-side

Já descrito em [§12.4](12-feature-conta-login.md#124-perfil-de-outro-jogador-playerprofileviewmodel--o-mesmo-dado-contexto-diferente):
não existe endpoint dedicado — o `PlayerProfileViewModel` busca a lista completa de amigos dos
dois usuários e faz a interseção em memória (`HashSet` + `.Where(Contains)`). Reforçando aqui o
trade-off: simples de implementar, correto, mas transfere mais dados pela rede do que um endpoint
dedicado faria (`GET /api/friends/mutual?a=&b=` que fizesse a interseção no banco).

## 14.5 O fluxo completo em `FriendsViewModel`

A tela de Amigos tem quatro abas (`ActiveTab` 0-3: Amigos, Recebidos, Enviados, Convites de
Time), cada uma carregada de uma vez só no `LoadAsync()` inicial — não há carregamento sob
demanda por aba, todas as quatro listas são buscadas de uma vez sempre que a tela abre ou uma
ação é concluída:

```csharp
private async Task LoadAsync()
{
    Friends     = await _friendRepo.GetFriendsAsync(me.Id);
    Incoming    = await _friendRepo.GetIncomingRequestsAsync(me.Id);
    Outgoing    = await _friendRepo.GetOutgoingRequestsAsync(me.Id);
    TeamInvites = await _teamRepo.GetInvitationsForUserAsync(me.Id);
}
```

Note que a quarta aba (**Convites de Time**) não é uma "amizade" de forma nenhuma — é reaproveitar
a mesma tela para também mostrar convites de time pendentes (`TeamInvitation`, ver
[Capítulo 13](13-feature-times.md)), porque ambos são "coisas que outra pessoa te mandou e você
precisa aceitar/recusar" do ponto de vista de UX, mesmo sendo dois domínios de dado
completamente diferentes por trás. `AcceptTeamInviteCommand`/`DeclineTeamInviteCommand` nessa
tela chamam `App.TeamService`, não `FriendshipRepository`.

Buscar um amigo por nickname exato (`SendRequestAsync`) valida dois casos triviais antes de
mandar o pedido — não pode adicionar a si mesmo, e o alvo precisa existir:

```csharp
var target = await _userRepo.GetByNicknameAsync(SearchNickname.Trim());
if (target == null) { SearchMessage = "Jogador não encontrado."; return; }
if (target.Id == me.Id) { SearchMessage = "Você não pode adicionar a si mesmo."; return; }
```

A segunda checagem (`target.Id == me.Id`) só existe no client — o endpoint
`POST /api/friends/request` também tem a mesma checagem (`if (req.RequesterId == req.AddresseeId)
return Results.Ok(false)`), então mesmo que o client pulasse essa validação, o backend recusaria
de qualquer forma (reforçando a regra de "nunca confiar só na validação do client", ver
[§3.7](03-padroes-projeto.md#37-validação-sempre-no-backend-nunca-só-no-client)).

## 14.6 O que a especificação previa e não foi implementado

`docs/espec-times.md §22-26` menciona "denúncia de perfil" como parte do fluxo de
amizades/perfis. Essa peça foi **deixada de fora conscientemente** — não existe fila de
moderação/revisão administrativa no sistema hoje, e implementar denúncia sem ter para onde ela
vai (um painel de admin, um processo de revisão) seria construir metade de uma feature. Isso está
documentado como decisão de escopo em `docs/pendencias.md`, não como um esquecimento.
