using Godot;
using LittleLich.UI.Theme;

namespace LittleLich.UI.Components;

/// <summary>Small builders for labels and containers so pages stay short and use the tokens.</summary>
public static class Ui
{
    public static Label Text(string text, int size = Tokens.FontBody, Color? color = null, Font? font = null, bool wrap = false)
    {
        var l = new Label
        {
            Text = text,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Pass,
            VerticalAlignment = VerticalAlignment.Center,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Tokens.TextMain);
        if (font is not null) l.AddThemeFontOverride("font", font);
        return l;
    }

    public static Label Bold(string text, int size = Tokens.FontBody, Color? color = null) => Text(text, size, color, Tokens.Bold);

    /// <summary>Cinzel heading (page and card titles).</summary>
    public static Label Heading(string text, int size = Tokens.FontCardTitle, Color? color = null) => Text(text, size, color, Tokens.Heading);

    /// <summary>Muted description text that wraps.</summary>
    public static Label Muted(string text, int size = Tokens.FontSmall) => Text(text, size, Tokens.TextMuted, wrap: true);

    /// <summary>Uppercase spaced label (10.5-11 px bold, TextDim): "CHANNELING SLOTS".</summary>
    public static Label Section(string text) => Text(text.ToUpperInvariant(), Tokens.FontLabel, Tokens.TextDim, Tokens.LabelFont);

    public static HBoxContainer Row(int gap = 8, params Control[] children)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", gap);
        foreach (var c in children) h.AddChild(c);
        return h;
    }

    public static VBoxContainer Col(int gap = 8, params Control[] children)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", gap);
        foreach (var c in children) v.AddChild(c);
        return v;
    }

    /// <summary>A spacer that takes the free width of a row.</summary>
    public static Control Fill() => new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    public static T Expand<T>(T c) where T : Control
    {
        c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return c;
    }

    public static T Width<T>(T c, float w) where T : Control
    {
        c.CustomMinimumSize = new Vector2(w, c.CustomMinimumSize.Y);
        return c;
    }

    /// <summary>Show a control as locked (55% opacity).</summary>
    public static void SetLocked(Control c, bool locked) => c.Modulate = new Color(1, 1, 1, locked ? Tokens.LockedAlpha : 1);

    /// <summary>A small coloured dot (status, skills, resources).</summary>
    public static Dot Dot(Color color, float size = 9) => new(color, size);
}

/// <summary>A filled circle (status dots, resource icons).</summary>
public partial class Dot : Control
{
    private Color _color;

    public Dot(Color color, float size)
    {
        _color = color;
        CustomMinimumSize = new Vector2(size, size);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public Color Color
    {
        get => _color;
        set { _color = value; QueueRedraw(); }
    }

    public override void _Draw() => DrawCircle(Size / 2, Mathf.Min(Size.X, Size.Y) / 2, _color);
}
