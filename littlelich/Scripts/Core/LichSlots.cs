using System.Collections.Generic;

namespace LittleLich.Core;

/// <summary>The lich's seven equipment slots (the Lich page mockup's throne).</summary>
public static class LichSlots
{
    public const string Hood = "hood";
    public const string Amulet = "amulet";
    public const string Ring = "ring";
    public const string Robe = "robe";
    public const string Staff = "staff";
    public const string Grimoire = "grimoire";
    public const string Boots = "boots";

    public static readonly IReadOnlyList<string> All = new[] { Hood, Amulet, Ring, Robe, Staff, Grimoire, Boots };

    /// <summary>Modifier source for whatever is worn in a slot ("gear:staff").</summary>
    public static string Source(string slot) => "gear:" + slot;
}
