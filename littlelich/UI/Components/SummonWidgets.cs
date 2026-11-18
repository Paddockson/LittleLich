using System;
using Godot;
using LittleLich.UI.Theme;

namespace LittleLich.UI.Components;

/// <summary>A bar with its percentage written in the middle (summon slots: 16 px, radius 5).</summary>
public partial class PercentBar : Control
{
    private double _value;
    private Color _fill;
    private string? _text;

    public PercentBar(Color fill, float height = 16)
    {
        _fill = fill;
        CustomMinimumSize = new Vector2(0, height);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Pass;
    }

    /// <summary>0..1.</summary>
    public double Value
    {
        get => _value;
        set
        {
            var v = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            if (v == _value) return;
            _value = v;
            QueueRedraw();
        }
    }

    /// <summary>Centre text; null = none (empty slot).</summary>
    public string? Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
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
        var r = (int)Mathf.Min(Size.Y / 2, 5);
        DrawStyleBox(Tokens.Flat(Tokens.Track, r), new Rect2(Vector2.Zero, Size));
        if (_value > 0) DrawStyleBox(Tokens.Flat(_fill, r), new Rect2(Vector2.Zero, new Vector2(Size.X * (float)_value, Size.Y)));
        if (string.IsNullOrEmpty(_text)) return;
        var font = Tokens.Bold;
        const int size = Tokens.FontLabel;
        var w = font.GetStringSize(_text, HorizontalAlignment.Left, -1, size).X;
        var baseline = (Size.Y + font.GetAscent(size) - font.GetDescent(size)) / 2;
        DrawString(font, new Vector2((Size.X - w) / 2, baseline), _text, HorizontalAlignment.Left, -1, size, Tokens.TextMain);
    }
}

/// <summary>The ring in the middle of a click block (blocky stand-in for art): a coloured circle with a label.</summary>
public partial class RingIcon : Control
{
    private readonly float _diameter;
    private readonly string _label;
    private readonly Color _ring, _text;

    public RingIcon(string label = "CIRCLE", Color? ring = null, Color? text = null, float diameter = 86)
    {
        _diameter = diameter;
        _label = label;
        _ring = ring ?? Tokens.Purple;
        _text = text ?? Tokens.NecroText;
        CustomMinimumSize = new Vector2(diameter, diameter);
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var c = Size / 2;
        var r = _diameter / 2;
        DrawCircle(c, r, _ring);
        DrawCircle(c, r - 3, Tokens.BgPanelDark);
        var font = Tokens.Heading;
        const int size = 13;
        var text = _label;
        var w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawString(font, new Vector2(c.X - w / 2, c.Y + (font.GetAscent(size) - font.GetDescent(size)) / 2), text,
            HorizontalAlignment.Left, -1, size, _text);
    }
}

/// <summary>
/// A big clickable block (Summoning Circle, Mana Well): coloured bg, 2 px border, a ring. Press to click once; hold to repeat (at most
/// <see cref="MaxClicksPerSecond"/>). Each click that lands shows a small floating label (e.g. "+1").
/// It only calls <see cref="OnClick"/>; the simulation decides what a click does.
/// </summary>
public partial class CircleButton : PanelContainer
{
    public const double MaxClicksPerSecond = 10;
    private const double Interval = 1 / MaxClicksPerSecond;

    private readonly Control _fx;
    private bool _held;
    private double _sinceClick = Interval;
    private Vector2 _pointer;

    private readonly ClickBlockStyle _style;

    public CircleButton(string hint, float height = 176, ClickBlockStyle? style = null)
    {
        _style = style ?? ClickBlockStyle.Circle;
        CustomMinimumSize = new Vector2(0, height);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        SetHover(false);
        MouseEntered += () => SetHover(true);
        MouseExited += () => SetHover(false);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        var col = Ui.Col(6);
        col.MouseFilter = MouseFilterEnum.Ignore;
        col.AddChild(new RingIcon(_style.RingLabel, _style.Ring, _style.Text, _style.Diameter));
        var title = Ui.Bold(_style.Title, Tokens.FontSmall, _style.Text);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(title);
        Hint = Ui.Text(hint, Tokens.FontLabel, Tokens.TextMuted);
        Hint.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(Hint);
        center.AddChild(col);
        AddChild(center);

        _fx = new Control { MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        AddChild(_fx);
        TooltipText = _style.Tooltip;
    }

    /// <summary>The "+1 power per click" line.</summary>
    public Label Hint { get; }

    /// <summary>One click. Returns the floating text to show, or null if the click did nothing.</summary>
    public Func<string?>? OnClick { get; set; }

    private void SetHover(bool on) =>
        AddThemeStyleboxOverride("panel", Tokens.Box(_style.Bg, on ? _style.Ring : _style.Border, Tokens.RadiusCard, 0, 0, 2));

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            _held = mb.Pressed;
            _pointer = mb.Position;
            if (mb.Pressed && _sinceClick >= Interval) Fire();
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm) _pointer = mm.Position;
    }

    public override void _Process(double delta)
    {
        _sinceClick += delta;
        if (!_held) return;
        if (!Input.IsMouseButtonPressed(MouseButton.Left)) { _held = false; return; }
        if (_sinceClick >= Interval) Fire();
    }

    /// <summary>Scripted clicks (behaviour checks).</summary>
    public void SimulateClick() => Fire();

    private void Fire()
    {
        _sinceClick = 0;
        if (OnClick?.Invoke() is not { } text) return;
        var label = Ui.Bold(text, Tokens.FontBody, _style.Text);
        label.MouseFilter = MouseFilterEnum.Ignore;
        var start = _pointer == Vector2.Zero ? Size / 2 : _pointer;
        label.Position = start + new Vector2(-8, -18);
        _fx.AddChild(label);
        var t = label.CreateTween().SetParallel();
        t.TweenProperty(label, "position", label.Position + new Vector2(0, -34), 0.6);
        t.TweenProperty(label, "modulate:a", 0.0, 0.6);
        t.Chain().TweenCallback(Callable.From(label.QueueFree));
    }
}

/// <summary>Colours and words of a <see cref="CircleButton"/>.</summary>
public sealed record ClickBlockStyle(string Title, string RingLabel, Color Bg, Color Border, Color Ring, Color Text, string Tooltip, float Diameter = 86)
{
    /// <summary>Summoning Circle (image 02): NecroBg, PurpleDeep border, purple ring.</summary>
    public static readonly ClickBlockStyle Circle = new("CLICK TO SUMMON", "CIRCLE", Tokens.NecroBg, Tokens.PurpleDeep, Tokens.Purple, Tokens.NecroText,
        "Click to add power to the first slot that is not full. Hold to keep clicking.");

    /// <summary>Mana Well (image 01): WellBg, WellBorder, blue ring.</summary>
    public static readonly ClickBlockStyle Well = new("CLICK TO CHANNEL", "WELL", Tokens.WellBg, Tokens.WellBorder, Tokens.Blue, Tokens.WellText,
        "Click to channel mana by hand. Hold to keep clicking.", 96);
}
