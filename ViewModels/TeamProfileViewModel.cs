using Summit.Commands;
using Summit.Models;

namespace Summit.ViewModels;

public class TeamProfileViewModel : BaseViewModel
{
    private Team? _team;
    private bool _isLoading = true;

    public Team? Team { get => _team; set { SetProperty(ref _team, value); OnPropertyChanged(nameof(HasTeam)); } }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }
    public bool HasTeam => Team != null;

    public RelayCommand OpenPlayerCommand { get; }

    public TeamProfileViewModel(string teamId)
    {
        OpenPlayerCommand = new RelayCommand(p =>
        {
            if (p is string id) App.Navigation.NavigateTo(new PlayerProfileViewModel(id));
        });
        _ = LoadAsync(teamId);
    }

    private async Task LoadAsync(string teamId)
    {
        IsLoading = true;
        Team = await App.TeamService.GetTeamAsync(teamId);
        IsLoading = false;
    }
}
