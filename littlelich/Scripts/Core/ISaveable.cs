namespace LittleLich.Core;

/// <summary>
/// Anything that writes its own section of the save file.
/// Saves hold ids and counts only, never definitions.
/// </summary>
public interface ISaveable
{
    /// <summary>Stable key of this object's section in the save file (e.g. "lich", "crypt").</summary>
    string SaveKey { get; }

    SaveData Save();

    /// <summary>
    /// Restore state. Called with an empty <see cref="SaveData"/> when the save has no section
    /// for this key (new game, or a feature added after the save was written).
    /// </summary>
    void Load(SaveData data);
}
