using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleLich.Core;

/// <summary>
/// The game's big-number type (v0.1 decision: backed by <see cref="double"/>).
///
/// Used for XP and any value that can pass 2^63 (XP near level 999 is about 10^43).
/// Necromancer counts and resource stock stay <see cref="long"/> (100B fits easily).
///
/// All callers go through this struct so the backing type can be swapped later
/// (for example to a mantissa/exponent BigNum) without touching game code.
/// Precision: exact for whole numbers up to 2^53 (~9e15); above that, relative error ~1e-16.
/// </summary>
[JsonConverter(typeof(NumJsonConverter))]
public readonly struct Num : IEquatable<Num>, IComparable<Num>
{
    private readonly double _value;

    public Num(double value)
    {
        if (double.IsNaN(value))
            throw new ArgumentException("Num cannot be NaN.", nameof(value));
        _value = value;
    }

    public static readonly Num Zero = new(0);
    public static readonly Num One = new(1);

    /// <summary>Raw backing value. Avoid in game logic; prefer Num operators.</summary>
    public double ToDouble() => _value;

    /// <summary>Converts to long, clamping to the long range and truncating toward zero.</summary>
    public long ToLongClamped()
    {
        if (_value >= long.MaxValue) return long.MaxValue;
        if (_value <= long.MinValue) return long.MinValue;
        return (long)_value;
    }

    public bool IsZero => _value == 0;
    public bool IsFinite => double.IsFinite(_value);

    public static Num Floor(Num n) => new(Math.Floor(n._value));
    public static Num Max(Num a, Num b) => a._value >= b._value ? a : b;
    public static Num Min(Num a, Num b) => a._value <= b._value ? a : b;
    public static Num Pow(Num b, double exponent) => new(Math.Pow(b._value, exponent));

    public static implicit operator Num(double v) => new(v);
    public static implicit operator Num(long v) => new(v);
    public static implicit operator Num(int v) => new(v);

    public static Num operator +(Num a, Num b) => new(a._value + b._value);
    public static Num operator -(Num a, Num b) => new(a._value - b._value);
    public static Num operator -(Num a) => new(-a._value);
    public static Num operator *(Num a, Num b) => new(a._value * b._value);
    public static Num operator /(Num a, Num b) => new(a._value / b._value);

    public static bool operator ==(Num a, Num b) => a._value == b._value;
    public static bool operator !=(Num a, Num b) => a._value != b._value;
    public static bool operator <(Num a, Num b) => a._value < b._value;
    public static bool operator >(Num a, Num b) => a._value > b._value;
    public static bool operator <=(Num a, Num b) => a._value <= b._value;
    public static bool operator >=(Num a, Num b) => a._value >= b._value;

    public bool Equals(Num other) => _value.Equals(other._value);
    public override bool Equals(object? obj) => obj is Num n && Equals(n);
    public override int GetHashCode() => _value.GetHashCode();
    public int CompareTo(Num other) => _value.CompareTo(other._value);

    private static readonly string[] Suffixes = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };

    /// <summary>
    /// Short display form: 999, 1.23K, 45.6M, 100B ... up to Dc (1e33), then scientific (1.23e43).
    /// Values are truncated, never rounded up (999,999 shows "999K", not "1000K").
    /// </summary>
    public string ToShortString()
    {
        double v = _value;
        if (!double.IsFinite(v)) return v > 0 ? "inf" : "-inf";
        double abs = Math.Abs(v);
        if (abs < 1000) return Math.Truncate(v).ToString("0", CultureInfo.InvariantCulture);

        int group = (int)Math.Floor(Math.Log10(abs) / 3);
        if (group < Suffixes.Length)
        {
            double scaled = v / Math.Pow(1000, group);
            double truncated = Math.Truncate(scaled * 100) / 100;
            string fmt = Math.Abs(truncated) >= 100 ? "0" : Math.Abs(truncated) >= 10 ? "0.#" : "0.##";
            if (Math.Abs(truncated) >= 100) truncated = Math.Truncate(truncated);
            else if (Math.Abs(truncated) >= 10) truncated = Math.Truncate(truncated * 10) / 10;
            return truncated.ToString(fmt, CultureInfo.InvariantCulture) + Suffixes[group];
        }

        int exp = (int)Math.Floor(Math.Log10(abs));
        double mantissa = Math.Truncate(v / Math.Pow(10, exp) * 100) / 100;
        return mantissa.ToString("0.##", CultureInfo.InvariantCulture) + "e" + exp.ToString(CultureInfo.InvariantCulture);
    }

    public override string ToString() => _value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Writes Num as a plain JSON number, so saves stay readable.</summary>
public sealed class NumJsonConverter : JsonConverter<Num>
{
    public override Num Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new Num(reader.GetDouble());
        if (reader.TokenType == JsonTokenType.String &&
            double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
            !double.IsNaN(d))
            return new Num(d);
        throw new JsonException($"Expected a number for Num, got {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, Num value, JsonSerializerOptions options)
    {
        double d = value.ToDouble();
        if (double.IsFinite(d)) writer.WriteNumberValue(d);
        else writer.WriteStringValue(d > 0 ? "Infinity" : "-Infinity");
    }
}
