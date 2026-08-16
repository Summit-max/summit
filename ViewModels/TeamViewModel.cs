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
    private bool _isEditingTeam;
    private bool _confirmingDelete;
    private string _deleteErrorMessage = string.Empty;
    private string _editTeamName = string.Empty;
    private string _editTeamDescription = string.Empty;
    private string _editTeamLogoUrl = string.Empty;
    private string _editTeamCountry = string.Empty;

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
    public bool IsMyTeamCaptain => App.UserService.CurrentUser?.IsCaptain ?? false;
    public bool IsEditingTeam    { get => _isEditingTeam;   set => SetProperty(ref _isEditingTeam, value); }
    public bool ConfirmingDelete { get => _confirmingDelete;set => SetProperty(ref _confirmingDelete, value); }
    public string DeleteErrorMessage { get => _deleteErrorMessage; set => SetProperty(ref _deleteErrorMessage, value); }
    public string EditTeamName        { get => _editTeamName;        set => SetProperty(ref _editTeamName, value); }
    public string EditTeamDescription { get => _editTeamDescription; set => SetProperty(ref _editTeamDescription, value); }
    public string EditTeamLogoUrl     { get => _editTeamLogoUrl;     set => SetProperty(ref _editTeamLogoUrl, value); }
    public string EditTeamCountry     { get => _editTeamCountry;     set => SetProperty(ref _editTeamCountry, value); }

    public RelayCommand CreateTeamCommand    { get; }
    public RelayCommand ConfirmCreateCommand { get; }
    public RelayCommand CancelCreateCommand  { get; }
    public RelayCommand OpenInviteCommand    { get; }
    public RelayCommand SendInviteCommand    { get; }
    public RelayCommand CancelInviteCommand  { get; }
    public RelayCommand LeaveTeamCommand     { get; }
    public RelayCommand ViewPlayerCommand    { get; }
    public RelayCommand PromoteCommand           { get; }
    public RelayCommand DemoteCommand            { get; }
    public RelayCommand TransferOwnershipCommand { get; }
    public RelayCommand OpenJoinRequestsCommand  { get; }
    public RelayCommand OpenAuditLogCommand      { get; }
    public RelayCommand OpenEditTeamCommand      { get; }
    public RelayCommand SaveTeamEditCommand      { get; }
    public RelayCommand CancelTeamEditCommand    { get; }
    public RelayCommand DeleteTeamCommand        { get; }
    public RelayCommand ConfirmDeleteTeamCommand { get; }
    public RelayCommand CancelDeleteTeamCommand  { get; }
    public RelayCommand KickCommand              { get; }

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
        PromoteCommand = new RelayCommand(async p => await PromoteAsync(p as string ?? ""));
        DemoteCommand  = new RelayCommand(async p => await DemoteAsync(p as string ?? ""));
        TransferOwnershipCommand = new RelayCommand(async p => await TransferOwnershipAsync(p as string ?? ""));
        OpenJoinRequestsCommand = new RelayCommand(_ => App.Navigation.NavigateTo(new JoinRequestsViewModel()), _ => IsMyTeamCaptain);
        OpenAuditLogCommand = new RelayCommand(_ =>
        {
            if (Team != null) App.Navigation.NavigateTo(new AuditLogViewModel(teamId: Team.Id));
        });
        OpenEditTeamCommand = new RelayCommand(_ =>
        {
            if (Team == null) return;
            EditTeamName        = Team.Name;
            EditTeamDescription = Team.Description;
            EditTeamLogoUrl     = Team.LogoUrl;
            EditTeamCountry     = Team.Country;
            IsEditingTeam       = true;
        }, _ => IsMyTeamCaptain);
        SaveTeamEditCommand   = new RelayCommand(async _ => await SaveTeamEditAsync(), _ => !string.IsNullOrWhiteSpace(EditTeamName));
        CancelTeamEditCommand = new RelayCommand(_ => IsEditingTeam = false);
        DeleteTeamCommand        = new RelayCommand(_ => { ConfirmingDelete = true; DeleteErrorMessage = string.Empty; }, _ => IsMyTeamCaptain);
        ConfirmDeleteTeamCommand = new RelayCommand(async _ => await DeleteTeamAsync());
        CancelDeleteTeamCommand  = new RelayCommand(_ => ConfirmingDelete = false);
        KickCommand = new RelayCommand(async p => await KickAsync(p as string ?? ""));
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
        var nickname = InviteNickname;
        var (ok, message) = await App.TeamService.InviteByNicknameAsync(nickname.Trim());
        InviteMessage = ok
            ? $"Convite enviado para {nickname}."
            : message ?? "Não foi possível convidar: jogador não encontrado ou já tem time.";
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
        OnPropertyChanged(nameof(IsMyTeamCaptain));
    }

    private async Task PromoteAsync(string userId)
    {
        if (Team == null || string.IsNullOrEmpty(userId)) return;
        await App.TeamService.PromoteAsync(Team.Id, userId);
        await ReloadCurrentUserAndTeamAsync();
    }

    private async Task DemoteAsync(string userId)
    {
        if (Team == null || string.IsNullOrEmpty(userId)) return;
        await App.TeamService.DemoteAsync(Team.Id, userId);
        await ReloadCurrentUserAndTeamAsync();
    }

    private async Task TransferOwnershipAsync(string userId)
    {
        if (Team == null || string.IsNullOrEmpty(userId)) return;
        await App.TeamService.TransferOwnershipAsync(Team.Id, userId);
        await ReloadCurrentUserAndTeamAsync();
    }

    private async Task SaveTeamEditAsync()
    {
        if (Team == null) return;
        var updated = await App.TeamService.UpdateTeamAsync(Team.Id, EditTeamName.Trim(),
            EditTeamDescription, EditTeamLogoUrl, EditTeamCountry);
        IsEditingTeam = false;
        if (updated != null) Team = updated;
    }

    private async Task DeleteTeamAsync()
    {
        if (Team == null) return;
        var (ok, message) = await App.TeamService.DeleteTeamAsync(Team.Id);
        if (!ok)
        {
            DeleteErrorMessage = message ?? "Não foi possível excluir o time.";
            return;
        }
        ConfirmingDelete = false;
        await ReloadCurrentUserAndTeamAsync();
    }

    private async Task KickAsync(string userId)
    {
        if (Team == null || string.IsNullOrEmpty(userId)) return;
        await App.TeamService.KickMemberAsync(Team.Id, userId);
        await LoadTeamAsync();
    }
}
