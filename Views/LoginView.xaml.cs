using System.Windows;
using System.Windows.Input;

namespace Wallbang.Views;

public partial class LoginView : Window
{
    public LoginView() => InitializeComponent();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) return;
        DragMove();
    }
}
