using System.Windows;
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
        var restoreTask = App.SteamAuth.TryRestoreSessionAsync();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(2.4));

        await Task.WhenAll(restoreTask, delayTask);

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
