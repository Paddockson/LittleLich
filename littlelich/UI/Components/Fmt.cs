using System;
using System.Globalization;
using LittleLich.Core;

namespace LittleLich.UI.Components;

/// <summary>Number formatting for the UI: abbreviated (2.4B, 97.6B, 142k, 1.2M) with exact values for tooltips.</summary>
public static class Fmt
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly (double Size, string Suffix)[] Units =
        { (1e15, "Q"), (1e12, "T"), (1e9, "B"), (1e6, "M"), (1e3, "k") };

    /// <summary>Three significant digits with k / M / B / T / Q, trailing zeros dropped (12.5k, 142k, 2.4B).</summary>
    public static string Short(double v)
    {
        if (!double.IsFinite(v)) return "∞";
        var a = Math.Abs(v);
        if (a < 1000) return a == Math.Floor(a) ? v.ToString("N0", Inv) : v.ToString("0.#", Inv);
        if (a >= 1e18) return v.ToString("0.##e0", Inv);
        foreach (var (size, suffix) in Units)
        {
            if (a < size) continue;
            var x = v / size;
            var fmt = Math.Abs(x) < 10 ? "0.##" : Math.Abs(x) < 100 ? "0.#" : "0";
            return x.ToString(fmt, Inv) + suffix;
        }
        return v.ToString("N0", Inv);
    }

    public static string Short(long v) => Short((double)v);
    public static string Short(Num v) => Short(v.ToDouble());

    /// <summary>Exact value with thousands separators (for tooltips).</summary>
    public static string Exact(long v) => v.ToString("N0", Inv);
    public static string Exact(double v) => Math.Abs(v) < 1e15 ? v.ToString(v == Math.Floor(v) ? "N0" : "#,0.###", Inv) : v.ToString("0.###e0", Inv);
    public static string Exact(Num v) => Exact(v.ToDouble());

    /// <summary>"+3,096/tick" style rate (signed, abbreviated above 10k).</summary>
    public static string Rate(double perTick) =>
        (perTick >= 0 ? "+" : "") + (Math.Abs(perTick) >= 10_000 ? Short(perTick) : perTick.ToString(Math.Abs(perTick) < 10 && perTick != Math.Floor(perTick) ? "0.##" : "N0", Inv));

    public static string Num1(double v) => v.ToString("0.#", Inv);
    public static string Num2(double v) => v.ToString("0.##", Inv);
}
