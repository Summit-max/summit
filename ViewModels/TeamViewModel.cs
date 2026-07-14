using Summit.Commands;
using Summit.Models;

namespace Summit.ViewModels;

public class TeamViewModel : BaseViewModel
{
    private Team? _team;
    private bool _isCreating;
    private bool _isInviting;
    private string _newTeamName = string.Empty;
    private string _newTeamTag  = string.Empty;
    private string _inviteNickname = string.Empty;
    private string _inviteMessage = string.Empty;
    private bool _isLoading;

    public Team? Team
    {
        get => _team;
        set
        {
            SetProperty(ref _team, value);
            OnPropertyChanged(nameof(HasTeam));
            OnPropertyChanged(nameof(HasNoTeam));
            OnPropertyChanged(nameof(CanInvite));
        }
    }

    public bool HasTeam     => Team != null;
    public bool HasNoTeam   => Team == null;
    public bool IsCreating  { get => _isCreating;  set => SetProperty(ref _isCreating, value); }
    public bool IsInviting  { get => _isInviting;  set => SetProperty(ref _isInviting, value); }
    public bool IsLoading   { get => _isLoading;   set => SetProperty(ref _isLoading, value); }
    public string NewTeamName   { get => _newTeamName;   set => SetProperty(ref _newTeamName, value); }
    public string NewTeamTag    { get => _newTeamTag;    set => SetProperty(ref _newTeamTag, value); }
    public string InviteNickname{ get => _inviteNickname;set => SetProperty(ref _inviteNickname, value); }
    public string InviteMessage { get => _inviteMessage; set => SetProperty(ref _inviteMessage, value); }

    public bool CanInvite => App.UserService.CurrentUser?.CanInvite ?? false;

    public RelayCommand CreateTeamCommand    { get; }
    public RelayCommand ConfirmCreateCommand { get; }
    public RelayCommand CancelCreateCommand  { get; }
    public RelayCommand OpenInviteCommand    { get; }
    public RelayCommand SendInviteCommand    { get; }
    public RelayCommand CancelInviteCommand  { get; }
    public RelayCommand LeaveTeamCommand     { get; }
    public RelayCommand ViewPlayerCommand    { get; }

    public TeamViewModel()
    {
        CreateTeamCommand    = new RelayCommand(_ => IsCreating = true, _ => !HasTeam);
        ConfirmCreateCommand = new RelayCommand(async _ => await CreateTeamAsync(),
                                                _ => !string.IsNullOrWhiteSpace(NewTeamName) && !string.IsNullOrWhiteSpace(NewTeamTag));
        CancelCreateCommand  = new RelayCommand(_ => IsCreating = false);
        OpenInviteCommand    = new RelayCommand(_ => { IsInviting = true; InviteMessage = ""; }, _ => CanInvite);
        SendInviteCommand    = new RelayCommand(async _ => await SendInviteAsync(), _ => !string.IsNullOrWhiteSpace(InviteNickname));
        CancelInviteCommand  = new RelayCommand(_ => { IsInviting = false; InviteNickname = ""; InviteMessage = ""; });
        LeaveTeamCommand     = new RelayCommand(async _ => await LeaveTeamAsync(), _ => HasTeam);
        ViewPlayerCommand    = new RelayCommand(p =>
        {
            if (p is string userId && !string.IsNullOrEmpty(userId))
                App.Navigation.NavigateTo(new PlayerProfileViewModel(userId));
        });
        _ = LoadTeamAsync();
    }

    private async Task LoadTeamAsync()
    {
        IsLoading = true;
        var teamId = App.UserService.CurrentUser?.TeamId;
        Team = string.IsNullOrEmpty(teamId) ? null : await App.TeamService.GetTeamAsync(teamId);
        IsLoading = false;
    }

    private async Task CreateTeamAsync()
    {
        Team = await App.TeamService.CreateTeamAsync(NewTeamName, NewTeamTag);
        IsCreating = false;
        NewTeamName = string.Empty;
        NewTeamTag  = string.Empty;
        await ReloadCurrentUserAndTeamAsync();
    }

    private async Task SendInviteAsync()
    {
        var ok = await App.TeamService.InviteByNicknameAsync(InviteNickname.Trim());
        InviteMessage = ok
            ? $"Convite enviado para {InviteNickname}."
            : $"Não foi possível convidar: jogador não encontrado ou já tem time.";
        if (ok) InviteNickname = string.Empty;
    }

    private async Task LeaveTeamAsync()
    {
        await App.TeamService.LeaveTeamAsync();
        await ReloadCurrentUserAndTeamAsync();
    }

    private async Task ReloadCurrentUserAndTeamAsync()
    {
        await LoadTeamAsync();
        OnPropertyChanged(nameof(CanInvite));
    }
}
