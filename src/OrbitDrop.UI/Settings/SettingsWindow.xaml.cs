using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OrbitDrop.Core;
using System.Windows.Media;
using Microsoft.Win32;

namespace OrbitDrop.UI.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Nav.SelectedIndex = 0;
        BuildActions();
        ThemeManager.Current.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeManager.Current.Changed -= OnThemeChanged;
    }

    private void OnSwitchToStore(object sender, RoutedEventArgs e) => _vm.SwitchToStore();

    public static readonly string[] PageNames = ["General", "Appearance", "Shortcuts", "Wheel", "Conversions", "MyActions", "Advanced", "About"];

    public void ShowPage(string name)
    {
        int index = Array.IndexOf(PageNames, name);
        if (index >= 0) Nav.SelectedIndex = index;
    }

    /// <summary>The content root, for offscreen design-review renders.</summary>
    public FrameworkElement ContentRoot => (FrameworkElement)Content;

    private void OnThemeChanged() => _vm.Refresh();

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem { Tag: string page }) return;
        if (page == "Wheel") BuildWheelPage(); // built on first visit: the catalog asks every provider
        foreach (UIElement child in Pages.Children)
            child.Visibility = child is FrameworkElement fe && fe.Name == page ? Visibility.Visible : Visibility.Collapsed;
        PageScroller.ScrollToTop();
    }

    // ------------------------------------------------------------------ Wheel page

    private HashSet<string> _draft = [];
    private List<string> _draftOrder = [];
    private IReadOnlyList<WheelCategory> _catalog = [];
    private WheelCategory? _kind;
    private OperationMode _mode = OperationMode.Convert;

    /// <summary>Builds the pickers on first visit; edits go into a draft until Save.</summary>
    private void BuildWheelPage()
    {
        if (KindPicker.Children.Count > 0) return;
        _catalog = _vm.WheelCatalog;
        _draft = new HashSet<string>(_vm.HiddenOptions, StringComparer.Ordinal);
        _draftOrder = _vm.OptionOrder.ToList();
        foreach (var category in _catalog)
        {
            var kind = category;
            var button = new RadioButton { Style = (Style)FindResource("Orbit.Segment"), GroupName = "WheelKind", Content = kind.Title };
            button.Checked += (_, _) => { _kind = kind; BuildModePicker(); };
            KindPicker.Children.Add(button);
        }
        Editor.Reordered += keys =>
        {
            // This wheel's order first, then everything else as it was.
            _draftOrder = [.. keys, .. _draftOrder.Where(k => !keys.Contains(k))];
            ShowEditor();
        };
        Editor.Toggled += key =>
        {
            if (!_draft.Remove(key)) _draft.Add(key);
            ShowEditor();
        };
        if (KindPicker.Children.Count > 0) ((RadioButton)KindPicker.Children[0]).IsChecked = true;
    }

    private void BuildModePicker()
    {
        ModePicker.Children.Clear();
        if (_kind is null) return;
        if (!_kind.Modes.Any(m => m.Mode == _mode)) _mode = _kind.Modes[0].Mode;
        foreach (var section in _kind.Modes)
        {
            var mode = section.Mode;
            var button = new RadioButton { Style = (Style)FindResource("Orbit.Segment"), GroupName = "WheelMode", Content = mode.ToString(), IsChecked = mode == _mode };
            button.Checked += (_, _) => { _mode = mode; ShowEditor(); };
            ModePicker.Children.Add(button);
        }
        ShowEditor();
    }

    private void ShowEditor()
    {
        var entries = (_kind?.Modes.FirstOrDefault(m => m.Mode == _mode)?.Entries ?? []).ToList();
        // Same rule as the real wheel: arranged slices first, in order; the rest keep their default order.
        var rank = _draftOrder.Select((k, i) => (k, i)).GroupBy(x => x.k).ToDictionary(g => g.Key, g => g.First().i);
        entries = entries.Select((e, i) => (e, i)).OrderBy(x => rank.TryGetValue(x.e.Choice.Key, out int r) ? r : int.MaxValue).ThenBy(x => x.i).Select(x => x.e).ToList();
        Editor.Show(entries, _draft);
        bool changed = !_draft.SetEquals(_vm.HiddenOptions) || !_draftOrder.SequenceEqual(_vm.OptionOrder);
        UnsavedText.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
        SaveWheelButton.IsEnabled = UndoWheelButton.IsEnabled = changed;
    }

    private void OnSaveWheel(object sender, RoutedEventArgs e)
    {
        _vm.SetWheel(_draft, _draftOrder);
        ShowEditor();
    }

    private void OnUndoWheel(object sender, RoutedEventArgs e)
    {
        _draft = new HashSet<string>(_vm.HiddenOptions, StringComparer.Ordinal);
        _draftOrder = _vm.OptionOrder.ToList();
        ShowEditor();
    }

    private void OnShowAll(object sender, RoutedEventArgs e)
    {
        _draft.Clear();
        _draftOrder.Clear(); // back to the default wheel: everything shown, default order
        ShowEditor();
    }

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where OrbitDrop saves new files" };
        if (dialog.ShowDialog(this) == true) _vm.ChooseOutputFolder(dialog.FolderName);
    }

    private void OnGetHelp(object sender, RoutedEventArgs e) => _vm.OpenSupport("help", "about");

    private void OnCopyDetails(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetDataObject(_vm.SupportDetails(), copy: true); CopyDetailsButton.Content = "Copied"; }
        catch (System.Runtime.InteropServices.COMException) { CopyDetailsButton.Content = "Try again"; }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "Restore all OrbitDrop settings to their defaults?", "Reset settings",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK) _vm.ResetCommand.Execute(null);
    }
}
