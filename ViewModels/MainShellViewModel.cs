using System.Windows;
using System.Windows.Threading;
using Summit.Commands;
using Summit.Data;
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
    private readonly NotificationRepository _notificationRepo = new();
    private readonly DispatcherTimer _notificationTimer;

    private BaseViewModel? _currentView;
    private string _pageTitle = "HOME";
    private int _unreadNotificationCount;

    public BaseViewModel? CurrentView { get => _currentView; set => SetProperty(ref _currentView, value); }
    public string PageTitle    { get => _pageTitle;    set => SetProperty(ref _pageTitle, value); }
    public string UserNickname  => App.UserService.CurrentUser?.Nickname  ?? "—";
    public string UserRank      => App.UserService.CurrentUser?.Rank      ?? "—";
    public int    UserLevel     => App.UserService.CurrentUser?.Level     ?? 0;
    public string UserAvatarUrl => App.UserService.CurrentUser?.AvatarUrl ?? string.Empty;
    public bool   HasAvatar     => !string.IsNullOrWhiteSpace(UserAvatarUrl);

    public int  UnreadNotificationCount { get => _unreadNotificationCount; set { SetProperty(ref _unreadNotificationCount, value); OnPropertyChanged(nameof(HasUnreadNotifications)); } }
    public bool HasUnreadNotifications  => UnreadNotificationCount > 0;

    public List<SidebarItem> NavCompete   { get; }
    public List<SidebarItem> NavCommunity { get; }
    public List<SidebarItem> NavYou       { get; }
    public List<SidebarItem> NavItems     { get; }

    public RelayCommand OpenProfileCommand       { get; }
    public RelayCommand OpenNotificationsCommand { get; }
    public RelayCommand OpenFriendsCommand       { get; }
    public RelayCommand MinimizeCommand          { get; }
    public RelayCommand MaximizeCommand          { get; }
    public RelayCommand CloseCommand             { get; }

    public MainShellViewModel()
    {
        NavCompete = new()
        {
            new() { Icon = "", Label = "Home",        Command = new RelayCommand(_ => Navigate(new HomeViewModel(),        "HOME")) },
            new() { Icon = "", Label = "Campeonatos", Command = new RelayCommand(_ => Navigate(new TournamentsViewModel(), "CAMPEONATOS")) },
            new() { Icon = "", Label = "Partidas",    Command = new RelayCommand(_ => Navigate(new MatchesViewModel(),     "PARTIDAS")) },
        };
        NavCommunity = new()
        {
            new() { Icon = "", Label = "Time",    Command = new RelayCommand(_ => Navigate(new TeamViewModel(),    "TIME")) },
            new() { Icon = "", Label = "Amigos",  Command = new RelayCommand(_ => Navigate(new FriendsViewModel(), "AMIGOS")) },
            new() { Icon = "", Label = "Ranking", Command = new RelayCommand(_ => Navigate(new RankingViewModel(), "RANKING")) },
        };
        NavYou = new()
        {
            new() { Icon = "", Label = "Perfil", Command = new RelayCommand(_ => Navigate(new ProfileViewModel(),  "PERFIL")) },
            new() { Icon = "", Label = "Badges", Command = new RelayCommand(_ => Navigate(new BadgesViewModel(),   "BADGES")) },
            new() { Icon = "", Label = "Configurações", Command = new RelayCommand(_ => Navigate(new SettingsViewModel(), "CONFIG")) },
        };
        NavItems = NavCompete.Concat(NavCommunity).Concat(NavYou).ToList();

        OpenProfileCommand = new RelayCommand(_ => Navigate(new ProfileViewModel(), "PERFIL"));
        OpenNotificationsCommand = new RelayCommand(_ => Navigate(new NotificationsViewModel(), "NOTIFICAÇÕES"));
        OpenFriendsCommand = new RelayCommand(_ => Navigate(new FriendsViewModel(), "AMIGOS"));

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
                TeamProfileViewModel       => "TIME",
                MatchDetailsViewModel      => "PARTIDA",
                MatchRoomViewModel         => "SALA DA PARTIDA",
                JoinRequestsViewModel      => "SOLICITAÇÕES",
                AuditLogViewModel          => "HISTÓRICO",
                LineupViewModel            => "ESCALAÇÃO",
                NotificationsViewModel     => "NOTIFICAÇÕES",
                CreateTournamentViewModel  => "CRIAR CAMPEONATO",
                _                          => PageTitle
            };
            CurrentView = vm;
            PageTitle   = title;
            foreach (var item in NavItems) item.IsSelected = false;
        };

        if (string.IsNullOrWhiteSpace(App.UserService.CurrentUser?.Country))
            Navigate(new OnboardingViewModel(), "BEM-VINDO");
        else
            Navigate(new HomeViewModel(), "HOME");

        // sininho de notificações — polling leve, mesmo padrão do MatchRoomViewModel
        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _notificationTimer.Tick += async (_, _) => await RefreshUnreadCountAsync();
        _notificationTimer.Start();
        _ = RefreshUnreadCountAsync();
    }

    private async Task RefreshUnreadCountAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me == null) return;
        var unread = await _notificationRepo.GetAsync(me.Id, unreadOnly: true);
        UnreadNotificationCount = unread.Count;
    }

    private void Navigate(BaseViewModel vm, string title)
    {
        CurrentView = vm;
        PageTitle   = title;
        foreach (var item in NavItems)
            item.IsSelected = item.Label == title;
    }
}
