using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Phase 0 development UI root (UI/Dev/DevShell.tscn). Loaded instead of the real shell in debug builds.
///
///   top bar:  tick bar, pause/speed, necromancers Free | Assigned | Max, console toggle
///   left:     skills list
///   centre:   tabs from <see cref="DevPanelRegistry"/>
///   right:    event log + console
///   bottom:   resources (stock / cap, rate per tick)
///
/// Reads state, calls manager methods, refreshes on manager signals. Never mutates state itself.
/// Command-line (after "--"): --dev-cmd="cmd1;cmd2" runs console commands after boot,
/// --dev-tab=Saves selects a tab, --dev-screenshot=path.png saves a screenshot and quits.
/// </summary>
public partial class DevShell : Control
{
    public const string ScenePath = "res://UI/Dev/DevShell.tscn";

    private readonly List<IDevPanel> _panels = new();
    private TabContainer _tabs = null!;
    private DevConsole _console = null!;
    private VBoxContainer _skills = null!;
    private HFlowContainer _resources = null!;
    private Label _resourceNote = null!;
    private Label _poolFree = null!, _poolAssigned = null!, _poolMax = null!;
    private ProgressBar _poolBar = null!;
    private Label _banner = null!;
    private ProgressBar _tickBar = null!;
    private Label _tickText = null!;
    private Label _tickCount = null!;
    private Button _pause = null!;

    private long _lastTotal = -1, _lastMax = -1;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 6);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 4);
        margin.AddChild(root);

        root.AddChild(BuildTopBar());

        _banner = new Label { Visible = false, Modulate = new Color(1f, 0.45f, 0.45f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_banner);

        var outer = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(outer);
        outer.AddChild(BuildSkills());

        var inner = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        outer.AddChild(inner);
        _tabs = new TabContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 0) };
        inner.AddChild(_tabs);
        _console = new DevConsole();
        _console.CommandRan += RefreshAll;
        inner.AddChild(_console);

        root.AddChild(BuildResourceBar());

        foreach (var make in DevPanelRegistry.Panels)
        {
            var p = make();
            p.Root.Name = p.Title;
            _tabs.AddChild(p.Root);
            _panels.Add(p);
        }

        BindLog();
        foreach (var p in _panels) p.Bind();
        RefreshAll();
        LogBoot();
        RunCommandLine();
    }

    // ---- Layout ----

    private Control BuildTopBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 8);

        // Tick bar: driven by TickManager.TickProgress, the only per-frame update in the dev UI.
        var tm = TickManager.Instance;
        _tickBar = new ProgressBar { CustomMinimumSize = new Vector2(180, 0), ShowPercentage = false, SizeFlagsVertical = SizeFlags.ShrinkCenter, MaxValue = 1 };
        _tickBar.TooltipText = "Progress to the next tick.";
        bar.AddChild(_tickBar);
        _tickText = new Label { CustomMinimumSize = new Vector2(80, 0), TooltipText = _tickBar.TooltipText, MouseFilter = MouseFilterEnum.Pass };
        bar.AddChild(_tickText);
        _pause = new Button { Text = "Pause", ToggleMode = true, TooltipText = "Pause ticking (bar and processing)." };
        _pause.Toggled += on => tm.SetPaused(on);
        bar.AddChild(_pause);
        foreach (var x in new[] { 1.0, 5.0 })
        {
            var b = new Button { Text = $"{x:0}x", TooltipText = "Dev time scale. Tick length and offline maths stay at 2 s." };
            b.Pressed += () => tm.SetSpeed(x);
            bar.AddChild(b);
        }
        _tickCount = new Label { MouseFilter = MouseFilterEnum.Pass, TooltipText = "Ticks processed in this save." };
        bar.AddChild(_tickCount);

        bar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        bar.AddChild(new Label { Text = "Necromancers  Free" });
        _poolFree = DevFormat.CountLabel(0, 0);
        bar.AddChild(_poolFree);
        bar.AddChild(new Label { Text = "   Assigned" });
        _poolAssigned = DevFormat.CountLabel(0, 0);
        bar.AddChild(_poolAssigned);
        bar.AddChild(new Label { Text = "   Max" });
        _poolMax = DevFormat.CountLabel(0, 0);
        bar.AddChild(_poolMax);
        _poolBar = new ProgressBar { CustomMinimumSize = new Vector2(100, 8), ShowPercentage = false, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        bar.AddChild(_poolBar);

        var toggle = new Button { Text = "Console", ToggleMode = true, ButtonPressed = true, TooltipText = "Show/hide the log and console." };
        toggle.Toggled += on => _console.Visible = on;
        bar.AddChild(toggle);
        return bar;
    }

    private Control BuildSkills()
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(170, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _skills = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_skills);
        _skills.AddChild(new Label { Text = "Skills", ThemeTypeVariation = "HeaderSmall" });
        // Phase 4 fills this with one button per skill ("Mining  12/999") that opens its page in the main panel.
        _skills.AddChild(new Label { Text = "No skills yet\n(phase 4).", Modulate = new Color(1, 1, 1, 0.6f) });
        return scroll;
    }

    private Control BuildResourceBar()
    {
        var box = new VBoxContainer();
        _resourceNote = new Label { Text = "Resources", ThemeTypeVariation = "HeaderSmall" };
        box.AddChild(_resourceNote);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 70), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _resources = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _resources.AddThemeConstantOverride("h_separation", 24);
        scroll.AddChild(_resources);
        return box;
    }

    // ---- Refresh ----

    private void RefreshAll()
    {
        RefreshPool();
        RefreshResources();
        foreach (var p in _panels) p.Refresh();
    }

    private void RefreshPool()
    {
        var p = LichManager.Instance.State.Pool;
        DevFormat.SetCount(_poolFree, p.Free);
        DevFormat.SetCount(_poolAssigned, p.Assigned);
        DevFormat.SetCount(_poolMax, p.Max);
        _poolBar.MaxValue = Math.Max(1, p.Max);
        _poolBar.Value = p.Assigned;
        _poolBar.TooltipText = $"{DevFormat.Exact(p.Assigned)} of {DevFormat.Exact(p.Max)} assigned (total {DevFormat.Exact(p.Total)})";
    }

    /// <summary>Built from the content database: adding a resource to JSON adds a line here.</summary>
    private void RefreshResources()
    {
        foreach (var c in _resources.GetChildren()) c.QueueFree();
        var defs = GameManager.Instance.Content.All<ResourceDefinitionBase>().ToList();
        // Storage (phase 5) provides stock, cap and rate; until then stock and rate are unknown and cap is the base cap.
        _resourceNote.Text = $"Resources ({defs.Count})   stock / cap   rate per tick   [Storage arrives in phase 5]";
        foreach (var r in defs)
        {
            _resources.AddChild(new Label
            {
                Text = $"{r.Name}  - / {DevFormat.Exact(r.BaseCap)}  -/tick",
                TooltipText = $"{r.Id}{(r.Placeholder ? " (placeholder)" : "")}",
                MouseFilter = MouseFilterEnum.Pass,
            });
        }
    }

    // ---- Event log ----

    private void BindLog()
    {
        var gm = GameManager.Instance;
        var lm = LichManager.Instance;
        gm.GameSaved += slot => DevLog.Add(LogCategory.Save, $"Save written: slot {slot}.");
        gm.GameStarted += (slot, kind) =>
        {
            DevLog.Add(LogCategory.Save, $"{(SessionStart)kind} in slot {slot}.");
            RefreshAll();
        };
        lm.AssignmentChanged += (owner, activity, delta) =>
            DevLog.Add(LogCategory.Pool, $"{(delta > 0 ? "Assigned" : "Unassigned")} {DevFormat.Exact(Math.Abs(delta))} " +
                                         $"{owner}/{activity} (now {DevFormat.Exact(lm.GetAssigned(owner, activity))}).");
        lm.NecromancerPoolChanged += (_, _, _) =>
        {
            RefreshPool();
            var p = lm.State.Pool;
            if (p.Total != _lastTotal || p.Max != _lastMax)
            {
                if (_lastTotal >= 0)
                    DevLog.Add(LogCategory.Pool, $"Pool total {DevFormat.Exact(p.Total)}, max {DevFormat.Exact(p.Max)}.");
                _lastTotal = p.Total;
                _lastMax = p.Max;
            }
        };
        lm.StatChanged += (id, value) => DevLog.Add(LogCategory.Lich, $"Stat {id} = {DevFormat.Exact((Num)value)}.");
        lm.HealthChanged += (cur, max) => DevLog.Add(LogCategory.Lich, $"Health {DevFormat.Exact((Num)cur)} / {DevFormat.Exact((Num)max)}.");

        var pool = lm.State.Pool;
        _lastTotal = pool.Total;
        _lastMax = pool.Max;

        // Ticks. Single live ticks are not logged (one every 2 s would push everything else out of the log);
        // batches (speed-up, `ticks`, offline) are.
        var tm = TickManager.Instance;
        tm.TickProgress += (elapsed, length) =>
        {
            _tickBar.Value = elapsed / length;
            _tickText.Text = $"{elapsed:0.0}s/{length:0.0}s";
        };
        tm.TickEnded += (tick, count) =>
        {
            _tickCount.Text = $"Tick {DevFormat.Exact(tick)}";
            if (count > 1 && tm.Scheduler.ActiveRecap is null)
                DevLog.Add(LogCategory.Tick, $"Ran {DevFormat.Exact(count)} ticks as one batch (now tick {DevFormat.Exact(tick)}).");
        };
        tm.PausedChanged += paused =>
        {
            _pause.SetPressedNoSignal(paused);
            _pause.Text = paused ? "Resume" : "Pause";
            DevLog.Add(LogCategory.Tick, paused ? "Paused." : "Resumed.");
        };
        tm.SpeedChanged += speed => DevLog.Add(LogCategory.Tick, $"Speed {speed:0.##}x.");
        tm.OfflineRecap += recap => DevLog.Add(LogCategory.Tick, recap.Describe());
        tm.TickError += msg => DevLog.Error(msg);
        _tickCount.Text = $"Tick {DevFormat.Exact(tm.TickCount)}";
    }

    private void LogBoot()
    {
        var gm = GameManager.Instance;
        if (gm.BootError is { } err)
        {
            _banner.Text = "BOOT ERROR (see log): " + err.Split('\n')[0];
            _banner.Visible = true;
            foreach (var line in err.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                DevLog.Error("Content/boot: " + line.Trim());
            return;
        }
        DevLog.Add(LogCategory.Content, $"Loaded {gm.Content.Count} content definitions.");
        if (gm.Session is { } s)
        {
            DevLog.Add(LogCategory.Save, $"{s.StartKind} in slot {s.CurrentSlot}.");
            if (s.LastLoadWarning is { } w) DevLog.Error($"Main save was unreadable, loaded the backup: {w}");
            // The boot offline run happened before this scene existed.
            if (TickManager.Instance.LastRecap is { } recap)
                DevLog.Add(LogCategory.Tick, recap.Describe());
        }
        DevLog.Add(LogCategory.Command, "Type help for console commands.");
    }

    // ---- Command-line automation (for headless checks and screenshots) ----

    private void RunCommandLine()
    {
        string? shot = null;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--dev-cmd="))
                foreach (var c in arg["--dev-cmd=".Length..].Split(';')) _console.Run(c);
            else if (arg.StartsWith("--dev-tab="))
            {
                var name = arg["--dev-tab=".Length..];
                var i = _panels.FindIndex(p => p.Title.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) _tabs.CurrentTab = i;
            }
            else if (arg.StartsWith("--dev-screenshot=")) shot = arg["--dev-screenshot=".Length..];
        }
        if (shot is not null) _ = Screenshot(shot);
    }

    private async System.Threading.Tasks.Task Screenshot(string path)
    {
        for (var i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"[DevShell] Screenshot {path}: {err}");
        GetTree().Quit();
    }
}
