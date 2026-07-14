using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class HomeViewModel : BaseViewModel
{
    private readonly MatchRepository _matchRepo = new();
    private readonly TournamentRepository _tourRepo = new();

    private List<MatchListItem> _recentMatches = new();
    private List<Tournament>    _featuredTournaments = new();

    public string WelcomeText   => $"BEM-VINDO, {App.UserService.CurrentUser?.Nickname?.ToUpper() ?? "JOGADOR"}";
    public string UserRank      => App.UserService.CurrentUser?.Rank      ?? "—";
    public int    UserLevel     => App.UserService.CurrentUser?.Level     ?? 0;
    public double WinRate       => App.UserService.CurrentUser?.WinRate   ?? 0;
    public double KD            => App.UserService.CurrentUser?.KD        ?? 0;
    public string TeamName      => App.UserService.CurrentUser?.Team?.Name ?? "Sem time";
    public string WinRateText   => $"{WinRate:P0}";
    public string KDText        => $"{KD:F2}";
    public double HSPercent     => App.UserService.CurrentUser?.HeadshotPercent ?? 0;
    public string HSText        => $"{HSPercent:P0}";

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

    public RelayCommand OpenMatchCommand      { get; }
    public RelayCommand OpenTournamentCommand { get; }

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
    }
}
