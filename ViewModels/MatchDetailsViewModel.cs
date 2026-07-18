using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class ScoreboardRow
{
    public string UserId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public int Level { get; set; }
    public string Country { get; set; } = string.Empty;
    public bool IsMvp { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }
    public double KD { get; set; }
    public double Adr { get; set; }
    public double HSPercent { get; set; }
    public double Rating { get; set; }

    public string KDText     => KD.ToString("F2");
    public string AdrText    => Adr.ToString("F1");
    public string HSText     => $"{(int)(HSPercent * 100)}%";
    public string RatingText => Rating.ToString("F2");
}

public class MatchDetailsViewModel : BaseViewModel
{
    private readonly MatchRepository _repo = new();

    private Match? _match;
    private List<ScoreboardRow> _sideA = new();
    private List<ScoreboardRow> _sideB = new();
    private bool _isLoading;

    public Match? Match { get => _match; set { SetProperty(ref _match, value); OnPropertyChanged(nameof(HasMatch)); OnPropertyChanged(nameof(WinnerLabel)); OnPropertyChanged(nameof(DateLabel)); OnPropertyChanged(nameof(DurationLabel)); OnPropertyChanged(nameof(HasRoom)); OnPropertyChanged(nameof(RoomIp)); OnPropertyChanged(nameof(RoomPassword)); OnPropertyChanged(nameof(RoomMap)); } }
    public bool HasMatch => _match != null;
    public List<ScoreboardRow> SideA { get => _sideA; set => SetProperty(ref _sideA, value); }
    public List<ScoreboardRow> SideB { get => _sideB; set => SetProperty(ref _sideB, value); }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public string WinnerLabel => _match == null ? "" : (_match.TeamAWon ? $"{_match.TeamATag} VENCEU" : (_match.TeamBWon ? $"{_match.TeamBTag} VENCEU" : "EMPATE"));
    public string DateLabel => _match?.PlayedAt.ToString("dd/MM/yyyy HH:mm") ?? "";
    public string DurationLabel => _match == null ? "" : $"{_match.DurationMinutes} min";

    // ───── Sala da partida (partida agendada com servidor pronto) ─────
    public bool HasRoom => _match != null
        && !string.IsNullOrEmpty(_match.ServerIp)
        && _match.Status != MatchStatus.Finished;
    public string RoomIp => _match?.ServerIp ?? "";
    public string RoomPassword => _match?.ServerPassword ?? "";
    public string RoomMap => _match?.Map ?? "";

    private string _connectLabel = "ENTRAR NO SERVIDOR";
    public string ConnectLabel { get => _connectLabel; set => SetProperty(ref _connectLabel, value); }

    public RelayCommand ConnectCommand { get; private set; } = null!;

    public RelayCommand BackCommand       { get; }
    public RelayCommand ViewPlayerCommand { get; }

    public MatchDetailsViewModel() : this("m_001") { }

    public MatchDetailsViewModel(string matchId)
    {
        BackCommand = new RelayCommand(_ =>
        {
            if (App.Navigation.CanGoBack) App.Navigation.GoBack();
        });
        ConnectCommand = new RelayCommand(_ =>
        {
            if (_match == null) return;
            try
            {
                System.Windows.Clipboard.SetText($"connect {_match.ServerIp}; password {_match.ServerPassword}");
                ConnectLabel = "COMANDO COPIADO! COLE NO CONSOLE DO CS2";
            }
            catch { }
        });
        ViewPlayerCommand = new RelayCommand(p =>
        {
            if (p is string uid && !string.IsNullOrEmpty(uid))
                App.Navigation.NavigateTo(new PlayerProfileViewModel(uid));
        });
        _ = LoadAsync(matchId);
    }

    private async Task LoadAsync(string id)
    {
        IsLoading = true;
        Match = await _repo.GetByIdAsync(id);
        if (Match != null)
        {
            SideA = Match.Players.Where(p => p.TeamSide == "A")
                                 .OrderByDescending(p => p.Rating)
                                 .Select(ToRow).ToList();
            SideB = Match.Players.Where(p => p.TeamSide == "B")
                                 .OrderByDescending(p => p.Rating)
                                 .Select(ToRow).ToList();
        }
        IsLoading = false;
    }

    private static ScoreboardRow ToRow(MatchPlayer p) => new()
    {
        UserId = p.UserId,
        Nickname = p.User?.Nickname ?? "???",
        Level = p.User?.Level ?? 0,
        Country = p.User?.Country ?? "",
        IsMvp = p.IsMvp,
        Kills = p.Kills,
        Deaths = p.Deaths,
        Assists = p.Assists,
        KD = p.KD,
        Adr = p.AvgDamagePerRound,
        HSPercent = p.HSPercent,
        Rating = p.Rating
    };
}
