[← Sumário](00-indice.md)

# Capítulo 5 — MVVM na Prática

O [Capítulo 3](03-padroes-projeto.md#31-mvvm-no-client-model-view-viewmodel) já apresentou o
esqueleto de `BaseViewModel` e `RelayCommand`. Este capítulo aprofunda como esses blocos se
combinam no dia a dia, com o ciclo de vida completo de um ViewModel típico.

## 5.1 O ciclo de vida padrão de um ViewModel

Quase todo ViewModel do projeto segue esta receita:

1. Campos privados com estado (`_isLoading`, `_team`, etc.), expostos como propriedades públicas
   via `SetProperty`.
2. Um construtor que:
   a. Instancia os `RelayCommand`s.
   b. Dispara um carregamento assíncrono **sem esperar** (`_ = LoadAsync();`).
3. Um método privado `LoadAsync()` (ou nome equivalente) que marca `IsLoading = true`, busca dados
   via `App.XyzService`/repositório, popula as propriedades, e marca `IsLoading = false`.

Exemplo completo e representativo (`ViewModels/TeamProfileViewModel.cs`):

```csharp
public class TeamProfileViewModel : BaseViewModel
{
    private Team? _team;
    private bool _isLoading = true;

    public Team? Team { get => _team; set { SetProperty(ref _team, value); OnPropertyChanged(nameof(HasTeam)); } }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public bool HasTeam => Team != null;

    public RelayCommand OpenPlayerCommand { get; }

    public TeamProfileViewModel(string teamId)
    {
        OpenPlayerCommand = new RelayCommand(p => { if (p is string id) App.Navigation.NavigateTo(new PlayerProfileViewModel(id)); });
        _ = LoadAsync(teamId);
    }

    private async Task LoadAsync(string teamId)
    {
        IsLoading = true;
        Team = await App.TeamService.GetTeamAsync(teamId);
        IsLoading = false;
    }
}
```

### 5.1.1 Por que `_ = LoadAsync();` e não `await`?

O construtor de uma classe C# não pode ser `async`. Como toda tela precisa buscar dados da API
antes de ter algo para mostrar, o padrão do projeto é: o construtor **dispara** a tarefa
assíncrona e a descarta explicitamente (`_ = ...`, satisfazendo o analisador estático que avisaria
sobre uma `Task` não aguardada), e a UI simplesmente começa mostrando o estado "vazio"/"carregando"
até a tarefa completar e disparar `PropertyChanged` para as propriedades que ela populou. Isso é
o que dá aos ViewModels a sensação de "a tela aparece na hora e os dados chegam logo depois",
sem nenhum spinner bloqueante de tela cheia — cada tela decide como mostrar `IsLoading` (ou nem
mostra, e só aparece vazio por um instante).

**Consequência importante para quem for escrever um ViewModel novo**: se um construtor recebe
parâmetros (como `TeamProfileViewModel(string teamId)`), o `LoadAsync` precisa recebê-los também
como parâmetro do método (não pode depender de os campos já estarem populados, porque o
`LoadAsync` roda de forma concorrente com o resto do construtor terminando).

## 5.2 Notificação manual de propriedades derivadas

Como não há nenhum framework reativo por trás, toda vez que uma propriedade "resumo" depende de
outra, o código precisa **disparar `OnPropertyChanged` da derivada manualmente** dentro do
setter da propriedade base. Isso aparece em praticamente toda tela com contagem ou rótulo
calculado:

```csharp
// ViewModels/LineupViewModel.cs
public int SelectedCount => Members.Count(m => m.IsSelected);
public string SelectedCountLabel => $"{SelectedCount}/{RequiredCount} SELECIONADOS";

private void NotifyCount()
{
    OnPropertyChanged(nameof(SelectedCount));
    OnPropertyChanged(nameof(SelectedCountLabel));
}
```

`NotifyCount()` é chamado manualmente depois de qualquer ação que altera a seleção
(`ToggleSelect`, `LoadAsync`). Esquecer de chamá-lo depois de uma mudança de estado é o bug de
UI mais comum e mais fácil de cometer neste padrão — o valor em memória muda corretamente, mas a
tela simplesmente não atualiza até algo mais disparar um refresh geral.

## 5.3 Itens de lista com estado próprio: `BaseViewModel` também é usado para itens

Quando uma lista precisa que **cada item** tenha estado interativo próprio (selecionado, expandido,
etc.), o item também vira uma classe que herda `BaseViewModel` — não é só um DTO estático:

```csharp
// ViewModels/LineupViewModel.cs
public class LineupMemberItem : BaseViewModel
{
    public User User { get; init; } = null!;
    private bool _isSelected;
    private bool _isCaptainChoice;
    public bool IsSelected      { get => _isSelected;      set => SetProperty(ref _isSelected, value); }
    public bool IsCaptainChoice { get => _isCaptainChoice; set => SetProperty(ref _isCaptainChoice, value); }
}
```

Isso permite que o XAML faça binding direto a `IsSelected` de um item individual dentro de um
`ItemsControl`/`ListBox`, e que clicar num item dispare `PropertyChanged` só daquele item — sem
precisar recriar a lista inteira (`Members = new List<...>(...)`) toda vez que uma seleção muda.
O mesmo padrão aparece em `SidebarItem` (`MainShellViewModel.cs`, cada item do menu lateral tem
seu próprio `IsSelected`) e em `FilterItem` (`TournamentsViewModel.cs`, cada chip de filtro).

**Exemplo genérico do porquê isso importa** (não é código do projeto): se `LineupMemberItem`
fosse uma `class` comum sem `INotifyPropertyChanged`, marcar `item.IsSelected = true` não
avisaria a UI de nada — a única forma de atualizar a tela seria reatribuir a coleção inteira
(`Members = Members.ToList()`), que é mais caro e mais fácil de esquecer.

## 5.4 Reatividade a eventos globais (troca de usuário, navegação)

Alguns ViewModels persistentes (o shell principal, por exemplo) precisam reagir a mudanças que
acontecem "por fora" deles — o usuário atual mudou (login/logout), ou a navegação foi para outra
tela. Isso é feito assinando eventos expostos pelos services globais:

```csharp
// ViewModels/MainShellViewModel.cs
App.UserService.CurrentUserChanged += (_, _) =>
{
    OnPropertyChanged(nameof(UserNickname));
    OnPropertyChanged(nameof(UserRank));
    OnPropertyChanged(nameof(UserLevel));
    OnPropertyChanged(nameof(UserAvatarUrl));
    OnPropertyChanged(nameof(HasAvatar));
};

App.Navigation.CurrentViewChanged += (_, vm) =>
{
    if (vm == null) return;
    var title = vm switch
    {
        TournamentDetailsViewModel => "CAMPEONATO",
        PlayerProfileViewModel     => "JOGADOR",
        // ...
        _ => PageTitle
    };
    CurrentView = vm;
    PageTitle   = title;
};
```

Esse segundo bloco é também o mecanismo real de troca de título da barra superior: sempre que
qualquer parte do app navega para um novo ViewModel, o `MainShellViewModel` decide o título via
um `switch` de tipo sobre o ViewModel de destino. Adicionar uma tela nova que precisa de um
título próprio na barra superior significa adicionar um `case` aqui — é um dos poucos lugares
"centrais" que precisa ser tocado ao criar uma tela nova (junto com o `DataTemplate` em
`App.xaml`, ver [Capítulo 6](06-client-navegacao-api.md)).

## 5.5 Estado de edição "inline" (padrão editar/salvar/cancelar)

Várias telas (Perfil, Time) implementam edição inline sem modal: um booleano `IsEditing` alterna
entre modo leitura e modo formulário, com campos "de rascunho" separados dos campos "confirmados"
até o salvamento:

```csharp
// ViewModels/ProfileViewModel.cs (resumido)
private bool _isEditing;
private string _editBio = string.Empty;

public bool IsEditing { get => _isEditing; set { SetProperty(ref _isEditing, value); OnPropertyChanged(nameof(IsNotEditing)); } }
public string EditBio { get => _editBio; set => SetProperty(ref _editBio, value); }

private void StartEdit()
{
    EditBio = Bio;              // copia o valor atual pro campo de rascunho
    IsEditing = true;
}

private async Task SaveAsync()
{
    await App.UserService.UpdateBioAsync(EditBio);
    IsEditing = false;
    OnPropertyChanged(nameof(Bio));   // Bio é computado a partir de App.UserService.CurrentUser
}

private void CancelEdit() => IsEditing = false;   // simplesmente descarta EditBio, nunca foi salvo
```

O truque é que `Bio` (modo leitura) e `EditBio` (modo formulário) são propriedades **diferentes**
— cancelar a edição não precisa "desfazer" nada porque o valor real (`Bio`, lido de
`App.UserService.CurrentUser`) nunca foi tocado até `SaveAsync` de fato chamar a API. O mesmo
padrão, com mais campos, aparece em `TeamViewModel` para editar o time
(`EditTeamName`/`EditTeamDescription`/...) e para o fluxo de confirmação de exclusão
(`ConfirmingDelete`, um booleano simples que alterna um painel de "tem certeza?").

## 5.6 Timers para simular tempo real (polling)

Como o sistema não tem WebSocket/SignalR (ver [Capítulo 2](02-arquitetura.md#22-como-os-dois-se-comunicam)),
qualquer tela que precisa parecer "ao vivo" usa um `DispatcherTimer` do WPF chamando o mesmo
método de recarga em intervalos curtos. O exemplo mais claro é a sala de partida durante o veto:

```csharp
// ViewModels/MatchRoomViewModel.cs
_timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
_timer.Tick += async (_, _) => await RefreshAsync();
_timer.Start();
_ = RefreshAsync();
```

`DispatcherTimer` (em vez de um `System.Threading.Timer` comum) é usado especificamente porque
seu callback já roda na *dispatcher thread* da UI do WPF — ou seja, pode atualizar propriedades
ligadas ao XAML diretamente, sem precisar de `Dispatcher.Invoke` manual. O timer é parado
explicitamente quando o usuário sai da tela (`BackCommand` chama `_timer.Stop()`) ou quando o
fluxo termina (assim que a sala fica pronta, `MatchRoomViewModel` chama `_timer.Stop()` dentro do
próprio `RefreshAsync`) — esquecer de parar um `DispatcherTimer` ao navegar para longe da tela
deixaria ele rodando (e fazendo requisições HTTP) indefinidamente em segundo plano, então esse
`Stop()` é um detalhe de correção, não só de performance.

## 5.7 Views: o quanto de código-behind é aceitável

O `.xaml.cs` de toda `View` é, por convenção do projeto, praticamente vazio:

```csharp
// Views/AuditLogView.xaml.cs (padrão típico, se repete em quase toda View)
public partial class AuditLogView : UserControl
{
    public AuditLogView() => InitializeComponent();
}
```

A única exceção regular é `Components/LevelBadge.xaml.cs`, que **não é uma View de tela** — é um
`UserControl` reutilizável com `DependencyProperty`s próprias (`Level`, `Size`, `ShowTier`) e
lógica de desenho (escolher cor/nome de "tier" a partir do nível numérico). Isso é aceitável
porque é lógica de **apresentação pura de um componente visual reutilizável**, sem nenhuma regra
de negócio ou chamada de rede — a distinção que o projeto mantém é: code-behind pode conter lógica
de desenho/layout de um controle puramente visual, mas nunca lógica de aplicação (chamadas HTTP,
regra de permissão, navegação) — isso sempre mora no ViewModel.
