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
///   top bar:  tick bar, pause/speed, necromancers Free | Assigned | Max
///   status:   one line with the latest message from a panel (red for errors, white otherwise), cleared after a while
///   left:     resources (stock / cap, rate per tick) above the skills list
///   centre:   tabs from <see cref="DevPanelRegistry"/>, or a skill page
///
/// Reads state, calls manager methods, refreshes on manager signals. Never mutates state itself.
/// Command-line (after "--"): --dev-tab=Saves selects a tab, --dev-screenshot=path.png saves a screenshot and quits
/// (after --dev-frames=N frames, default 10).
/// </summary>
public partial class DevShell : Control
{
    public const string ScenePath = "res://UI/Dev/DevShell.tscn";

    private readonly List<IDevPanel> _panels = new();
    private TabContainer _tabs = null!;
    private VBoxContainer _skills = null!;
    private GridContainer _resources = null!;
    private Label _resourceNote = null!;
    private Label _poolFree = null!, _poolAssigned = null!, _poolMax = null!;
    private ProgressBar _poolBar = null!;
    private Label _banner = null!;
    private Label _status = null!;
    private double _statusLeft;
    private ProgressBar _tickBar = null!;
    private Label _tickText = null!;
    private Label _tickCount = null!;
    private Button _pause = null!;
    private SkillPage _skillPage = null!;
    private readonly Dictionary<string, Button> _skillButtons = new();
    private Button _tabsButton = null!;
    private readonly Dictionary<string, Label> _resourceLabels = new();

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
        _status = new Label { ClipText = true, MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(0, 22) };
        root.AddChild(_status);

        var outer = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(outer);
        outer.AddChild(BuildSkills());

        // Main panel: the tabs, or a skill page when a skill is selected in the sidebar.
        var center = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(420, 0) };
        outer.AddChild(center);
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        center.AddChild(_tabs);
        _skillPage = new SkillPage { SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };
        center.AddChild(_skillPage);

        foreach (var make in DevPanelRegistry.Panels)
        {
            var p = make();
            p.Root.Name = p.Title;
            _tabs.AddChild(p.Root);
            _panels.Add(p);
        }

        BindStatus();
        BindTicksAndPool();
        foreach (var p in _panels) p.Bind();
        _skillPage.Bind();
        BindSkillsAndStock();
        BuildSkillButtons();
        RefreshAll();
        ShowBootError();
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
        return bar;
    }

    private Control BuildSkills()
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(190, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _skills = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_skills);
        BuildResourceList(_skills);
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

    /// <summary>Skill buttons and the resource list.</summary>
    private void BindSkillsAndStock()
    {
        LichManager.Instance.SkillXpChanged += (_, _) => RefreshSkillButtons();
        var crypt = CryptManager.Instance;
        crypt.StorageRatesUpdated += RefreshResources;
        crypt.StockChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) RefreshResources(); };
        crypt.StorageCapsChanged += RefreshResources;
    }

    /// <summary>
    /// The resources section at the top of the left column, like the skills list: one row per resource
    /// (name, stock / cap, net rate per tick). Only mana, gold, souls and the four elements are resources.
    /// </summary>
    private void BuildResourceList(VBoxContainer into)
    {
        _resourceNote = new Label { Text = "Resources", ThemeTypeVariation = "HeaderSmall", MouseFilter = MouseFilterEnum.Pass,
                                    TooltipText = "Stock / cap and net change per tick over the last tick batch. Exact values in each tooltip." };
        into.AddChild(_resourceNote);
        _resources = new GridContainer { Columns = 3 };
        _resources.AddThemeConstantOverride("h_separation", 6);
        into.AddChild(_resources);
        into.AddChild(new HSeparator());
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
        foreach (var r in GameManager.Instance.Content.All<ResourceDefinitionBase>())
        {
            if (!_resourceLabels.TryGetValue(r.Id, out var name))
            {
                name = new Label { Text = r.Name, MouseFilter = MouseFilterEnum.Pass };
                _resources.AddChild(name);
                _resources.AddChild(new Label { Name = "stock_" + r.Id, MouseFilter = MouseFilterEnum.Pass, HorizontalAlignment = HorizontalAlignment.Right, SizeFlagsHorizontal = SizeFlags.ExpandFill });
                _resources.AddChild(new Label { Name = "rate_" + r.Id, Modulate = new Color(1, 1, 1, 0.65f), MouseFilter = MouseFilterEnum.Pass });
                _resourceLabels[r.Id] = name;
            }
            var stockLabel = _resources.GetNode<Label>("stock_" + r.Id);
            var rateLabel = _resources.GetNode<Label>("rate_" + r.Id);
            var stock = storage.Get(r.Id);
            var cap = storage.Cap(r.Id);
            var rate = storage.RatePerTick(r.Id);
            stockLabel.Text = $"{DevFormat.Short(stock)}/{DevFormat.Short(cap)}";
            stockLabel.TooltipText = $"{DevFormat.Exact(stock)} / {DevFormat.Exact(cap)}" + (stock >= cap ? " (full: production stops)" : "");
            rateLabel.Text = $"{(rate >= 0 ? "+" : "")}{rate.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}";
            rateLabel.TooltipText = $"{rate.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} per tick (last tick batch)";
            var full = stock >= cap;
            name.Modulate = stockLabel.Modulate = full ? new Color(1f, 0.6f, 0.4f) : Colors.White;
            name.TooltipText = $"{r.Id}{(r.Placeholder ? " (placeholder numbers)" : "")}";
        }
    }

    // ---- Ticks, pool and the status line ----

    private void BindTicksAndPool()
    {
        var gm = GameManager.Instance;
        var lm = LichManager.Instance;
        gm.GameStarted += (_, _) =>
        {
            RefreshAll();
            _tickCount.Text = $"Tick {DevFormat.Exact(TickManager.Instance.TickCount)}";   // TickManager synced it from the save first
        };
        lm.NecromancerPoolChanged += (_, _, _) => RefreshPool();

        var tm = TickManager.Instance;
        tm.TickProgress += (elapsed, length) =>
        {
            _tickBar.Value = elapsed / length;
            _tickText.Text = $"{elapsed:0.0}s/{length:0.0}s";
        };
        tm.TickEnded += (tick, _) => _tickCount.Text = $"Tick {DevFormat.Exact(tick)}";
        tm.PausedChanged += paused =>
        {
            _pause.SetPressedNoSignal(paused);
            _pause.Text = paused ? "Resume" : "Pause";
        };
        tm.TickError += msg => DevLog.Error(msg);
        tm.OfflineRecap += recap => DevLog.Add(LogCategory.Tick, recap.Describe().Replace('\n', ' '));
        _tickCount.Text = $"Tick {DevFormat.Exact(tm.TickCount)}";
    }

    /// <summary>The status line shows the latest message from a panel: red for errors, white otherwise, for a few seconds.</summary>
    private void BindStatus()
    {
        DevLog.Posted += (text, error) =>
        {
            _status.Text = text;
            _status.TooltipText = text;
            _status.Modulate = error ? new Color(1f, 0.45f, 0.45f) : new Color(1, 1, 1, 0.8f);
            _statusLeft = error ? 10 : 6;
        };
    }

    public override void _Process(double delta)
    {
        if (_statusLeft <= 0) return;
        _statusLeft -= delta;
        if (_statusLeft <= 0) _status.Text = "";
    }

    private void ShowBootError()
    {
        var gm = GameManager.Instance;
        if (gm.BootError is { } err)
        {
            _banner.Text = "BOOT ERROR:\n" + err;
            _banner.Visible = true;
            return;
        }
        if (gm.Session?.LastLoadWarning is { } w) DevLog.Error($"Main save was unreadable, loaded the backup: {w}");
        else if (TickManager.Instance.LastRecap is { } recap) DevLog.Add(LogCategory.Tick, recap.Describe().Replace('\n', ' '));
    }

    // ---- Command-line automation (for screenshots) ----

    private void RunCommandLine()
    {
        string? shot = null;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--dev-tab="))
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
            else if (arg.StartsWith("--dev-frames=") && int.TryParse(arg["--dev-frames=".Length..], out var f)) _shotFrames = System.Math.Max(1, f);
        }
        if (shot is not null) _ = Screenshot(shot);
    }

    private int _shotFrames = 10;

    private async System.Threading.Tasks.Task Screenshot(string path)
    {
        for (var i = 0; i < _shotFrames; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"[DevShell] Screenshot {path}: {err}");
        GetTree().Quit();
    }
}
