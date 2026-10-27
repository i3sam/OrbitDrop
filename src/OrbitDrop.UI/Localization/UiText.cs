using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace OrbitDrop.UI.Localization;

/// <summary>
/// Translates UI literals when WPF loads an element. English remains the fallback.
/// The English source text is the key so existing XAML and provider labels can be
/// localized without changing operation identifiers or saved settings.
/// </summary>
public static class UiText
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Translations = new(LoadTranslations);
    private static bool _initialized;

    public static bool IsTraditionalChinese
    {
        get
        {
            string culture = CultureInfo.CurrentUICulture.Name;
            return culture.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase)
                || culture.StartsWith("zh-Hant-", StringComparison.OrdinalIgnoreCase)
                || culture.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
                || culture.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
                || culture.Equals("zh-MO", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string T(string english)
    {
        if (!IsTraditionalChinese) return english;
        if (Translations.Value.TryGetValue(english, out string? translation)) return translation;

        return english;
    }

    public static string Format(string english, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, T(english), args);

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        if (!IsTraditionalChinese) return;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
    }

    /// <summary>Also covers windows rendered offscreen, where Loaded is not raised.</summary>
    public static void Apply(DependencyObject root)
    {
        if (!IsTraditionalChinese) return;
        var visited = new HashSet<DependencyObject>();
        void Visit(DependencyObject node)
        {
            if (!visited.Add(node)) return;
            if (node is FrameworkElement element) TranslateElement(element);
            foreach (object child in LogicalTreeHelper.GetChildren(node).Cast<object>().ToArray())
                if (child is DependencyObject next) Visit(next);
        }
        Visit(root);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element) TranslateElement(element);
    }

    private static void TranslateElement(FrameworkElement element)
    {
        if (element is TextBlock text)
        {
            TranslateLocal(text, TextBlock.TextProperty);
            foreach (Inline inline in text.Inlines.ToArray())
                if (inline is Run run) TranslateLocal(run, Run.TextProperty);
        }
        if (element is ContentControl content) TranslateLocal(content, ContentControl.ContentProperty);
        if (element is HeaderedItemsControl items) TranslateLocal(items, HeaderedItemsControl.HeaderProperty);
        if (element is HeaderedContentControl header) TranslateLocal(header, HeaderedContentControl.HeaderProperty);
        if (element is Window window) TranslateLocal(window, Window.TitleProperty);
        TranslateLocal(element, FrameworkElement.ToolTipProperty);
        TranslateLocal(element, AutomationProperties.NameProperty);
    }

    private static void TranslateLocal(DependencyObject element, DependencyProperty property)
    {
        if (BindingOperations.IsDataBound(element, property)) return;
        if (element.ReadLocalValue(property) is string english)
        {
            string translation = T(english);
            if (translation != english) element.SetCurrentValue(property, translation);
        }
    }

    private static IReadOnlyDictionary<string, string> LoadTranslations()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "OrbitDrop.UI.Localization.zh-Hant.json");
        if (stream is null) throw new InvalidOperationException("Traditional Chinese UI resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("Traditional Chinese UI resource is empty.");
    }
}
