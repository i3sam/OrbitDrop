using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitDrop.UI.Settings;

/// <summary>
/// "New in OrbitDrop 0.6", shown once after the update: Dock & Scenes, which come with the Microsoft Store version.
/// One button opens it in the Store; closing it is always fine, and this version keeps working as before.
/// </summary>
public sealed class WhatsNewWindow : Window
{
    public WhatsNewWindow(Action getIt)
    {
        Title = "What's new in OrbitDrop";
        Width = 620; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; UseLayoutRounding = true;
        Icon = new BitmapImage(new Uri("pack://application:,,,/OrbitDrop.UI;component/Assets/orbitdrop.ico"));
        SetResourceReference(BackgroundProperty, "Orbit.SurfaceBase");
        SetResourceReference(ForegroundProperty, "Orbit.TextPrimary");
        FontFamily = (FontFamily)FindResource("Orbit.Font.Text");

        var picture = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/OrbitDrop.UI;component/Assets/whats-new-dock.jpg")),
            Stretch = Stretch.UniformToFill, Height = 320,
        };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);

        var body = new StackPanel { Margin = new Thickness(32, 24, 32, 28) };
        body.Children.Add(new TextBlock { Text = "NEW IN ORBITDROP 0.6", FontSize = 11.5, FontWeight = FontWeights.Bold, Style = (Style)FindResource("Orbit.Text.Caption") });
        body.Children.Add(new TextBlock { Text = "Introducing Dock & Scenes", Style = (Style)FindResource("Orbit.Text.Title"), FontSize = 28, Margin = new Thickness(0, 6, 0, 0) });
        body.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("Orbit.Text.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            Text = "Press one key and your favourite apps, websites and folders open in a circle at your pointer. Or set up your whole PC for gaming, work or study in one click.",
        }.Dim());
        body.Children.Add(new TextBlock
        {
            Style = (Style)FindResource("Orbit.Text.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0),
            Text = "They're in the Microsoft Store version of OrbitDrop, free to try for 3 days. Everything you use here stays free, and your settings come with you.",
        }.Dim());

        var go = new Button { Content = "Get it from the Microsoft Store", Style = (Style)FindResource("Orbit.Button.Accent"), Padding = new Thickness(22, 11, 22, 11), FontSize = 14.5 };
        go.Click += (_, _) => { Close(); getIt(); };
        var later = new Button { Content = "Maybe later", Style = (Style)FindResource("Orbit.Button.Subtle"), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(8, 0, 0, 0) };
        later.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(go);
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
