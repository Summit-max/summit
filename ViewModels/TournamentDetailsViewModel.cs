using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class TournamentDetailsViewModel : BaseViewModel
{
    private readonly DebugRepository _debug = new();

    private Tournament? _tournament;
    private bool _isLoading;
    private bool _isDevBusy;

    public Tournament? Tournament
    {
        get => _tournament;
        set
        {
            SetProperty(ref _tournament, value);
            OnPropertyChanged(nameof(UpperColumns));
            OnPropertyChanged(nameof(LowerColumns));
            OnPropertyChanged(nameof(GrandFinalColumns));
            OnPropertyChanged(nameof(HasLowerBracket));
        }
    }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public List<BracketColumnViewModel> UpperColumns =>
        BracketLayout.Build(Tournament?.Bracket.Where(r => r.Side == BracketSide.Upper) ?? Enumerable.Empty<BracketRound>());
    public List<BracketColumnViewModel> LowerColumns =>
        BracketLayout.Build(Tournament?.Bracket.Where(r => r.Side == BracketSide.Lower) ?? Enumerable.Empty<BracketRound>());
    public List<BracketColumnViewModel> GrandFinalColumns =>
        BracketLayout.Build(Tournament?.Bracket.Where(r => r.Side == BracketSide.GrandFinal) ?? Enumerable.Empty<BracketRound>());
    public bool HasLowerBracket => LowerColumns.Count > 0;

    public bool IsDevBusy { get => _isDevBusy; set => SetProperty(ref _isDevBusy, value); }

    public RelayCommand RegisterCommand  { get; }
    public RelayCommand BackCommand      { get; }
    public RelayCommand OpenMatchCommand { get; }
    public RelayCommand OpenLineupCommand { get; }
    public RelayCommand CheckInCommand   { get; }
    public RelayCommand EditTournamentCommand { get; }
    public RelayCommand AddGhostTeamsCommand { get; }
    public RelayCommand SkipToNowCommand     { get; }
    public RelayCommand AdvanceStageCommand  { get; }

    public TournamentDetailsViewModel() : this("trn_001") { }

    public TournamentDetailsViewModel(string tournamentId)
    {
        RegisterCommand = new RelayCommand(async _ => await RegisterAsync());
        OpenMatchCommand = new RelayCommand(p =>
        {
            if (p is not BracketMatch bm) return;
            if (bm.TeamATag == "TBD" || bm.TeamBTag == "TBD") return;
            // finalizada com scoreboard → tela de resultado; senão → sala (hub c/ veto)
            if (bm.IsFinished && !string.IsNullOrEmpty(bm.MatchId))
                App.Navigation.NavigateTo(new MatchDetailsViewModel(bm.MatchId!));
            else
                App.Navigation.NavigateTo(new MatchRoomViewModel(bm.Id));
        });
        BackCommand     = new RelayCommand(_ =>
        {
            if (App.Navigation.CanGoBack) App.Navigation.GoBack();
            else App.Navigation.NavigateTo(new TournamentsViewModel());
        });
        OpenLineupCommand = new RelayCommand(_ =>
        {
            var teamId = App.UserService.CurrentUser?.TeamId;
            if (Tournament != null && !string.IsNullOrEmpty(teamId))
                App.Navigation.NavigateTo(new LineupViewModel(Tournament.Id, teamId));
        }, _ => Tournament?.CanEditLineup == true);
        CheckInCommand = new RelayCommand(async _ =>
        {
            var me = App.UserService.CurrentUser;
            if (Tournament == null || me?.TeamId == null) return;
            await App.TournamentService.CheckInAsync(Tournament.Id, me.TeamId, me.Id);
            await LoadAsync(Tournament.Id);
        }, _ => Tournament?.CanCheckIn == true);
        EditTournamentCommand = new RelayCommand(_ =>
        {
            if (Tournament != null) App.Navigation.NavigateTo(new CreateTournamentViewModel(Tournament));
        }, _ => Tournament?.CanEdit == true);

        // ───── DEV: ferramentas de teste local, docs/spec/summit-fase-final ─────
        AddGhostTeamsCommand = new RelayCommand(async _ =>
        {
            if (Tournament == null) return;
            IsDevBusy = true;
            await _debug.AddGhostTeamsAsync(Tournament.Id, 4);
            await LoadAsync(Tournament.Id);
            IsDevBusy = false;
        }, _ => !IsDevBusy);
        SkipToNowCommand = new RelayCommand(async _ =>
        {
            if (Tournament == null) return;
            IsDevBusy = true;
            // start em +50min: check-in (abre em start-1h, fecha em start-30min) fica aberto por ~20min a partir de agora
            await _debug.SetTournamentDateAsync(Tournament.Id, DateTime.UtcNow.AddMinutes(50));
            await LoadAsync(Tournament.Id);
            IsDevBusy = false;
        }, _ => !IsDevBusy);
        AdvanceStageCommand = new RelayCommand(async _ =>
        {
            if (Tournament == null) return;
            IsDevBusy = true;
            // fecha check-in no passado: o LifecycleWorker (tick de 20s) marca no-show e gera a chave sozinho
            await _debug.SetTournamentDateAsync(Tournament.Id, DateTime.UtcNow.AddSeconds(1));
            await Task.Delay(TimeSpan.FromSeconds(21));
            await LoadAsync(Tournament.Id);
            IsDevBusy = false;
        }, _ => !IsDevBusy);

        _ = LoadAsync(tournamentId);
    }

    private async Task LoadAsync(string id)
    {
        IsLoading  = true;
        Tournament = await App.TournamentService.GetTournamentAsync(id);
        IsLoading  = false;
    }

    private async Task RegisterAsync()
    {
        if (Tournament == null) return;
        var teamId = App.UserService.CurrentUser?.TeamId ?? "";
        await App.TournamentService.RegisterAsync(Tournament.Id, teamId);
        if (Tournament != null) Tournament.IsRegistered = true;
        OnPropertyChanged(nameof(Tournament));
    }
}
