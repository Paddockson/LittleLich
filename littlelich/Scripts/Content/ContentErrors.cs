using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Content;

/// <summary>One problem found while loading content. Always names the file, and the id and field when known.</summary>
public sealed record ContentError(string File, string? Id, string? Field, string Message)
{
    public override string ToString()
    {
        var where = File;
        if (!string.IsNullOrEmpty(Id)) where += $" [{Id}]";
        if (!string.IsNullOrEmpty(Field)) where += $" .{Field}";
        return $"{where}: {Message}";
    }
}

/// <summary>Collects errors for one entry (file + id are fixed, field varies).</summary>
public sealed class ContentErrors
{
    private readonly List<ContentError> _sink;

    internal ContentErrors(List<ContentError> sink, string file, string? id)
    {
        _sink = sink;
        File = file;
        Id = id;
    }

    public string File { get; }
    public string? Id { get; }

    public void Add(string? field, string message) => _sink.Add(new ContentError(File, Id, field, message));
}

/// <summary>Thrown when content fails to load. Lists every error found, not just the first.</summary>
public sealed class ContentLoadException : Exception
{
    public IReadOnlyList<ContentError> Errors { get; }

    public ContentLoadException(IReadOnlyList<ContentError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    private static string BuildMessage(IReadOnlyList<ContentError> errors) =>
        $"Content failed to load ({errors.Count} error{(errors.Count == 1 ? "" : "s")}):\n" +
        string.Join("\n", errors.Select(e => "  " + e));
}
