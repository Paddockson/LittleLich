using System;
using System.Globalization;
using Godot;
using LittleLich.Core;

namespace LittleLich.UI.Dev;

/// <summary>Number display and parsing for the dev UI. Exact values by default, short form where space is tight.</summary>
public static class DevFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>1,234,567</summary>
    public static string Exact(long n) => n.ToString("N0", Inv);

    /// <summary>Exact for anything a double holds exactly (below 1e15), otherwise scientific.</summary>
    public static string Exact(Num n)
    {
        var d = n.ToDouble();
        if (Math.Abs(d) < 1e15)
            return d == Math.Floor(d) ? d.ToString("N0", Inv) : d.ToString("#,0.###", Inv);
        return d.ToString("0.###e0", Inv);
    }

    /// <summary>2.4B</summary>
    public static string Short(long n) => ((Num)n).ToShortString();
    public static string Short(Num n) => n.ToShortString();

    /// <summary>A label showing the short form with the exact value in its tooltip.</summary>
    public static Label CountLabel(long n, int minWidth = 70)
    {
        var l = new Label { CustomMinimumSize = new Vector2(minWidth, 0), MouseFilter = Control.MouseFilterEnum.Pass };
        SetCount(l, n);
        return l;
    }

    public static void SetCount(Label l, long n)
    {
        l.Text = Short(n);
        l.TooltipText = Exact(n);
    }

    /// <summary>Parses "250", "1,000", "1.5K", "2M", "100B", "3T". Returns false for anything else.</summary>
    public static bool TryParseCount(string s, out long value)
    {
        value = 0;
        s = s.Trim().Replace(",", "").Replace("_", "");
        if (s.Length == 0) return false;
        double mul = char.ToUpperInvariant(s[^1]) switch { 'K' => 1e3, 'M' => 1e6, 'B' => 1e9, 'T' => 1e12, _ => 1 };
        if (mul != 1) s = s[..^1];
        if (!double.TryParse(s, NumberStyles.Float, Inv, out var d) || !double.IsFinite(d)) return false;
        d *= mul;
        if (d > long.MaxValue || d < long.MinValue) return false;
        value = (long)Math.Floor(d);
        return true;
    }

    public static bool TryParseNum(string s, out Num value)
    {
        value = Num.Zero;
        s = s.Trim().Replace(",", "");
        double mul = s.Length > 0 ? char.ToUpperInvariant(s[^1]) switch { 'K' => 1e3, 'M' => 1e6, 'B' => 1e9, 'T' => 1e12, _ => 1 } : 1;
        if (mul != 1) s = s[..^1];
        if (!double.TryParse(s, NumberStyles.Float, Inv, out var d) || double.IsNaN(d)) return false;
        value = d * mul;
        return true;
    }

    /// <summary>Escapes text for a BBCode RichTextLabel.</summary>
    public static string Bb(string s) => s.Replace("[", "[lb]");
}
