[← Sumário](00-indice.md)

# Capítulo 12 — Conta, Login e Perfil

## 12.1 Visão de ponta a ponta

```
LoginView ──clica "Entrar com Steam"──▶ SteamAuthService.LoginWithSteamAsync()
                                              │ (navegador abre, usuário loga na Steam)
                                              ▼
                                   POST /api/users/steam-login (upsert)
                                              │
                                              ▼
                          MainShellViewModel decide: tem User.Country vazio?
                              ├─ sim ──▶ OnboardingViewModel (bem-vindo, país + role)
                              └─ não ──▶ HomeViewModel direto
```

O mecanismo técnico completo do login (OpenID, validação com a Steam, restauração de sessão,
modo demo) já foi explicado em
[§6.4](06-client-navegacao-api.md#64-autenticação-steamauthservice-steamconfig-steamwebapiclient) —
este capítulo foca na parte de produto: o que acontece **depois** do login (onboarding) e a tela
de Perfil em si.

## 12.2 Onboarding — o gate do primeiro login

`MainShellViewModel`, ao construir, decide para onde navegar primeiro com uma única condição:

```csharp
if (string.IsNullOrWhiteSpace(App.UserService.CurrentUser?.Country))
    Navigate(new OnboardingViewModel(), "BEM-VINDO");
else
    Navigate(new HomeViewModel(), "HOME");
```

`Country` vazio é usado como o sinal de "usuário nunca completou o onboarding" — não existe um
campo booleano dedicado tipo `HasCompletedOnboarding`. Isso é simples e funciona porque nenhum
outro fluxo do sistema define `Country` sozinho antes do onboarding (nem o `SteamAuthService`, que
só preenche `Nickname`/`AvatarUrl` a partir da Steam). A implicação prática: se algum dia alguém
adicionar um jeito de definir `Country` em outro lugar do fluxo de criação de conta, esse gate de
onboarding pararia de funcionar silenciosamente (todo mundo pularia direto para a Home).

`OnboardingViewModel` é propositalmente mínimo — só dois campos, país e função principal — e o
botão continuar só habilita quando os dois estão preenchidos:

```csharp
ContinueCommand = new RelayCommand(async _ => await ContinueAsync(),
    _ => !string.IsNullOrWhiteSpace(Country) && !string.IsNullOrWhiteSpace(Role));

private async Task ContinueAsync()
{
    await App.UserService.UpdateCountryAsync(Country.Trim());
    await App.UserService.UpdateRoleAsync(Role.Trim());
    App.Navigation.NavigateTo(new HomeViewModel());
}
```

Note que a navegação para a Home usa `NavigateTo` diretamente (não passa de novo pelo
`MainShellViewModel`) — o onboarding, uma vez completo, simplesmente troca a tela atual, sem
re-executar a checagem do construtor do shell.

## 12.3 A tela de Perfil (`ProfileViewModel`)

Segue o padrão de edição inline já descrito em
[§5.5](05-client-mvvm.md#55-estado-de-edição-inline-padrão-editarsalvarcancelar) — campos de
leitura (`Bio`, `PrimaryRole`, `AvatarUrl`, `Country`) espelhados por campos de rascunho
(`EditBio`, `EditRole`, `EditAvatarUrl`, `EditCountry`) que só substituem os originais quando
`SaveAsync` é confirmado. Os quatro campos editáveis viram quatro chamadas separadas ao
`UserService`:

```csharp
private async Task SaveAsync()
{
    await App.UserService.UpdateBioAsync(EditBio);
    await App.UserService.UpdateRoleAsync(EditRole);
    await App.UserService.UpdateAvatarUrlAsync(EditAvatarUrl);
    await App.UserService.UpdateCountryAsync(EditCountry);
    IsEditing = false;
    // ... notifica as propriedades de leitura que mudaram
}
```

Cada um desses quatro métodos do `UserService` faz seu próprio `PUT /api/users/{id}` completo
(mandando o objeto `User` inteiro, não um PATCH parcial — ver
[§10.1](10-backend-endpoints.md#101-users-programcs)) — ou seja, salvar o perfil dispara **quatro
requisições HTTP sequenciais**, não uma só. Isso funciona, mas é um candidato natural de
otimização futura (por exemplo, um único `UpdateProfileAsync(bio, role, avatarUrl, country)` que
fizesse só uma chamada) se a tela crescer com mais campos editáveis.

As estatísticas mostradas na tela (`WinRateText`, `KDText`, `Matches`, `Wins`, `FavoriteMap`) são
lidas direto de `App.UserService.CurrentUser` — são os mesmos campos agregados discutidos em
[§4.4](04-banco-dados.md#44-tabela-por-tabela) (tabela `users`), hoje só populados pelo
`SeedData` (ver a ressalva sobre isso no [Capítulo 21](21-feature-pos-partida-gaps.md)).

## 12.4 Perfil de outro jogador (`PlayerProfileViewModel`) — o mesmo dado, contexto diferente

`PlayerProfileViewModel` mostra o perfil de **qualquer** usuário (não necessariamente o atual),
recebendo o `userId` alvo no construtor. A diferença central em relação a `ProfileViewModel` é
que aqui existe uma **relação** a considerar entre "quem está olhando" (`App.UserService.CurrentUser`)
e "quem está sendo visto" (`User`, carregado por id):

```csharp
public bool IsSelf          => App.UserService.CurrentUser?.Id == _user?.Id;
public bool CanAddFriend    => !IsSelf && _relation == FriendshipRepository.RelationStatus.None;
public bool IsAlreadyFriend => _relation == FriendshipRepository.RelationStatus.Friends;
public bool HasOutgoing     => _relation == FriendshipRepository.RelationStatus.OutgoingPending;
public bool HasIncoming     => _relation == FriendshipRepository.RelationStatus.IncomingPending;
public bool IsBlocked       => _relation == FriendshipRepository.RelationStatus.Blocked;
```

Essas propriedades controlam quais botões de ação aparecem (adicionar amigo, bloquear,
desbloquear) — a lógica completa de amizade/bloqueio está no [Capítulo 14](14-feature-amizades.md);
este capítulo só nota que o *Perfil* é a superfície de UI onde essas ações também ficam
disponíveis (além da tela dedicada de Amigos).

Amigos em comum são calculados **inteiramente no client**, sem endpoint dedicado:

```csharp
var myFriends = await _friendRepo.GetFriendsAsync(me.Id);
var theirFriends = await _friendRepo.GetFriendsAsync(User.Id);
var theirIds = theirFriends.Select(f => f.Id).ToHashSet();
MutualFriends = myFriends.Where(f => theirIds.Contains(f.Id)).ToList();
```

Isso significa duas chamadas HTTP (a lista de amigos de cada um) mais uma interseção em memória
— simples e correto para o volume de amigos que um jogador tem tipicamente (dezenas, não
milhares), mas seria o primeiro ponto a mover para um endpoint dedicado no servidor
(`GET /api/friends/mutual?a=&b=`) se a lista de amigos crescesse a ponto de tornar duas
transferências completas caras.

## 12.5 O que este capítulo não cobre

Bloqueio/desbloqueio e pedidos de amizade em si são detalhados no
[Capítulo 14](14-feature-amizades.md). Badges mostradas no perfil são só leitura hoje — a lógica
de *quando* uma badge é concedida é um gap conhecido, coberto no
[Capítulo 21](21-feature-pos-partida-gaps.md).
