// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Pagjoro.App;

/// <summary>
/// The toolbar and tab icons: bare, bold, round-ended line drawings on a 24 by 24 grid, one colour per group of tools (the same
/// approach as the sidebar of Tusivo's Ledjoro). They are vector shapes, so they stay sharp at any screen scaling and need no image files.
/// Use in XAML as <c>Content="{local:Icon Open}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(UIElement))]
public sealed class IconExtension : MarkupExtension
{
    public IconExtension() { }
    public IconExtension(string key) { Key = key; }

    public string Key { get; set; } = "";
    public double Size { get; set; } = 18;

    // group colours: deeper for light themes, lighter for dark themes
    enum Group { Graphite, Red, Orange, Green, Purple, Blue, Slate, Pink }
    static readonly Color[][] Palette =
    [
        [Color.FromRgb(55, 65, 81),    Color.FromRgb(209, 213, 219)],
        [Color.FromRgb(185, 28, 28),   Color.FromRgb(248, 113, 113)],
        [Color.FromRgb(234, 108, 12),  Color.FromRgb(251, 146, 60)],
        [Color.FromRgb(22, 163, 74),   Color.FromRgb(74, 222, 128)],
        [Color.FromRgb(124, 58, 237),  Color.FromRgb(167, 139, 250)],
        [Color.FromRgb(6, 119, 163),   Color.FromRgb(77, 196, 232)],
        [Color.FromRgb(100, 116, 139), Color.FromRgb(148, 163, 184)],
        [Color.FromRgb(219, 39, 119),  Color.FromRgb(244, 114, 182)],
    ];

    // key -> (group, outline path, optional solid path)
    static readonly Dictionary<string, (Group Group, string Line, string? Solid)> Shapes = new()
    {
        ["Open"]      = (Group.Graphite, "M3 6.5 H9 L11 9 H21 V19 H3 Z", null),
        ["Save"]      = (Group.Graphite, "M4 4 H17 L20 7 V20 H4 Z M8 4 V9 H15 V4 M8 20 V14 H16 V20", null),
        ["Print"]     = (Group.Graphite, "M7 9 V4 H17 V9 M7 17 H4 V9 H20 V17 H17 M7 14 H17 V20 H7 Z", null),
        ["Undo"]      = (Group.Slate, "M9 5 L4 10 L9 15 M4 10 H15 A5 5 0 0 1 15 20 H10", null),
        ["Redo"]      = (Group.Slate, "M15 5 L20 10 L15 15 M20 10 H9 A5 5 0 0 0 9 20 H14", null),
        ["ZoomOut"]   = (Group.Blue, "M10.5 3.5 A7 7 0 1 0 10.5 17.5 A7 7 0 1 0 10.5 3.5 Z M15.5 15.5 L21 21 M7 10.5 H14", null),
        ["ZoomIn"]    = (Group.Blue, "M10.5 3.5 A7 7 0 1 0 10.5 17.5 A7 7 0 1 0 10.5 3.5 Z M15.5 15.5 L21 21 M7 10.5 H14 M10.5 7 V14", null),
        ["FitWidth"]  = (Group.Blue, "M3 12 H21 M7 8 L3 12 L7 16 M17 8 L21 12 L17 16", null),
        ["FitPage"]   = (Group.Blue, "M4 9 V4 H9 M15 4 H20 V9 M20 15 V20 H15 M9 20 H4 V15", null),
        ["Prev"]      = (Group.Blue, "M6 15 L12 9 L18 15", null),
        ["Next"]      = (Group.Blue, "M6 9 L12 15 L18 9", null),
        ["Find"]      = (Group.Blue, "M10.5 3.5 A7 7 0 1 0 10.5 17.5 A7 7 0 1 0 10.5 3.5 Z M15.5 15.5 L21 21", null),

        ["Select"]    = (Group.Graphite, "M9 4 H15 M9 20 H15 M12 4 V20", null),
        ["Highlight"] = (Group.Orange, "M14 5 L19 10 L10 19 H5 V14 Z M3 21.5 H21", null),
        ["Underline"] = (Group.Orange, "M7 4 V11 A5 5 0 0 0 17 11 V4 M5 20.5 H19", null),
        ["Strike"]    = (Group.Orange, "M4 12 H20 M16 7.5 C16 5.5 14.5 4 12 4 C9.5 4 8 5.5 8 7.5 C8 9 9 10 12 12 M8 16.5 C8 18.5 10 20 12 20 C14.5 20 16 18.5 16 16.5", null),
        ["Note"]      = (Group.Orange, "M4 5 H20 V16 H11 L6 20 V16 H4 Z", null),
        ["Draw"]      = (Group.Orange, "M4 20 L5 15 L16 4 L20 8 L9 19 Z M14 6 L18 10", null),
        ["Box"]       = (Group.Orange, "M4 6 H20 V18 H4 Z", null),
        ["Oval"]      = (Group.Orange, "M12 6 A9 6 0 1 0 12 18 A9 6 0 1 0 12 6 Z", null),

        ["EditContent"] = (Group.Purple, "M4 6 H20 V18 H4 Z M8 15 L12 8 L16 15 M9.5 12.5 H14.5", null),
        ["Text"]      = (Group.Purple, "M5 7 V4 H19 V7 M12 4 V20 M9 20 H15", null),
        ["Picture"]   = (Group.Purple, "M3 5 H21 V19 H3 Z M3 16 L9 10 L14 15 L17 12 L21 16", "M16 7.2 A1.4 1.4 0 1 0 16 10 A1.4 1.4 0 1 0 16 7.2 Z"),
        ["Form"]      = (Group.Purple, "M4 5 H20 V11 H4 Z M4 15 H11 V19 H4 Z M15 15 L17 17 L21 13", null),

        ["TextField"] = (Group.Purple, "M3 7 H21 V17 H3 Z M7 10 V14", null),
        ["CheckBox"]  = (Group.Purple, "M4 4 H20 V20 H4 Z M8 12 L11 15 L16 9", null),

        ["Redact"]    = (Group.Red, "M5 4.5 H19 M5 19.5 H15", "M3 9.5 H21 V14.5 H3 Z"),
        ["Apply"]     = (Group.Red, "M4 12.5 L9.5 18 L20 6.5", null),
        ["Sign"]      = (Group.Red, "M4 17 C7 9 8 9 9 12 C10 15 11 15 13 11 C14 9 15 10 16 12 M3 21 H21", null),

        ["Rotate"]    = (Group.Green, "M20 12 A8 8 0 1 1 16 5 M20 4 V9 H15", null),
        ["Delete"]    = (Group.Green, "M4 7 H20 M9 7 V4 H15 V7 M6 7 L7 20 H17 L18 7 M10 11 V16 M14 11 V16", null),

        // side tabs
        ["Pages"]     = (Group.Slate, "M6 3 H14 L19 8 V21 H6 Z M14 3 V8 H19", null),
        ["Outline"]   = (Group.Slate, "M9 7 H20 M9 12 H20 M9 17 H20 M4 7 H5 M4 12 H5 M4 17 H5", null),
        ["Marks"]     = (Group.Slate, "M4 5 H20 V16 H11 L6 20 V16 H4 Z", null),
    };

    static bool dark = SystemIsDark();

    static bool SystemIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>True when the window has a dark background: icons use their lighter colours. Setting it recolours every icon already on screen.</summary>
    public static bool Dark
    {
        get => dark;
        set
        {
            dark = value;
            live.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var w in live) if (w.TryGetTarget(out var icon)) icon.Apply();
        }
    }

    sealed class Live(Group group, Path line, Path? solid)
    {
        public void Apply()
        {
            var brush = new SolidColorBrush(Palette[(int)group][dark ? 1 : 0]); brush.Freeze();
            line.Stroke = brush;
            if (solid is not null) solid.Fill = brush;
        }
    }
    static readonly List<WeakReference<Live>> live = [];

    public override object ProvideValue(IServiceProvider serviceProvider) => Create(Key, Size);

    /// <summary>Builds the icon for <paramref name="key"/>; an unknown key gives an empty element.</summary>
    public static UIElement Create(string key, double size = 18)
    {
        if (!Shapes.TryGetValue(key, out var shape)) return new Border { Width = size, Height = size };
        var grid = new Grid { Width = size, Height = size, IsHitTestVisible = false };
        var host = new Viewbox { Stretch = Stretch.Uniform, Child = new Canvas { Width = 24, Height = 24 } };
        var canvas = (Canvas)host.Child;
        var line = new Path
        {
            Data = Geometry.Parse(shape.Line), StrokeThickness = 2.3,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        canvas.Children.Add(line);
        Path? solid = null;
        if (shape.Solid is not null) { solid = new Path { Data = Geometry.Parse(shape.Solid) }; canvas.Children.Add(solid); }
        var icon = new Live(shape.Group, line, solid);
        icon.Apply();
        live.Add(new WeakReference<Live>(icon));
        // keep the recolouring object alive as long as the element is
        grid.Tag = icon;
        grid.Children.Add(host);
        return grid;
    }
}
