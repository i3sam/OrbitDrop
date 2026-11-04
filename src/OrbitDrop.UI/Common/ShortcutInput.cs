using System.Windows.Input;
using OrbitDrop.Infrastructure;

namespace OrbitDrop.UI.Common;

/// <summary>What a shortcut picker hears: the modifier keys held now, and a mouse button as a shortcut key.</summary>
public static class ShortcutInput
{
    /// <summary>The modifier keys held now, as <see cref="KeyCombo"/> bits.</summary>
    public static uint HeldModifiers()
    {
        var m = Keyboard.Modifiers;
        return (m.HasFlag(ModifierKeys.Control) ? KeyCombo.Control : 0) | (m.HasFlag(ModifierKeys.Alt) ? KeyCombo.Alt : 0)
            | (m.HasFlag(ModifierKeys.Shift) ? KeyCombo.Shift : 0) | (m.HasFlag(ModifierKeys.Windows) ? KeyCombo.Win : 0);
    }

    /// <summary>The mouse button as a shortcut key (back, forward, middle), or 0 for the left and right buttons.</summary>
    public static uint MouseKey(MouseButton button) => button switch
    {
        MouseButton.XButton1 => KeyCombo.Mouse4, MouseButton.XButton2 => KeyCombo.Mouse5, MouseButton.Middle => KeyCombo.MouseMiddle, _ => 0,
    };
}
