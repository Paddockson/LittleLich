using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleLich.Core;

/// <summary>Shared JSON settings. JSON keys are snake_case (e.g. "base_cap").</summary>
public static class GameJson
{
    /// <summary>For content definitions: strict, unknown fields are errors (catches typos).</summary>
    public static readonly JsonSerializerOptions Content = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>For save files: lenient on read (forward compatible), indented on write.</summary>
    public static readonly JsonSerializerOptions Save = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };
}
