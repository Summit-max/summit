[← Sumário](00-indice.md)

# Capítulo 6 — Navegação e Comunicação com a API

## 6.1 `NavigationService` — uma pilha, não um `Frame`

WPF tem um controle de navegação nativo (`Frame`/`Page`), mas o Summit não o usa. Em vez disso,
`Services/NavigationService.cs` implementa uma pilha de histórico manual sobre `BaseViewModel`:

```csharp
public class NavigationService
{
    private BaseViewModel? _currentView;
    private readonly Stack<BaseViewModel> _history = new();

    public BaseViewModel? CurrentView
    {
        get => _currentView;
        private set { _currentView = value; CurrentViewChanged?.Invoke(this, value); }
    }

    public bool CanGoBack => _history.Count > 0;
    public event EventHandler<BaseViewModel?>? CurrentViewChanged;

    public void NavigateTo(BaseViewModel viewModel)
    {
        if (_currentView != null) _history.Push(_currentView);
        CurrentView = viewModel;
    }

    public void GoBack()
    {
        if (_history.Count == 0) return;
        CurrentView = _history.Pop();
    }
}
```

Navegar para a frente empilha o ViewModel atual antes de trocar; voltar desempilha. Não existe
navegação "para frente" depois de voltar (diferente de um browser) — uma vez que você chama
`GoBack()`, o ViewModel que você tinha "avançado" antes é descartado, não fica disponível para
um "avançar" futuro. Isso é suficiente para o produto porque toda a navegação do Summit é
estritamente hierárquica (lista → detalhe → sub-detalhe), sem necessidade de navegação lateral
complexa.

**Ponto de atenção**: `NavigateTo` sempre cria uma **instância nova** do ViewModel de destino
(`new TeamProfileViewModel(id)`), nunca reaproveita uma existente. Isso significa que toda
navegação recarrega os dados do zero (o construtor do novo ViewModel dispara seu próprio
`LoadAsync`, ver [Capítulo 5](05-client-mvvm.md#51-o-ciclo-de-vida-padrão-de-um-viewmodel)) — o
que é o comportamento certo aqui (você quer ver dados frescos ao entrar numa tela de novo), mas
também significa que **nenhum estado de tela sobrevive a uma navegação embora a instância antiga
ainda exista na pilha `_history`** até você voltar para ela (nesse momento ela reaparece com
qualquer estado que tinha antes de você sair, porque o objeto C# nunca foi descartado — só não
estava sendo mostrado).

## 6.2 De ViewModel para tela: `DataTemplate` implícito

Quando `NavigationService.CurrentView` muda, quem realmente decide "o que desenhar na tela" é o
WPF, através da tabela de `DataTemplate`s registrada globalmente em `App.xaml`:

```xml
<DataTemplate DataType="{x:Type vm:TeamProfileViewModel}">
    <views:TeamProfileView/>
</DataTemplate>
```

O shell principal (`MainShellView.xaml`) tem um `ContentControl` cujo `Content` está ligado a
`MainShellViewModel.CurrentView`. Quando esse `Content` é, por exemplo, uma instância de
`TeamProfileViewModel`, o WPF procura na tabela de `DataTemplate`s um template cujo `DataType`
seja esse tipo exato, encontra `TeamProfileView`, instancia e a exibe com
`DataContext = <a instância de TeamProfileViewModel>` automaticamente.

**Checklist para adicionar uma tela nova ao projeto** (o "cerimonial" mínimo, sempre os mesmos
três passos):

1. Criar o ViewModel em `ViewModels/NovaTelaViewModel.cs` (herdando `BaseViewModel`).
2. Criar a View em `Views/NovaTelaView.xaml` + `.xaml.cs` (o `.xaml.cs` só chama
   `InitializeComponent()`).
3. Registrar o par em `App.xaml`:
   ```xml
   <DataTemplate DataType="{x:Type vm:NovaTelaViewModel}">
       <views:NovaTelaView/>
   </DataTemplate>
   ```

Se você esquecer o passo 3, a navegação não vai lançar exceção — o WPF simplesmente não encontra
template e renderiza o `ToString()` do ViewModel como texto puro. Esse é o sintoma mais comum de
"esqueci de registrar o DataTemplate": uma tela que deveria aparecer com UI vira uma linha de
texto crua (o nome completo da classe) na tela.

Opcionalmente, um quarto passo: se a tela precisa de um título específico na barra superior,
adicionar um `case` no `switch` de `MainShellViewModel` (ver
[§5.4](05-client-mvvm.md#54-reatividade-a-eventos-globais-troca-de-usuário-navegação)).

## 6.3 `ApiClient` — referência completa dos métodos

`Services/ApiClient.cs` é o único ponto de saída HTTP do client inteiro — nenhum repositório ou
service chama `HttpClient` diretamente, todos passam por aqui. Um único `HttpClient` estático é
compartilhado por toda a aplicação (a prática recomendada pela Microsoft, para evitar esgotamento
de sockets que aconteceria criando um `HttpClient` novo por requisição):

```csharp
public static readonly string BaseUrl =
    Environment.GetEnvironmentVariable("SUMMIT_API_URL")?.TrimEnd('/') ?? "http://localhost:5180";

private static readonly HttpClient Http = new()
{
    BaseAddress = new Uri(BaseUrl),
    Timeout = TimeSpan.FromSeconds(15)
};
```

Tabela de referência de todos os métodos e quando usar cada um (a lógica de design já foi
explicada em [§3.6](03-padroes-projeto.md#36-cliente-http-tolerante-a-falha-client--api); aqui
está o catálogo completo):

| Método | Verbo | Em falha/erro devolve | Uso típico |
|---|---|---|---|
| `GetAsync<T>(path)` | GET | `default(T)` (ex. `null`, ou lista deve ser tratada com `?? new()` por quem chama) | Buscar qualquer recurso de leitura |
| `PostAsync<T>(path, body)` | POST | `default(T)` | Criar algo e usar o objeto criado de volta (ex. convite) |
| `PostRequiredAsync<T>(path, body)` | POST | **lança `InvalidOperationException`** | Login — falha precisa parar o fluxo e ser mostrada |
| `PostBoolAsync(path, body?)` | POST | `false` | Ações que só importam sucesso/falha (aceitar convite, promover, etc.) |
| `PutAsync<T>(path, body)` | PUT | `default(T)` | Atualizar e receber o objeto atualizado (ex. editar time) |
| `PutWithMessageAsync(path, body)` | PUT | `(false, mensagem da API ou "Erro de conexão.")` | Atualizar quando a API pode recusar com um motivo específico (ex. escalação) |
| `DeleteBoolAsync(path)` | DELETE | `false` | Excluir/remover (time, amizade) |

Todos usam a mesma configuração de serialização:

```csharp
private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
{
    ReferenceHandler = ReferenceHandler.IgnoreCycles
};
```

`ReferenceHandler.IgnoreCycles` existe porque os Models compartilhados têm referências
bidirecionais em memória (`Team.Members` → `User.Team` → de volta ao mesmo `Team`) — sem essa
opção, a serialização JSON entraria em loop infinito na primeira entidade com referência
circular. A API usa a mesma configuração do lado dela
(`builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles)`
em `Program.cs`) — os dois lados precisam concordar nessa opção, ou o formato do JSON trocado
diverge.

### 6.3.1 Exemplo genérico: como escrever um novo método de repositório

Se você precisar adicionar uma chamada nova (não é código real do projeto, é um exemplo de
referência para seguir o padrão):

```csharp
// Um repositório fictício de "convites de campeonato" seguindo o padrão do projeto:
public class TournamentInviteRepository
{
    public Task<bool> SendAsync(string tournamentId, string teamId)
        => ApiClient.PostBoolAsync($"/api/tournaments/{tournamentId}/invite", new { teamId });

    public async Task<List<TournamentInvite>> GetPendingAsync(string teamId)
        => await ApiClient.GetAsync<List<TournamentInvite>>($"/api/teams/{teamId}/tournament-invites") ?? new();
}
```

Note o `?? new()` no segundo método — essa é a convenção do projeto para todo método que retorna
uma `List<T>`: nunca deixar `GetAsync` devolver `null` para quem chama, sempre normalizar para
lista vazia ali mesmo no repositório (evita que todo consumidor precise checar `null` de novo).

## 6.4 Autenticação: `SteamAuthService`, `SteamConfig`, `SteamWebApiClient`

### 6.4.1 O fluxo OpenID (login real via Steam)

O Summit não usa OAuth — usa **Steam OpenID 2.0**, o protocolo legado que a Steam ainda suporta
para "Login com Steam" sem precisar registrar uma aplicação OAuth. O fluxo, implementado em
`Services/SteamAuthService.LoginWithSteamAsync()`:

1. Abre um `HttpListener` local numa porta livre aleatória (`GetFreePort()`), escutando em
   `http://127.0.0.1:{port}/auth/steam/callback/`.
2. Monta a URL de autenticação da Steam (`BuildAuthUrl`) com esse endereço de callback e abre no
   navegador padrão do usuário (`Process.Start` com `UseShellExecute = true`).
3. O usuário faz login no site da Steam normalmente (fora do app — na aba do navegador).
4. A Steam redireciona o navegador de volta para `http://127.0.0.1:{port}/...`, que o
   `HttpListener` local está esperando — ao receber essa requisição, o app já tem a resposta (com
   um timeout de 2 minutos, `LoginTimeout`, caso o usuário nunca complete o login).
5. Os parâmetros `openid.*` da query string são **revalidados diretamente com a Steam**
   (`ValidateWithSteamAsync`, um POST de volta para `steamcommunity.com/openid/login` com
   `openid.mode=check_authentication`) — isso é essencial, porque sem essa segunda chamada
   qualquer um poderia forjar um redirecionamento local alegando ser um SteamID arbitrário.
6. Extrai o SteamID64 da URL `claimed_id` via regex, busca nome/avatar reais
   (`SteamWebApiClient.GetPlayerSummaryAsync`), e faz upsert do usuário na API
   (`UserRepository.UpsertFromSteamAsync`).
7. Salva a sessão localmente (`SessionStore.Save`) para restaurar automaticamente da próxima vez.

```csharp
var claimedId = query["openid.claimed_id"];
var match = SteamIdRegex.Match(claimedId);   // ^https?://steamcommunity\.com/openid/id/(\d{17})$
var steamId64 = match.Groups[1].Value;
if (!await ValidateWithSteamAsync(query)) return null;
```

### 6.4.2 `SteamWebApiClient` — nome e avatar reais, com fallback sem chave de API

Buscar nome de exibição e avatar reais da Steam precisa de uma API Key oficial
(`ISteamUser/GetPlayerSummaries`). Como nem todo ambiente de desenvolvimento tem uma configurada,
`SteamWebApiClient.GetPlayerSummaryAsync` tenta dois caminhos em cascata:

```csharp
public async Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId64)
    => await GetViaWebApiAsync(steamId64)
    ?? await GetViaCommunityXmlAsync(steamId64);
```

1. **Via Web API oficial** (`GetViaWebApiAsync`) — só tenta se `SteamConfig.GetApiKey()` achar
   uma chave configurada; devolve `null` de propósito se não achar (não é erro, é "pula essa
   opção").
2. **Fallback via XML público do perfil** (`GetViaCommunityXmlAsync`) —
   `steamcommunity.com/profiles/{id}?xml=1`, que não precisa de chave nenhuma, mas só funciona
   se o perfil da Steam do usuário for público.

Se os dois falharem (perfil privado + sem API Key), o login ainda funciona — só usa um nome
genérico gerado localmente: `$"Player_{steamId64[^4..]}"` (os 4 últimos dígitos do SteamID64),
visto em `SteamAuthService.LoginWithSteamAsync`.

### 6.4.3 `SteamConfig` — onde a chave de API é lida

```csharp
public static string? GetApiKey()
{
    var env = Environment.GetEnvironmentVariable("SUMMIT_STEAM_API_KEY");
    if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
    // fallback: arquivo %LOCALAPPDATA%\Summit\steam.config
}
```

Mesma filosofia do resto do projeto: variável de ambiente primeiro; se ausente, um arquivo local
opcional (`steam.config`, texto puro com a chave) — útil para configurar a chave uma vez na
máquina de desenvolvimento sem precisar redefinir a variável de ambiente em toda sessão de
terminal nova.

### 6.4.4 `SessionStore` — persistência local da sessão

Um JSON simples em `%LOCALAPPDATA%\Summit\session.json`, guardando só o `SteamId`:

```csharp
public static void Save(string steamId) { /* grava { SteamId, SavedAt } */ }
public static string? Load() { /* lê e devolve SteamId, ou null se não existir/corrompido */ }
public static void Clear() { /* apaga o arquivo (usado no logout) */ }
```

No próximo início do app, `SteamAuthService.TryRestoreSessionAsync()` lê esse SteamId, busca o
`User` correspondente na API (`UserRepository.GetBySteamIdAsync`) e — se achar — já loga
automaticamente sem passar pelo fluxo OpenID de novo. Repare que essa restauração também dispara
`RefreshSummaryAsync` **sem aguardar** (fire-and-forget), que busca nick/avatar atualizados da
Steam em segundo plano e só grava de volta na API se algo realmente mudou — a sessão restaura
instantaneamente com os dados já em cache, e se atualiza silenciosamente depois.

### 6.4.5 Modo demo

`SteamAuthService.LoginDemoAsync()` existe para testar o app sem depender da Steam nem de
internet: cria/atualiza um usuário fixo (`SteamId = "76561198000000000"`, nick `xGhostFrag`) com
estatísticas hardcoded, e loga com ele. É o botão "entrar em modo demo" da tela de login
(`LoginViewModel.LoginDemoCommand`).
