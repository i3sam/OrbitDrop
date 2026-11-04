using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitDrop.UI.Settings;

/// <summary>
/// "New in OrbitDrop 0.7", shown once after the update: a mouse's side buttons can open the wheel, and what was fixed.
/// Below, what's new in the Microsoft Store version (Media wheel, groups in the Dock, one key for everything), with one
/// button to see it there; this version keeps working as before.
/// </summary>
public sealed class WhatsNewWindow : Window
{
    public WhatsNewWindow(Action setUp, Action getIt)
    {
        Title = "What's new in OrbitDrop";
        Width = 640; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; UseLayoutRounding = true;
        Icon = new BitmapImage(new Uri("pack://application:,,,/OrbitDrop.UI;component/Assets/orbitdrop.ico"));
        SetResourceReference(BackgroundProperty, "Orbit.SurfaceBase");
        SetResourceReference(ForegroundProperty, "Orbit.TextPrimary");
        FontFamily = (FontFamily)FindResource("Orbit.Font.Text");

        var picture = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/OrbitDrop.UI;component/Assets/whats-new-07.jpg")),
            Stretch = Stretch.UniformToFill, Height = 300,
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

        var body = new StackPanel { Margin = new Thickness(32, 24, 32, 28) };
        body.Children.Add(new TextBlock { Text = "NEW IN ORBITDROP 0.7", FontSize = 11.5, FontWeight = FontWeights.Bold, Style = (Style)FindResource("Orbit.Text.Caption") });
        body.Children.Add(new TextBlock { Text = "Your mouse's side buttons, as shortcuts", Style = (Style)FindResource("Orbit.Text.Title"), FontSize = 26, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("Orbit.Text.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            Text = "Open the wheel with a side button on your mouse, for the files you selected. In Settings › Shortcuts, click the shortcut, then press the button. Ctrl or Alt with the middle button works too.",
        }.Dim());

        body.Children.Add(new TextBlock { Text = "ALSO IN THIS UPDATE", FontSize = 11, FontWeight = FontWeights.Bold, Style = (Style)FindResource("Orbit.Text.Caption"), Margin = new Thickness(0, 18, 0, 0) });
        foreach (var line in new[]
        {
            "Print works again. It said \"The calling thread cannot access this object\" and printed nothing.",
            "Word to PDF tries again when Word is slow to start, and says what to do if it doesn't.",
            "AVIF and HEIC photos on Windows 10: OrbitDrop opens the free Windows extension they need, in one click.",
        })
            body.Children.Add(new TextBlock { Text = "•  " + line, Style = (Style)FindResource("Orbit.Text.Body"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }.Dim());

        var store = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 18, 0, 0) };
        store.SetResourceReference(Border.BackgroundProperty, "Orbit.SurfaceAlt");
        store.Child = new TextBlock
        {
            Style = (Style)FindResource("Orbit.Text.Body"), FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Text = "New in the Microsoft Store version: a Media wheel for your music (play, skip, volume, from one key), groups in your Dock, and one key for everything. Free to try for 3 days. Everything you use here stays free, and your settings come with you.",
        };
        body.Children.Add(store);

        var go = new Button { Content = "Set up a mouse button", Style = (Style)FindResource("Orbit.Button.Accent"), Padding = new Thickness(22, 11, 22, 11), FontSize = 14.5 };
        go.Click += (_, _) => { Close(); setUp(); };
        var get = new Button { Content = "See the Store version", Style = (Style)FindResource("Orbit.Button.Standard"), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(8, 0, 0, 0) };
        get.Click += (_, _) => { Close(); getIt(); };
        var later = new Button { Content = "Got it", Style = (Style)FindResource("Orbit.Button.Subtle"), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(8, 0, 0, 0) };
        later.Click += (_, _) => Close();
        var buttons = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(go);
        buttons.Children.Add(get);
        buttons.Children.Add(later);
        body.Children.Add(buttons);

        var page = new StackPanel();
        page.Children.Add(picture);
        page.Children.Add(body);
        Content = page;
    }
}

internal static class WhatsNewText
{
    public static TextBlock Dim(this TextBlock block)
    {
        block.SetResourceReference(TextBlock.ForegroundProperty, "Orbit.TextSecondary");
        return block;
    }
}
