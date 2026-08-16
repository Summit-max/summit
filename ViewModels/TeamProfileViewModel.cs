using Summit.Commands;
using Summit.Models;

namespace Summit.ViewModels;

public class TeamProfileViewModel : BaseViewModel
{
    private Team? _team;
    private bool _isLoading = true;
    private bool _requestSent;
    private string _joinMessage = string.Empty;

    public Team? Team { get => _team; set { SetProperty(ref _team, value); OnPropertyChanged(nameof(HasTeam)); OnPropertyChanged(nameof(CanRequestJoin)); } }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public bool HasTeam => Team != null;
    public string JoinMessage { get => _joinMessage; set => SetProperty(ref _joinMessage, value); }

    public bool CanRequestJoin => HasTeam && !_requestSent && App.UserService.CurrentUser?.TeamId == null;
    public bool RequestSent => _requestSent;

    public RelayCommand OpenPlayerCommand  { get; }
    public RelayCommand RequestJoinCommand { get; }

    public TeamProfileViewModel(string teamId)
    {
        OpenPlayerCommand = new RelayCommand(p =>
        {
            if (p is string id) App.Navigation.NavigateTo(new PlayerProfileViewModel(id));
        });
        RequestJoinCommand = new RelayCommand(async _ => await RequestJoinAsync(), _ => CanRequestJoin);
        _ = LoadAsync(teamId);
    }

    private async Task LoadAsync(string teamId)
    {
        IsLoading = true;
        Team = await App.TeamService.GetTeamAsync(teamId);
        IsLoading = false;
    }

    private async Task RequestJoinAsync()
    {
        if (Team == null) return;
        var result = await App.TeamService.RequestToJoinAsync(Team.Id, null);
        _requestSent = result != null;
        JoinMessage = result != null ? "Solicitação enviada ao dono do time." : "Não foi possível enviar a solicitação.";
        OnPropertyChanged(nameof(CanRequestJoin));
        OnPropertyChanged(nameof(RequestSent));
    }
}
