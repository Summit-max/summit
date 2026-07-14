using System.Windows;
using Summit.Commands;
using Summit.Models;

namespace Summit.ViewModels;

public class SidebarItem : BaseViewModel
{
    private bool _isSelected;
    public string Icon { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public RelayCommand Command { get; init; } = null!;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public class MainShellViewModel : BaseViewModel
{
    private BaseViewModel? _currentView;
    private string _pageTitle = "HOME";
    private SidebarItem? _selectedItem;

    public BaseViewModel? CurrentView { get => _currentView; set => SetProperty(ref _currentView, value); }
    public string PageTitle    { get => _pageTitle;    set => SetProperty(ref _pageTitle, value); }
    public string UserNickname => App.UserService.CurrentUser?.Nickname ?? "—";
    public string UserRank     => App.UserService.CurrentUser?.Rank     ?? "—";
    public int    UserLevel    => App.UserService.CurrentUser?.Level    ?? 0;

    public List<SidebarItem> NavItems { get; }

    public RelayCommand MinimizeCommand { get; }
    public RelayCommand MaximizeCommand { get; }
    public RelayCommand CloseCommand    { get; }

    public MainShellViewModel()
    {
        NavItems = new()
        {
            new() { Icon = "", Label = "HOME",        Command = new RelayCommand(_ => Navigate(new HomeViewModel(),        "HOME")) },
            new() { Icon = "", Label = "PERFIL",      Command = new RelayCommand(_ => Navigate(new ProfileViewModel(),     "PERFIL")) },
            new() { Icon = "", Label = "TIME",        Command = new RelayCommand(_ => Navigate(new TeamViewModel(),        "TIME")) },
            new() { Icon = "", Label = "AMIGOS",      Command = new RelayCommand(_ => Navigate(new FriendsViewModel(),     "AMIGOS")) },
            new() { Icon = "", Label = "CAMPEONATOS", Command = new RelayCommand(_ => Navigate(new TournamentsViewModel(), "CAMPEONATOS")) },
            new() { Icon = "", Label = "PARTIDAS",    Command = new RelayCommand(_ => Navigate(new MatchesViewModel(),     "PARTIDAS")) },
            new() { Icon = "", Label = "RANKING",     Command = new RelayCommand(_ => Navigate(new RankingViewModel(),     "RANKING")) },
            new() { Icon = "", Label = "BADGES",      Command = new RelayCommand(_ => Navigate(new BadgesViewModel(),      "BADGES")) },
            new() { Icon = "", Label = "CONFIG",      Command = new RelayCommand(_ => Navigate(new SettingsViewModel(),    "CONFIG")) },
        };

        MinimizeCommand = new RelayCommand(_ =>
        {
            if (Application.Current.MainWindow != null)
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
        });
        MaximizeCommand = new RelayCommand(_ =>
        {
            if (Application.Current.MainWindow == null) return;
            Application.Current.MainWindow.WindowState =
                Application.Current.MainWindow.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
        });
        CloseCommand = new RelayCommand(_ => Application.Current.Shutdown());

        App.Navigation.CurrentViewChanged += (_, vm) =>
        {
            if (vm == null) return;
            var title = vm switch
            {
                TournamentDetailsViewModel => "CAMPEONATO",
                PlayerProfileViewModel     => "JOGADOR",
                MatchDetailsViewModel      => "PARTIDA",
                _                          => PageTitle
            };
            CurrentView = vm;
            PageTitle   = title;
            foreach (var item in NavItems) item.IsSelected = false;
        };

        Navigate(new HomeViewModel(), "HOME");
    }

    private void Navigate(BaseViewModel vm, string title)
    {
        CurrentView = vm;
        PageTitle   = title;
        foreach (var item in NavItems)
            item.IsSelected = item.Label == title;
        _selectedItem = NavItems.FirstOrDefault(i => i.Label == title);
    }
}
