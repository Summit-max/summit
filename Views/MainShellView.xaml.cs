using System.Windows;
using System.Windows.Input;

namespace Wallbang.Views;

public partial class MainShellView : Window
{
    public MainShellView()
    {
        InitializeComponent();
        Application.Current.MainWindow = this;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        DragMove();
    }
}
