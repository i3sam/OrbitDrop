using OrbitDrop.Infrastructure;

namespace OrbitDrop.Tests;

public sealed class KeyComboTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", "Ctrl+Alt+Space")]
    [InlineData("alt + shift + d", "Alt+Shift+D")]
    [InlineData("Ctrl+F5", "Ctrl+F5")]
    [InlineData("Alt+Ctrl+1", "Ctrl+Alt+1")]
    [InlineData("Ctrl+Win+Space", "Ctrl+Win+Space")]
    [InlineData("Mouse5", "Mouse5")]                // a side button alone: nobody types with it
    [InlineData("mouse 4", "Mouse4")]
    [InlineData("Ctrl+Mouse5", "Ctrl+Mouse5")]
    [InlineData("Alt+MiddleClick", "Alt+MiddleClick")]
    [InlineData("F13", "F13")]                      // F13-F24 alone: what mouse and keyboard apps send
    public void Usable_combinations_get_one_spelling(string text, string canonical) =>
        Assert.Equal(canonical, KeyCombo.Normalize(text));

    [Theory]
    [InlineData("")]
    [InlineData("Space")]          // no Ctrl or Alt: typing would trigger it
    [InlineData("Shift+A")]
    [InlineData("Win+E")]
    [InlineData("Ctrl+Alt")]       // no key
    [InlineData("Ctrl+A+B")]       // two keys
    [InlineData("Ctrl+Tab")]
    [InlineData("MiddleClick")]    // the middle button alone is used everywhere
    [InlineData("Shift+MiddleClick")]
    [InlineData("F5")]             // ordinary F-keys still need Ctrl or Alt
    public void Unusable_combinations_are_refused(string text) => Assert.Null(KeyCombo.Normalize(text));

    [Fact]
    public void Space_maps_to_its_virtual_key()
    {
        Assert.True(KeyCombo.TryParse("Ctrl+Alt+Space", out uint m, out uint k));
        Assert.Equal(KeyCombo.Control | KeyCombo.Alt, m);
        Assert.Equal(0x20u, k);
        Assert.Equal("Ctrl + Alt + Space", KeyCombo.Display(KeyCombo.Format(m, k)));
    }

    [Fact]
    public void A_broken_saved_shortcut_falls_back_to_the_default()
    {
        var settings = new AppSettings { WheelShortcut = "Banana" };
        settings.Normalize();
        Assert.Equal(KeyCombo.DefaultWheel, settings.WheelShortcut);
        settings.WheelShortcut = "alt+shift+d";
        settings.Normalize();
        Assert.Equal("Alt+Shift+D", settings.WheelShortcut);
    }

    [Fact]
    public void Mouse_buttons_are_told_apart_from_keys()
    {
        Assert.True(KeyCombo.TryParse("Mouse5", out uint m, out uint k));
        Assert.Equal(0u, m);
        Assert.Equal(KeyCombo.Mouse5, k);
        Assert.True(KeyCombo.IsMouse("Ctrl+Mouse4"));
        Assert.False(KeyCombo.IsMouse("Ctrl+Alt+M"));
        Assert.Equal("Ctrl + Mouse forward button", KeyCombo.Display("Ctrl+Mouse5"));
    }
}
