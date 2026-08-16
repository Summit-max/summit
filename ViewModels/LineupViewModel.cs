using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class LineupMemberItem : BaseViewModel
{
    public User User { get; init; } = null!;
    private bool _isSelected;
    private bool _isCaptainChoice;
    public bool IsSelected      { get => _isSelected;      set => SetProperty(ref _isSelected, value); }
    public bool IsCaptainChoice { get => _isCaptainChoice; set => SetProperty(ref _isCaptainChoice, value); }
}

/// <summary>Escolha dos 5 jogadores + capitão da escalação (espec-times §16-21).</summary>
public class LineupViewModel : BaseViewModel
{
    private readonly TeamRepository _teamRepo = new();
    private readonly TournamentRepository _tourRepo = new();
    private readonly string _tournamentId;
    private readonly string _teamId;

    private List<LineupMemberItem> _members = new();
    private int _requiredCount = 5;
    private string _message = string.Empty;
    private bool _isLoading = true;
    private bool _isSaving;

    public List<LineupMemberItem> Members { get => _members; set => SetProperty(ref _members, value); }
    public int RequiredCount { get => _requiredCount; set => SetProperty(ref _requiredCount, value); }
    public string Message    { get => _message;       set => SetProperty(ref _message, value); }
    public bool IsLoading    { get => _isLoading;      set => SetProperty(ref _isLoading, value); }
    public bool IsSaving     { get => _isSaving;       set => SetProperty(ref _isSaving, value); }
    public int SelectedCount => Members.Count(m => m.IsSelected);
    public string SelectedCountLabel => $"{SelectedCount}/{RequiredCount} SELECIONADOS";

    public RelayCommand ToggleSelectCommand { get; }
    public RelayCommand SetCaptainCommand   { get; }
    public RelayCommand SaveCommand         { get; }
    public RelayCommand BackCommand         { get; }

    public LineupViewModel(string tournamentId, string teamId)
    {
        _tournamentId = tournamentId;
        _teamId = teamId;

        ToggleSelectCommand = new RelayCommand(p => ToggleSelect(p as string ?? ""));
        SetCaptainCommand   = new RelayCommand(p => SetCaptain(p as string ?? ""));
        SaveCommand         = new RelayCommand(async _ => await SaveAsync());
        BackCommand         = new RelayCommand(_ =>
        {
            if (App.Navigation.CanGoBack) App.Navigation.GoBack();
        });
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        var team = await _teamRepo.GetByIdAsync(_teamId);
        var tournament = await _tourRepo.GetByIdAsync(_tournamentId);
        var tt = tournament?.TournamentTeams.FirstOrDefault(x => x.TeamId == _teamId);
        var currentLineupIds = tt?.Lineup.Select(l => l.UserId).ToHashSet() ?? new HashSet<string>();

        RequiredCount = Math.Min(5, team?.Members.Count ?? 0);
        Members = (team?.Members ?? new()).Select(u => new LineupMemberItem
        {
            User = u,
            IsSelected = currentLineupIds.Contains(u.Id),
            IsCaptainChoice = tt?.CaptainUserId == u.Id
        }).ToList();
        IsLoading = false;
        NotifyCount();
    }

    private void ToggleSelect(string userId)
    {
        var item = Members.FirstOrDefault(m => m.User.Id == userId);
        if (item == null) return;
        if (!item.IsSelected && SelectedCount >= RequiredCount) return;
        item.IsSelected = !item.IsSelected;
        if (!item.IsSelected) item.IsCaptainChoice = false;
        NotifyCount();
    }

    private void SetCaptain(string userId)
    {
        var item = Members.FirstOrDefault(m => m.User.Id == userId);
        if (item == null || !item.IsSelected) return;
        foreach (var m in Members) m.IsCaptainChoice = false;
        item.IsCaptainChoice = true;
    }

    private async Task SaveAsync()
    {
        var selected = Members.Where(m => m.IsSelected).ToList();
        var captain = Members.FirstOrDefault(m => m.IsCaptainChoice);
        if (selected.Count != RequiredCount)
        {
            Message = $"Selecione exatamente {RequiredCount} jogadores.";
            return;
        }
        if (captain == null)
        {
            Message = "Escolha o capitão da escalação entre os selecionados.";
            return;
        }

        var me = App.UserService.CurrentUser;
        if (me == null) return;

        IsSaving = true;
        var (ok, msg) = await _tourRepo.UpdateLineupAsync(_tournamentId, _teamId, me.Id,
            selected.Select(m => m.User.Id).ToList(), captain.User.Id);
        IsSaving = false;
        Message = ok ? "Escalação salva." : (msg ?? "Não foi possível salvar a escalação.");
    }

    private void NotifyCount()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountLabel));
    }
}
