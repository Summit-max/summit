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
        var statusTask = ApiClient.GetAsync<AppStatus>("/api/app/status");
        var restoreTask = App.SteamAuth.TryRestoreSessionAsync();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(2.4));

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
