using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class HomeViewModel : BaseViewModel
{
    private readonly MatchRepository _matchRepo = new();
    private readonly TournamentRepository _tourRepo = new();

    private List<MatchListItem>  _recentMatches = new();
    private List<Tournament>     _featuredTournaments = new();
    private List<RankingPlayer>  _topPlayers = new();

    public string WelcomeText   => App.UserService.CurrentUser?.Nickname ?? "Jogador";
    public string UserRank      => App.UserService.CurrentUser?.Rank      ?? "—";
    public int    UserLevel     => App.UserService.CurrentUser?.Level     ?? 0;
    public double WinRate       => App.UserService.CurrentUser?.WinRate   ?? 0;
    public double KD            => App.UserService.CurrentUser?.KD        ?? 0;
    public string TeamName      => App.UserService.CurrentUser?.Team?.Name ?? "Sem time";
    public string WinRateText   => $"{WinRate:P0}";
    public string KDText        => $"{KD:F2}";
    public double HSPercent     => App.UserService.CurrentUser?.HeadshotPercent ?? 0;
    public string HSText        => $"{HSPercent:P0}";
    public string SteamId       => App.UserService.CurrentUser?.SteamId ?? "—";
    public bool   HasAvatar     => !string.IsNullOrWhiteSpace(App.UserService.CurrentUser?.AvatarUrl);
    public string AvatarUrl     => App.UserService.CurrentUser?.AvatarUrl ?? string.Empty;
    public bool   HasTeam       => App.UserService.CurrentUser?.Team != null;
    public int    TeamMemberCount => App.UserService.CurrentUser?.Team?.Members?.Count ?? 0;

    public List<MatchListItem> RecentMatches
    {
        get => _recentMatches;
        set => SetProperty(ref _recentMatches, value);
    }

    public List<Tournament> FeaturedTournaments
    {
        get => _featuredTournaments;
        set => SetProperty(ref _featuredTournaments, value);
    }

    public List<RankingPlayer> TopPlayers
    {
        get => _topPlayers;
        set => SetProperty(ref _topPlayers, value);
    }

    public RelayCommand OpenMatchCommand      { get; }
    public RelayCommand OpenTournamentCommand { get; }
    public RelayCommand OpenTournamentsCommand { get; }
    public RelayCommand OpenTeamCommand        { get; }
    public RelayCommand OpenPlayerCommand      { get; }
    public RelayCommand OpenMatchesCommand     { get; }
    public RelayCommand OpenRankingCommand     { get; }

    public HomeViewModel()
    {
        OpenMatchCommand = new RelayCommand(p =>
        {
            if (p is string id) App.Navigation.NavigateTo(new MatchDetailsViewModel(id));
        });
        OpenTournamentCommand = new RelayCommand(p =>
        {
            if (p is string id) App.Navigation.NavigateTo(new TournamentDetailsViewModel(id));
        });
        OpenTournamentsCommand = new RelayCommand(_ => App.Navigation.NavigateTo(new TournamentsViewModel()));
        OpenTeamCommand = new RelayCommand(_ =>
        {
            var teamId = App.UserService.CurrentUser?.TeamId;
            if (!string.IsNullOrEmpty(teamId)) App.Navigation.NavigateTo(new TeamViewModel());
        });
        OpenPlayerCommand = new RelayCommand(p =>
        {
            if (p is string id) App.Navigation.NavigateTo(new PlayerProfileViewModel(id));
        });
        OpenMatchesCommand = new RelayCommand(_ => App.Navigation.NavigateTo(new MatchesViewModel()));
        OpenRankingCommand = new RelayCommand(_ => App.Navigation.NavigateTo(new RankingViewModel()));
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me != null)
        {
            var raw = await _matchRepo.GetRecentForUserAsync(me.Id, 5);
            RecentMatches = raw.Select(m =>
            {
                var mp = m.Players.FirstOrDefault(p => p.UserId == me.Id);
                var won = mp != null && ((mp.TeamSide == "A" && m.TeamAWon) || (mp.TeamSide == "B" && m.TeamBWon));
                return new MatchListItem
                {
                    Id = m.Id, Map = m.Map, PlayedAt = m.PlayedAt,
                    TeamATag = m.TeamATag, TeamBTag = m.TeamBTag, Score = m.Score, Won = won,
                    Kills = mp?.Kills ?? 0, Deaths = mp?.Deaths ?? 0, Assists = mp?.Assists ?? 0,
                    Adr = mp?.AvgDamagePerRound ?? 0,
                    TournamentName = m.TournamentName
                };
            }).ToList();
        }

        var tours = await _tourRepo.GetAllAsync();
        FeaturedTournaments = tours.Where(t => t.Status != TournamentStatus.Finished).Take(3).ToList();

        var players = await App.RankingService.GetTopPlayersAsync();
        TopPlayers = players.Take(3).ToList();
    }
}
