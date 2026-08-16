using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Summit.Views;

public partial class ProfileView : UserControl
{
    public ProfileView() => InitializeComponent();

    /// <summary>Inclina o cartão de perfil seguindo o cursor. WPF (.NET 8) não tem mais
    /// UIElement.Projection/PlaneProjection (removido do port pra .NET Core), então o "3D" aqui
    /// é falso: Skew+Scale 2D no cartão inteiro, mais um brilho radial que segue o mouse.
    /// Durante o move o valor é setado direto (sem animação) pra acompanhar o cursor 1:1 — uma
    /// animação de ~180ms reagindo a MouseMove nunca alcança o mouse e fica com cara de travado;
    /// a animação suave entra só na volta ao centro (MouseLeave).</summary>
    private void ProfileCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (ProfileCard.RenderSize.Width <= 0 || ProfileCard.RenderSize.Height <= 0) return;
        var pos = e.GetPosition(ProfileCard);
        var offsetX = pos.X / ProfileCard.RenderSize.Width - 0.5;  // -0.5..0.5
        var offsetY = pos.Y / ProfileCard.RenderSize.Height - 0.5;

        ClearAnim(CardSkew, SkewTransform.AngleXProperty);
        ClearAnim(CardSkew, SkewTransform.AngleYProperty);
        ClearAnim(CardScale, ScaleTransform.ScaleXProperty);
        ClearAnim(CardScale, ScaleTransform.ScaleYProperty);
        ClearAnim(Glint, UIElement.OpacityProperty);

        CardSkew.AngleX = offsetY * -3.5;
        CardSkew.AngleY = offsetX * 3;
        CardScale.ScaleX = 1.006;
        CardScale.ScaleY = 1.006;
        Glint.Opacity = 1.0;
        GlintBrush.GradientOrigin = new Point(pos.X / ProfileCard.RenderSize.Width, pos.Y / ProfileCard.RenderSize.Height);
        GlintBrush.Center = GlintBrush.GradientOrigin;
    }

    private void ProfileCard_MouseLeave(object sender, MouseEventArgs e)
    {
        Animate(CardSkew, SkewTransform.AngleXProperty, 0);
        Animate(CardSkew, SkewTransform.AngleYProperty, 0);
        Animate(CardScale, ScaleTransform.ScaleXProperty, 1);
        Animate(CardScale, ScaleTransform.ScaleYProperty, 1);
        Animate(Glint, UIElement.OpacityProperty, 0);
    }

    private static void ClearAnim(IAnimatable target, DependencyProperty prop) => target.BeginAnimation(prop, null);

    private static void Animate(IAnimatable target, DependencyProperty prop, double to)
    {
        var anim = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(260)))
        {
            EasingFunction = new QuadraticEase()
        };
        target.BeginAnimation(prop, anim);
    }
}
