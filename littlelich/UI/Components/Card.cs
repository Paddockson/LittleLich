using Godot;
using LittleLich.UI.Theme;

namespace LittleLich.UI.Components;

/// <summary>Every panel: BgCard, BorderCard, radius 12, padding 14. Put content in <see cref="Body"/>.</summary>
public partial class Card : PanelContainer
{
    public VBoxContainer Body { get; }

    public Card(int gap = 10, int padding = Tokens.PadCard)
    {
        AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgCard, Tokens.BorderCard, Tokens.RadiusCard, padding, padding));
        Body = Ui.Col(gap);
        AddChild(Body);
    }

    /// <summary>Restyle as selected (SelectedBg / SelectedBorder) or highlighted with a custom border.</summary>
    public void SetStyle(Color bg, Color border, int borderWidth = 1) =>
        AddThemeStyleboxOverride("panel", Tokens.Box(bg, border, Tokens.RadiusCard, Tokens.PadCard, Tokens.PadCard, borderWidth));
}

/// <summary>A row inside a card: BgInset, Border, radius 10, padding 11x9.</summary>
public partial class InsetRow : PanelContainer
{
    public HBoxContainer Body { get; }

    public InsetRow(int gap = 10)
    {
        SetSelected(false);
        Body = Ui.Row(gap);
        AddChild(Body);
    }

    public void SetSelected(bool selected) => AddThemeStyleboxOverride("panel", selected
        ? Tokens.Box(Tokens.SelectedBg, Tokens.SelectedBorder, Tokens.RadiusInset, 11, 9)
        : Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusInset, 11, 9));

    public void SetStyle(Color bg, Color border) => AddThemeStyleboxOverride("panel", Tokens.Box(bg, border, Tokens.RadiusInset, 11, 9));
}

/// <summary>Status pill: radius 999, bold 11 px ("RUNNING" green on RunningPill).</summary>
public partial class Pill : PanelContainer
{
    private readonly Label _label;

    public Pill(string text = "")
    {
        _label = Ui.Text(text, Tokens.FontLabel, null, Tokens.LabelFont);
        AddChild(_label);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        SetKind(PillKind.Running);
    }

    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public void SetKind(PillKind kind)
    {
        var (bg, border, fg) = kind switch
        {
            PillKind.Running => (Tokens.RunningPill, Tokens.SelectedBorder, Tokens.Green),
            PillKind.Warning => (Tokens.BgInset, Tokens.YellowBorder, Tokens.Yellow),
            PillKind.Selected => (Tokens.SelectedBg, Tokens.SelectedBorder, Tokens.Green),
            PillKind.Danger => (Tokens.BgInset, Tokens.Red, Tokens.Red),
            _ => (Tokens.BgInset, Tokens.Border, Tokens.TextDim),
        };
        AddThemeStyleboxOverride("panel", Tokens.Box(bg, border, 999, 10, 3));
        _label.AddThemeColorOverride("font_color", fg);
    }
}

public enum PillKind { Running, Warning, Selected, Danger, Neutral }

/// <summary>A flat bar: Track background, coloured fill, 3 to 10 px tall. Value 0..1.</summary>
public partial class StatBar : Control
{
    private double _value;
    private Color _fill;

    public StatBar(Color fill, float height = 3)
    {
        _fill = fill;
        CustomMinimumSize = new Vector2(0, height);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Pass;
    }

    public double Value
    {
        get => _value;
        set
        {
            var v = double.IsFinite(value) ? System.Math.Clamp(value, 0, 1) : 0;
            if (v == _value) return;
            _value = v;
            QueueRedraw();
        }
    }

    public Color FillColor
    {
        get => _fill;
        set { _fill = value; QueueRedraw(); }
    }

    public override void _Draw()
    {
        var r = Mathf.Min(Size.Y / 2, 5);
        DrawStyleBox(Tokens.Flat(Tokens.Track, (int)r), new Rect2(Vector2.Zero, Size));
        if (_value > 0) DrawStyleBox(Tokens.Flat(_fill, (int)r), new Rect2(Vector2.Zero, new Vector2(Size.X * (float)_value, Size.Y)));
    }
}

/// <summary>A cost chip: "Iron Bar 2/3", green when owned >= needed, red when not.</summary>
public partial class ResourceChip : PanelContainer
{
    private readonly Label _label;

    public ResourceChip()
    {
        _label = Ui.Bold("", Tokens.FontSmall);
        AddChild(_label);
        AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgInset, Tokens.SelectedBorder, 999, 9, 2));
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
    }

    public void Set(string name, long owned, long needed)
    {
        _label.Text = $"{Fmt.Short(needed)} {name.ToLowerInvariant()} {Fmt.Short(owned)}";
        _label.AddThemeColorOverride("font_color", owned >= needed ? Tokens.Green : Tokens.Red);
        AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgInset, owned >= needed ? Tokens.SelectedBorder : Tokens.Red with { A = 0.5f }, 999, 9, 2));
        TooltipText = $"{name}: {Fmt.Exact(owned)} owned, {Fmt.Exact(needed)} needed";
    }
}
