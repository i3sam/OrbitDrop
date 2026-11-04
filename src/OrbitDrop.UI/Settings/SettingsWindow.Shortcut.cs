using System.Windows;
using System.Windows.Input;
using OrbitDrop.Infrastructure;

namespace OrbitDrop.UI.Settings;

/// <summary>
/// Settings → Shortcuts → Keyboard shortcut: the wheel's shortcut (Ctrl + Alt + Space) can be any Ctrl or Alt
/// combination. Click the button, press the keys; Esc cancels, Backspace or Reset goes back to Ctrl + Alt + Space.
/// </summary>
public partial class SettingsWindow
{
    private bool _pickingShortcut;

    private void RefreshWheelShortcut()
    {
        WheelShortcutButton.Content = _pickingShortcut ? "Press keys or a mouse button…" : _vm.WheelShortcutText;
        WheelShortcutReset.Visibility = _vm.WheelShortcutIsDefault || _pickingShortcut ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowShortcutProblem(string? text)
    {
        WheelShortcutProblem.Text = text ?? "";
        WheelShortcutProblem.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool _mouseHooked;

    private void OnWheelShortcutClick(object sender, RoutedEventArgs e)
    {
        if (!_mouseHooked)
        {
            // A mouse's side buttons (or Ctrl/Alt + middle click) can open the wheel too.
            _mouseHooked = true;
            WheelShortcutButton.PreviewMouseDown += (_, args) =>
            {
                if (!_pickingShortcut || OrbitDrop.UI.Common.ShortcutInput.MouseKey(args.ChangedButton) is not (> 0 and var vk)) return;
                args.Handled = true;
                PickWheelShortcut(OrbitDrop.UI.Common.ShortcutInput.HeldModifiers(), vk);
            };
            WheelShortcutButton.PreviewMouseUp += (_, args) => { if (OrbitDrop.UI.Common.ShortcutInput.MouseKey(args.ChangedButton) > 0) args.Handled = true; };
        }
        _pickingShortcut = !_pickingShortcut;
        ShowShortcutProblem(null);
        RefreshWheelShortcut();
        if (_pickingShortcut) WheelShortcutButton.Focus();
    }

    private void OnWheelShortcutLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_pickingShortcut) return;
        _pickingShortcut = false;
        RefreshWheelShortcut();
    }

    private void OnWheelShortcutReset(object sender, RoutedEventArgs e) => UseWheelShortcut(KeyCombo.DefaultWheel);

    private void OnWheelShortcutKey(object sender, KeyEventArgs e)
    {
        if (!_pickingShortcut) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { _pickingShortcut = false; ShowShortcutProblem(null); RefreshWheelShortcut(); return; }
        if (key == Key.Back) { UseWheelShortcut(KeyCombo.DefaultWheel); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return; // wait for the key itself
        PickWheelShortcut(OrbitDrop.UI.Common.ShortcutInput.HeldModifiers(), (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private void PickWheelShortcut(uint modifiers, uint vk)
    {
        string? combo = KeyCombo.Normalize(KeyCombo.Format(modifiers, vk));
        if (combo is null)
        {
            ShowShortcutProblem(vk == KeyCombo.MouseMiddle ? "Hold Ctrl or Alt with the middle button: it's used everywhere on its own."
                : "Hold Ctrl or Alt and press a letter, number, F-key or Space, like Ctrl + Alt + Space. Or press a side button on your mouse.");
            return;
        }
        string? problem = null;
        CheckWheelShortcut(combo, ref problem);
        if (problem is not null) { ShowShortcutProblem(problem); return; }
        UseWheelShortcut(combo);
    }

    private void UseWheelShortcut(string combo)
    {
        _pickingShortcut = false;
        _vm.SetWheelShortcut(combo);
        ShowShortcutProblem(null);
        RefreshWheelShortcut();
    }

    /// <summary>Design-review renders: the picker waiting for keys, or saying why a combination can't be used.</summary>
    public void PreviewShortcutPicker(bool picking, string? problem = null)
    {
        _pickingShortcut = picking;
        ShowShortcutProblem(problem);
        RefreshWheelShortcut();
    }

    /// <summary>Another reason <paramref name="combo"/> can't open the wheel (the Store version checks its own shortcuts).</summary>
    partial void CheckWheelShortcut(string combo, ref string? problem);
}
