using Summit.Commands;

namespace Summit.ViewModels;

/// <summary>Prompt mínimo de primeiro login — pede país e role antes de liberar a Home.</summary>
public class OnboardingViewModel : BaseViewModel
{
    private string _country = string.Empty;
    private string _role = string.Empty;

    public string Nickname => App.UserService.CurrentUser?.Nickname ?? "";
    public string Country { get => _country; set => SetProperty(ref _country, value); }
    public string Role    { get => _role;    set => SetProperty(ref _role, value); }

    public RelayCommand ContinueCommand { get; }

    public OnboardingViewModel()
    {
        ContinueCommand = new RelayCommand(async _ => await ContinueAsync(),
            _ => !string.IsNullOrWhiteSpace(Country) && !string.IsNullOrWhiteSpace(Role));
    }

    private async Task ContinueAsync()
    {
        await App.UserService.UpdateCountryAsync(Country.Trim());
        await App.UserService.UpdateRoleAsync(Role.Trim());
        App.Navigation.NavigateTo(new HomeViewModel());
    }
}
