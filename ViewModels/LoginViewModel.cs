using System.Windows;
using Summit.Commands;
using Summit.Views;

namespace Summit.ViewModels;

public class LoginViewModel : BaseViewModel
{
    private bool _isLoading;
    private string _errorMessage = string.Empty;

    public bool IsLoading
    {
        get => _isLoading;
        set { SetProperty(ref _isLoading, value); OnPropertyChanged(nameof(HasError)); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { SetProperty(ref _errorMessage, value); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public RelayCommand LoginWithSteamCommand { get; }
    public RelayCommand LoginDemoCommand { get; }
    public RelayCommand MinimizeCommand { get; }
    public RelayCommand CloseCommand { get; }

    public LoginViewModel()
    {
        LoginWithSteamCommand = new RelayCommand(async _ => await LoginAsync(false), _ => !IsLoading);
        LoginDemoCommand      = new RelayCommand(async _ => await LoginAsync(true),  _ => !IsLoading);
        MinimizeCommand = new RelayCommand(_ =>
        {
            if (Application.Current.MainWindow != null)
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
        });
        CloseCommand = new RelayCommand(_ => Application.Current.Shutdown());
    }

    private async Task LoginAsync(bool demo)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var user = demo
                ? await App.SteamAuth.LoginDemoAsync()
                : await App.SteamAuth.LoginWithSteamAsync();

            if (user == null)
            {
                ErrorMessage = "Não foi possível autenticar. Tente novamente.";
                return;
            }

            var shell = new MainShellView();
            shell.DataContext = new MainShellViewModel();
            shell.Show();

            foreach (Window w in Application.Current.Windows)
                if (w is LoginView) { w.Close(); break; }
        }
        catch
        {
            ErrorMessage = "Erro de conexão. Verifique sua internet e tente novamente.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
