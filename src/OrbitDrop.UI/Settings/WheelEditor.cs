using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OrbitDrop.UI.Wheel;
using OrbitDrop.UI.Localization;
using static OrbitDrop.UI.Wheel.WheelGeometry;

namespace OrbitDrop.UI.Settings;

/// <summary>
/// An editable copy of the wheel for Settings → Wheel: the same slices, in the same places, as the real one. Click a
/// slice to hide it (it turns into a faded, dashed outline) or to bring it back.
/// </summary>
public sealed class WheelEditor : FrameworkElement
{
    private IReadOnlyList<WheelEntry> _entries = [];
    private IReadOnlySet<string> _hidden = new HashSet<string>();
    private int _hover = HitOutside;

    /// <summary>A slice was clicked: its key (an option id or a group key).</summary>
    public event Action<string>? Toggled;

    public WheelEditor()
    {
        Cursor = Cursors.Arrow;
        Focusable = false;
    }

    /// <summary>Highlights a slice as if hovered (the guide's demo; real hovering sets this itself).</summary>
    public void SetHover(int index)
    {
        if (_hover == index) return;
        _hover = index;
        InvalidateVisual();
    }

    public void Show(IReadOnlyList<WheelEntry> entries, IReadOnlySet<string> hidden)
    {
        _entries = entries;
        _hidden = hidden;
        InvalidateVisual();
    }

    private double Scale => Math.Min(ActualWidth, ActualHeight) / Disc;

    private int Hit(Point p)
    {
        double k = Scale;
        if (k <= 0) return HitOutside;
        return HitTest(p.X / k - Disc / 2, p.Y / k - Disc / 2, _entries.Count);
    }

    /// <summary>The slice whose direction the pointer is in (anywhere on or around the wheel): the drop position.</summary>
    private int SliceAtAngle(Point p)
    {
        double k = Scale, n = Math.Max(1, _entries.Count);
        double angle = Math.Atan2(p.Y / k - Disc / 2, p.X / k - Disc / 2) * 180 / Math.PI;
        double step = 360 / n, rel = ((angle + 90 + step / 2) % 360 + 360) % 360;
        return Math.Min((int)n - 1, (int)(rel / step));
    }

    // ------------------------------------------------------------------ click to hide, drag to move

    private const double DragThreshold = 6;
    private int _press = -1, _dropAt = -1;
    private Point _pressAt, _pointer;
    private bool _dragging;

    /// <summary>A slice was dragged to a new place: the keys of the whole wheel in their new order.</summary>
    public event Action<IReadOnlyList<string>>? Reordered;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        int hit = Hit(p);
        if (hit < 0 || hit >= _entries.Count) return;
        _press = hit;
        _pressAt = p;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_press >= 0)
        {
            if (!_dragging && (p - _pressAt).Length > DragThreshold) _dragging = true;
            if (_dragging)
            {
                _pointer = p;
                _dropAt = SliceAtAngle(p);
                Cursor = Cursors.SizeAll;
                InvalidateVisual();
                return;
            }
        }
        int hit = Hit(p);
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Arrow;
        if (hit == _hover) return;
        _hover = hit;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_dragging) return; // captured: keep dragging
        _hover = HitOutside;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (_press < 0) return;
        int press = _press, drop = _dropAt;
        bool dragged = _dragging;
        _press = _dropAt = -1;
        _dragging = false;
        Cursor = Cursors.Hand;
        if (dragged)
        {
            if (drop >= 0 && drop != press) Reordered?.Invoke(Moved(_entries, press, drop).Select(x => x.Choice.Key).ToList());
            InvalidateVisual();
        }
        else if (Hit(e.GetPosition(this)) == press) Toggled?.Invoke(_entries[press].Choice.Key);
    }

    /// <summary>Shows a drag in progress without a mouse (the guide's demo).</summary>
    public void PreviewDrag(int from, int to, Point pointer)
    {
        _press = from; _dropAt = to; _dragging = true; _pointer = pointer;
        InvalidateVisual();
    }

    public void EndPreview()
    {
        if (!_dragging) return;
        _press = _dropAt = -1; _dragging = false;
        InvalidateVisual();
    }

    private static List<WheelEntry> Moved(IReadOnlyList<WheelEntry> list, int from, int to)
    {
        var result = list.ToList();
        var item = result[from];
        result.RemoveAt(from);
        result.Insert(Math.Clamp(to, 0, result.Count), item);
        return result;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double k = Scale;
        if (k <= 0) return;
        // Draw in the wheel's own units (a 300-unit disc), centred, then scale to the control.
        dc.PushTransform(new ScaleTransform(k, k));
        dc.PushTransform(new TranslateTransform(Disc / 2 - Extent / 2, Disc / 2 - Extent / 2));
        var c = new Point(Extent / 2, Extent / 2);

        Color segment = ThemeManager.Token("WheelSegment"), text = ThemeManager.Token("WheelSegmentText"), accent = ThemeManager.Token("Accent");
        dc.DrawEllipse(new SolidColorBrush(ThemeManager.Token("WheelBase")), new Pen(new SolidColorBrush(Color.FromArgb(0x30, text.R, text.G, text.B)), 1), c, DiscR, DiscR);

        // While dragging, draw the order as it would be after dropping, so the slices make room as you move.
        bool dragging = _dragging && _press >= 0 && _dropAt >= 0;
        var entries = dragging ? Moved(_entries, _press, _dropAt) : _entries.ToList();
        int n = entries.Count;
        double step = Step(n);
        var display = (FontFamily)FindResource("Orbit.Font.Display");
        var icons = (FontFamily)FindResource("Orbit.Font.Icons");
        for (int i = 0; i < n; i++)
        {
            var entry = entries[i];
            bool hidden = _hidden.Contains(entry.Choice.Key), moving = dragging && i == _dropAt, hover = moving || (!dragging && i == _hover);
            double mid = MidAngle(i, n);
            var geo = RadialWheel.Sector(mid - step / 2, mid + step / 2, n == 1 ? 0 : Gap);
            Brush fill = hidden ? Brushes.Transparent : new SolidColorBrush(hover ? Blend(segment, accent, 0.18) : segment);
            var outline = hidden
                ? new Pen(new SolidColorBrush(Color.FromArgb(hover ? (byte)0xB0 : (byte)0x66, text.R, text.G, text.B)), 1.5) { DashStyle = new DashStyle([4, 3], 0) }
                : new Pen(new SolidColorBrush(hover ? accent : segment), moving ? 3 : hover ? 2 : Corner) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(fill, outline, geo);

            // Glyph and label at the same radius as the real wheel; hidden slices say so.
            double a = mid * Math.PI / 180;
            var at = new Point(c.X + LabelR * Math.Cos(a), c.Y + LabelR * Math.Sin(a));
            byte alpha = hidden ? (byte)0x70 : (byte)0xFF;
            var ink = new SolidColorBrush(Color.FromArgb(alpha, text.R, text.G, text.B));
            var label = Text(UiText.T(entry.Choice.Label), display, n > 6 ? 10.5 : 11.5, FontWeights.SemiBold, ink, 74);
            var glyph = entry.Choice.Glyph is { } g ? Text(hidden ? "" : g, icons, 17, FontWeights.Normal, ink, 40) : hidden ? Text("", icons, 15, FontWeights.Normal, ink, 40) : null;
            double total = label.Height + (glyph is null ? 0 : glyph.Height + 2);
            double y = at.Y - total / 2;
            if (glyph is not null) { dc.DrawText(glyph, new Point(at.X - 20, y)); y += glyph.Height + 2; }
            dc.DrawText(label, new Point(at.X - 37, y));
            if (entry.Members.Count > 0 && !hidden)
                dc.DrawText(Text("• • •", display, 7, FontWeights.Normal, ink, 40), new Point(at.X - 20, y + label.Height + 1));
        }

        // Hub: how many slices are on.
        dc.DrawEllipse(new SolidColorBrush(ThemeManager.Token("WheelHub")), new Pen(new SolidColorBrush(Color.FromArgb(0x22, text.R, text.G, text.B)), 1), c, HubR, HubR);
        int shown = _entries.Count(e => !_hidden.Contains(e.Choice.Key));
        var ink2 = new SolidColorBrush(text);
        var big = Text(n == 0 ? "—" : UiText.Format("{0} of {1}", shown, n), display, 17, FontWeights.SemiBold, ink2, 100);
        var small = Text(UiText.T(dragging ? "Drop to place it" : _hover >= 0 && _hover < n ? (_hidden.Contains(_entries[_hover].Choice.Key) ? "Click to show · drag to move" : "Click to hide · drag to move") : "on the wheel"),
            display, 10, FontWeights.Normal, new SolidColorBrush(Color.FromArgb(0xA0, text.R, text.G, text.B)), 100);
        dc.DrawText(big, new Point(c.X - 50, c.Y - big.Height / 2 - 6));
        dc.DrawText(small, new Point(c.X - 50, c.Y + big.Height / 2 - 4));
        dc.Pop();
        dc.Pop();

        // The slice being moved, as a label under the pointer.
        if (dragging)
        {
            var label = Text(UiText.T(_entries[_press].Choice.Label), display, 12, FontWeights.SemiBold, new SolidColorBrush(ThemeManager.Token("OnAccent")), 160);
            label.TextAlignment = TextAlignment.Left;
            var box = new Rect(_pointer.X + 12, _pointer.Y + 14, label.Width + 20, label.Height + 10);
            dc.DrawRoundedRectangle(new SolidColorBrush(accent), null, box, box.Height / 2, box.Height / 2);
            dc.DrawText(label, new Point(box.X + 10, box.Y + 5));
        }
    }

    private FormattedText Text(string s, FontFamily family, double size, FontWeight weight, Brush brush, double maxWidth)
    {
        var t = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { TextAlignment = TextAlignment.Center, MaxTextWidth = maxWidth, Trimming = TextTrimming.CharacterEllipsis, MaxLineCount = 2 };
        return t;
    }

    private static Color Blend(Color a, Color b, double k) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
}
