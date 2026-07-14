using Summit.Models;

namespace Summit.ViewModels;

public class BadgesViewModel : BaseViewModel
{
    private List<Badge> _badges = new();
    private bool _isLoading;

    public List<Badge> Badges   { get => _badges;    set => SetProperty(ref _badges, value); }
    public bool IsLoading       { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public int UnlockedCount => Badges.Count(b => b.IsUnlocked);
    public int TotalCount    => Badges.Count;

    public BadgesViewModel()
    {
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        Badges    = await App.BadgeService.GetAllForCurrentUserAsync();
        IsLoading = false;
        OnPropertyChanged(nameof(UnlockedCount));
        OnPropertyChanged(nameof(TotalCount));
    }
}
