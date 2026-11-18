using Godot;

namespace LittleLich.UI.Theme;

/// <summary>
/// Every colour, size and font of the UI (phase-00-ui-spec.md "Design tokens"). Pages and components use these,
/// never literals. Reference space is 1280x760 (stretch canvas_items, aspect expand).
/// </summary>
public static class Tokens
{
    // ---- Colours ----
    public static readonly Color BgApp = new("#0D110F");
    public static readonly Color BgBar = new("#101512");
    public static readonly Color BgPanelDark = new("#0F1411");
    public static readonly Color BgCard = new("#121815");
    public static readonly Color BgCardAlt = new("#141A17");
    public static readonly Color BgInset = new("#0F1411");
    public static readonly Color Border = new("#26302B");
    public static readonly Color BorderCard = new("#2A352F");
    public static readonly Color BorderSkill = new("#222C27");
    public static readonly Color TextMain = new("#E8E4D6");
    public static readonly Color TextSoft = new("#C9D2CB");
    public static readonly Color TextTab = new("#A8B4AB");
    public static readonly Color TextMuted = new("#9FB0A6");
    public static readonly Color TextDim = new("#7C8A81");
    public static readonly Color Track = new("#252E29");
    public static readonly Color Green = new("#7FE0A8");
    public static readonly Color GreenDeep = new("#2FA866");
    public static readonly Color SelectedBg = new("#1A2B22");
    public static readonly Color SelectedBorder = new("#2F6B4D");
    public static readonly Color RunningPill = new("#12201A");
    public static readonly Color Yellow = new("#E3C26B");
    public static readonly Color YellowBorder = new("#5C4F25");
    public static readonly Color Red = new("#E0745F");
    public static readonly Color Orange = new("#F0A07C");
    public static readonly Color Purple = new("#A58BFF");
    public static readonly Color PurpleDeep = new("#7B6BC9");
    public static readonly Color NecroBg = new("#161230");
    public static readonly Color NecroBorder = new("#4A3F7A");
    public static readonly Color NecroText = new("#C7B8FF");
    public static readonly Color Blue = new("#7B9CFF");
    public static readonly Color Teal = new("#4FD1C5");
    public static readonly Color LockedFill = new("#1B1F1D");
    public static readonly Color LockedBorder = new("#1F2824");
    public static readonly Color DotOff = new("#3A4740");
    // Summoning Circle (image 02): slot bar fill, the slot clear button, pop-ups (unit chooser, tooltip).
    public static readonly Color SummonBar = new("#7B2FE0");
    public static readonly Color DangerBg = new("#3A1A1A");
    public static readonly Color DangerBorder = new("#8A3A3A");
    public static readonly Color BgPopup = new("#0A0D0C");
    // Research (image 03): tree background, inner dot colours (bone = units, green = upgrades).
    public static readonly Color TreeBg = new("#0E1310");
    public static readonly Color Bone = new("#D9D2B8");
    public static readonly Color UpgradeGreen = new("#6FA35A");
    // Mana Well (image 01): the well block.
    public static readonly Color WellBg = new("#0F1A2E");
    public static readonly Color WellBorder = new("#4A6FD0");
    public static readonly Color WellText = new("#9DB7FF");

    /// <summary>Opacity of locked content.</summary>
    public const float LockedAlpha = 0.55f;

    // ---- Shell geometry (reference space) ----
    public const int RefWidth = 1280;
    public const int RefHeight = 760;
    public const int TopBarHeight = 48;
    public const int TabsHeight = 46;
    public const int SidebarWidth = 210;
    public const int ResourceBarHeight = 60;
    public const int CardGap = 14;

    // ---- Radii and padding ----
    public const int RadiusCard = 12;
    public const int RadiusInset = 10;
    public const int RadiusButton = 8;
    public const int PadCard = 14;

    // ---- Type sizes ----
    public const int FontTitle = 19;
    public const int FontCardTitle = 17;
    public const int FontTab = 14;
    public const int FontBody = 13;
    public const int FontSmall = 12;
    public const int FontLabel = 11;
    public const int FontNumber = 15;

    // ---- Fonts (Cinzel for headings, Inter for the rest; system fallbacks when not installed) ----
    private static Font? _heading, _body, _bold, _label;

    public static Font Heading => _heading ??= new SystemFont { FontNames = new[] { "Cinzel", "Georgia", "Times New Roman", "serif" }, FontWeight = 600 };
    public static Font Body => _body ??= new SystemFont { FontNames = new[] { "Inter", "Segoe UI", "Arial", "sans-serif" } };
    public static Font Bold => _bold ??= new SystemFont { FontNames = new[] { "Inter", "Segoe UI", "Arial", "sans-serif" }, FontWeight = 700 };

    /// <summary>Bold with letter spacing, for uppercase section labels.</summary>
    public static Font LabelFont => _label ??= new FontVariation { BaseFont = Bold, SpacingGlyph = 1 };

    // ---- Style boxes ----
    public static StyleBoxFlat Box(Color bg, Color border, int radius, int padX = 0, int padY = 0, int borderWidth = 1)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        sb.SetBorderWidthAll(borderWidth);
        sb.SetCornerRadiusAll(radius);
        sb.ContentMarginLeft = sb.ContentMarginRight = padX;
        sb.ContentMarginTop = sb.ContentMarginBottom = padY;
        return sb;
    }

    /// <summary>A flat box with no border (bars, fills).</summary>
    public static StyleBoxFlat Flat(Color bg, int radius = 2)
    {
        var sb = new StyleBoxFlat { BgColor = bg };
        sb.SetCornerRadiusAll(radius);
        return sb;
    }

    /// <summary>A UI theme built from the tokens (default font, label colours, tooltip and scrollbar styling).</summary>
    public static Godot.Theme BuildTheme()
    {
        var t = new Godot.Theme { DefaultFont = Body, DefaultFontSize = FontBody };
        t.SetColor("font_color", "Label", TextMain);
        t.SetColor("font_color", "Button", TextSoft);
        t.SetColor("font_hover_color", "Button", TextMain);
        t.SetColor("font_pressed_color", "Button", TextMain);
        t.SetColor("font_disabled_color", "Button", TextDim);
        t.SetStylebox("panel", "TooltipPanel", Box(BgCard, BorderCard, RadiusButton, 8, 6));
        t.SetColor("font_color", "TooltipLabel", TextMain);
        t.SetStylebox("normal", "LineEdit", Box(BgInset, Border, RadiusButton, 8, 4));
        t.SetStylebox("focus", "LineEdit", Box(BgInset, SelectedBorder, RadiusButton, 8, 4));
        t.SetColor("font_color", "LineEdit", TextMain);
        t.SetStylebox("scroll", "VScrollBar", Flat(BgPanelDark, 4));
        t.SetStylebox("grabber", "VScrollBar", Flat(Track, 4));
        t.SetStylebox("grabber_highlight", "VScrollBar", Flat(Border, 4));
        t.SetStylebox("scroll", "HScrollBar", Flat(BgPanelDark, 4));
        t.SetStylebox("grabber", "HScrollBar", Flat(Track, 4));
        return t;
    }
}
