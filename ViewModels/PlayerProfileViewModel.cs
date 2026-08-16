using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class PlayerProfileViewModel : BaseViewModel
{
    private readonly UserRepository _userRepo = new();
    private readonly FriendshipRepository _friendRepo = new();

    private User? _user;
    private List<Badge> _badges = new();
    private List<User> _mutualFriends = new();
    private FriendshipRepository.RelationStatus _relation;
    private string _actionMessage = string.Empty;
    private bool _isLoading;

    public User?  User     { get => _user;   set { SetProperty(ref _user, value); OnPropertyChanged(nameof(HasUser)); OnPropertyChanged(nameof(HasAvatar)); OnPropertyChanged(nameof(InitialLetter)); OnPropertyChanged(nameof(TeamLabel)); } }
    public bool   HasUser  => _user != null;
    public bool   HasAvatar => !string.IsNullOrEmpty(_user?.AvatarUrl);
    public string InitialLetter => string.IsNullOrEmpty(_user?.Nickname) ? "?" : _user!.Nickname[..1].ToUpperInvariant();
    public string TeamLabel => string.IsNullOrEmpty(_user?.Team?.Tag) ? "Sem time" : $"[{_user.Team.Tag}] {_user.Team.Name}";

    public List<Badge> Badges   { get => _badges;   set => SetProperty(ref _badges, value); }
    public List<User> MutualFriends { get => _mutualFriends; set { SetProperty(ref _mutualFriends, value); OnPropertyChanged(nameof(HasMutualFriends)); OnPropertyChanged(nameof(MutualFriendsLabel)); } }
    public bool HasMutualFriends => _mutualFriends.Count > 0;
    public string MutualFriendsLabel => _mutualFriends.Count == 1 ? "1 amigo em comum" : $"{_mutualFriends.Count} amigos em comum";
    public string ActionMessage { get => _actionMessage; set => SetProperty(ref _actionMessage, value); }
    public bool IsLoading       { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public bool IsSelf          => App.UserService.CurrentUser?.Id == _user?.Id;
    public bool CanAddFriend    => !IsSelf && _relation == FriendshipRepository.RelationStatus.None;
    public bool IsAlreadyFriend => _relation == FriendshipRepository.RelationStatus.Friends;
    public bool HasOutgoing     => _relation == FriendshipRepository.RelationStatus.OutgoingPending;
    public bool HasIncoming     => _relation == FriendshipRepository.RelationStatus.IncomingPending;
    public bool IsBlocked       => _relation == FriendshipRepository.RelationStatus.Blocked;
    public bool CanBlock        => !IsSelf && !IsBlocked;

    public RelayCommand AddFriendCommand { get; }
    public RelayCommand BlockCommand     { get; }
    public RelayCommand UnblockCommand   { get; }
    public RelayCommand BackCommand      { get; }

    public PlayerProfileViewModel() : this("usr_ghost") { }

    public PlayerProfileViewModel(string userId)
    {
        AddFriendCommand = new RelayCommand(async _ => await AddFriendAsync(), _ => CanAddFriend);
        BlockCommand     = new RelayCommand(async _ => await BlockAsync(), _ => CanBlock);
        UnblockCommand   = new RelayCommand(async _ => await UnblockAsync(), _ => IsBlocked);
        BackCommand      = new RelayCommand(_ =>
        {
            if (App.Navigation.CanGoBack) App.Navigation.GoBack();
        });
        _ = LoadAsync(userId);
    }

    private async Task LoadAsync(string userId)
    {
        IsLoading = true;
        User = await _userRepo.GetByIdAsync(userId);
        if (User != null)
        {
            Badges = await App.BadgeService.GetUnlockedForUserAsync(User.Id);
            var me = App.UserService.CurrentUser;
            if (me != null && me.Id != User.Id)
            {
                _relation = await _friendRepo.GetRelationAsync(me.Id, User.Id);
                var myFriends = await _friendRepo.GetFriendsAsync(me.Id);
                var theirFriends = await _friendRepo.GetFriendsAsync(User.Id);
                var theirIds = theirFriends.Select(f => f.Id).ToHashSet();
                MutualFriends = myFriends.Where(f => theirIds.Contains(f.Id)).ToList();
            }
        }
        IsLoading = false;
        NotifyRelation();
    }

    private async Task AddFriendAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me == null || User == null) return;
        var ok = await _friendRepo.SendRequestAsync(me.Id, User.Id);
        if (ok)
        {
            _relation = FriendshipRepository.RelationStatus.OutgoingPending;
            ActionMessage = "Pedido de amizade enviado.";
        }
        else
        {
            ActionMessage = "Não foi possível enviar o pedido.";
        }
        NotifyRelation();
    }

    private async Task BlockAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me == null || User == null) return;
        var ok = await _friendRepo.BlockAsync(me.Id, User.Id);
        if (ok)
        {
            _relation = FriendshipRepository.RelationStatus.Blocked;
            ActionMessage = "Usuário bloqueado.";
        }
        NotifyRelation();
    }

    private async Task UnblockAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me == null || User == null) return;
        var ok = await _friendRepo.UnblockAsync(me.Id, User.Id);
        if (ok)
        {
            _relation = FriendshipRepository.RelationStatus.None;
            ActionMessage = "Usuário desbloqueado.";
        }
        NotifyRelation();
    }

    private void NotifyRelation()
    {
        OnPropertyChanged(nameof(CanAddFriend));
        OnPropertyChanged(nameof(IsAlreadyFriend));
        OnPropertyChanged(nameof(HasOutgoing));
        OnPropertyChanged(nameof(HasIncoming));
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(CanBlock));
        OnPropertyChanged(nameof(IsSelf));
    }
}
