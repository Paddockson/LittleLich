using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Core;
using LittleLich.Managers;
using LittleLich.Skills;

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
    private SkillPage _skillPage = null!;
    private readonly Dictionary<string, Button> _skillButtons = new();
    private Button _tabsButton = null!;
    private readonly Dictionary<string, Label> _resourceLabels = new();

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
        // Main panel: the tabs, or a skill page when a skill is selected in the sidebar.
        var center = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 0) };
        inner.AddChild(center);
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        center.AddChild(_tabs);
        _skillPage = new SkillPage { SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };
        center.AddChild(_skillPage);
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
        _skillPage.Bind();
        BindSkillsAndStock();
        BuildSkillButtons();
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
        _tabsButton = new Button { Text = "Crypt tabs", ToggleMode = true, ButtonPressed = true, Alignment = HorizontalAlignment.Left };
        _tabsButton.Pressed += ShowTabs;
        _skills.AddChild(_tabsButton);
        return scroll;
    }

    /// <summary>One button per skill ("Mining  12/999"), grouped Lich / Gathering / Crafting. Built after boot.</summary>
    private void BuildSkillButtons()
    {
        var skills = LichManager.Instance.Skills;
        if (skills.Count == 0)
        {
            _skills.AddChild(new Label { Text = "No skills (boot failed?)", Modulate = new Color(1, 1, 1, 0.6f) });
            return;
        }
        string? group = null;
        foreach (var s in skills)
        {
            var g = s switch { GatheringSkill => "Gathering", CraftingSkill => "Crafting", _ => "Lich" };
            if (g != group)
            {
                group = g;
                _skills.AddChild(new Label { Text = g, ThemeTypeVariation = "HeaderSmall" });
            }
            var b = new Button { ToggleMode = true, Alignment = HorizontalAlignment.Left };
            var skill = s;
            b.Pressed += () => ShowSkill(skill);
            _skills.AddChild(b);
            _skillButtons[s.Id] = b;
        }
        RefreshSkillButtons();
    }

    private void RefreshSkillButtons()
    {
        foreach (var s in LichManager.Instance.Skills)
            if (_skillButtons.TryGetValue(s.Id, out var b))
            {
                b.Text = $"{s.Name}  {s.Level}/{XpCurve.MaxLevel}";
                b.TooltipText = $"{DevFormat.Exact(s.Xp)} XP, {s.LevelProgress:P0} to next level";
            }
    }

    private void ShowSkill(SkillBase skill)
    {
        _tabs.Visible = false;
        _skillPage.Visible = true;
        _skillPage.Show(skill);
        _tabsButton.SetPressedNoSignal(false);
        foreach (var (id, b) in _skillButtons) b.SetPressedNoSignal(id == skill.Id);
    }

    private void ShowTabs()
    {
        _skillPage.Visible = false;
        _tabs.Visible = true;
        _tabsButton.SetPressedNoSignal(true);
        foreach (var b in _skillButtons.Values) b.SetPressedNoSignal(false);
        foreach (var p in _panels) p.Refresh();
    }

    /// <summary>Skill buttons, the resource bar and the Level/Skill log.</summary>
    private void BindSkillsAndStock()
    {
        var lm = LichManager.Instance;
        var content = GameManager.Instance.Content;
        lm.SkillXpChanged += (_, _) => RefreshSkillButtons();
        lm.SkillLeveledUp += (id, from, to) =>
            DevLog.Add(LogCategory.Level, $"{lm.Skill(id)?.Name ?? id} level {from} -> {to}.");
        lm.ActivityCappedChanged += (skill, activity, capped) =>
        {
            var name = lm.Gathering(skill)?.Activity(activity).Name ?? activity;
            DevLog.Add(LogCategory.Skill, capped ? $"{name}: output full, production stopped." : $"{name}: producing again.");
        };
        lm.RecipeStatusChanged += (skill, recipe, status) =>
        {
            var c = lm.Crafting(skill);
            if (c is null) return;
            var why = (RecipeStatus)status switch
            {
                RecipeStatus.MissingInput => $"paused, missing {StockIds.NameOf(content, c.MissingInput(recipe) ?? "?")}",
                RecipeStatus.OutputFull => "paused, output full",
                RecipeStatus.Running => "running",
                _ => "idle",
            };
            DevLog.Add(LogCategory.Skill, $"{c.Recipe(recipe).Name}: {why}.");
        };

        var crypt = CryptManager.Instance;
        crypt.StorageRatesUpdated += RefreshResources;
        crypt.StockChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) RefreshResources(); };
        crypt.StorageCapsChanged += RefreshResources;
        crypt.BuildingLevelChanged += (id, level) =>
        {
            if (level > 1) DevLog.Add(LogCategory.Level, $"{crypt.Building(id)?.Name ?? id} is level {level}.");
        };
        crypt.BuildingStatusChanged += (id, status, detail) =>
        {
            var b = crypt.Building(id);
            if (b is null || (Crypt.Buildings.BuildingStatus)status is Crypt.Buildings.BuildingStatus.Passive) return;
            var what = (Crypt.Buildings.BuildingStatus)status switch
            {
                Crypt.Buildings.BuildingStatus.MissingInput => $"paused, missing {StockIds.NameOf(content, detail)}",
                Crypt.Buildings.BuildingStatus.OutputFull => $"paused, {StockIds.NameOf(content, detail)} full",
                Crypt.Buildings.BuildingStatus.PoolFull => detail == "target" ? "paused, target reached" : "paused, pool at max",
                Crypt.Buildings.BuildingStatus.NothingToDo => "nothing to do",
                Crypt.Buildings.BuildingStatus.Running => "running",
                _ => "idle",
            };
            DevLog.Add(LogCategory.Workshop, $"{b.Name}: {what}.");
        };
    }

    private Control BuildResourceBar()
    {
        var box = new VBoxContainer();
        _resourceNote = new Label { Text = "Resources", ThemeTypeVariation = "HeaderSmall" };
        box.AddChild(_resourceNote);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 84), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
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
        var storage = CryptManager.Instance.Storage;
        var defs = GameManager.Instance.Content.All<ResourceDefinitionBase>().ToList();
        _resourceNote.Text = $"Resources ({defs.Count})   stock / cap   net rate per tick (last tick batch)";
        foreach (var r in defs)
        {
            if (!_resourceLabels.TryGetValue(r.Id, out var label))
            {
                label = new Label { MouseFilter = MouseFilterEnum.Pass };
                _resources.AddChild(label);
                _resourceLabels[r.Id] = label;
            }
            var stock = storage.Get(r.Id);
            var cap = storage.Cap(r.Id);
            var rate = storage.RatePerTick(r.Id);
            label.Text = $"{r.Name}  {DevFormat.Exact(stock)} / {DevFormat.Exact(cap)}  " +
                         $"{(rate >= 0 ? "+" : "")}{rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}/tick" +
                         (stock >= cap ? "  FULL" : "");
            label.Modulate = stock >= cap ? new Color(1f, 0.6f, 0.4f) : Colors.White;
            label.TooltipText = $"{r.Id}{(r.Placeholder ? " (placeholder)" : "")}";
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
                else if (LichManager.Instance.Skill(name.ToLowerInvariant()) is { } skill) ShowSkill(skill);
                else if (_panels.OfType<WorkshopPanel>().FirstOrDefault() is { } ws && CryptManager.Instance.Building(name) is not null)
                {
                    _tabs.CurrentTab = _panels.IndexOf(ws);
                    ws.SelectId(name);
                }
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
