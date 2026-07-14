using System.Windows;
using Summit.Commands;
using Summit.Views;

namespace Summit.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    public string Nickname  => App.UserService.CurrentUser?.Nickname ?? "—";
    public string SteamId   => App.UserService.CurrentUser?.SteamId  ?? "—";
    public string AppVersion => "0.1.0-MVP";

    public RelayCommand LogoutCommand { get; }

    public SettingsViewModel()
    {
        LogoutCommand = new RelayCommand(_ => Logout());
    }

    private void Logout()
    {
        App.SteamAuth.Logout();
        var login = new LoginView { DataContext = new LoginViewModel() };
        login.Show();
        foreach (Window w in Application.Current.Windows)
            if (w is MainShellView) { w.Close(); break; }
    }
}
