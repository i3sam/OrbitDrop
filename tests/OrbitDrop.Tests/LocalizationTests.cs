using System.Globalization;
using OrbitDrop.UI.Localization;

namespace OrbitDrop.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("zh-TW")]
    [InlineData("zh-HK")]
    [InlineData("zh-MO")]
    public void Traditional_Chinese_uses_translation_and_English_fallback(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("設定", UiText.T("Settings"));
            Assert.Equal("Untranslated new feature", UiText.T("Untranslated new feature"));
            Assert.Equal("1 / 2", UiText.Format("{0} of {1}", 1, 2));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void English_keeps_original_text()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Settings", UiText.T("Settings"));
            Assert.Equal("1 of 2", UiText.Format("{0} of {1}", 1, 2));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
