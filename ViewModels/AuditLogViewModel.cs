using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class AuditLogViewModel : BaseViewModel
{
    private readonly AuditRepository _repo = new();

    private List<AuditLog> _logs = new();
    private bool _isLoading;

    public List<AuditLog> Logs { get => _logs; set { SetProperty(ref _logs, value); OnPropertyChanged(nameof(HasNone)); } }
    public bool IsLoading { get => _isLoading; set { SetProperty(ref _isLoading, value); OnPropertyChanged(nameof(HasNone)); } }
    public bool HasNone => !IsLoading && Logs.Count == 0;

    public RelayCommand BackCommand { get; }

    public AuditLogViewModel() : this(null, null) { }

    public AuditLogViewModel(string? teamId = null, string? tournamentId = null)
    {
        BackCommand = new RelayCommand(_ =>
        {
            if (App.Navigation.CanGoBack) App.Navigation.GoBack();
        });
        _ = LoadAsync(teamId, tournamentId);
    }

    private async Task LoadAsync(string? teamId, string? tournamentId)
    {
        IsLoading = true;
        Logs = await _repo.GetAsync(teamId, tournamentId, take: 100);
        IsLoading = false;
    }
}
