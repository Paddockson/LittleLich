using System;
using System.Linq;
using Godot;

namespace LittleLich.UI.Shell;

/// <summary>
/// Window resolution and fullscreen, saved in user://settings.cfg (separate from game saves) and applied at startup.
/// The UI is laid out at 1280x760 and scales to whatever window size is chosen (stretch canvas_items, aspect expand).
/// </summary>
public static class DisplaySettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "display";

    public static readonly Vector2I[] Resolutions =
    {
        new(1280, 720), new(1280, 760), new(1600, 900), new(1920, 1080), new(2560, 1440),
    };

    public static readonly Vector2I Default = new(1920, 1080);

    public static Vector2I Resolution { get; private set; } = Default;
    public static bool Fullscreen { get; private set; }

    /// <summary>Loads the saved choice and applies it. Skipped when the command line sets --resolution (screenshots).</summary>
    public static void LoadAndApply()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) == Error.Ok)
        {
            Resolution = new Vector2I((int)cfg.GetValue(Section, "width", Default.X), (int)cfg.GetValue(Section, "height", Default.Y));
            Fullscreen = (bool)cfg.GetValue(Section, "fullscreen", false);
        }
        if (OS.GetCmdlineArgs().Any(a => a.StartsWith("--resolution")) || DisplayServer.GetName() == "headless") return;
        Apply();
    }

    public static void SetResolution(Vector2I size)
    {
        Resolution = size;
        Fullscreen = false;
        Apply();
        Save();
    }

    public static void SetFullscreen(bool on)
    {
        Fullscreen = on;
        Apply();
        Save();
    }

    /// <summary>True if the resolution fits on the current screen (bigger ones are shrunk to fit).</summary>
    public static bool FitsScreen(Vector2I size)
    {
        var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen()).Size;
        return size.X <= usable.X && size.Y <= usable.Y;
    }

    private static void Apply()
    {
        if (Fullscreen)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            return;
        }
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        var screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        // Keep the window on screen: shrink (keeping the aspect) if the screen is smaller than the choice.
        var scale = Math.Min(1.0, Math.Min((double)usable.Size.X / Resolution.X, (double)usable.Size.Y / Resolution.Y));
        var size = new Vector2I((int)(Resolution.X * scale), (int)(Resolution.Y * scale));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "width", Resolution.X);
        cfg.SetValue(Section, "height", Resolution.Y);
        cfg.SetValue(Section, "fullscreen", Fullscreen);
        cfg.Save(Path);
    }
}
