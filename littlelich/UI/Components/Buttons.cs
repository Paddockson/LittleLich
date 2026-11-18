using System;
using Godot;
using LittleLich.UI.Theme;

namespace LittleLich.UI.Components;

/// <summary>
/// Bordered button. Primary = SelectedBg / SelectedBorder with Green bold 12 px text (Level up, Equip, Craft).
/// Also used for selectable list rows and tabs via <see cref="SetSelected"/>.
/// </summary>
public partial class ActionButton : Button
{
    private bool _primary;
    private bool _selected;
    private readonly int _padX, _padY, _radius;

    public ActionButton(string text, bool primary = false, int padX = 12, int padY = 6, int radius = Tokens.RadiusButton, int fontSize = Tokens.FontSmall, Font? font = null)
    {
        Text = text;
        _primary = primary;
        _padX = padX;
        _padY = padY;
        _radius = radius;
        FocusMode = FocusModeEnum.None;
        AddThemeFontOverride("font", font ?? Tokens.Bold);
        AddThemeFontSizeOverride("font_size", fontSize);
        Restyle();
    }

    public bool Primary
    {
        get => _primary;
        set { _primary = value; Restyle(); }
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        Restyle();
    }

    private void Restyle()
    {
        var on = _primary || _selected;
        var normal = on ? Tokens.Box(Tokens.SelectedBg, Tokens.SelectedBorder, _radius, _padX, _padY)
                        : Tokens.Box(Tokens.BgCard, Tokens.Border, _radius, _padX, _padY);
        var hover = on ? Tokens.Box(Tokens.SelectedBg, Tokens.Green, _radius, _padX, _padY)
                       : Tokens.Box(Tokens.BgCardAlt, Tokens.SelectedBorder, _radius, _padX, _padY);
        var disabled = Tokens.Box(Tokens.LockedFill, Tokens.LockedBorder, _radius, _padX, _padY);
        AddThemeStyleboxOverride("normal", normal);
        AddThemeStyleboxOverride("hover", hover);
        AddThemeStyleboxOverride("pressed", hover);
        AddThemeStyleboxOverride("disabled", disabled);
        AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var fg = _primary ? Tokens.Green : _selected ? Tokens.TextMain : Tokens.TextSoft;
        AddThemeColorOverride("font_color", fg);
        AddThemeColorOverride("font_hover_color", _primary ? Tokens.Green : Tokens.TextMain);
        AddThemeColorOverride("font_pressed_color", fg);
        AddThemeColorOverride("font_disabled_color", Tokens.TextDim);
    }
}

/// <summary>
/// An <see cref="ActionButton"/> for purchases: it fires <see cref="Confirmed"/> only after being held for
/// <see cref="HoldSeconds"/> (a fill shows progress). Releasing early cancels.
/// </summary>
public partial class HoldButton : ActionButton
{
    public const double HoldSeconds = 0.6;
    private double _held = -1;

    public HoldButton(string text) : base(text, primary: true) { }

    public event Action? Confirmed;

    public override void _Ready()
    {
        ButtonDown += () => _held = 0;
        ButtonUp += () => { _held = -1; QueueRedraw(); };
        TooltipText = "Hold to confirm";
    }

    public override void _Process(double delta)
    {
        if (_held < 0) return;
        if (Disabled) { _held = -1; QueueRedraw(); return; }
        _held += delta;
        QueueRedraw();
        if (_held < HoldSeconds) return;
        _held = -1;
        QueueRedraw();
        Confirmed?.Invoke();
    }

    public override void _Draw()
    {
        if (_held <= 0) return;
        var f = (float)Math.Clamp(_held / HoldSeconds, 0, 1);
        DrawRect(new Rect2(0, Size.Y - 3, Size.X * f, 3), Tokens.Green);
    }

    /// <summary>Tests and scripted checks: hold for a given time (fires only if long enough).</summary>
    public bool SimulateHold(double seconds)
    {
        if (Disabled || seconds < HoldSeconds) return false;
        Confirmed?.Invoke();
        return true;
    }
}

/// <summary>
/// The shared "Assign by" choice: 1, 1k, 1M or All free. One value for the whole UI; every <see cref="Stepper"/>
/// moves that many necromancers per press.
/// </summary>
public partial class StepSelector : HBoxContainer
{
    public static readonly (string Label, long Step)[] Steps = { ("1", 1), ("1k", 1_000), ("1M", 1_000_000), ("All free", long.MaxValue) };

    private static long _current = 1;
    private readonly ActionButton[] _buttons;

    /// <summary>The current step (long.MaxValue = all free / all assigned).</summary>
    public static long Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            Changed?.Invoke();
        }
    }

    public static event Action? Changed;

    public StepSelector()
    {
        AddThemeConstantOverride("separation", 6);
        AddChild(Ui.Text("Assign by", Tokens.FontSmall, Tokens.TextMuted));
        _buttons = new ActionButton[Steps.Length];
        for (var i = 0; i < Steps.Length; i++)
        {
            var step = Steps[i].Step;
            var b = new ActionButton(Steps[i].Label, padX: 8, padY: 2);
            b.Pressed += () => Current = step;
            AddChild(b);
            _buttons[i] = b;
        }
        Changed += Sync;
        TreeExiting += () => Changed -= Sync;
        Sync();
    }

    private void Sync()
    {
        for (var i = 0; i < Steps.Length; i++)
        {
            _buttons[i].SetSelected(Steps[i].Step == _current);
            _buttons[i].AddThemeColorOverride("font_color", Steps[i].Step == _current ? Tokens.Green : Tokens.TextSoft);
        }
    }
}

/// <summary>
/// "● [-] 1.2M [+]": the assigned count with minus and plus, moving <see cref="StepSelector.Current"/> per press.
/// It only calls the delegates it is given (e.g. MiningManager.AddNecromancer); the count is read back on refresh.
/// </summary>
public partial class Stepper : PanelContainer
{
    private readonly Func<long> _count;
    private readonly Func<long, long> _add;
    private readonly Func<long, long> _remove;
    private readonly Label _value;
    private readonly ActionButton _minus, _plus;

    public Stepper(Func<long> count, Func<long, long> add, Func<long, long> remove)
    {
        _count = count;
        _add = add;
        _remove = remove;
        AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusButton, 8, 3));
        var row = Ui.Row(6);
        AddChild(row);
        row.AddChild(Ui.Dot(Tokens.PurpleDeep, 10));
        _minus = new ActionButton("-", padX: 6, padY: 0);
        _minus.Pressed += () => { _remove(StepSelector.Current); Refresh(); };
        row.AddChild(_minus);
        _value = Ui.Bold("0", Tokens.FontBody);
        _value.CustomMinimumSize = new Vector2(44, 0);
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        row.AddChild(_value);
        _plus = new ActionButton("+", padX: 6, padY: 0);
        _plus.Pressed += () => { _add(StepSelector.Current); Refresh(); };
        row.AddChild(_plus);
        TooltipText = "Necromancers assigned. +/- move the Assign by amount.";
    }

    /// <summary>Locked: + is disabled (the count can still go down).</summary>
    public bool Locked { get; set; }

    /// <summary>When set, the value reads "count / max" (e.g. staffed / places: "1 / 2").</summary>
    public Func<long>? Max { get; set; }

    /// <summary>Free necromancers right now (+ is disabled at 0).</summary>
    public Func<long>? Free { get; set; }

    public void Refresh()
    {
        var n = _count();
        _value.Text = Max is { } max ? $"{Fmt.Short(n)} / {Fmt.Short(max())}" : Fmt.Short(n);
        _value.TooltipText = Fmt.Exact(n);
        _minus.Disabled = n <= 0;
        _plus.Disabled = Locked || (Free?.Invoke() ?? 1) <= 0;
    }

    /// <summary>Scripted presses (behaviour checks).</summary>
    public void PressPlus() { if (!_plus.Disabled) { _add(StepSelector.Current); Refresh(); } }
    public void PressMinus() { if (!_minus.Disabled) { _remove(StepSelector.Current); Refresh(); } }
}

/// <summary>Small pop-ups, bottom-right, 3 s each ("+12 Iron Ore (1,980 stored)"). The shell provides the host.</summary>
public partial class Toast : VBoxContainer
{
    private static Toast? _host;

    public Toast()
    {
        _host = this;
        AddThemeConstantOverride("separation", 6);
        MouseFilter = MouseFilterEnum.Ignore;
        Alignment = AlignmentMode.End;
    }

    /// <summary>Show a message (red when it is an error).</summary>
    public static void Show(string text, bool error = false)
    {
        if (_host is null || !GodotObject.IsInstanceValid(_host)) { GD.Print("[toast] " + text); return; }
        var card = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        card.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.BgCard, error ? Tokens.Red : Tokens.SelectedBorder, Tokens.RadiusInset, 12, 8));
        card.AddChild(Ui.Text(text, Tokens.FontSmall, error ? Tokens.Red : Tokens.TextMain, wrap: true));
        card.CustomMinimumSize = new Vector2(260, 0);
        _host.AddChild(card);
        while (_host.GetChildCount() > 4) _host.GetChild(0).QueueFree();
        var timer = _host.GetTree().CreateTimer(3.0);
        timer.Timeout += () => { if (GodotObject.IsInstanceValid(card)) card.QueueFree(); };
    }
}
