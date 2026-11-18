using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Managers;
using LittleLich.UI.Components;
using LittleLich.UI.Theme;
using static LittleLich.UI.Pages.Kit;

namespace LittleLich.UI.Pages;

/// <summary>A spell as the Spell Book shows it.</summary>
public sealed record SpellInfo(string Id, string Name, string Kind, int Insight, long Mana, string Effect, string Tome, bool Learned, Color Color,
    string AutoRule = "", double CooldownSeconds = 8);

/// <summary>What the Spell Book page needs. <see cref="MockData.SpellBook"/> stands in until the spell system exists.</summary>
public interface ISpellBook
{
    IReadOnlyList<SpellInfo> Spells { get; }
    IReadOnlyList<string> Channelled { get; }
    int MaxInsight { get; }
    int UsedInsight { get; }
    bool IsAuto(string spellId);
    double Cooldown(string spellId);
    bool Channel(string spellId);
    void Remove(string spellId);
    void ToggleAuto(string spellId);
}

/// <summary>Placeholder data behind the same interfaces the managers will expose (phase 12, "MockData").</summary>
public static class MockData
{
    public static readonly ISpellBook SpellBook = new MockSpellBook();

    private sealed class MockSpellBook : ISpellBook
    {
        private readonly List<string> _channelled = new() { "raise_dead", "mend_flesh", "bone_spear" };
        private readonly HashSet<string> _manual = new() { "bone_spear" };

        public IReadOnlyList<SpellInfo> Spells { get; } = new List<SpellInfo>
        {
            new("raise_dead", "Raise Dead", "Support", 1, 40, "Raises remains", "Necromancy 1", true, Tokens.TextMain, "When remains are on the field"),
            new("bone_spear", "Bone Spear", "Attack", 2, 48, "Damage 168", "Necromancy 1", true, Tokens.TextMain, "", 8),
            new("bone_ward", "Bone Ward", "Support", 1, 30, "Shields a stack", "Necromancy 1", true, Tokens.Blue, "When a stack is attacked"),
            new("mend_flesh", "Mend Flesh", "Support", 1, 35, "Heals a stack", "Necromancy 1", true, Tokens.Green, "When a stack is under 40%", 5),
            new("soul_drain", "Soul Drain", "Attack", 2, 50, "Drains life", "Necromancy 1", true, Tokens.Purple, "When the enemy is in range"),
            new("pack_howl", "Pack Howl", "Siege", 2, 60, "Rallies beasts", "Beast Magic", false, Tokens.TextDim),
            new("rot_gate", "Rot Gate", "Siege", 3, 80, "Weakens walls", "Crypt Magic", false, Tokens.TextDim),
            new("plague_cloud", "Plague Cloud", "Attack", 3, 90, "Damages over time", "Crypt Magic", false, Tokens.TextDim),
            new("unhallow", "Unhallow", "Siege", 3, 100, "Defiles holy ground", "Holy Magic", false, Tokens.TextDim),
        };

        public IReadOnlyList<string> Channelled => _channelled;
        public int MaxInsight => (int)Math.Max(0, Math.Floor(LichManager.Instance.State.MaxInsight.ToDouble()));
        public int UsedInsight => _channelled.Sum(id => Spells.First(s => s.Id == id).Insight);
        public bool IsAuto(string id) => !_manual.Contains(id);
        public double Cooldown(string id) => (Time.GetTicksMsec() / 1000.0 + id.Length) % (Spells.First(s => s.Id == id).CooldownSeconds + 3) is var t && t > Spells.First(s => s.Id == id).CooldownSeconds ? 0 : Spells.First(s => s.Id == id).CooldownSeconds - t;

        public bool Channel(string id)
        {
            var s = Spells.FirstOrDefault(x => x.Id == id);
            if (s is null || !s.Learned || _channelled.Contains(id) || UsedInsight + s.Insight > MaxInsight) return false;
            _channelled.Add(id);
            return true;
        }

        public void Remove(string id) => _channelled.Remove(id);
        public void ToggleAuto(string id) { if (!_manual.Remove(id)) _manual.Add(id); }
    }
}

/// <summary>
/// Spell Book (image 05): spell grid with search and filters plus the selected spell's details; the Insight meter;
/// the channelled spells with Auto/Manual and cooldown bars. Runs on <see cref="MockData.SpellBook"/> for now.
/// </summary>
public partial class SpellBookPage : PageBase
{
    private readonly ISpellBook _book = MockData.SpellBook;
    private string _filter = "All";
    private string _search = "";
    private string? _selected = "bone_spear";

    public override void Bind()
    {
        base.Bind();
        Lich.StatChanged += (_, _) => MarkDirty();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (IsVisibleInTree()) foreach (var u in _cooldowns) u();
    }

    private readonly List<Action> _cooldowns = new();

    protected override void Build()
    {
        _cooldowns.Clear();
        var row = CardRow();
        row.AddChild(SpellsCard());
        row.AddChild(InsightCard());
        row.AddChild(ChannelledCard());
    }

    private Control SpellsCard()
    {
        var card = new Card(10) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        var learned = _book.Spells.Count(s => s.Learned);
        card.Body.AddChild(Ui.Row(8, Ui.Heading("Spells"), Ui.Text($"{learned} learned, {_book.Spells.Count - learned} to find", Tokens.FontSmall, Tokens.TextMuted),
            Ui.Fill(), Ui.Text("Preview: spells come in a later phase", Tokens.FontLabel, Tokens.Yellow)));
        var search = new LineEdit { PlaceholderText = "Search spells", Text = _search, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        search.TextSubmitted += t => { _search = t; Rebuild(); };
        search.FocusExited += () => { if (search.Text != _search) { _search = search.Text; Rebuild(); } };
        var filters = Ui.Row(6, search);
        foreach (var f in new[] { "All", "Attack", "Support", "Siege" })
        {
            var b = new ActionButton(f, padX: 12, padY: 4, radius: 999);
            b.SetSelected(f == _filter);
            b.AddThemeColorOverride("font_color", f == _filter ? Tokens.Green : Tokens.TextSoft);
            var ff = f;
            b.Pressed += () => { _filter = ff; Rebuild(); };
            filters.AddChild(b);
        }
        card.Body.AddChild(filters);

        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        foreach (var s in _book.Spells.Where(s => (_filter == "All" || s.Kind == _filter) && (_search.Length == 0 || s.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))))
            grid.AddChild(SpellTile(s));
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(grid);
        card.Body.AddChild(scroll);

        if (_book.Spells.FirstOrDefault(s => s.Id == _selected) is { } sel)
        {
            var detail = new Card(6);
            detail.SetStyle(Tokens.BgInset, Tokens.Border);
            detail.Body.AddChild(Ui.Row(6, Ui.Bold(sel.Name, 14), Ui.Text($"from {sel.Tome}", Tokens.FontSmall, Tokens.TextMuted), Ui.Fill(),
                Ui.Text(sel.Learned ? "Learned" : "Not found", Tokens.FontSmall, Tokens.TextMuted)));
            detail.Body.AddChild(Ui.Text($"{sel.Effect}   Mana {sel.Mana}   Insight {sel.Insight}   Cooldown {Fmt.Num1(sel.CooldownSeconds)}s", Tokens.FontSmall, Tokens.TextSoft));
            detail.Body.AddChild(Ui.Section("Modifiers"));
            var mods = Ui.Row(6);
            foreach (var m in new[] { "Wider area", "Lower cost", "Shorter cooldown" })
            {
                var chip = new Pill(m + " · locked");
                chip.SetKind(PillKind.Neutral);
                mods.AddChild(chip);
            }
            detail.Body.AddChild(mods);
            card.Body.AddChild(detail);
        }
        return card;
    }

    private Control SpellTile(SpellInfo s)
    {
        var on = _book.Channelled.Contains(s.Id);
        var item = new NavItem(8, 10, 4) { CustomMinimumSize = new Vector2(0, 84), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        item.AddThemeStyleboxOverride("panel", s.Id == _selected ? Tokens.Box(Tokens.SelectedBg, Tokens.Green, Tokens.RadiusInset, 8, 10, 2)
            : on ? Tokens.Box(Tokens.NecroBg, Tokens.NecroBorder, Tokens.RadiusInset, 8, 10)
            : Tokens.Box(Tokens.BgCard, Tokens.BorderSkill, Tokens.RadiusInset, 8, 10));
        var icon = Icon(s.Color, 30);
        icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        var name = Ui.Bold(s.Learned ? s.Name : s.Name, Tokens.FontSmall);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        var sub = Ui.Text(s.Learned ? $"Insight {s.Insight} · {s.Mana} mana" : s.Tome, Tokens.FontLabel, Tokens.TextMuted);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        item.Body.AddChild(icon);
        item.Body.AddChild(name);
        item.Body.AddChild(sub);
        Ui.SetLocked(item, !s.Learned);
        item.TooltipText = s.Learned ? (on ? "Channelled" : "Click to select; click again to channel") : $"Found in {s.Tome}";
        item.Pressed += () =>
        {
            if (_selected == s.Id && s.Learned && !on)
            {
                if (!_book.Channel(s.Id)) Toast.Show($"Not enough Insight to channel {s.Name}.", true);
            }
            _selected = s.Id;
            Rebuild();
        };
        return item;
    }

    private Control InsightCard()
    {
        var card = new Card(6) { CustomMinimumSize = new Vector2(104, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        var title = Ui.Heading("Insight", 15);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        card.Body.AddChild(title);
        var dot = new CenterContainer();
        dot.AddChild(Icon(Tokens.Purple, 34));
        card.Body.AddChild(dot);
        var free = Big(Tokens.Green);
        var used = Big(Tokens.TextMain);
        var max = Big(Tokens.TextMain);
        foreach (var (label, value) in new[] { ("Free", free), ("Used", used), ("Max", max) })
        {
            var l = Ui.Text(label, Tokens.FontLabel, Tokens.TextMuted);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            card.Body.AddChild(l);
            card.Body.AddChild(value);
        }
        card.Body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var note = Ui.Text("Grows with Magic level and gear", Tokens.FontLabel, Tokens.TextMuted, wrap: true);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        card.Body.AddChild(note);
        Update(() =>
        {
            free.Text = Math.Max(0, _book.MaxInsight - _book.UsedInsight).ToString();
            used.Text = _book.UsedInsight.ToString();
            max.Text = _book.MaxInsight.ToString();
        });
        return card;
    }

    private static Label Big(Color c)
    {
        var l = Ui.Bold("", 22, c);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        return l;
    }

    private Control ChannelledCard()
    {
        var card = new Card(10) { CustomMinimumSize = new Vector2(370, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        card.Body.AddChild(Ui.Heading("Channelled"));
        foreach (var id in _book.Channelled.ToList())
        {
            var s = _book.Spells.First(x => x.Id == id);
            var p = new PanelContainer();
            p.AddThemeStyleboxOverride("panel", Tokens.Box(Tokens.NecroBg, Tokens.NecroBorder, Tokens.RadiusInset, 12, 10));
            var col = Ui.Col(6);
            p.AddChild(col);
            var ready = Ui.Bold("", Tokens.FontSmall, Tokens.Green);
            var remove = new ActionButton("×", padX: 7, padY: 0);
            remove.AddThemeColorOverride("font_color", Tokens.Orange);
            remove.Pressed += () => { _book.Remove(id); Rebuild(); };
            var info = Ui.Col(1, Ui.Bold(s.Name, 14), Ui.Text($"Mana {s.Mana} · {s.Effect} · Insight {s.Insight}", Tokens.FontLabel, Tokens.TextMuted));
            info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            col.AddChild(Ui.Row(10, Icon(s.Color, 38), info, ready, remove));
            var bar = new StatBar(Tokens.Purple, 4);
            col.AddChild(bar);
            var auto = new ActionButton(_book.IsAuto(id) ? "AUTO" : "MANUAL", primary: _book.IsAuto(id), padX: 10, padY: 1, radius: 999, fontSize: Tokens.FontLabel);
            auto.Pressed += () => { _book.ToggleAuto(id); Rebuild(); };
            col.AddChild(Ui.Row(8, auto, Ui.Fill(), Ui.Text(_book.IsAuto(id) ? s.AutoRule : "You cast it", Tokens.FontSmall, Tokens.TextSoft)));
            card.Body.AddChild(p);
            _cooldowns.Add(() =>
            {
                var cd = _book.Cooldown(id);
                ready.Text = cd <= 0 ? "Ready" : $"{Fmt.Num1(cd)}s";
                ready.AddThemeColorOverride("font_color", cd <= 0 ? Tokens.Green : Tokens.NecroText);
                bar.Value = cd <= 0 ? 1 : 1 - cd / s.CooldownSeconds;
            });
        }
        var prompt = new PanelContainer();
        prompt.AddThemeStyleboxOverride("panel", Tokens.Box(Colors.Transparent, Tokens.Border, Tokens.RadiusInset, 12, 12));
        var pl = Ui.Text("", Tokens.FontSmall, Tokens.TextMuted);
        pl.HorizontalAlignment = HorizontalAlignment.Center;
        prompt.AddChild(pl);
        card.Body.AddChild(prompt);
        Update(() => pl.Text = $"{Math.Max(0, _book.MaxInsight - _book.UsedInsight)} Insight free. Click a spell twice to channel it.");
        return card;
    }
}
