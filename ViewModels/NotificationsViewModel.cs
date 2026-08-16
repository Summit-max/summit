using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class NotificationsViewModel : BaseViewModel
{
    private readonly NotificationRepository _repo = new();

    private List<Notification> _notifications = new();
    private bool _isLoading;

    public List<Notification> Notifications
    {
        get => _notifications;
        set { SetProperty(ref _notifications, value); OnPropertyChanged(nameof(HasNone)); OnPropertyChanged(nameof(UnreadCount)); }
    }
    public bool IsLoading { get => _isLoading; set { SetProperty(ref _isLoading, value); OnPropertyChanged(nameof(HasNone)); } }
    public bool HasNone => !IsLoading && Notifications.Count == 0;
    public int UnreadCount => Notifications.Count(n => !n.IsRead);

    public RelayCommand MarkReadCommand { get; }
    public RelayCommand MarkAllReadCommand { get; }
    public RelayCommand BackCommand { get; }

    public NotificationsViewModel()
    {
        MarkReadCommand = new RelayCommand(async p => await MarkReadAsync(p as string ?? ""));
        MarkAllReadCommand = new RelayCommand(async _ => await MarkAllReadAsync(), _ => UnreadCount > 0);
        BackCommand = new RelayCommand(_ =>
        {
            if (App.Navigation.CanGoBack) App.Navigation.GoBack();
        });
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        var me = App.UserService.CurrentUser;
        Notifications = me == null ? new() : await _repo.GetAsync(me.Id);
        IsLoading = false;
    }

    private async Task MarkReadAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        await _repo.MarkReadAsync(id);
        var item = Notifications.FirstOrDefault(n => n.Id == id);
        if (item != null) item.IsRead = true;
        OnPropertyChanged(nameof(Notifications));
        OnPropertyChanged(nameof(UnreadCount));
    }

    private async Task MarkAllReadAsync()
    {
        var me = App.UserService.CurrentUser;
        if (me == null) return;
        await _repo.MarkAllReadAsync(me.Id);
        foreach (var n in Notifications) n.IsRead = true;
        OnPropertyChanged(nameof(Notifications));
        OnPropertyChanged(nameof(UnreadCount));
    }
}
