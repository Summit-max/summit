using Summit.Commands;
using Summit.Models;

namespace Summit.ViewModels;

public class FilterItem : BaseViewModel
{
    private bool _isSelected;
    public string Name { get; init; } = string.Empty;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public class TournamentsViewModel : BaseViewModel
{
    private List<Tournament> _all = new();
    private List<Tournament> _filtered = new();
    private bool _isLoading;

    public List<Tournament> Filtered  { get => _filtered;  set => SetProperty(ref _filtered, value); }
    public bool             IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public List<FilterItem> Filters { get; } = new()
    {
        new() { Name = "Todos",        IsSelected = true },
        new() { Name = "Abertas",      IsSelected = false },
        new() { Name = "Em Andamento", IsSelected = false },
        new() { Name = "Em Breve",     IsSelected = false },
        new() { Name = "Encerrados",   IsSelected = false },
    };

    public RelayCommand FilterCommand   { get; }
    public RelayCommand RegisterCommand { get; }
    public RelayCommand DetailsCommand  { get; }

    public TournamentsViewModel()
    {
        FilterCommand   = new RelayCommand(p => { if (p is string f) SetFilter(f); });
        RegisterCommand = new RelayCommand(async p => await RegisterAsync(p as string ?? ""));
        DetailsCommand  = new RelayCommand(p =>
        {
            if (p is string id)
                App.Navigation.NavigateTo(new TournamentDetailsViewModel(id));
        });
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        _all = await App.TournamentService.GetTournamentsAsync();
        ApplyFilter("Todos");
        IsLoading = false;
    }

    private void SetFilter(string name)
    {
        foreach (var f in Filters) f.IsSelected = f.Name == name;
        ApplyFilter(name);
    }

    private void ApplyFilter(string name)
    {
        Filtered = name switch
        {
            "Abertas"      => _all.Where(t => t.Status == TournamentStatus.Open).ToList(),
            "Em Andamento" => _all.Where(t => t.Status == TournamentStatus.InProgress).ToList(),
            "Em Breve"     => _all.Where(t => t.Status == TournamentStatus.Upcoming).ToList(),
            "Encerrados"   => _all.Where(t => t.Status == TournamentStatus.Finished).ToList(),
            _              => _all.ToList()
        };
    }

    private async Task RegisterAsync(string tournamentId)
    {
        var teamId = App.UserService.CurrentUser?.TeamId ?? "";
        await App.TournamentService.RegisterAsync(tournamentId, teamId);
        await LoadAsync();
    }
}
