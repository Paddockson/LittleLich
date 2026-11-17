using System;
using System.IO;

namespace LittleLich.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "littlelich-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

/// <summary>A clock tests can move forward.</summary>
public sealed class FakeClock
{
    public DateTime Now { get; set; } = new(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
    public DateTime Get() => Now;
    public void Advance(TimeSpan t) => Now += t;
}
