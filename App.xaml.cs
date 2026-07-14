using System.Windows;
using System.Windows.Threading;
using Summit.Data;
using Summit.Services;
using Summit.Views;

namespace Summit;

public partial class App : Application
{
    public static NavigationService   Navigation         { get; private set; } = null!;
    public static UserService         UserService        { get; private set; } = null!;
    public static UserRepository      UserRepository     { get; private set; } = null!;
    public static SteamWebApiClient   SteamApi           { get; private set; } = null!;
    public static SteamAuthService    SteamAuth          { get; private set; } = null!;
    public static TeamService         TeamService        { get; private set; } = null!;
    public static TournamentService   TournamentService  { get; private set; } = null!;
    public static StatsService        StatsService       { get; private set; } = null!;
    public static BadgeService        BadgeService       { get; private set; } = null!;
    public static RankingService      RankingService     { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Erro: {args.Exception.GetType().Name}\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "Summit — Exceção não tratada",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        // Todos os dados agora vêm da Summit API (ApiClient.BaseUrl).
        Navigation        = new NavigationService();
        UserService       = new UserService();
        UserRepository    = new UserRepository();
        SteamApi          = new SteamWebApiClient();
        SteamAuth         = new SteamAuthService(UserService, UserRepository, SteamApi);
        TeamService       = new TeamService();
        TournamentService = new TournamentService();
        StatsService      = new StatsService();
        BadgeService      = new BadgeService();
        RankingService    = new RankingService();

        new SplashView().Show();
    }
}
