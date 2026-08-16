[← Sumário](00-indice.md)

# Capítulo 3 — Padrões de Projeto Usados

Este capítulo cataloga os padrões arquiteturais recorrentes no sistema. A ideia é que, depois de
lê-lo, você reconheça a "forma" de qualquer arquivo novo que abrir no projeto, porque ele vai
seguir um desses moldes.

## 3.1 MVVM no client (Model-View-ViewModel)

O client é WPF clássico com MVVM manual (sem framework como Prism ou CommunityToolkit.Mvvm —
tudo escrito à mão, poucas classes de suporte). As três camadas:

- **Model** (`Models/*.cs`): dados puros, compartilhados com a API (ver Capítulo 2). Podem ter
  propriedades computadas em C# (getters sem setter) para lógica de exibição que não depende de
  estado de UI — ex. `Tournament.StatusLabel`, `Tournament.CountdownLabel`.
- **View** (`Views/*.xaml` + `.xaml.cs`): XAML puro de layout/estilo, com o `.xaml.cs` quase
  vazio — normalmente só o construtor chamando `InitializeComponent()`. Toda lógica de
  apresentação mora no ViewModel, nunca no code-behind.
- **ViewModel** (`ViewModels/*.cs`): estado da tela + comandos. Toda classe herda de
  `BaseViewModel` e expõe propriedades com `SetProperty` (dispara `INotifyPropertyChanged`) e
  comandos como `RelayCommand`.

A base de tudo:

```csharp
// ViewModels/BaseViewModel.cs
public abstract class BaseViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
```

`SetProperty` é o idioma que se repete em toda propriedade de todo ViewModel do projeto:

```csharp
private bool _isLoading;
public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
```

Quando uma propriedade depende de outra (ex. `HasNone` depende de `IsLoading` e `Logs.Count`), o
padrão é disparar manualmente o `OnPropertyChanged` da propriedade derivada dentro do setter da
propriedade base:

```csharp
// ViewModels/AuditLogViewModel.cs
public List<AuditLog> Logs { get => _logs; set { SetProperty(ref _logs, value); OnPropertyChanged(nameof(HasNone)); } }
public bool IsLoading { get => _isLoading; set { SetProperty(ref _isLoading, value); OnPropertyChanged(nameof(HasNone)); } }
public bool HasNone => !IsLoading && Logs.Count == 0;
```

Não existe nenhum mecanismo automático de dependência entre propriedades (como teria um
framework reativo) — é tudo explícito, o que é mais verboso mas também mais fácil de seguir sem
"mágica": você lê o setter e vê exatamente quais outras propriedades ele afeta.

### 3.1.1 Comandos: `RelayCommand`

```csharp
// Commands/RelayCommand.cs
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) { ... }
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute()) { }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}
```

Toda ação clicável no XAML (botão, item de lista) vira uma propriedade `RelayCommand` no
ViewModel, inicializada no construtor. O padrão mais comum é uma lambda `async` chamando um
método privado:

```csharp
SaveCommand = new RelayCommand(async _ => await SaveAsync());
```

O segundo construtor (que recebe `Action`/`Func<bool>` sem parâmetro) existe só para não precisar
escrever `_ =>` toda vez que o comando não precisa do parâmetro do clique — é açúcar sintático,
mas usado nos dois estilos dependendo se o comando precisa saber *qual* item foi clicado (ex.
`KickCommand` recebe o `userId` como parâmetro) ou não (ex. `SaveCommand`).

### 3.1.2 Ligação View ↔ ViewModel: DataTemplates implícitos

Não existe navegação de `Window` para `Window`, nem `Frame.Navigate`. A troca de tela dentro do
shell principal funciona porque `App.xaml` registra um `DataTemplate` para cada tipo de
ViewModel, mapeando para a View correspondente:

```xml
<DataTemplate DataType="{x:Type vm:TeamViewModel}">
    <views:TeamView/>
</DataTemplate>
```

Quando um `ContentControl` (no shell principal) recebe um objeto `TeamViewModel` como
`Content`/`DataContext`, o WPF olha essa tabela e renderiza automaticamente a `TeamView` — sem
nenhum código de "qual view mostrar" em lugar nenhum. Ver [Capítulo 6](06-client-navegacao-api.md)
para o mecanismo completo de navegação que usa isso.

## 3.2 Repository Pattern (client) — uma classe HTTP por área

Cada arquivo em `Data/*.cs` é um repositório fino: só sabe montar a URL e o corpo da requisição,
chamando `ApiClient`. Não tem lógica de negócio nenhuma — é tradução pura entre "o que o
ViewModel quer" e "qual endpoint HTTP chamar".

```csharp
// Data/TeamRepository.cs
public class TeamRepository
{
    public Task<Team?> GetByIdAsync(string teamId)
        => ApiClient.GetAsync<Team>($"/api/teams/{teamId}");

    public Task<bool> KickAsync(string teamId, string userId, string byUserId)
        => ApiClient.PostBoolAsync($"/api/teams/{teamId}/kick", new { userId, byUserId });
}
```

Todo repositório é instanciado com `new()` direto onde é usado (não há injeção de dependência do
lado do client) — normalmente como campo `private readonly` no ViewModel ou no Service que o usa:

```csharp
private readonly TeamRepository _repo = new();
```

## 3.3 Service Layer (client) — regra de aplicação acima do repositório

Acima dos repositórios existe uma camada de `Services/*.cs` que adiciona a lógica que depende do
*usuário atual* ou combina mais de uma chamada. Por exemplo, `TeamService.PromoteAsync` não
apenas chama o repositório — ele primeiro confere se quem está pedindo é o capitão:

```csharp
// Services/TeamService.cs
public async Task<bool> PromoteAsync(string teamId, string userId)
{
    var me = App.UserService.CurrentUser;
    if (me?.TeamId == null || !me.IsCaptain) return false;
    return await _repo.PromoteAsync(teamId, userId, me.Id);
}
```

Isso é uma **conveniência de UI, não uma barreira de segurança** — o texto da especificação
(`docs/espec-times.md §43`) é explícito: "todas as permissões validadas no backend. A UI nunca é
a única barreira." A API repete essa mesma checagem de forma independente
(`CompetitionEndpoints.IsOwner`). Se um client malicioso pular o `TeamService` e chamar a API
direto, a API ainda recusa. Essa checagem do lado do client existe só para não fazer uma
requisição HTTP destinada a falhar, e para desabilitar o botão na UI antes mesmo do clique.

Alguns Services têm interface (`Services/Interfaces/ITeamService.cs`, `ITournamentService.cs`,
etc.) e outros não (`RankingService`, por exemplo, não tem interface). Não há um critério rígido
documentado sobre quando criar interface — na prática, os que têm interface são os que existiam
desde as fases iniciais do projeto; os mais novos foram adicionados diretamente como classe
concreta.

## 3.4 Instâncias estáticas ao invés de injeção de dependência (client)

O client **não usa nenhum container de DI**. Todos os services e repositórios "globais" (um por
tipo de dado, compartilhados pelo app inteiro) são criados uma vez em `App.xaml.cs` e expostos
como propriedades estáticas da classe `App`:

```csharp
// App.xaml.cs
public static NavigationService   Navigation         { get; private set; } = null!;
public static UserService         UserService        { get; private set; } = null!;
public static TeamService         TeamService        { get; private set; } = null!;
public static TournamentService   TournamentService  { get; private set; } = null!;
// ...

protected override void OnStartup(StartupEventArgs e)
{
    Navigation        = new NavigationService();
    UserService        = new UserService();
    TeamService        = new TeamService();
    TournamentService  = new TournamentService();
    // ...
    new SplashView().Show();
}
```

Qualquer ViewModel acessa esses services simplesmente escrevendo `App.TeamService.XyzAsync(...)`.
Isso é o equivalente funcional de um container de DI com tudo registrado como *singleton*, só que
sem framework — o "container" é a própria classe `App`. Para um projeto deste tamanho (um client
desktop de usuário único, sem necessidade de trocar implementações em testes automatizados), isso
é suficiente e evita a complexidade extra de configurar um container real. Se o projeto crescer a
ponto de precisar testar ViewModels com mocks, essa é a primeira coisa que precisaria mudar.

**Exemplo genérico** de como isso se compara a um DI container tradicional (não é código do
projeto, é só para contraste):

```csharp
// Com um container de DI (não é assim no Summit):
services.AddSingleton<ITeamService, TeamService>();
// ... e no ViewModel: construtor recebe ITeamService via injeção

// Como é de fato no Summit — acesso direto ao estático:
await App.TeamService.CreateTeamAsync(name, tag);
```

## 3.5 Minimal API (backend) — rotas como funções, não Controllers

A API não usa o padrão MVC com Controllers/Actions. Cada rota é uma chamada
`app.MapGet/MapPost/MapPut/MapDelete` com uma lambda (ou método local) que recebe os serviços que
precisa via *parameter injection* automático do ASP.NET Core:

```csharp
app.MapPut("/api/teams/{id}", async (ApiDbContext db, string id, UpdateTeamRequest req) =>
{
    if (!await CompetitionEndpoints.IsOwner(db, id, req.ByUserId)) return Results.Forbid();
    var team = await db.Teams.FirstOrDefaultAsync(t => t.Id == id);
    if (team == null) return Results.NotFound();
    // ...
    return Results.Ok(team);
});
```

O ASP.NET Core resolve `ApiDbContext` do container de DI, `id` da rota (`{id}`), e `req` do corpo
JSON — tudo por convenção de tipo/nome, sem atributos explícitos na maioria dos casos. Esse é o
motivo de `Program.cs` ter quase 900 linhas: não há divisão em Controllers separados, é tudo
sequencial no mesmo arquivo (mais `CompetitionEndpoints.cs` para o segundo bloco de rotas).
Isso é uma escolha de simplicidade típica de Minimal API em projetos deste porte — funciona bem
até o arquivo ficar grande demais para navegar confortavelmente, ponto em que normalmente se
quebra em múltiplos arquivos de extensão como já foi feito com `CompetitionEndpoints`.

### 3.5.1 O padrão dos DTOs (`record`)

Toda requisição com corpo JSON define um `record` posicional logo no fim do arquivo:

```csharp
record UpdateTeamRequest(string Name, string? Description, string? LogoUrl, string? Country, string ByUserId);
```

`record` é usado (em vez de `class`) porque esses tipos são puramente dados de transporte —
igualdade estrutural e imutabilidade fazem sentido para um corpo de requisição, e a sintaxe
posicional deixa a declaração de uma linha só.

## 3.6 Cliente HTTP tolerante a falha (client → API)

`Services/ApiClient.cs` estabelece um padrão importante: a maioria dos métodos **engole exceção e
devolve um valor default** em vez de propagar o erro:

```csharp
public static async Task<T?> GetAsync<T>(string path)
{
    try
    {
        using var resp = await Http.GetAsync(path);
        if (!resp.IsSuccessStatusCode) return default;
        return await resp.Content.ReadFromJsonAsync<T>(Json);
    }
    catch
    {
        return default;
    }
}
```

Isso significa que se a API estiver fora do ar, uma tela que faz `GetAllAsync()` simplesmente
recebe uma lista vazia — a tela abre "vazia", sem crash, sem mensagem de erro genérica travando o
fluxo. É uma escolha deliberada de robustez para um app desktop: melhor UX degradada (tela vazia)
do que uma exceção não tratada derrubando a janela.

A exceção a essa regra é o login: `PostRequiredAsync` **propaga** a falha, porque ali o usuário
*precisa* saber que algo deu errado (não faz sentido "logar silenciosamente com sucesso vazio"):

```csharp
public static async Task<T> PostRequiredAsync<T>(string path, object? body)
{
    HttpResponseMessage resp;
    try { resp = await Http.PostAsJsonAsync(path, body, Json); }
    catch (Exception ex)
    {
        throw new InvalidOperationException(
            $"Não foi possível conectar à Summit API em {BaseUrl}. A API está rodando?", ex);
    }
    // ...
}
```

E `PutWithMessageAsync` é um meio-termo: nunca lança exceção, mas **carrega a mensagem de erro
real da API** de volta para quem chamou, porque a API por vezes retorna um `BadRequest(string)`
com um motivo específico que vale a pena mostrar ao usuário (ex. "A escalação precisa de
exatamente 5 jogadores."):

```csharp
public static async Task<(bool Ok, string? Message)> PutWithMessageAsync(string path, object? body)
{
    using var resp = await Http.PutAsJsonAsync(path, body, Json);
    var text = await resp.Content.ReadAsStringAsync();
    if (resp.IsSuccessStatusCode) return (true, null);
    return (false, string.IsNullOrWhiteSpace(text) ? null : text.Trim().Trim('"'));
}
```

**Regra geral para saber qual usar ao escrever código novo**: se a falha é "normal" (rede
instável, API momentaneamente fora) e a tela pode se recuperar mostrando vazio, use
`GetAsync`/`PostBoolAsync`. Se o usuário precisa ser bloqueado e informado, use
`PostRequiredAsync`. Se existe uma mensagem de validação específica da API que vale a pena
mostrar, use `PutWithMessageAsync`.

## 3.7 Validação sempre no backend, nunca só no client

Este é citado explicitamente na especificação (`docs/espec-times.md §43`) como "regra central de
segurança", e o código segue isso à risca: toda rota que muda estado (`POST`/`PUT`/`DELETE`)
revalida identidade, cargo e elegibilidade, mesmo que o client já tenha feito uma checagem
equivalente antes de habilitar o botão. Exemplos de helpers reutilizados para isso:

```csharp
// Summit.Api/CompetitionEndpoints.cs
public static async Task<bool> IsOwner(ApiDbContext db, string teamId, string userId)
    => await db.Users.AnyAsync(u => u.Id == userId && u.TeamId == teamId && u.TeamRole == TeamRole.Captain);

public static async Task<bool> IsOwnerOrSub(ApiDbContext db, string teamId, string userId)
    => await db.Users.AnyAsync(u => u.Id == userId && u.TeamId == teamId &&
        (u.TeamRole == TeamRole.Captain || u.TeamRole == TeamRole.ViceCaptain));
```

Praticamente toda rota sensível do sistema começa com uma dessas duas linhas.

## 3.8 Auditoria como efeito colateral padronizado

Toda ação administrativa relevante (promover, remover, editar time, trocar escalação, etc.)
termina com uma chamada ao helper `Audit`:

```csharp
public static Task Audit(ApiDbContext db, string action, string? actor, string? target,
    string? teamId, string? tournamentId, string? oldValue, string? newValue, string? reason)
{
    db.AuditLogs.Add(new AuditLog { Id = $"aud_{Guid.NewGuid():N}", Action = action, ... });
    return Task.CompletedTask;
}
```

Note que `Audit` só *adiciona* a entidade ao `DbContext` — não chama `SaveChangesAsync()` sozinho.
Isso é intencional: o registro de auditoria entra na **mesma transação implícita** da alteração
principal (o próximo `await db.SaveChangesAsync()` do endpoint grava os dois juntos). Isso importa
porque significa que uma auditoria nunca fica "órfã" de uma mudança que falhou antes de salvar —
os dois sempre ou salvam juntos, ou nenhum salva.

## 3.9 Propriedades computadas nos Models compartilhados

Os `Models/*.cs` (compartilhados entre client e API) frequentemente têm propriedades derivadas
(getter-only, sem campo de apoio) que calculam algo a partir de outros campos:

```csharp
// Models/Tournament.cs
public bool IsRegistrationOpen =>
    Status == TournamentStatus.Open && DateTime.UtcNow < RegistrationClosesAt;

public string CountdownLabel { get { /* ... */ } }
```

Do lado da API, o `ApiDbContext.OnModelCreating` explicitamente instrui o EF Core a **ignorar**
essas propriedades no mapeamento do banco (`tour.Ignore(t => t.IsRegistrationOpen);` etc.) — elas
não viram coluna, só existem em memória, calculadas sob demanda toda vez que são lidas. Isso
permite que a mesma lógica de "está aberto para inscrição?" seja calculada de forma idêntica tanto
quando o client recebe o objeto via JSON quanto quando a própria API o usa internamente (por
exemplo, no `LifecycleWorker`), sem duplicar a fórmula em dois lugares.

## 3.10 Padrão de endpoint de debug (dev-only, deliberado)

Espalhados pelo `Program.cs` existem vários `app.MapGet/MapPost("/api/debug/...")` — inspeção de
instâncias EC2, volumes, snapshots, estado do pool, um endpoint para mandar comando RCON ad-hoc,
um para gerar a chave na hora sem esperar o horário automático. Isso é um padrão deliberado deste
ambiente de desenvolvimento: em vez de abrir um console de banco ou entrar na AWS toda vez que
for preciso inspecionar algo, um endpoint HTTP simples resolve com um `curl`. **Esses endpoints
não têm autenticação nem checagem de permissão** — isso é aceitável precisamente porque são
ferramentas de operação/depuração local, não superfície de produto; não devem ser expostos numa
API voltada à internet pública sem revisão.
