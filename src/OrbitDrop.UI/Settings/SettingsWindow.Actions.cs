using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using OrbitDrop.Core;
using OrbitDrop.UI.Localization;

namespace OrbitDrop.UI.Settings;

/// <summary>
/// Settings → Your actions: open the dropped files in your favourite apps. "+ Add an action" shows the apps found on
/// this PC with their icons (VS Code, Photoshop, Paint…), so one click makes "Open in Photoshop" for photos; "Another
/// app…" takes any program. Each action is a simple card (icon, name, what it works on); the full editor (program,
/// arguments with {file} and friends, a command instead of an app) waits behind "Advanced". Two actions are free;
/// adding a third without the upgrade opens the upgrade ("You've used 2 of 2 free custom actions"), and actions past
/// the free ones wear a crown until it's bought.
/// </summary>
public partial class SettingsWindow
{
    private List<CustomAction> _actions = [];
    private readonly HashSet<int> _advancedOpen = [];

    /// <summary>The colour of "what's wrong" text under a card (shared with the shortcut cards).</summary>
    private static readonly System.Windows.Media.Brush ProblemBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C));

    /// <summary>The sample a card's "Runs:" line fills in.</summary>
    private const string SampleFile = @"C:\Users\You\Pictures\Holiday photo.jpg";

    private void BuildActions()
    {
        _actions = _vm.CustomActions.Select(a => a.Clone()).ToList();
        ActionsHost.Children.Clear();
        for (int i = 0; i < _actions.Count; i++) ActionsHost.Children.Add(ActionCard(i));
        int free = CustomAction.FreeActions;
        ActionsUsage.Text = _vm.Licensed
            ? UiText.Format("{0} of {1} · unlimited with your upgrade", _actions.Count, CustomAction.MaxActions)
            : UiText.Format("{0} of {1} free actions used · more with the upgrade 👑", Math.Min(_actions.Count, free), free);
        AddActionButton.IsEnabled = _actions.Count < CustomAction.MaxActions;
        // Nothing yet: show the apps straight away, so the first action is one click.
        if (_actions.Count == 0) ShowPicker();
    }

    private void SaveActions(bool rebuild)
    {
        _vm.SetCustomActions(_actions);
        if (rebuild) BuildActions();
    }

    /// <summary>Add: past the free actions without the upgrade, the upgrade; otherwise the apps on this PC.</summary>
    private void OnAddAction(object sender, RoutedEventArgs e)
    {
        if (!_vm.Licensed && _actions.Count >= CustomAction.FreeActions)
        {
            _vm.ShowActionsUpgrade();
            return;
        }
        if (ActionPicker.Visibility == Visibility.Visible && _actions.Count > 0) ActionPicker.Visibility = Visibility.Collapsed;
        else ShowPicker();
    }

    /// <summary>A tile for each app found on this PC, then "Another app…" and "A command".</summary>
    private void ShowPicker()
    {
        ActionPicker.Visibility = Visibility.Visible;
        if (ActionPickerApps.Children.Count > 0) return;
        foreach (var app in AppCatalog.Installed)
            ActionPickerApps.Children.Add(PickerTile(AppCatalog.IconOf(app.Program), "\uE7AC", app.Name, UiText.Format("Opens {0} in {1}", KindPhrase(app.Kind, app.Extensions), app.Name),
                () => AddAction(app.ToAction(), advanced: false)));
        ActionPickerApps.Children.Add(PickerTile(null, "\uE8E5", "Another app…", "Choose any program on your PC", () =>
        {
            var dialog = new OpenFileDialog { Title = UiText.T("Which app should open your files?"), Filter = "Programs (*.exe)|*.exe",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            if (dialog.ShowDialog(this) != true) return;
            string name = AppCatalog.NameOf(dialog.FileName);
            AddAction(new CustomAction { Name = $"Open in {name}", Program = dialog.FileName, Arguments = "{files}", PerFile = false }, advanced: false);
        }));
        ActionPickerApps.Children.Add(PickerTile(null, "\uE756", "A command", "For scripts and command lines (advanced)",
            () => AddAction(new CustomAction { Name = "My command", Program = "", Arguments = "echo {file}", ShowWindow = true }, advanced: true)));
    }

    private Button PickerTile(System.Windows.Media.ImageSource? icon, string glyph, string title, string tip, Action pick)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(IconFor(icon, glyph, 32));
        stack.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Orbit.Text.BodyStrong"), FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        var tile = new Button { Content = stack, Style = (Style)FindResource("Orbit.Button.Standard"), Width = 118, Height = 92, Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(6, 10, 6, 8), ToolTip = tip };
        AutomationProperties.SetName(tile, title);
        tile.Click += (_, _) => pick();
        return tile;
    }

    /// <summary>The app's own icon, or a Fluent glyph when it has none.</summary>
    private FrameworkElement IconFor(System.Windows.Media.ImageSource? icon, string glyph, double size) => icon is not null
        ? new Image { Source = icon, Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center }
        : new TextBlock { Text = glyph, Style = (Style)FindResource("Orbit.Glyph"), FontSize = size * 0.75, Width = size, Height = size, TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(0, size * 0.1, 0, 0) };

    private void AddAction(CustomAction action, bool advanced)
    {
        _actions.Add(action);
        if (advanced) _advancedOpen.Add(_actions.Count - 1);
        ActionPicker.Visibility = Visibility.Collapsed;
        SaveActions(rebuild: true);
    }

    /// <summary>"photos", "files and folders", ".psd and .ai files"…: what an action works on, in words.</summary>
    private static string KindPhrase(string kind, string extensions) => kind switch
    {
        "Everything" => UiText.T("files and folders"),
        "Chosen types" => UiText.Format("{0} files", string.Join(", ", new CustomAction { Extensions = extensions }.ChosenExtensions())),
        _ => UiText.T(kind == "PDFs" ? "PDFs" : kind.ToLowerInvariant()),
    };

    /// <summary>"Opens photos in Photoshop", or "Runs a command".</summary>
    private static string Summary(CustomAction action)
    {
        if (action.Program.Length == 0) return UiText.Format("Runs a command on {0}", KindPhrase(action.Kind, action.Extensions));
        string app = Path.IsPathRooted(action.Program) ? AppCatalog.NameOf(action.Program) : action.Program;
        return UiText.Format("Opens {0} in {1}", KindPhrase(action.Kind, action.Extensions), app);
    }

    /// <summary>Cards narrower than this put Works on and Advanced on a row of their own.</summary>
    private const double WideCard = 560;

    private FrameworkElement ActionCard(int index)
    {
        var action = _actions[index];
        bool locked = !_vm.Licensed && index >= CustomAction.FreeActions;
        bool open = _advancedOpen.Contains(index);
        var card = new Border { Style = (Style)FindResource("Orbit.Card"), Padding = new Thickness(16, 12, 16, 12) };
        var panel = new StackPanel();
        card.Child = panel;

        var summary = new TextBlock { Style = (Style)FindResource("RowHint"), Text = Summary(action), Margin = new Thickness(2, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
        var problem = new TextBlock { Style = (Style)FindResource("Orbit.Text.Caption"), Foreground = ProblemBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        var runs = new TextBlock { Style = (Style)FindResource("Orbit.Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas") };
        void Refresh()
        {
            string? why = action.Problem();
            problem.Text = why ?? "";
            problem.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
            runs.Text = why is null ? UiText.Format("Runs: {0}", Preview(action)) : "";
            runs.Visibility = why is null ? Visibility.Visible : Visibility.Collapsed;
            summary.Text = Summary(action);
        }
        void Changed() { SaveActions(rebuild: false); Refresh(); }

        // The simple card: icon · name and what it does · works on · advanced · delete
        var top = new DockPanel();
        var icon = IconFor(AppCatalog.IconOf(action.Program), action.Program.Length == 0 ? "\uE756" : "\uE7AC", 32);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(icon, Dock.Left);
        top.Children.Add(icon);
        var delete = new Button { Style = (Style)FindResource("Orbit.Button.Subtle"), ToolTip = "Delete this action", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Style = (Style)FindResource("Orbit.Glyph"), Text = "\uE74D", FontSize = 14 } };
        delete.Click += (_, _) => { _actions.RemoveAt(index); _advancedOpen.Clear(); SaveActions(rebuild: true); };
        DockPanel.SetDock(delete, Dock.Right);
        top.Children.Add(delete);
        var more = new Button { Style = (Style)FindResource("Orbit.Button.Subtle"), Content = open ? "Advanced ▴" : "Advanced ▾", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12, ToolTip = "The program, what's passed to it, and a few options" };
        more.Click += (_, _) => { if (!_advancedOpen.Remove(index)) _advancedOpen.Add(index); BuildActions(); };
        var kind = new ComboBox { Width = 140, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Which files it shows up for" };
        foreach (var k in CustomAction.Kinds) kind.Items.Add(new ComboBoxItem { Content = k, Tag = k, IsSelected = k == action.Kind });
        AutomationProperties.SetName(kind, "Works on");
        // Works on and Advanced sit beside the name, or on a row of their own when the window is narrow.
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(kind);
        controls.Children.Add(more);
        DockPanel.SetDock(controls, Dock.Right);
        // Its name as it shows on the wheel (renamed under Advanced), and what it does; both shorten rather than overlap.
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = action.Name, Style = (Style)FindResource("Orbit.Text.BodyStrong"), FontSize = 14, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = action.Name };
        words.Children.Add(title);
        summary.TextWrapping = TextWrapping.NoWrap;
        summary.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(summary);
        if (locked)
            words.Children.Add(new TextBlock { Text = UiText.Format("👑 Needs the upgrade: the first {0} actions are free", CustomAction.FreeActions), Style = (Style)FindResource("Orbit.Text.Caption"), Margin = new Thickness(2, 3, 0, 0) });
        top.Children.Add(words);
        panel.Children.Add(top);
        var below = new Border { Margin = new Thickness(46, 10, 0, 0) };
        panel.Children.Add(below);
        void Fit(double width)
        {
            bool beside = width >= WideCard;
            if (beside && controls.Parent != top)
            {
                below.Child = null;
                controls.Margin = new Thickness(16, 0, 0, 0);
                top.Children.Insert(top.Children.IndexOf(words), controls);
            }
            else if (!beside && controls.Parent != below)
            {
                top.Children.Remove(controls);
                controls.Margin = new Thickness(0);
                below.Child = controls;
            }
            below.Visibility = beside ? Visibility.Collapsed : Visibility.Visible;
        }
        Fit(WideCard);
        card.SizeChanged += (_, e) => Fit(e.NewSize.Width);

        // Chosen types: the extensions, right under the card (it's part of "works on").
        var extensions = new TextBox { Text = action.Extensions, Width = 200, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0), ToolTip = "File types, like .psd .ai", Visibility = action.Kind == "Chosen types" ? Visibility.Visible : Visibility.Collapsed };
        AutomationProperties.SetName(extensions, "File types");
        extensions.LostFocus += (_, _) => { if (extensions.Text != action.Extensions) { action.Extensions = extensions.Text; Changed(); } };
        kind.SelectionChanged += (_, _) =>
        {
            if (kind.SelectedItem is not ComboBoxItem { Tag: string k } || k == action.Kind) return;
            action.Kind = k;
            extensions.Visibility = k == "Chosen types" ? Visibility.Visible : Visibility.Collapsed;
            Changed();
        };
        panel.Children.Add(extensions);

        if (open) panel.Children.Add(AdvancedEditor(action, () => { title.Text = action.Name; title.ToolTip = action.Name; Changed(); }));
        panel.Children.Add(problem);
        if (open) panel.Children.Add(runs);
        Refresh();
        if (!open) runs.Visibility = Visibility.Collapsed;
        if (locked) card.Opacity = 0.7;
        return card;
    }

    /// <summary>Behind "Advanced": the program, the arguments with their placeholders, and how it runs.</summary>
    private StackPanel AdvancedEditor(CustomAction action, Action changed)
    {
        var editor = new StackPanel { Margin = new Thickness(46, 4, 0, 0) };

        var nameRow = Labelled("Name", out var nameSlot);
        var name = new TextBox { Text = action.Name, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Its name on the wheel" };
        AutomationProperties.SetName(name, "Action name");
        name.LostFocus += (_, _) => { if (name.Text.Trim() != action.Name) { action.Name = name.Text.Trim(); changed(); } };
        nameSlot.Children.Add(name);
        editor.Children.Add(nameRow);

        var programRow = Labelled("Program", out var programSlot);
        var program = new TextBox { Text = action.Program, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "A program (Browse), or a name like code or python. Empty: the arguments are a command." };
        AutomationProperties.SetName(program, "Program");
        program.LostFocus += (_, _) => { if (program.Text.Trim() != action.Program) { action.Program = program.Text.Trim(); changed(); } };
        var browse = new Button { Content = "Browse…", Style = (Style)FindResource("Orbit.Button.Standard"), Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = UiText.T("Choose the program"), Filter = "Programs (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            program.Text = action.Program = dialog.FileName;
            changed();
        };
        DockPanel.SetDock(browse, Dock.Right);
        programSlot.Children.Add(browse);
        programSlot.Children.Add(program);
        editor.Children.Add(programRow);

        var argsRow = Labelled("Arguments", out var argsSlot);
        var args = new TextBox { Text = action.Arguments, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas") };
        AutomationProperties.SetName(args, "Arguments");
        args.LostFocus += (_, _) => { if (args.Text != action.Arguments) { action.Arguments = args.Text; changed(); } };
        argsSlot.Children.Add(args);
        editor.Children.Add(argsRow);
        var chipsRow = Labelled("", out var chipsSlot); // lined up under the arguments box
        chipsRow.Margin = new Thickness(0, 6, 0, 0);
        var chips = new WrapPanel();
        foreach (var (token, tip) in new[] { ("{file}", "The dropped file, in quotes"), ("{files}", "All the dropped files, each in quotes"), ("{folder}", "Their folder, in quotes"), ("{name}", "The file's name, without extension"), ("{ext}", "The file's extension, like jpg") })
        {
            var chip = new Button { Content = token, Style = (Style)FindResource("Orbit.Button.Standard"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0), FontSize = 12, ToolTip = tip };
            chip.Click += (_, _) =>
            {
                int at = args.CaretIndex;
                args.Text = args.Text.Insert(at, token);
                args.CaretIndex = at + token.Length;
                action.Arguments = args.Text;
                changed();
            };
            chips.Children.Add(chip);
        }
        chipsSlot.Children.Add(chips);
        editor.Children.Add(chipsRow);

        var optionsRow = Labelled("Runs", out var optionsSlot);
        var options = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var perFile = new CheckBox { Content = "Once per file", IsChecked = action.PerFile, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
            ToolTip = "On: runs separately for each file. Off: runs once, with all of them ({files})." };
        perFile.Click += (_, _) => { action.PerFile = perFile.IsChecked == true; changed(); };
        var show = new CheckBox { Content = "Show its window", IsChecked = action.ShowWindow, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Shows the window and keeps it open when it's done, to read a script's output." };
        show.Click += (_, _) => { action.ShowWindow = show.IsChecked == true; changed(); };
        options.Children.Add(perFile);
        options.Children.Add(show);
        optionsSlot.Children.Add(options);
        editor.Children.Add(optionsRow);
        return editor;
    }

    /// <summary>A row with a fixed-width label and a slot for its controls.</summary>
    private DockPanel Labelled(string label, out DockPanel slot)
    {
        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var text = new TextBlock { Text = label, Style = (Style)FindResource("RowHint"), Width = 86, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(text, Dock.Left);
        row.Children.Add(text);
        slot = new DockPanel();
        row.Children.Add(slot);
        return row;
    }

    /// <summary>The command line, as it'll run on the sample photo (the program as typed, so it reads naturally).</summary>
    private static string Preview(CustomAction action)
    {
        string expanded = action.Expand(action.PerFile ? [SampleFile] : [SampleFile, @"C:\Users\You\Pictures\Beach.png"]);
        return action.Program.Length == 0 ? expanded : $"{(action.Program.Contains(' ') ? CustomAction.Quote(action.Program) : action.Program)} {expanded}";
    }
}
