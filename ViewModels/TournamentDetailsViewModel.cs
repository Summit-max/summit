using Summit.Commands;
using Summit.Models;

namespace Summit.ViewModels;

public class TournamentDetailsViewModel : BaseViewModel
{
    private Tournament? _tournament;
    private bool _isLoading;

    public Tournament? Tournament { get => _tournament; set => SetProperty(ref _tournament, value); }
    public bool IsLoading         { get => _isLoading;  set => SetProperty(ref _isLoading, value); }

    public RelayCommand RegisterCommand  { get; }
    public RelayCommand BackCommand      { get; }
    public RelayCommand OpenMatchCommand { get; }

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
