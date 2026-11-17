using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Managers;
using LittleLich.Skills;

namespace LittleLich.UI.Dev;

/// <summary>
/// Phase 4: the page shown in the main panel when a skill is selected in the sidebar.
/// Gathering: one row per activity (level lock, output stock/cap, rate, XP, status, assignment buttons).
/// Crafting: one row per recipe (inputs owned/needed, output, ticks, XP, status with reason, assignment buttons).
/// Level-only skills: level, XP and what the level does.
/// Built from the content database, so new activities/recipes in JSON appear with no scene change.
/// Rows are rebuilt on level up (locks change) and updated in place otherwise.
/// </summary>
public partial class SkillPage : ScrollContainer
{
    private sealed record Row(string Id, Label Info, Label Status, AssignmentRow Assign);

    private readonly VBoxContainer _col;
    private readonly Label _title;
    private readonly ProgressBar _xpBar;
    private readonly Label _xpText;
    private readonly Label _note;
    private readonly VBoxContainer _rows;
    private readonly List<Row> _rowList = new();

    public SkillBase? Skill { get; private set; }

    public SkillPage()
    {
        HorizontalScrollMode = ScrollMode.Disabled;
        _col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _col.AddThemeConstantOverride("separation", 6);
        AddChild(_col);

        _title = new Label { ThemeTypeVariation = "HeaderMedium" };
        _col.AddChild(_title);
        var xp = new HBoxContainer();
        _col.AddChild(xp);
        _xpBar = new ProgressBar { CustomMinimumSize = new Vector2(260, 0), MaxValue = 1, ShowPercentage = false, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        xp.AddChild(_xpBar);
        _xpText = new Label();
        xp.AddChild(_xpText);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1, 1, 1, 0.75f) };
        _col.AddChild(_note);
        _col.AddChild(new HSeparator());
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 10);
        _col.AddChild(_rows);
    }

    public void Bind()
    {
        var lm = LichManager.Instance;
        lm.SkillXpChanged += (id, _) => { if (id == Skill?.Id) RefreshHeader(); };
        lm.SkillLeveledUp += (id, _, _) => { if (Skill is { } s && id == s.Id) Show(s); };
        lm.NecromancerPoolChanged += (_, _, _) => RefreshRows();
        lm.ActivityCappedChanged += (id, _, _) => { if (id == Skill?.Id) RefreshRows(); };
        lm.RecipeStatusChanged += (id, _, _) => { if (id == Skill?.Id) RefreshRows(); };
        lm.StatChanged += (_, _) => RefreshRows();
        TickManager.Instance.TickEnded += (_, _) => RefreshRows();   // stock moves every tick
        CryptManager.Instance.StockChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) RefreshRows(); };
        CryptManager.Instance.InventoryChanged += (_, _) => { if (!TickManager.Instance.Scheduler.IsRunning) RefreshRows(); };
    }

    /// <summary>Shows a skill (rebuilds rows).</summary>
    public void Show(SkillBase skill)
    {
        Skill = skill;
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        _rowList.Clear();

        switch (skill)
        {
            case GatheringSkill g:
                _note.Text = $"Each necromancer gathers on its own. Output x {DevFormat.Exact(g.SpeedMultiplier)} ({SkillIdsSpeed(g)}). " +
                             "A full output stops that activity; necromancers stay assigned.";
                foreach (var a in g.Activities) AddRow(a.Id, g.Id);
                break;
            case CraftingSkill c:
                _note.Text = $"Each necromancer crafts its recipe again and again while inputs last. Speed x {DevFormat.Exact(c.SpeedMultiplier)} ({SkillIdsSpeed(c)}).";
                foreach (var r in c.Recipes) AddRow(r.Id, c.Id);
                break;
            case HealthSkill:
                _note.Text = $"Level only. +{HealthSkill.MaxHealthPerLevel:0} max health per level above 1 " +
                             $"(now +{(skill.Level - 1) * HealthSkill.MaxHealthPerLevel:N0}). Gain XP with the console: xp health 100";
                break;
            default:
                _note.Text = $"Level only in v0.1. Gain XP with the console: xp {skill.Id} 100";
                break;
        }
        RefreshHeader();
        RefreshRows();
    }

    private static string SkillIdsSpeed(SkillBase s) => $"stat {LittleLich.Core.SkillIds.SpeedStat(s.Id)}";

    private void AddRow(string id, string skillId)
    {
        var box = new VBoxContainer();
        var info = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        var status = new Label();
        var assign = new AssignmentRow(skillId, id, "Necromancers");
        box.AddChild(info);
        box.AddChild(status);
        box.AddChild(assign);
        _rows.AddChild(box);
        _rowList.Add(new Row(id, info, status, assign));
    }

    private void RefreshHeader()
    {
        if (Skill is not { } s) return;
        _title.Text = $"{s.Name}   level {s.Level} / {XpCurve.MaxLevel}";
        _xpBar.Value = s.LevelProgress;
        _xpText.Text = s.IsMaxLevel
            ? $"{DevFormat.Exact(s.Xp)} XP (max level)"
            : $"{DevFormat.Exact(s.Xp)} / {DevFormat.Exact(s.NextLevelXp)} XP  ({DevFormat.Exact(s.NextLevelXp - s.Xp)} to level {s.Level + 1})";
    }

    private void RefreshRows()
    {
        if (!IsVisibleInTree() || Skill is null) return;
        var content = GameManager.Instance.Content;
        var stock = CryptManager.Instance.Stockpile;

        foreach (var row in _rowList)
        {
            switch (Skill)
            {
                case GatheringSkill g:
                {
                    var a = g.Activity(row.Id);
                    var locked = !g.IsUnlocked(a);
                    row.Info.Text = $"{a.Name}  (level {a.RequiredLevel})   -> {StockIds.NameOf(content, a.Output)} " +
                                    $"{StockText(stock, a.Output)}   {DevFormat.Exact((LittleLich.Core.Num)a.Rate)}/tick per necromancer, {DevFormat.Exact((LittleLich.Core.Num)a.Xp)} XP each";
                    row.Status.Text = locked ? (g.IsResearchLocked(a) ? "Locked: needs research (see the Research tab)" : $"Locked: needs {g.Name} level {a.RequiredLevel}")
                        : g.IsCapped(a.Id) ? $"CAPPED: {StockIds.NameOf(content, a.Output)} is full (production stopped)"
                        : g.GetAssigned(a.Id) > 0 ? $"Gathering +{RateText(g.RatePerTick(a.Id))}/tick"
                        : "Idle";
                    row.Status.Modulate = locked ? new Color(1, 1, 1, 0.5f) : g.IsCapped(a.Id) ? new Color(1f, 0.6f, 0.4f) : Colors.White;
                    row.Assign.Locked = locked;
                    break;
                }
                case CraftingSkill c:
                {
                    var r = c.Recipe(row.Id);
                    var locked = !c.IsUnlocked(r);
                    var inputs = string.Join(", ", r.Inputs.Select(i =>
                        $"{StockIds.NameOf(content, i.Id)} {DevFormat.Exact(stock.Get(i.Id))}/{DevFormat.Exact(i.Count)}"));
                    row.Info.Text = $"{r.Name}  (level {r.RequiredLevel})   in: {inputs}   -> {DevFormat.Exact(r.Output.Count)} " +
                                    $"{StockIds.NameOf(content, r.Output.Id)} {StockText(stock, r.Output.Id)}   " +
                                    $"{DevFormat.Exact(r.Ticks)} ticks per craft per necromancer, {DevFormat.Exact((LittleLich.Core.Num)r.Xp)} XP each";
                    var st = c.Status(r.Id);
                    row.Status.Text = locked ? (c.IsResearchLocked(r) ? "Locked: needs research (see the Research tab)" : $"Locked: needs {c.Name} level {r.RequiredLevel}") : st switch
                    {
                        RecipeStatus.Running => $"Running, up to {RateText(c.CraftsPerTick(r.Id))} crafts/tick",
                        RecipeStatus.MissingInput => $"PAUSED: missing {StockIds.NameOf(content, c.MissingInput(r.Id) ?? "?")}",
                        RecipeStatus.OutputFull => $"PAUSED: {StockIds.NameOf(content, r.Output.Id)} is full",
                        _ => c.GetAssigned(r.Id) > 0 ? "Starting (next tick)" : "Idle",
                    };
                    row.Status.Modulate = locked ? new Color(1, 1, 1, 0.5f)
                        : st is RecipeStatus.MissingInput or RecipeStatus.OutputFull ? new Color(1f, 0.6f, 0.4f) : Colors.White;
                    row.Assign.Locked = locked;
                    break;
                }
            }
            row.Assign.Refresh();
        }
    }

    private static string StockText(LittleLich.Crypt.Stockpile stock, string id) =>
        stock.Storage.IsResource(id)
            ? $"[{DevFormat.Exact(stock.Get(id))} / {DevFormat.Exact(stock.Storage.Cap(id))}]"
            : $"[{DevFormat.Exact(stock.Get(id))} in inventory]";

    private static string RateText(double perTick) =>
        perTick >= 100 ? DevFormat.Exact((long)Math.Round(perTick)) : perTick.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
