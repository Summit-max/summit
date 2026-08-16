using Summit.Commands;
using Summit.Data;
using Summit.Models;

namespace Summit.ViewModels;

public class PickerOption<T> : BaseViewModel
{
    public T Value { get; init; } = default!;
    public string Label { get; init; } = string.Empty;
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>Criação (e edição — plan.md RF-09) de campeonato pelo organizador. Mesma tela pros
/// dois casos: sem <see cref="_editingId"/> é criação; com ele, pré-preenche e chama
/// UpdateTournamentAsync no lugar de CreateTournamentAsync.</summary>
public class CreateTournamentViewModel : BaseViewModel
{
    private readonly TournamentRepository _repo = new();
    private readonly string? _editingId;

    private string _name = string.Empty;
    private string _description = string.Empty;
    private string _region = "América do Sul";
    private string _startDateText = DateTime.UtcNow.AddDays(7).ToString("dd/MM/yyyy HH:mm");
    private string _mapPoolCsv = "Mirage, Inferno, Nuke, Ancient, Anubis, Dust2, Vertigo";
    private string _minTeams = "4";
    private string _maxTeams = "8";
    private string _prize = string.Empty;
    private bool _isPaidEntry;
    private string _entryFee = string.Empty;
    private string _message = string.Empty;
    private bool _isSaving;

    public bool   IsEditMode      => _editingId != null;
    public string PageTitle       => IsEditMode ? "EDITAR CAMPEONATO" : "CRIAR CAMPEONATO";
    public string SaveButtonText  => IsEditMode ? "SALVAR ALTERAÇÕES" : "CRIAR CAMPEONATO";

    public string Name          { get => _name;          set => SetProperty(ref _name, value); }
    public string Description   { get => _description;   set => SetProperty(ref _description, value); }
    public string Region        { get => _region;         set => SetProperty(ref _region, value); }
    public string StartDateText { get => _startDateText;  set => SetProperty(ref _startDateText, value); }
    public string MapPoolCsv    { get => _mapPoolCsv;     set => SetProperty(ref _mapPoolCsv, value); }
    public string MinTeams      { get => _minTeams;       set => SetProperty(ref _minTeams, value); }
    public string MaxTeams      { get => _maxTeams;       set => SetProperty(ref _maxTeams, value); }
    public string Prize         { get => _prize;          set => SetProperty(ref _prize, value); }
    public bool   IsPaidEntry   { get => _isPaidEntry;    set => SetProperty(ref _isPaidEntry, value); }
    public string EntryFee      { get => _entryFee;       set => SetProperty(ref _entryFee, value); }
    public string Message       { get => _message;        set => SetProperty(ref _message, value); }
    public bool   IsSaving      { get => _isSaving;       set => SetProperty(ref _isSaving, value); }

    public List<PickerOption<TournamentFormat>> FormatOptions { get; }
    public List<PickerOption<SeriesFormat>> SeriesOptions { get; }
    public List<PickerOption<SeriesFormat>> FinalSeriesOptions { get; }

    public RelayCommand SelectFormatCommand      { get; }
    public RelayCommand SelectSeriesCommand      { get; }
    public RelayCommand SelectFinalSeriesCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand BackCommand { get; }

    public CreateTournamentViewModel() : this(null) { }

    public CreateTournamentViewModel(Tournament? existing)
    {
        _editingId = existing?.Id;

        FormatOptions = new()
        {
            new() { Value = TournamentFormat.SingleElimination, Label = "ELIMINAÇÃO SIMPLES" },
            new() { Value = TournamentFormat.DoubleElimination,  Label = "ELIMINAÇÃO DUPLA" },
            new() { Value = TournamentFormat.Swiss,               Label = "SUÍÇO" },
        };
        SeriesOptions = new()
        {
            new() { Value = SeriesFormat.MD1, Label = "MD1" },
            new() { Value = SeriesFormat.MD3, Label = "MD3" },
            new() { Value = SeriesFormat.MD5, Label = "MD5" },
        };
        FinalSeriesOptions = new()
        {
            new() { Value = SeriesFormat.MD1, Label = "MD1" },
            new() { Value = SeriesFormat.MD3, Label = "MD3" },
            new() { Value = SeriesFormat.MD5, Label = "MD5" },
        };

        if (existing != null)
        {
            Name = existing.Name;
            Description = existing.Description;
            Region = existing.Region;
            StartDateText = existing.StartDate.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            MapPoolCsv = existing.MapPoolCsv;
            MinTeams = existing.MinTeams.ToString();
            MaxTeams = existing.MaxTeams.ToString();
            Prize = existing.Prize;
            IsPaidEntry = existing.IsPaidEntry;
            EntryFee = existing.EntryFee;
            foreach (var o in FormatOptions) o.IsSelected = o.Value == existing.FormatType;
            foreach (var o in SeriesOptions) o.IsSelected = o.Value == existing.Series;
            foreach (var o in FinalSeriesOptions) o.IsSelected = o.Value == existing.FinalSeries;
        }
        else
        {
            FormatOptions[0].IsSelected = true;
            SeriesOptions[0].IsSelected = true;
            FinalSeriesOptions[1].IsSelected = true;
        }

        SelectFormatCommand = new RelayCommand(p =>
        {
            foreach (var o in FormatOptions) o.IsSelected = Equals(o.Value, p);
        });
        SelectSeriesCommand = new RelayCommand(p =>
        {
            foreach (var o in SeriesOptions) o.IsSelected = Equals(o.Value, p);
        });
        SelectFinalSeriesCommand = new RelayCommand(p =>
        {
            foreach (var o in FinalSeriesOptions) o.IsSelected = Equals(o.Value, p);
        });

        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => !IsSaving);
        BackCommand = new RelayCommand(_ =>
        {
            if (App.Navigation.CanGoBack) App.Navigation.GoBack();
        });
    }

    private async Task SaveAsync()
    {
        Message = string.Empty;
        var me = App.UserService.CurrentUser;
        if (me == null) return;

        if (string.IsNullOrWhiteSpace(Name)) { Message = "Nome é obrigatório."; return; }
        if (!DateTime.TryParse(StartDateText, out var startDateLocal))
        {
            Message = "Data inválida — use o formato dd/MM/yyyy HH:mm.";
            return;
        }
        if (!int.TryParse(MinTeams, out var minTeams) || !int.TryParse(MaxTeams, out var maxTeams))
        {
            Message = "Mínimo/máximo de times precisam ser números.";
            return;
        }

        var format = FormatOptions.First(o => o.IsSelected).Value;
        var series = SeriesOptions.First(o => o.IsSelected).Value;
        var finalSeries = FinalSeriesOptions.First(o => o.IsSelected).Value;

        IsSaving = true;
        if (IsEditMode)
        {
            var (ok, message) = await _repo.UpdateTournamentAsync(_editingId!,
                Name.Trim(), Description, Region, startDateLocal.ToUniversalTime(),
                format, series, finalSeries, MapPoolCsv,
                minTeams, maxTeams, Prize, IsPaidEntry, EntryFee, me.Id);
            IsSaving = false;

            if (!ok)
            {
                Message = message ?? "Não foi possível salvar as alterações.";
                return;
            }
            App.Navigation.NavigateTo(new TournamentDetailsViewModel(_editingId!));
        }
        else
        {
            var (ok, tournament, message) = await _repo.CreateTournamentAsync(
                Name.Trim(), Description, Region, startDateLocal.ToUniversalTime(),
                format, series, finalSeries, MapPoolCsv,
                minTeams, maxTeams, Prize, IsPaidEntry, EntryFee,
                me.Id, me.Nickname);
            IsSaving = false;

            if (!ok || tournament == null)
            {
                Message = message ?? "Não foi possível criar o campeonato.";
                return;
            }
            App.Navigation.NavigateTo(new TournamentDetailsViewModel(tournament.Id));
        }
    }
}
