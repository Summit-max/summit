using System.Windows.Threading;
using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class VetoMapItem : BaseViewModel
{
    public string Map { get; init; } = string.Empty;
    public string StateLabel { get; init; } = string.Empty;   // "BAN NAVI" | "DECIDER" | ""
    public bool IsAvailable { get; init; }
    public bool IsClickable { get; init; }
    public bool IsDecider { get; init; }
}

/// <summary>Hub da partida (estilo FACEIT): elencos, veto ao vivo e servidor.</summary>
public class MatchRoomViewModel : BaseViewModel
{
    private readonly VetoRepository _veto = new();
    private readonly TeamRepository _teams = new();
    private readonly string _bracketMatchId;
    private readonly DispatcherTimer _timer;
    private bool _teamsLoaded;

    private string _teamATag = "—";
    private string _teamBTag = "—";
    private Team? _teamA;
    private Team? _teamB;
    private List<VetoMapItem> _maps = new();
    private string _statusLine = "CARREGANDO SALA...";
    private bool _myTurn;
    private Match? _room;
    private string _connectLabel = "ENTRAR NO SERVIDOR";

    public string TeamATag { get => _teamATag; set => SetProperty(ref _teamATag, value); }
    public string TeamBTag { get => _teamBTag; set => SetProperty(ref _teamBTag, value); }
    public Team? TeamA { get => _teamA; set => SetProperty(ref _teamA, value); }
    public Team? TeamB { get => _teamB; set => SetProperty(ref _teamB, value); }
    public List<VetoMapItem> Maps { get => _maps; set => SetProperty(ref _maps, value); }
    public string StatusLine { get => _statusLine; set => SetProperty(ref _statusLine, value); }
    public bool MyTurn { get => _myTurn; set => SetProperty(ref _myTurn, value); }
    public Match? Room { get => _room; set { SetProperty(ref _room, value); OnPropertyChanged(nameof(HasRoom)); } }
    public bool HasRoom => _room != null && !string.IsNullOrWhiteSpace(_room.ServerIp);
    public string ConnectLabel { get => _connectLabel; set => SetProperty(ref _connectLabel, value); }

    public RelayCommand BackCommand { get; }
    public RelayCommand BanCommand { get; }
    public RelayCommand ConnectCommand { get; }

    public MatchRoomViewModel() : this("bm_none") { }

    public MatchRoomViewModel(string bracketMatchId)
    {
        _bracketMatchId = bracketMatchId;

        BackCommand = new RelayCommand(_ =>
        {
            _timer!.Stop();
            if (App.Navigation.CanGoBack) App.Navigation.GoBack();
        });
        BanCommand = new RelayCommand(async p =>
        {
            var myTag = App.UserService.CurrentUser?.Team?.Tag;
            if (p is string map && !string.IsNullOrEmpty(myTag))
            {
                await _veto.ActAsync(_bracketMatchId, myTag, map);
                await RefreshAsync();
            }
        });
        ConnectCommand = new RelayCommand(_ =>
        {
            if (_room == null) return;
            try
            {
                System.Windows.Clipboard.SetText($"connect {_room.ServerIp}; password {_room.ServerPassword}");
                ConnectLabel = "COMANDO COPIADO! COLE NO CONSOLE DO CS2";
            }
            catch { }
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var state = await _veto.GetStateAsync(_bracketMatchId)
                 ?? await _veto.StartAsync(_bracketMatchId);
        if (state?.Session == null)
        {
            StatusLine = "AGUARDANDO ABERTURA DO VETO...";
            return;
        }

        var s = state.Session;
        TeamATag = s.TeamATag;
        TeamBTag = s.TeamBTag;

        if (!_teamsLoaded)
        {
            _teamsLoaded = true;
            TeamA = await _teams.GetByTagAsync(s.TeamATag);
            TeamB = await _teams.GetByTagAsync(s.TeamBTag);
        }

        var myTag = App.UserService.CurrentUser?.Team?.Tag ?? "";
        var next = state.Next;
        MyTurn = !s.IsComplete && next != null &&
                 string.Equals(next.Team, myTag, StringComparison.OrdinalIgnoreCase);

        var stepByMap = s.Steps
            .GroupBy(x => x.Map, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Maps = s.MapPool.Select(m =>
        {
            stepByMap.TryGetValue(m, out var step);
            var label = step?.Action switch
            {
                VetoActionType.Ban     => $"BAN {step.TeamTag}",
                VetoActionType.Pick    => $"PICK {step.TeamTag}",
                VetoActionType.Decider => "DECIDER",
                _                      => ""
            };
            var available = step == null;
            return new VetoMapItem
            {
                Map = m,
                StateLabel = label,
                IsAvailable = available,
                IsDecider = step?.Action == VetoActionType.Decider,
                IsClickable = available && MyTurn
            };
        }).ToList();

        if (s.IsComplete)
        {
            Room = await _veto.GetRoomAsync(_bracketMatchId);
            if (HasRoom)
            {
                StatusLine = "SERVIDOR PRONTO — BOA SORTE!";
                _timer.Stop();
            }
            else
            {
                StatusLine = "PROVISIONANDO SERVIDOR NA AWS... ~90S";
            }
        }
        else if (next != null)
        {
            var verb = next.Action == "Pick" ? "ESCOLHER" : "BANIR";
            StatusLine = MyTurn
                ? $"SUA VEZ — {verb} UM MAPA"
                : $"VEZ DE {next.Team} {verb}...";
        }
    }
}
