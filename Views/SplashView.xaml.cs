using System.Windows;
using Summit.Models;
using Summit.Services;
using Summit.ViewModels;

namespace Summit.Views;

public partial class SplashView : Window
{
    public SplashView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
#if !DEBUG
        // kill-switch só vale pra build de Release (a que é distribuída pra fora) — build de
        // Debug (dotnet run / F5) nunca consulta isso, pra não travar o próprio dev.
        var statusTask = ApiClient.GetAsync<AppStatus>("/api/app/status");
#endif
        var restoreTask = App.SteamAuth.TryRestoreSessionAsync();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(2.4));

#if !DEBUG
        await Task.WhenAll(statusTask, restoreTask, delayTask);

        var status = await statusTask;
        if (status != null && !status.Active)
        {
            MessageBox.Show(
                string.IsNullOrWhiteSpace(status.Message) ? "Este teste já foi encerrado." : status.Message,
                "Summit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Application.Current.Shutdown();
            return;
        }
#else
        await Task.WhenAll(restoreTask, delayTask);
#endif

        var user = await restoreTask;
        if (user != null)
        {
            var shell = new MainShellView { DataContext = new MainShellViewModel() };
            shell.Show();
        }
        else
        {
            var login = new LoginView { DataContext = new LoginViewModel() };
            login.Show();
        }

        Close();
    }
}
