using System;
using System.Linq;
using Godot;
using LittleLich.Core;
using LittleLich.Core.Ticks;
using LittleLich.Managers;
using LittleLich.UI.Components;
using LittleLich.UI.Shell;
using LittleLich.UI.Theme;

namespace LittleLich.UI.Pages;

/// <summary>Registers every page with the router.</summary>
public static class PageRegistry
{
    public static void Register(PageRouter router, Action<string> navigate)
    {
        router.Register("Crypt/Workshop", _ => new WorkshopPage());
        router.Register("Crypt/Research", _ => new ResearchPage());
        router.Register("Crypt/UnitBuilder", _ => new UnitBuilderPage());
        router.Register("Crypt/Army", _ => new ArmyPage());
        router.Register("Crypt/SpellBook", _ => new SpellBookPage());
        router.Register("Crypt/Lich", _ => new LichPage());
        router.Register("Skill/", key => SkillPages.For(key["Skill/".Length..]));
        router.Register("Map", _ => new MapPage());
        router.Register("BossPrep", _ => new BossPrepPage());
        router.Register("Battle", _ => new BattlePage());
        router.Register("Settings", _ => new SettingsPage());
    }
}

/// <summary>Modal card over the content when returning: time away, gains, capped resources. One Continue button.</summary>
public partial class AwayRecapModal : Control
{
    public static void ShowFor(Control shell, AwayRecap recap)
    {
        if (recap.Ticks <= 0) return;
        foreach (var old in shell.GetChildren().OfType<AwayRecapModal>()) old.QueueFree();
        shell.AddChild(new AwayRecapModal(recap));
    }

    private AwayRecapModal(AwayRecap recap)
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var card = new Card(10) { CustomMinimumSize = new Vector2(420, 0) };
        center.AddChild(card);
        card.Body.AddChild(Ui.Heading("While you were away", Tokens.FontTitle));
        card.Body.AddChild(Ui.Text($"Away {Kit.FormatTime(recap.ElapsedSeconds)} · {Fmt.Exact(recap.Ticks)} ticks" +
                                   (recap.TicksLostToCap > 0 ? $" ({Fmt.Exact(recap.TicksLostToCap)} lost to the 24 h cap)" : ""),
                                   Tokens.FontSmall, Tokens.TextMuted));
        if (recap.Gained.Count > 0)
        {
            card.Body.AddChild(Ui.Section("Gained"));
            foreach (var ((kind, id), amount) in recap.Gained.OrderBy(g => g.Key.Kind).Take(14))
            {
                var name = kind is "xp" ? $"{Kit.Name(id)} XP" : kind is "necromancers" ? "Necromancers" : Kit.Name(id);
                var (row, v) = Kit.KeyValue(name, Tokens.Green);
                v.Text = "+" + Fmt.Short(amount);
                card.Body.AddChild(row);
            }
        }
        if (recap.Capped.Count > 0)
        {
            card.Body.AddChild(Ui.Section("Capped"));
            card.Body.AddChild(Ui.Text(string.Join(", ", recap.Capped.Select(Kit.Name)), Tokens.FontSmall, Tokens.Yellow, wrap: true));
        }
        if (recap.IsEmpty) card.Body.AddChild(Ui.Muted("Nothing was produced."));
        var ok = new ActionButton("Continue", primary: true);
        ok.Pressed += QueueFree;
        card.Body.AddChild(ok);
    }
}
