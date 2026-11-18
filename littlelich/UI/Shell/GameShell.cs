using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Managers;
using LittleLich.Core.Ticks;
using LittleLich.UI.Components;
using LittleLich.UI.Pages;
using LittleLich.UI.Theme;

namespace LittleLich.UI.Shell;

/// <summary>
/// The one window (UI/Main.tscn). Five fixed regions in the 1280x760 reference space:
///   top bar (h48), Crypt tabs (h46), skills sidebar (w210) + content area, resource bar (h60).
/// Pages swap inside the content area through <see cref="PageRouter"/>. Reads state and calls managers only.
///
/// Command line (after "--"):
///   --ui-page=Crypt/Workshop | Skill/mining | Map | ...   open a page (Workshop sub-page: Crypt/Workshop:summoning_circle)
///   --ui-screenshot=path.png    save a screenshot after --ui-frames=N frames (default 12) and quit
///   --capture-ui[=dir]          open every page in turn and save &lt;dir&gt;/&lt;page&gt;.png (default captures/), then quit
/// </summary>
public partial class GameShell : Control
{
    /// <summary>Every page the capture run visits (file name, router key).</summary>
    public static readonly (string File, string Key)[] CapturePages =
    {
        ("01-workshop-mana-well", "Crypt/Workshop:mana_well"),
        ("02-workshop-summoning-circle", "Crypt/Workshop:summoning_circle"),
        ("03-research", "Crypt/Research"),
        ("04-unit-builder", "Crypt/UnitBuilder"),
        ("05-spell-book", "Crypt/SpellBook"),
        ("06-lich", "Crypt/Lich"),
        ("07-world-map", "Map"),
        ("08-boss-prep", "BossPrep"),
        ("09-boss-fight", "Battle"),
        ("10-skill-mining", "Skill/mining"),
        ("11-skill-smithing", "Skill/smithing"),
        ("12-skill-alchemy", "Skill/alchemy"),
        ("13-skill-enchanting", "Skill/enchanting"),
        ("14-settings", "Settings"),
    };

    private PageRouter _router = null!;
    private TopBar _top = null!;
    private CryptTabs _tabs = null!;
    private SkillsSidebar _sidebar = null!;
    private ResourceBar _resources = null!;
    private Label _bootError = null!;
    private bool _resourcesDirty, _skillsDirty;

    public static GameShell? Instance { get; private set; }

    public PageRouter Router => _router;

    public override void _Ready()
    {
        Instance = this;
        DisplaySettings.LoadAndApply();
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = Tokens.BuildTheme();

        var bg = new ColorRect { Color = Tokens.BgApp, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = Ui.Col(0);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        _top = new TopBar(Navigate);
        root.AddChild(_top);
        _tabs = new CryptTabs(Navigate);
        root.AddChild(_tabs);

        var middle = Ui.Row(0);
        middle.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(middle);
        _sidebar = new SkillsSidebar(Navigate);
        middle.AddChild(_sidebar);

        var content = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("margin_left", 20);
        content.AddThemeConstantOverride("margin_right", 20);
        content.AddThemeConstantOverride("margin_top", 16);
        content.AddThemeConstantOverride("margin_bottom", 16);
        content.ClipContents = true;
        middle.AddChild(content);
        var host = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        content.AddChild(host);

        _resources = new ResourceBar();
        root.AddChild(_resources);

        // Overlays: boot error banner (top), toasts (bottom-right, above the resource bar).
        _bootError = Ui.Text("", Tokens.FontBody, Tokens.Red, wrap: true);
        _bootError.Visible = false;
        _bootError.SetAnchorsPreset(LayoutPreset.TopWide);
        _bootError.OffsetTop = Tokens.TopBarHeight + Tokens.TabsHeight + 8;
        _bootError.OffsetLeft = Tokens.SidebarWidth + 20;
        AddChild(_bootError);
        var toasts = new Toast();
        toasts.SetAnchorsPreset(LayoutPreset.BottomRight);
        toasts.GrowHorizontal = GrowDirection.Begin;
        toasts.GrowVertical = GrowDirection.Begin;
        toasts.OffsetRight = -16;
        toasts.OffsetBottom = -(Tokens.ResourceBarHeight + 12);
        AddChild(toasts);

        _router = new PageRouter(host);
        PageRegistry.Register(_router, Navigate);
        _router.Changed += OnPageChanged;

        _sidebar.Build();
        _resources.Build();
        Bind();
        RefreshShell();

        if (GameManager.Instance.BootError is { } err)
        {
            _bootError.Text = "BOOT ERROR:\n" + err;
            _bootError.Visible = true;
            return;
        }
        Navigate("Crypt/Workshop");
        if (GameManager.Instance.Session?.LastLoadWarning is { } w) Toast.Show($"Main save was unreadable, loaded the backup: {w}", true);
        if (TickManager.Instance.LastRecap is { } recap && !Capturing) AwayRecapModal.ShowFor(this, recap);
        RunCommandLine();
    }

    /// <summary>Open a page. "Crypt/Workshop:soul_well" also selects a building on the Workshop page.</summary>
    public void Navigate(string key)
    {
        string? sub = null;
        var colon = key.IndexOf(':');
        if (colon > 0) { sub = key[(colon + 1)..]; key = key[..colon]; }
        if (!_router.Has(key)) { Toast.Show($"No page '{key}' yet.", true); return; }
        try
        {
            _router.Show(key);
            if (sub is not null && _router.CurrentPage is ISubPage sp) sp.Select(sub);
        }
        catch (Exception e)
        {
            GD.PushError($"[GameShell] Page {key} failed: {e}");
            Toast.Show($"The {key} page hit an error: {e.Message}", true);
        }
    }

    private void OnPageChanged(string key)
    {
        _tabs.Select(key);
        _sidebar.Select(key.StartsWith("Skill/") ? key["Skill/".Length..] : null);
    }

    private void Bind()
    {
        var gm = GameManager.Instance;
        var lm = LichManager.Instance;
        var tm = TickManager.Instance;
        var crypt = CryptManager.Instance;
        gm.GameStarted += (_, _) =>
        {
            _sidebar.Build();
            _resources.Build();
            RefreshShell();
            _router.RefreshCurrent();
        };
        lm.NecromancerPoolChanged += (_, _, _) => _top.RefreshPool();
        lm.SkillXpChanged += (_, _) => _skillsDirty = true;
        crypt.StorageRatesUpdated += () => _resourcesDirty = true;
        crypt.StockChanged += (_, _) => _resourcesDirty = true;
        crypt.StorageCapsChanged += () => _resourcesDirty = true;
        tm.TickProgress += (elapsed, length) => _top.SetTick(elapsed, length);
        tm.TickEnded += (_, _) => _router.RefreshCurrent();
        tm.TickError += msg => Toast.Show(msg, true);
        tm.OfflineRecap += recap => { if (!Capturing) AwayRecapModal.ShowFor(this, recap); };
    }

    private void RefreshShell()
    {
        _top.RefreshPool();
        _top.RefreshSiege();
        _sidebar.Refresh();
        _resources.Refresh();
    }

    public override void _Process(double delta)
    {
        // Events can fire many times inside one tick batch; redraw at most once per frame.
        if (_resourcesDirty) { _resourcesDirty = false; _resources.Refresh(); }
        if (_skillsDirty) { _skillsDirty = false; _sidebar.Refresh(); }
        _top.RefreshSiege();
    }

    // ---- Command line: screenshots and the capture run ----

    private int _frames = 12;

    /// <summary>True when launched to take screenshots (no modal pop-ups).</summary>
    private static bool Capturing => OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--ui-screenshot") || a.StartsWith("--capture-ui"));

    private void RunCommandLine()
    {
        string? shot = null, captureDir = null;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--ui-page=")) Navigate(arg["--ui-page=".Length..]);
            else if (arg.StartsWith("--ui-screenshot=")) shot = arg["--ui-screenshot=".Length..];
            else if (arg.StartsWith("--ui-frames=") && int.TryParse(arg["--ui-frames=".Length..], out var f)) _frames = Math.Max(1, f);
            else if (arg == "--capture-ui") captureDir = "captures";
            else if (arg.StartsWith("--capture-ui=")) captureDir = arg["--capture-ui=".Length..];
            else if (arg.StartsWith("--ui-debug-battle=") && OS.IsDebugBuild()) DebugBattle(arg["--ui-debug-battle=".Length..]);
        }
        if (captureDir is not null) _ = CaptureAll(captureDir);
        else if (shot is not null) _ = ScreenshotAndQuit(shot);
    }

    /// <summary>Debug: put 40 skeletons and 20 zombies in the roster and send them at an area (exercises the battle view).</summary>
    private static void DebugBattle(string areaId)
    {
        var wm = WorldManager.Instance;
        if (wm.State is not { } w || CryptManager.Instance.Units is not { } u || w.Battle is not null) return;
        wm.DebugAddUnits("skeleton", 40);
        wm.DebugAddUnits("zombie", 20);
        var f = u.CreateFormation("Debug");
        u.SetSlot(f.Id, 0, "zombie", 20);
        u.SetSlot(f.Id, 2, "skeleton", 40);
        GD.Print($"[GameShell] Debug battle at {areaId}: {w.Send(areaId, f)}");
    }

    private async System.Threading.Tasks.Task Capture(string path)
    {
        for (var i = 0; i < _frames; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[GameShell] Screenshot {path}: {err}");
    }

    private async System.Threading.Tasks.Task ScreenshotAndQuit(string path)
    {
        await Capture(path);
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task CaptureAll(string dir)
    {
        DirAccess.MakeDirRecursiveAbsolute(dir);
        foreach (var (file, key) in CapturePages)
        {
            Navigate(key);
            await Capture(System.IO.Path.Combine(dir, file + ".png"));
        }
        GetTree().Quit();
    }
}

/// <summary>A page with selectable sub-pages (the Workshop's buildings).</summary>
public interface ISubPage
{
    void Select(string id);
}
