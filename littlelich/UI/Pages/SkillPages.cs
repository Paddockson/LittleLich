using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Managers;
using LittleLich.Skills;
using LittleLich.UI.Components;
using LittleLich.UI.Shell;
using LittleLich.UI.Theme;
using static LittleLich.UI.Pages.Kit;

namespace LittleLich.UI.Pages;

/// <summary>Skill pages: one gathering scene (image 10) and one crafting scene (images 11-13), built from content.</summary>
public static class SkillPages
{
    public static IPage For(string skillId) => Lich.Skill(skillId) switch
    {
        GatheringSkill g => new GatheringPage(g.Id),
        CraftingSkill c => new CraftingPage(c.Id),
        _ => new SkillInfoPage(skillId),
    };
}

/// <summary>Shared parts of the skill pages: header card, necromancer pool card, automation and speed cards.</summary>
public abstract partial class SkillPageBase : PageBase
{
    protected readonly string SkillId;

    protected SkillPageBase(string skillId) => SkillId = skillId;

    protected SkillBase Skill => Lich.Skill(SkillId)!;

    public override void Bind()
    {
        base.Bind();
        Lich.SkillLeveledUp += (id, _, _) => { if (id == SkillId) Rebuild(); };
        Lich.SkillXpChanged += (id, _) => { if (id == SkillId) MarkDirty(); };
        Lich.ActivityCappedChanged += (id, _, _) => { if (id == SkillId) MarkDirty(); };
        Lich.RecipeStatusChanged += (id, _, _) => { if (id == SkillId) MarkDirty(); };
        CryptMgr.Unlocks.Unlocked += (_, _) => Rebuild();
    }

    /// <summary>Icon square with a dot (no art: blocky stand-in).</summary>
    protected static PanelContainer IconBox(Color color, float size = 34) => Icon(color, size);

    /// <summary>A small labelled box in the header: "XP PER TICK / 12".</summary>
    protected static (PanelContainer Box, Label Value) HeaderBox(string label, bool purple = false)
    {
        var p = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        p.AddThemeStyleboxOverride("panel", purple ? Tokens.Box(Tokens.NecroBg, Tokens.PurpleDeep, Tokens.RadiusInset, 12, 8)
                                                   : Tokens.Box(Tokens.BgInset, Tokens.Border, Tokens.RadiusInset, 12, 8));
        var v = Ui.Bold("", Tokens.FontSmall, purple ? Tokens.NecroText : Tokens.TextMain);
        p.AddChild(Ui.Col(2, Ui.Text(label.ToUpperInvariant(), 10, Tokens.TextMuted, Tokens.LabelFont), v));
        return (p, v);
    }

    /// <summary>Header card: icon, "Mining Level 31 / 999", XP bar and text, then the boxes from <paramref name="boxes"/>.</summary>
    protected void Header(Control parent, params Control[] boxes)
    {
        var card = new Card(0);
        var row = Ui.Row(18);
        card.Body.AddChild(row);
        row.AddChild(IconBox(Tokens.TextMuted, 54));
        var level = Ui.Text("", Tokens.FontBody, Tokens.TextMuted);
        var xpBar = Ui.Width(new StatBar(Tokens.Green, 6), 166);
        xpBar.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        xpBar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var xpText = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        var title = Ui.Col(6, Ui.Row(8, Ui.Heading(Skill.Name, Tokens.FontTitle), level), Ui.Row(8, xpBar, xpText));
        title.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(title);
        row.AddChild(Ui.Fill());
        foreach (var b in boxes) row.AddChild(b);
        parent.AddChild(card);
        Update(() =>
        {
            var s = Skill;
            level.Text = $"Level {s.Level} / {XpCurve.MaxLevel}";
            xpBar.Value = s.LevelProgress;
            xpText.Text = s.IsMaxLevel ? $"{Fmt.Short(s.Xp)} XP (max)" : $"{Fmt.Short(s.Xp - s.LevelStartXp)} / {Fmt.Short(s.NextLevelXp - s.LevelStartXp)} XP";
            xpText.TooltipText = $"{Fmt.Exact(s.Xp)} total XP; level {s.Level + 1} at {Fmt.Exact(s.NextLevelXp)}";
        });
    }

    /// <summary>"Necromancers  2.4B free of 100B", the step selector and a note.</summary>
    protected void PoolCard(VBoxContainer into, string note)
    {
        var card = new Card(8);
        var free = Ui.Bold("", Tokens.FontBody, Tokens.Green);
        card.Body.AddChild(Ui.Row(8, Ui.Heading("Necromancers"), Ui.Fill(), free));
        card.Body.AddChild(new StepSelector());
        card.Body.AddChild(Ui.Muted(note));
        into.AddChild(card);
        Update(() =>
        {
            var p = Lich.State.Pool;
            free.Text = $"{Fmt.Short(p.Free)} free of {Fmt.Short(p.Max)}";
            free.TooltipText = $"{Fmt.Exact(p.Free)} free, {Fmt.Exact(p.Assigned)} assigned, max {Fmt.Exact(p.Max)}";
        });
    }

    /// <summary>The automation card from the mockup. v0.1 has none of these rules yet, so the rows show as not built.</summary>
    protected static void AutomationCard(VBoxContainer into, params string[] rules)
    {
        var card = new Card(6);
        card.Body.AddChild(Ui.Heading("Automation"));
        foreach (var r in rules)
        {
            var row = new InsetRow();
            var text = Ui.Text(r, Tokens.FontSmall, Tokens.TextSoft, wrap: true);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.Body.AddChild(text);
            row.Body.AddChild(Ui.Bold("OFF", Tokens.FontSmall, Tokens.TextDim));
            row.TooltipText = "Not in v0.1";
            Ui.SetLocked(row, true);
            card.Body.AddChild(row);
        }
        into.AddChild(card);
    }

    /// <summary>Tool card (Pickaxe, Anvil...): the real speed multiplier; tool tiers are not in v0.1.</summary>
    protected void SpeedCard(VBoxContainer into, string tool, string what, Func<double> speed)
    {
        var card = new Card(8);
        card.Body.AddChild(Ui.Row(8, Ui.Heading(tool), Ui.Fill(), Ui.Text("Tier 1", Tokens.FontSmall, Tokens.TextMuted)));
        var speedText = Ui.Text("", Tokens.FontSmall, Tokens.TextSoft);
        card.Body.AddChild(speedText);
        var up = new ActionButton("Upgrade", primary: true) { Disabled = true, TooltipText = "Tool tiers are not in v0.1" };
        card.Body.AddChild(Ui.Muted("Tool tiers come later. Research and gear raise speed now."));
        card.Body.AddChild(up);
        into.AddChild(card);
        Update(() =>
        {
            var x = speed();
            speedText.Text = $"{what} speed x{Fmt.Num2(x)}" + (x > 1 ? $"  (+{x - 1:P0})" : "");
        });
    }

    /// <summary>Lays out: header across the top, then a left area (expands) and a right column (w326).</summary>
    protected (VBoxContainer Left, VBoxContainer Right) Layout(params Control[] headerBoxes)
    {
        var col = Ui.Col(Tokens.CardGap);
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(col);
        Header(col, headerBoxes);
        var row = Ui.Row(Tokens.CardGap);
        row.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(row);
        var left = Ui.Col(Tokens.CardGap);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(left);
        var rightScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(318, 0) };
        var right = Ui.Col(Tokens.CardGap);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightScroll.AddChild(right);
        row.AddChild(rightScroll);
        return (left, right);
    }

    /// <summary>Style for item cards/rows: purple when necromancers work it, dim when locked, default otherwise.</summary>
    protected static void StyleWorked(PanelContainer p, bool working, bool locked, int radius = Tokens.RadiusCard, int pad = 13)
    {
        p.AddThemeStyleboxOverride("panel", working
            ? Tokens.Box(Tokens.NecroBg, Tokens.Purple, radius, pad, pad - 1, 2)
            : Tokens.Box(Tokens.BgCard, Tokens.BorderCard, radius, pad, pad));
        Ui.SetLocked(p, locked);
    }

    protected static Color ItemColor(string id) =>
        Db.TryGet<ResourceDefinitionBase>(id, out var r) && r.Color is { } hex ? new Color(hex) : Tokens.TextSoft;
}

// ---- 6a. Gathering ----

public partial class GatheringPage : SkillPageBase
{
    public GatheringPage(string skillId) : base(skillId) { }

    private GatheringSkill G => (GatheringSkill)Skill;

    protected override void Build()
    {
        var g = G;
        var (xpBox, xpVal) = HeaderBox("XP per tick");
        var (nextBox, nextVal) = HeaderBox("Next unlock");
        var (workBox, workVal) = HeaderBox("Necromancers working", purple: true);
        var (left, right) = Layout(xpBox, nextBox, workBox);
        Update(() =>
        {
            var xp = g.Activities.Sum(a => g.RatePerTick(a.Id) * a.Xp);
            xpVal.Text = Fmt.Short(xp);
            var next = g.Activities.Where(a => !g.IsUnlocked(a)).OrderBy(a => a.RequiredLevel).FirstOrDefault();
            nextVal.Text = next is null ? "Everything unlocked" : g.IsResearchLocked(next) ? $"{next.Name} · Research" : $"{next.Name} · Lvl {next.RequiredLevel}";
            var working = g.Activities.Sum(a => g.GetAssigned(a.Id));
            var top = g.Activities.OrderByDescending(a => g.GetAssigned(a.Id)).FirstOrDefault();
            workVal.Text = working > 0 && top is not null ? $"{Fmt.Short(working)} · mostly {top.Name}" : "None yet";
        });

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(grid);
        left.AddChild(scroll);
        foreach (var a in g.Activities) grid.AddChild(ActivityCard(g, a));

        PoolCard(right, "Each necromancer gathers on its own. Assign them to items on the left, in the step chosen above. Others are working in the Workshop or on other skills.");
        AutomationCard(right, "Auto-assign idle necromancers", "When storage is full, switch item", "Stop when stock reaches a target", "Keep going while away");
        SpeedCard(right, ToolName(g.Id), "Gathering", () => g.SpeedMultiplier);
    }

    private static string ToolName(string skill) => skill switch
    {
        "mining" => "Pickaxe", "fishing" => "Rod", "hunting" => "Bow", "herbalism" => "Sickle", _ => "Tool",
    };

    private Control ActivityCard(GatheringSkill g, GatheringActivityDefinition a)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var col = Ui.Col(6);
        card.AddChild(col);
        var lvlPill = new Pill($"Lvl {a.RequiredLevel}");
        col.AddChild(Ui.Row(10, IconBox(ItemColor(a.Output), 30), Ui.Bold(a.Name, 14), Ui.Fill(), lvlPill));
        var perUnit = a.Rate > 0 ? 1 / a.Rate : 0;
        col.AddChild(Ui.Row(8, Ui.Text($"Gives 1 {Name(a.Output).ToLowerInvariant()}", Tokens.FontSmall, Tokens.TextMuted), Ui.Fill(),
            Ui.Text($"{Fmt.Num2(a.Xp)} XP · {Fmt.Num2(perUnit)} ticks", Tokens.FontSmall, Tokens.TextMuted)));
        var stock = Ui.Bold("", Tokens.FontSmall);
        var capped = Ui.Text("", Tokens.FontSmall, Tokens.Yellow);
        col.AddChild(Ui.Row(4, Ui.Text("In storage", Tokens.FontSmall, Tokens.TextMuted), stock, Ui.Fill(), capped));
        var bar = new StatBar(Tokens.Yellow, 4);
        col.AddChild(bar);
        var stepper = new Stepper(() => g.GetAssigned(a.Id),
            n => g.AddNecromancer(a.Id, Clamp(n, Lich.State.Pool.Free)),
            n => g.RemoveNecromancer(a.Id, Clamp(n, g.GetAssigned(a.Id))))
        { Free = () => Lich.State.Pool.Free };
        stepper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.AddChild(stepper);
        var rateLabel = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        var rateValue = Ui.Bold("", Tokens.FontSmall, Tokens.Green);
        col.AddChild(Ui.Row(4, rateLabel, rateValue));

        Update(() =>
        {
            var locked = !g.IsUnlocked(a);
            var working = g.GetAssigned(a.Id) > 0;
            StyleWorked(card, working && !locked, locked);
            lvlPill.SetKind(locked ? PillKind.Warning : PillKind.Running);
            if (g.IsResearchLocked(a)) lvlPill.Text = "Research";
            var owned = Owned(a.Output);
            stock.Text = Fmt.Short(owned);
            stock.TooltipText = Fmt.Exact(owned);
            var isRes = CryptMgr.Storage.IsResource(a.Output);
            bar.Visible = isRes;
            if (isRes) bar.Value = CryptMgr.Storage.Cap(a.Output) is var cap && cap > 0 ? (double)owned / cap : 0;
            capped.Text = g.IsCapped(a.Id) ? "Full" : "";
            stepper.Locked = locked;
            stepper.Refresh();
            rateLabel.Text = "Necromancers gather";
            rateValue.Text = locked ? "locked" : working ? $"{Fmt.Rate(g.RatePerTick(a.Id))} /tick" : "nothing yet";
            rateValue.AddThemeColorOverride("font_color", locked ? Tokens.TextDim : Tokens.Green);
            card.TooltipText = locked
                ? (g.IsResearchLocked(a) ? "Unlocks with research (see Research)." : $"Unlocks at {g.Name} level {a.RequiredLevel}.")
                : $"{Fmt.Num2(a.Rate)} {Name(a.Output)} per tick per necromancer, {Fmt.Num2(a.Xp)} XP each, times speed x{Fmt.Num2(g.SpeedMultiplier)}.";
        });
        return card;
    }
}

// ---- 6b. Crafting ----

public partial class CraftingPage : SkillPageBase
{
    private string? _tab;

    public CraftingPage(string skillId) : base(skillId) { }

    private CraftingSkill C => (CraftingSkill)Skill;

    /// <summary>Tabs come from the recipes' output item kinds: Weapons / Armour, Potions / Bombs, Runes...</summary>
    private static string TabOf(RecipeDefinition r) => Db.TryGet<ItemDefinitionBase>(r.Output.Id, out var item) ? item switch
    {
        WeaponDefinition => "Weapons",
        ArmourDefinition => "Armour",
        AccessoryDefinition => "Accessories",
        PotionDefinition => "Potions",
        BombDefinition => "Bombs",
        RuneDefinition => "Runes",
        _ => "Materials",
    } : "Resources";

    private static string Blurb(string id) => !Db.TryGet<ItemDefinitionBase>(id, out var item) ? "" : item switch
    {
        WeaponDefinition w => $"+{Fmt.Num2(w.Damage)} damage" + Mods(w),
        ArmourDefinition a => $"+{Fmt.Num2(a.Defence)} defence" + Mods(a),
        EquippableDefinitionBase e => Mods(e).TrimStart(' ', '·'),
        PotionDefinition p => p.Heal > 0 ? $"Heals {Fmt.Num2(p.Heal)}" : p.Effects.Count > 0 ? string.Join(", ", p.Effects.Select(Mod)) : "",
        BombDefinition b => $"{Fmt.Num2(b.Damage)} damage",
        RuneDefinition r => r.Trait,
        _ => "Material",
    };

    private static string Mods(EquippableDefinitionBase e) => e.Modifiers.Count == 0 ? "" : " · " + string.Join(", ", e.Modifiers.Select(Mod));
    private static string Mod(StatModifierDef m) => m.Op == "mult" || m.Op == "multiply" ? $"x{Fmt.Num2(m.Value)} {m.Stat.Replace('_', ' ')}" : $"+{Fmt.Num2(m.Value)} {m.Stat.Replace('_', ' ')}";

    protected override void Build()
    {
        var c = C;
        var tabs = c.Recipes.Select(TabOf).Distinct().ToList();
        if (_tab is null || !tabs.Contains(_tab)) _tab = tabs.FirstOrDefault();

        var (nextBox, nextVal) = HeaderBox("Next unlock");
        var (workBox, workVal) = HeaderBox("Crafting", purple: true);
        var (left, right) = Layout(nextBox, workBox);
        Update(() =>
        {
            var next = c.Recipes.Where(r => !c.IsUnlocked(r)).OrderBy(r => r.RequiredLevel).FirstOrDefault();
            nextVal.Text = next is null ? "Everything unlocked" : c.IsResearchLocked(next) ? $"{next.Name} · Research" : $"{next.Name} · Lvl {next.RequiredLevel}";
            var top = c.Recipes.OrderByDescending(r => c.GetAssigned(r.Id)).FirstOrDefault();
            workVal.Text = top is not null && c.GetAssigned(top.Id) > 0 ? $"{top.Name} · {Fmt.Short(c.GetAssigned(top.Id))} working" : "Nothing yet";
        });

        var card = new Card(10) { SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(card);
        var tabRow = Ui.Row(8);
        foreach (var t in tabs)
        {
            var b = new ActionButton(t, padX: 16, padY: 6, fontSize: Tokens.FontBody);
            b.SetSelected(t == _tab);
            b.AddThemeColorOverride("font_color", t == _tab ? Tokens.Green : Tokens.TextSoft);
            var tab = t;
            b.Pressed += () => { _tab = tab; Rebuild(); };
            tabRow.AddChild(b);
        }
        tabRow.AddChild(Ui.Fill());
        tabRow.AddChild(Ui.Text(SkillNote(c.Id), Tokens.FontSmall, Tokens.TextMuted));
        card.Body.AddChild(tabRow);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = Ui.Col(8);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);
        card.Body.AddChild(scroll);
        foreach (var r in c.Recipes.Where(r => TabOf(r) == _tab)) list.AddChild(RecipeRow(c, r));

        PoolCard(right, "Each necromancer assigned to a recipe crafts it again and again on its own.");
        AutomationCard(right, "Stop a recipe when its stock reaches the target", "Pause when an ingredient runs low", "Idle necromancers move to the next recipe", "Keep going while away");
        SpeedCard(right, c.Id switch { "smithing" => "Anvil", "alchemy" => "Cauldron", "enchanting" => "Runestone", _ => "Bench" }, "Crafting", () => c.SpeedMultiplier);
        if (c.Id == "enchanting")
        {
            var attach = new Card(6);
            attach.Body.AddChild(Ui.Heading("Attach rune"));
            attach.Body.AddChild(Ui.Muted("Choose a weapon or armour; a new rune replaces the old one. Attaching runes comes after v0.1."));
            Ui.SetLocked(attach, true);
            right.AddChild(attach);
        }
    }

    private static string SkillNote(string id) => id switch
    {
        "smithing" => "Gear for units and the lich",
        "alchemy" => "Potions for the lich, bombs for battles",
        "enchanting" => "Runes for weapons and armour",
        _ => "",
    };

    private Control RecipeRow(CraftingSkill c, RecipeDefinition r)
    {
        var row = new PanelContainer();
        var h = Ui.Row(10);
        row.AddChild(h);
        h.AddChild(IconBox(ItemColor(r.Output.Id), 34));
        var name = Ui.Col(2, Ui.Bold(r.Name, 14), Ui.Text($"Lvl {r.RequiredLevel} · {Blurb(r.Output.Id)}", Tokens.FontLabel, Tokens.TextMuted));
        name.CustomMinimumSize = new Vector2(150, 0);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        h.AddChild(name);
        var chips = Ui.Col(4);
        chips.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                var chipList = r.Inputs.Select(i => (i, new ResourceChip())).ToList();
        foreach (var (_, chip) in chipList) chips.AddChild(chip);
        h.AddChild(chips);
        var stock = Ui.Bold("", Tokens.FontSmall);
        var info = Ui.Col(2, Ui.Text($"{r.Ticks} ticks · {Fmt.Num2(r.Xp)} XP", Tokens.FontLabel, Tokens.TextMuted),
            Ui.Row(4, Ui.Text("In storage", Tokens.FontLabel, Tokens.TextMuted), stock));
        info.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        foreach (var l in info.GetChildren().OfType<Label>()) l.HorizontalAlignment = HorizontalAlignment.Right;
        h.AddChild(info);
        var stepper = new Stepper(() => c.GetAssigned(r.Id),
            n => c.AddNecromancer(r.Id, Clamp(n, Lich.State.Pool.Free)),
            n => c.RemoveNecromancer(r.Id, Clamp(n, c.GetAssigned(r.Id))))
        { Free = () => Lich.State.Pool.Free };
        var under = Ui.Text("", Tokens.FontLabel, Tokens.TextMuted);
        under.HorizontalAlignment = HorizontalAlignment.Center;
        var stepCol = Ui.Col(3, stepper, under);
        stepCol.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        h.AddChild(stepCol);
        var state = new Pill();
        state.CustomMinimumSize = new Vector2(64, 0);
        h.AddChild(state);
        Update(() =>
        {
            var locked = !c.IsUnlocked(r);
            var n = c.GetAssigned(r.Id);
            StyleWorked(row, n > 0 && !locked, locked, Tokens.RadiusInset, 12);
            foreach (var (input, chip) in chipList) chip.Set(Name(input.Id), Owned(input.Id), input.Count);
            stock.Text = Fmt.Short(Owned(r.Output.Id));
            stepper.Locked = locked;
            stepper.Refresh();
            var st = c.Status(r.Id);
            under.Text = locked ? (c.IsResearchLocked(r) ? "Research" : $"Needs Lvl {r.RequiredLevel}")
                : n > 0 ? $"{Fmt.Num2(c.CraftsPerTick(r.Id))} crafts/tick" : "No necromancers";
            (state.Text, var kind) = locked ? ("Locked", PillKind.Neutral) : st switch
            {
                RecipeStatus.Running => ("Running", PillKind.Running),
                RecipeStatus.MissingInput => ("Missing", PillKind.Warning),
                RecipeStatus.OutputFull => ("Full", PillKind.Warning),
                _ => (n > 0 ? "Starting" : "Idle", PillKind.Neutral),
            };
            state.SetKind(kind);
            state.TooltipText = st == RecipeStatus.MissingInput ? $"Missing {Name(c.MissingInput(r.Id))}" : "";
            row.TooltipText = $"Makes {r.Output.Count} {Name(r.Output.Id)} every {r.Ticks} ticks per necromancer (speed x{Fmt.Num2(c.SpeedMultiplier)}).";
        });
        return row;
    }
}

/// <summary>Lich skills have no page in the mockups; a small info card in case one is opened directly.</summary>
public partial class SkillInfoPage : SkillPageBase
{
    public SkillInfoPage(string skillId) : base(skillId) { }

    protected override void Build()
    {
        if (Lich.Skill(SkillId) is null) return;
        var (_, right) = Layout();
        right.AddChild(Ui.Muted("This skill levels on its own; it has no page."));
    }
}
