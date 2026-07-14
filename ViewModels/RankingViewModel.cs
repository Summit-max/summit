using Wallbang.Commands;
using Wallbang.Models;

namespace Wallbang.ViewModels;

public class RankingViewModel : BaseViewModel
{
    private List<RankingPlayer> _allPlayers = new();
    private List<RankingTeam> _allTeams = new();

    private List<RankingPlayer> _podiumPlayers = new();
    private List<RankingPlayer> _restPlayers = new();
    private List<RankingTeam> _podiumTeams = new();
    private List<RankingTeam> _restTeams = new();

    private bool _showingPlayers = true;
    private bool _isLoading;

    public List<RankingPlayer> PodiumPlayers { get => _podiumPlayers; set => SetProperty(ref _podiumPlayers, value); }
    public List<RankingPlayer> RestPlayers   { get => _restPlayers;   set => SetProperty(ref _restPlayers, value); }
    public List<RankingTeam>   PodiumTeams   { get => _podiumTeams;   set => SetProperty(ref _podiumTeams, value); }
    public List<RankingTeam>   RestTeams     { get => _restTeams;     set => SetProperty(ref _restTeams, value); }

    public bool ShowingPlayers
    {
        get => _showingPlayers;
        set
        {
            if (SetProperty(ref _showingPlayers, value))
            {
                OnPropertyChanged(nameof(ShowingTeams));
                OnPropertyChanged(nameof(HeaderSubtitle));
            }
        }
    }
    public bool ShowingTeams => !_showingPlayers;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public string HeaderSubtitle => ShowingPlayers
        ? "Os melhores jogadores da temporada"
        : "Os times mais fortes do cenário";

    public RankingPlayer? First  => PodiumPlayers.Count > 0 ? PodiumPlayers[0] : null;
    public RankingPlayer? Second => PodiumPlayers.Count > 1 ? PodiumPlayers[1] : null;
    public RankingPlayer? Third  => PodiumPlayers.Count > 2 ? PodiumPlayers[2] : null;

    public RankingTeam? FirstTeam  => PodiumTeams.Count > 0 ? PodiumTeams[0] : null;
    public RankingTeam? SecondTeam => PodiumTeams.Count > 1 ? PodiumTeams[1] : null;
    public RankingTeam? ThirdTeam  => PodiumTeams.Count > 2 ? PodiumTeams[2] : null;

    public RelayCommand ShowPlayersCommand { get; }
    public RelayCommand ShowTeamsCommand { get; }

    public RankingViewModel()
    {
        ShowPlayersCommand = new RelayCommand(_ =>
        {
            ShowingPlayers = true;
        });
        ShowTeamsCommand = new RelayCommand(_ =>
        {
            ShowingPlayers = false;
        });
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        _allPlayers = await App.RankingService.GetTopPlayersAsync();
        _allTeams = await App.RankingService.GetTopTeamsAsync();

        PodiumPlayers = _allPlayers.Take(3).ToList();
        RestPlayers = _allPlayers.Skip(3).ToList();
        PodiumTeams = _allTeams.Take(3).ToList();
        RestTeams = _allTeams.Skip(3).ToList();

        OnPropertyChanged(nameof(First));
        OnPropertyChanged(nameof(Second));
        OnPropertyChanged(nameof(Third));
        OnPropertyChanged(nameof(FirstTeam));
        OnPropertyChanged(nameof(SecondTeam));
        OnPropertyChanged(nameof(ThirdTeam));

        IsLoading = false;
    }
}
