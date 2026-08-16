using Summit.Commands;
using Summit.Models;

namespace Summit.ViewModels;

public class JoinRequestsViewModel : BaseViewModel
{
    private List<TeamJoinRequest> _requests = new();
    private bool _isLoading;

    public List<TeamJoinRequest> Requests { get => _requests; set { SetProperty(ref _requests, value); OnPropertyChanged(nameof(HasNone)); } }
    public bool IsLoading { get => _isLoading; set { SetProperty(ref _isLoading, value); OnPropertyChanged(nameof(HasNone)); } }
    public bool HasNone => !IsLoading && Requests.Count == 0;

    public RelayCommand AcceptCommand  { get; }
    public RelayCommand DeclineCommand { get; }
    public RelayCommand BackCommand    { get; }

    public JoinRequestsViewModel()
    {
        AcceptCommand  = new RelayCommand(async p => await AcceptAsync(p as string ?? ""));
        DeclineCommand = new RelayCommand(async p => await DeclineAsync(p as string ?? ""));
        BackCommand    = new RelayCommand(_ =>
        {
            if (App.Navigation.CanGoBack) App.Navigation.GoBack();
        });
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        var teamId = App.UserService.CurrentUser?.TeamId;
        Requests = string.IsNullOrEmpty(teamId) ? new() : await App.TeamService.GetJoinRequestsAsync(teamId);
        IsLoading = false;
    }

    private async Task AcceptAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        await App.TeamService.AcceptJoinRequestAsync(id);
        await LoadAsync();
    }

    private async Task DeclineAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        await App.TeamService.DeclineJoinRequestAsync(id);
        await LoadAsync();
    }
}
