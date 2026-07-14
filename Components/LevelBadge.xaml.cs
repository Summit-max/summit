using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Summit.Components;

public partial class LevelBadge : UserControl
{
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(int), typeof(LevelBadge),
            new PropertyMetadata(1, OnVisualChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(LevelBadge),
            new PropertyMetadata(96.0));

    public static readonly DependencyProperty ShowTierProperty =
        DependencyProperty.Register(nameof(ShowTier), typeof(bool), typeof(LevelBadge),
            new PropertyMetadata(true, OnVisualChanged));

    public int Level
    {
        get => (int)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public bool ShowTier
    {
        get => (bool)GetValue(ShowTierProperty);
        set => SetValue(ShowTierProperty, value);
    }

    public LevelBadge()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((LevelBadge)d).Apply();

    private record Tier(string Name, int Start, int End, Color Primary, Color Secondary);

    private static readonly Tier[] Tiers = new[]
    {
        new Tier("ROOKIE",    1,    9,   Color.FromRgb(0x6B, 0x72, 0x80), Color.FromRgb(0x9C, 0xA3, 0xAF)),
        new Tier("RUNNER",    10,   24,  Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0x60, 0xA5, 0xFA)),
        new Tier("OPERATOR",  25,   49,  Color.FromRgb(0x06, 0xB6, 0xD4), Color.FromRgb(0x67, 0xE8, 0xF9)),
        new Tier("TACTICIAN", 50,   74,  Color.FromRgb(0xFF, 0x6A, 0x00), Color.FromRgb(0xFF, 0x9A, 0x3C)),
        new Tier("HEADHUNT",  75,   99,  Color.FromRgb(0xD7, 0x26, 0x1E), Color.FromRgb(0xFF, 0x4D, 0x3C)),
        new Tier("ELITE",     100,  int.MaxValue, Color.FromRgb(0xFF, 0xD7, 0x00), Color.FromRgb(0xFF, 0x6A, 0x00)),
    };

    private void Apply()
    {
        var lvl = Math.Max(1, Level);
        var tier = GetTier(lvl);

        LevelText.Text = lvl >= 1000 ? "999+" : lvl.ToString();
        LevelText.FontSize = lvl >= 100 ? 20 : 26;

        Ring.Stroke = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(tier.Secondary, 0),
                new GradientStop(tier.Primary,   1)
            },
            new Point(0, 0), new Point(1, 1));

        TierStrip.Visibility = ShowTier ? Visibility.Visible : Visibility.Collapsed;
        TierStrip.Background = new SolidColorBrush(Color.FromArgb(0x22, tier.Primary.R, tier.Primary.G, tier.Primary.B));
        TierStrip.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, tier.Primary.R, tier.Primary.G, tier.Primary.B));
        TierText.Foreground = new SolidColorBrush(tier.Secondary);
        TierText.Text = tier.Name;
    }

    private static Tier GetTier(int level)
    {
        foreach (var t in Tiers)
            if (level >= t.Start && level <= t.End) return t;
        return Tiers[0];
    }
}
