using System.Windows.Markup;

namespace OrbitDrop.UI.Localization;

/// <summary>Resolves text used inside a WPF template, including trigger setters.</summary>
public sealed class LocalizedExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.T(text);
}
