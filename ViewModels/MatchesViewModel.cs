using Wallbang.Commands;
using Wallbang.Data;
using Wallbang.Models;

namespace Wallbang.ViewModels;

public class MatchListItem
{
    public string Id { get; set; } = string.Empty;
    public string Map { get; set; } = string.Empty;
    public DateTime PlayedAt { get; set; }
    public string TeamATag { get; set; } = "";
    public string TeamBTag { get; set; } = "";
    public string Score { get; set; } = "";
    public bool Won { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public double Adr { get; set; }
    public string? TournamentName { get; set; }

    public string KDA => $"{Kills} / {Deaths} / {Assists}";
    public string WonLabel => Won ? "VITÓRIA" : "DERROTA";
    public string DateLabel => PlayedAt.ToString("dd/MM/yyyy HH:mm");
    public string AdrLabel => Adr.ToString("F1");
}

public class MatchesViewModel : BaseViewModel
{
    private readonly MatchRepository _repo = new();
    private List<MatchListItem> _items = new();
    private bool _isLoading;

    public List<MatchListItem> Items { get => _items; set => SetProperty(ref _items, value); }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public int TotalCount => Items.Count;

    public RelayCommand OpenMatchCommand { get; }

    public MatchesViewModel()
    {
        OpenMatchCommand = new RelayCommand(p =>
        {
            if (p is string id && !string.IsNullOrEmpty(id))
                App.Navigation.NavigateTo(new MatchDetailsViewModel(id));
        });
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        var me = App.UserService.CurrentUser;
        if (me == null) { IsLoading = false; return; }

        var raw = await _repo.GetRecentForUserAsync(me.Id, 50);
        Items = raw.Select(m =>
        {
            var mp = m.Players.FirstOrDefault(p => p.UserId == me.Id);
            var won = mp != null &&
                    ((mp.TeamSide == "A" && m.TeamAWon) || (mp.TeamSide == "B" && m.TeamBWon));
            return new MatchListItem
            {
                Id = m.Id,
                Map = m.Map,
                PlayedAt = m.PlayedAt,
                TeamATag = m.TeamATag,
                TeamBTag = m.TeamBTag,
                Score = m.Score,
                Won = won,
                Kills = mp?.Kills ?? 0,
                Deaths = mp?.Deaths ?? 0,
                Assists = mp?.Assists ?? 0,
                Adr = mp?.AvgDamagePerRound ?? 0,
                TournamentName = m.TournamentName
            };
        }).ToList();
        IsLoading = false;
        OnPropertyChanged(nameof(TotalCount));
    }
}
