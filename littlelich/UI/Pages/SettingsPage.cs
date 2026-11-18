using System;
using System.Linq;
using Godot;
using LittleLich.Managers;
using LittleLich.UI.Components;
using LittleLich.UI.Shell;
using LittleLich.UI.Theme;
using static LittleLich.UI.Pages.Kit;

namespace LittleLich.UI.Pages;

/// <summary>
/// Settings (no mockup): window mode, tick speed and pause, save slots (new, save, load, delete) and, in debug
/// builds, test buttons that replace the old console.
/// </summary>
public partial class SettingsPage : PageBase
{
    public override void Bind()
    {
        base.Bind();
        GameManager.Instance.GameSaved += _ => Rebuild();
        TickManager.Instance.PausedChanged += _ => Rebuild();
        TickManager.Instance.SpeedChanged += _ => Rebuild();
    }

    protected override void Build()
    {
        var row = CardRow();
        var left = Ui.Width(Ui.Col(Tokens.CardGap), 330);
        row.AddChild(left);

        var display = new Card(8);
        display.Body.AddChild(Ui.Heading("Display"));
        var full = DisplaySettings.Fullscreen;
        var mode = new ActionButton(full ? "Windowed" : "Fullscreen", primary: full, padY: 6);
        mode.Pressed += () =>
        {
            DisplaySettings.SetFullscreen(!DisplaySettings.Fullscreen);
            CallDeferred(MethodName.Rebuild);
        };
        var size = DisplayServer.WindowGetSize();
        display.Body.AddChild(Ui.Row(8, Ui.Text(full ? "Fullscreen" : $"Window {size.X} x {size.Y}", Tokens.FontSmall, Tokens.TextMuted), Ui.Fill(), mode));
        display.Body.AddChild(Ui.Section("Resolution"));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var res in DisplaySettings.Resolutions)
        {
            var b = new ActionButton($"{res.X} x {res.Y}", padX: 8, padY: 5) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var on = !full && res == DisplaySettings.Resolution;
            b.SetSelected(on);
            b.AddThemeColorOverride("font_color", on ? Tokens.Green : Tokens.TextSoft);
            if (!DisplaySettings.FitsScreen(res)) b.TooltipText = "Bigger than this screen: the window is shrunk to fit.";
            var r = res;
            b.Pressed += () =>
            {
                DisplaySettings.SetResolution(r);
                CallDeferred(MethodName.Rebuild);
            };
            grid.AddChild(b);
        }
        display.Body.AddChild(grid);
        display.Body.AddChild(Ui.Muted("Saved for next time. The UI is laid out at 1280 x 760 and scales to the window."));
        left.AddChild(display);

        var tm = TickManager.Instance;
        var time = new Card(8);
        time.Body.AddChild(Ui.Heading("Time"));
        var pause = new ActionButton(tm.Paused ? "Resume" : "Pause", primary: tm.Paused, padY: 6);
        pause.Pressed += () => tm.SetPaused(!tm.Paused);
        var speeds = Ui.Row(6, pause, Ui.Fill());
        foreach (var x in new[] { 1.0, 5.0, 20.0 })
        {
            var b = new ActionButton($"{x:0}x", padX: 10, padY: 6);
            b.SetSelected(Math.Abs(tm.Speed - x) < 1e-9);
            b.Pressed += () => tm.SetSpeed(x);
            speeds.AddChild(b);
        }
        time.Body.AddChild(speeds);
        var ticks = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        time.Body.AddChild(ticks);
        Update(() => ticks.Text = $"Tick {Fmt.Exact(tm.TickCount)} · one tick every 2 s. Speed is a test setting; offline time always uses 2 s.");
        left.AddChild(time);

        if (OS.IsDebugBuild()) left.AddChild(DebugCard());

        // Saves
        var saves = new Card(8) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var gm = GameManager.Instance;
        var current = gm.Session?.CurrentSlot;
        var saveNow = new ActionButton("Save now", primary: true, padY: 6);
        saveNow.Pressed += () => { gm.SaveNow(); Toast.Show($"Saved to slot {current}."); };
        var newGame = new ActionButton("New game in a free slot", padY: 6);
        newGame.Pressed += () => { if (gm.Session is { } s) gm.NewGame(s.Saves.NextFreeSlot()); };
        saves.Body.AddChild(Ui.Row(8, Ui.Heading("Saves"), Ui.Fill(), newGame, saveNow));
        saves.Body.AddChild(Ui.Muted($"Playing slot {current?.ToString() ?? "none"}. The game saves every minute and on quit."));
        foreach (var info in gm.ListSlots())
        {
            var r = new InsetRow();
            r.SetSelected(info.Slot == current);
            var when = info.SavedAtUtc is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "never";
            var col = Ui.Col(2, Ui.Bold($"Slot {info.Slot}{(info.Slot == current ? " · playing" : "")}", Tokens.FontBody),
                Ui.Text($"Saved {when} · v{info.Version?.ToString() ?? "?"}{(info.MainCorrupt ? " · main file damaged" + (info.HasBackup ? ", backup kept" : "") : "")}",
                    Tokens.FontLabel, info.Readable ? Tokens.TextMuted : Tokens.Red));
            col.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            r.Body.AddChild(col);
            var slot = info.Slot;
            var load = new ActionButton("Load", padX: 10, padY: 3) { Disabled = slot == current || !info.Readable && !info.HasBackup };
            load.Pressed += () => gm.LoadSlot(slot);
            var del = new HoldButton("Delete") { Disabled = slot == current };
            del.Primary = false;
            del.AddThemeColorOverride("font_color", Tokens.Red);
            del.TooltipText = "Hold to delete this save";
            del.Confirmed += () => { if (gm.DeleteSlot(slot)) Toast.Show($"Deleted slot {slot}."); Rebuild(); };
            r.Body.AddChild(load);
            r.Body.AddChild(del);
            saves.Body.AddChild(r);
        }
        row.AddChild(saves);
    }

    private Control DebugCard()
    {
        var card = new Card(8);
        card.Body.AddChild(Ui.Row(8, Ui.Heading("Debug"), Ui.Fill(), Ui.Text("debug builds only", Tokens.FontLabel, Tokens.Yellow)));
        var necro = Ui.Row(6, Ui.Text("Necromancers", Tokens.FontSmall, Tokens.TextMuted), Ui.Fill());
        foreach (var n in new long[] { 10, 1_000, 1_000_000 })
        {
            var b = new ActionButton("+" + Fmt.Short(n), padX: 8, padY: 3);
            b.Pressed += () => Lich.DebugAddNecromancers(n);
            necro.AddChild(b);
        }
        card.Body.AddChild(necro);
        var res = Ui.Row(6, Ui.Text("Fill resources", Tokens.FontSmall, Tokens.TextMuted), Ui.Fill());
        var fill = new ActionButton("To cap", padX: 8, padY: 3);
        fill.Pressed += () =>
        {
            foreach (var r in Db.All<Content.Definitions.ResourceDefinitionBase>())
                CryptMgr.DebugGive(r.Id, Math.Max(0, CryptMgr.Storage.Cap(r.Id) - CryptMgr.Storage.Get(r.Id)));
            Toast.Show("Resources filled.");
        };
        res.AddChild(fill);
        card.Body.AddChild(res);
        var offline = Ui.Row(6, Ui.Text("Run ticks", Tokens.FontSmall, Tokens.TextMuted), Ui.Fill());
        foreach (var (label, ticks) in new[] { ("1 min", 30L), ("1 hour", 1800L) })
        {
            var b = new ActionButton(label, padX: 8, padY: 3);
            b.Pressed += () => TickManager.Instance.RunTicks(ticks);
            offline.AddChild(b);
        }
        card.Body.AddChild(offline);
        return card;
    }
}
