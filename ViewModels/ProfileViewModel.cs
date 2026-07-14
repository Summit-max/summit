using Wallbang.Commands;
using Wallbang.Models;

namespace Wallbang.ViewModels;

public class ProfileViewModel : BaseViewModel
{
    private bool _isEditing;
    private string _editBio = string.Empty;
    private string _editRole = string.Empty;

    public User? User         => App.UserService.CurrentUser;
    public string Nickname    => User?.Nickname          ?? "—";
    public string AvatarUrl   => User?.AvatarUrl         ?? string.Empty;
    public bool   HasAvatar   => !string.IsNullOrEmpty(User?.AvatarUrl);
    public string NicknameInitial => string.IsNullOrEmpty(User?.Nickname)
        ? "?"
        : User!.Nickname[..1].ToUpperInvariant();
    public string SteamId     => User?.SteamId           ?? "—";
    public string Rank        => User?.Rank              ?? "—";
    public int    Level       => User?.Level             ?? 0;
    public string Bio         => User?.Bio               ?? "Sem bio definida.";
    public string PrimaryRole => User?.PrimaryRole       ?? "—";
    public string WinRateText => $"{(User?.WinRate ?? 0):P0}";
    public string KDText      => $"{(User?.KD ?? 0):F2}";
    public string HSText      => $"{(User?.HeadshotPercent ?? 0):P0}";
    public int    Matches     => User?.TotalMatches      ?? 0;
    public int    Wins        => User?.TotalWins         ?? 0;
    public string FavoriteMap => User?.FavoriteMap       ?? "—";

    public bool IsEditing    { get => _isEditing;  set { SetProperty(ref _isEditing, value); OnPropertyChanged(nameof(IsNotEditing)); } }
    public bool IsNotEditing => !IsEditing;

    public string EditBio  { get => _editBio;  set => SetProperty(ref _editBio, value); }
    public string EditRole { get => _editRole; set => SetProperty(ref _editRole, value); }

    public List<Badge> Badges { get; private set; } = new();

    public RelayCommand EditCommand  { get; }
    public RelayCommand SaveCommand  { get; }
    public RelayCommand CancelCommand { get; }

    public ProfileViewModel()
    {
        EditCommand   = new RelayCommand(_ => StartEdit());
        SaveCommand   = new RelayCommand(async _ => await SaveAsync());
        CancelCommand = new RelayCommand(_ => CancelEdit());
        _ = LoadBadgesAsync();
    }

    private void StartEdit()
    {
        EditBio   = Bio;
        EditRole  = PrimaryRole;
        IsEditing = true;
    }

    private async Task SaveAsync()
    {
        await App.UserService.UpdateBioAsync(EditBio);
        await App.UserService.UpdateRoleAsync(EditRole);
        IsEditing = false;
        OnPropertyChanged(nameof(Bio));
        OnPropertyChanged(nameof(PrimaryRole));
    }

    private void CancelEdit() => IsEditing = false;

    private async Task LoadBadgesAsync()
    {
        if (User == null) return;
        Badges = await App.BadgeService.GetUnlockedForUserAsync(User.Id);
        OnPropertyChanged(nameof(Badges));
    }
}
