using Wallbang.Models;

namespace Wallbang.ViewModels;

public class StatsViewModel : BaseViewModel
{
    private PlayerStats? _stats;
    private bool _isLoading;

    public PlayerStats? Stats    { get => _stats;     set => SetProperty(ref _stats, value); }
    public bool         IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public string KDText         => $"{Stats?.KD:F2}";
    public string WinRateText    => $"{(Stats?.WinRate ?? 0):P0}";
    public string HSText         => $"{(Stats?.HeadshotPercent ?? 0):P0}";
    public string ADRText        => $"{Stats?.AvgDamagePerRound:F1}";
    public double WinRateValue   => Stats?.WinRate ?? 0;
    public double HSValue        => Stats?.HeadshotPercent ?? 0;
    public double KDNormalized   => Math.Min((Stats?.KD ?? 0) / 2.0, 1.0);
    public double ADRNormalized  => Math.Min((Stats?.AvgDamagePerRound ?? 0) / 150.0, 1.0);
    public int    TotalMatches   => Stats?.TotalMatches  ?? 0;
    public int    TotalWins      => Stats?.TotalWins     ?? 0;
    public int    TotalLosses    => TotalMatches - TotalWins;
    public int    TotalKills     => Stats?.TotalKills    ?? 0;
    public int    TotalDeaths    => Stats?.TotalDeaths   ?? 0;
    public int    TotalAssists   => Stats?.TotalAssists  ?? 0;
    public string FavoriteMap    => Stats?.FavoriteMap   ?? "—";
    public string FavoriteWeapon => Stats?.FavoriteWeapon ?? "—";

    public StatsViewModel()
    {
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        Stats     = await App.StatsService.GetStatsAsync(App.UserService.CurrentUser?.Id ?? "");
        IsLoading = false;
        OnPropertyChanged(nameof(KDText));
        OnPropertyChanged(nameof(WinRateText));
        OnPropertyChanged(nameof(HSText));
        OnPropertyChanged(nameof(ADRText));
        OnPropertyChanged(nameof(TotalMatches));
        OnPropertyChanged(nameof(TotalWins));
        OnPropertyChanged(nameof(TotalLosses));
        OnPropertyChanged(nameof(TotalKills));
        OnPropertyChanged(nameof(TotalDeaths));
        OnPropertyChanged(nameof(TotalAssists));
        OnPropertyChanged(nameof(WinRateValue));
        OnPropertyChanged(nameof(HSValue));
        OnPropertyChanged(nameof(KDNormalized));
        OnPropertyChanged(nameof(ADRNormalized));
        OnPropertyChanged(nameof(FavoriteMap));
        OnPropertyChanged(nameof(FavoriteWeapon));
    }
}
