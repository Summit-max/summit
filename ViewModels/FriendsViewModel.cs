using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class FriendsViewModel : BaseViewModel
{
    private readonly FriendshipRepository _friendRepo = new();
    private readonly UserRepository _userRepo = new();
    private readonly TeamRepository _teamRepo = new();

    private List<User>          _friends    = new();
    private List<Friendship>    _incoming   = new();
    private List<Friendship>    _outgoing   = new();
    private List<TeamInvitation>_teamInvites= new();
    private string _searchNickname = string.Empty;
    private string _searchMessage  = string.Empty;
    private int _activeTab; // 0 friends, 1 incoming, 2 outgoing, 3 team-invites
    private bool _isLoading;

    public List<User>           Friends     { get => _friends;   set { SetProperty(ref _friends, value); OnPropertyChanged(nameof(FriendsCountLabel)); } }
    public List<Friendship>     Incoming    { get => _incoming;  set { SetProperty(ref _incoming, value); OnPropertyChanged(nameof(IncomingCountLabel)); } }
    public List<Friendship>     Outgoing    { get => _outgoing;  set => SetProperty(ref _outgoing, value); }
    public List<TeamInvitation> TeamInvites { get => _teamInvites;set { SetProperty(ref _teamInvites, value); OnPropertyChanged(nameof(TeamInvitesCountLabel)); } }

    public string SearchNickname { get => _searchNickname; set => SetProperty(ref _searchNickname, value); }
    public string SearchMessage  { get => _searchMessage;  set => SetProperty(ref _searchMessage, value); }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public int ActiveTab
    {
        get => _activeTab;
        set
        {
            SetProperty(ref _activeTab, value);
            OnPropertyChanged(nameof(IsFriendsTab));
            OnPropertyChanged(nameof(IsIncomingTab));
            OnPropertyChanged(nameof(IsOutgoingTab));
            OnPropertyChanged(nameof(IsTeamInvitesTab));
        }
    }
    public bool IsFriendsTab     => _activeTab == 0;
    public bool IsIncomingTab    => _activeTab == 1;
    public bool IsOutgoingTab    => _activeTab == 2;
    public bool IsTeamInvitesTab => _activeTab == 3;

    public string FriendsCountLabel     => $"{Friends.Count}";
    public string IncomingCountLabel    => $"{Incoming.Count}";
    public string TeamInvitesCountLabel => $"{TeamInvites.Count}";

    public RelayCommand ShowFriendsCommand     { get; }
    public RelayCommand ShowIncomingCommand    { get; }
    public RelayCommand ShowOutgoingCommand    { get; }
    public RelayCommand ShowTeamInvitesCommand { get; }

    public RelayCommand SendRequestCommand { get; }
    public RelayCommand AcceptCommand      { get; }
    public RelayCommand DeclineCommand     { get; }
    public RelayCommand RemoveFriendCommand{ get; }
    public RelayCommand ViewPlayerCommand  { get; }

    public RelayCommand AcceptTeamInviteCommand  { get; }
    public RelayCommand DeclineTeamInviteCommand { get; }

    public FriendsViewModel()
    {
        ShowFriendsCommand      = new RelayCommand(_ => ActiveTab = 0);
        ShowIncomingCommand     = new RelayCommand(_ => ActiveTab = 1);
        ShowOutgoingCommand     = new RelayCommand(_ => ActiveTab = 2);
        ShowTeamInvitesCommand  = new RelayCommand(_ => ActiveTab = 3);

        SendRequestCommand = new RelayCommand(async _ => await SendRequestAsync(),
                                              _ => !string.IsNullOrWhiteSpace(SearchNickname));
        AcceptCommand      = new RelayCommand(async p => await AcceptAsync(p as string ?? ""));
        DeclineCommand     = new RelayCommand(async p => await DeclineAsync(p as string ?? ""));
        RemoveFriendCommand= new RelayCommand(async p => await RemoveAsync(p as string ?? ""));
        ViewPlayerCommand  = new RelayCommand(p =>
        {
            if (p is string uid && !string.IsNullOrEmpty(uid))
                App.Navigation.NavigateTo(new PlayerProfileViewModel(uid));
        });

        AcceptTeamInviteCommand  = new RelayCommand(async p => await AcceptTeamInviteAsync(p as string ?? ""));
        DeclineTeamInviteCommand = new RelayCommand(async p => await DeclineTeamInviteAsync(p as string ?? ""));

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        var me = App.UserService.CurrentUser;
        if (me == null) { IsLoading = false; return; }

        Friends     = await _friendRepo.GetFriendsAsync(me.Id);
        Incoming    = await _friendRepo.GetIncomingRequestsAsync(me.Id);
        Outgoing    = await _friendRepo.GetOutgoingRequestsAsync(me.Id);
        TeamInvites = await _teamRepo.GetInvitationsForUserAsync(me.Id);
        IsLoading = false;
    }

    private async Task SendRequestAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me == null) return;
        var target = await _userRepo.GetByNicknameAsync(SearchNickname.Trim());
        if (target == null)
        {
            SearchMessage = "Jogador não encontrado.";
            return;
        }
        if (target.Id == me.Id)
        {
            SearchMessage = "Você não pode adicionar a si mesmo.";
            return;
        }
        var ok = await _friendRepo.SendRequestAsync(me.Id, target.Id);
        SearchMessage = ok ? $"Pedido enviado para {target.Nickname}." : "Já existe uma relação com esse jogador.";
        if (ok)
        {
            SearchNickname = string.Empty;
            await LoadAsync();
        }
    }

    private async Task AcceptAsync(string friendshipId)
    {
        var me = App.UserService.CurrentUser;
        if (me == null || string.IsNullOrEmpty(friendshipId)) return;
        await _friendRepo.AcceptRequestAsync(friendshipId, me.Id);
        await LoadAsync();
    }

    private async Task DeclineAsync(string friendshipId)
    {
        var me = App.UserService.CurrentUser;
        if (me == null || string.IsNullOrEmpty(friendshipId)) return;
        await _friendRepo.DeclineRequestAsync(friendshipId, me.Id);
        await LoadAsync();
    }

    private async Task RemoveAsync(string otherUserId)
    {
        var me = App.UserService.CurrentUser;
        if (me == null || string.IsNullOrEmpty(otherUserId)) return;
        await _friendRepo.RemoveFriendAsync(me.Id, otherUserId);
        await LoadAsync();
    }

    private async Task AcceptTeamInviteAsync(string invitationId)
    {
        await App.TeamService.AcceptInvitationAsync(invitationId);
        await LoadAsync();
    }

    private async Task DeclineTeamInviteAsync(string invitationId)
    {
        await App.TeamService.DeclineInvitationAsync(invitationId);
        await LoadAsync();
    }
}
