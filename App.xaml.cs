using System.Windows;
using System.Windows.Threading;
using Wallbang.Data;
using Wallbang.Services;
using Wallbang.Views;

namespace Wallbang;

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
                "Wallbang — Exceção não tratada",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        EnsureDbSchema();

        new SeedService().EnsureSeededAsync().GetAwaiter().GetResult();

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

    private static void EnsureDbSchema()
    {
        try
        {
            using var db = new WallbangDbContext();
            db.Database.EnsureCreated();
            // probe a new-schema table to detect stale db
            _ = db.Teams.FirstOrDefault();
            _ = db.Matches.FirstOrDefault();
            _ = db.Friendships.FirstOrDefault();
        }
        catch
        {
            using var db = new WallbangDbContext();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
        }
    }
}
