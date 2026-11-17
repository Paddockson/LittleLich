using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Managers;
using LittleLich.World;

namespace LittleLich.UI.Dev;

/// <summary>
/// Phase 8: the battle view. Your army and the defenders side by side, by section; one HP bar per unit group
/// (with units standing and a brace marker) and one cooldown bar per action. A log of actions and a Retreat button.
/// Updated every frame while a battle runs (like the tick bar, the one other per-frame update).
/// </summary>
public partial class BattlePanel : VBoxContainer, IDevPanel
{
    public string Title => "Battle";
    public Control Root => this;

    private sealed record GroupView(BattleGroup Group, Label Name, ProgressBar Hp, ProgressBar[] Cooldowns);

    private readonly Label _header;
    private readonly Button _retreat;
    private readonly HBoxContainer _sides;
    private readonly RichTextLabel _log;
    private readonly List<GroupView> _views = new();
    private BattleController? _shown;

    public BattlePanel()
    {
        var top = new HBoxContainer();
        AddChild(top);
        _header = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        top.AddChild(_header);
        _retreat = new Button { Text = "Retreat" };
        _retreat.Pressed += () => WorldManager.Instance.Retreat();
        top.AddChild(_retreat);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        _sides = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sides.AddThemeConstantOverride("separation", 16);
        scroll.AddChild(_sides);
        _log = new RichTextLabel { CustomMinimumSize = new Vector2(0, 110), ScrollFollowing = true, BbcodeEnabled = true };
        AddChild(_log);
    }

    public void Bind()
    {
        var wm = WorldManager.Instance;
        wm.BattleStarted += _ => { _log.Clear(); Rebuild(); };
        wm.ActionFired += Log;
        wm.Resolved += r =>
        {
            _log.AppendText($"[color=#e6d27a]{DevFormat.Bb(MapPanel.ResultText(r))}[/color]\n");
            Rebuild();
        };
    }

    public void Refresh() => Rebuild();

    private void Rebuild()
    {
        foreach (var c in _sides.GetChildren()) c.QueueFree();
        _views.Clear();
        var w = WorldManager.Instance.State;
        _shown = w?.Battle;
        if (_shown is null)
        {
            _header.Text = "No battle. Send an army from the Map tab." + (w?.LastResult is { } r ? "\n" + MapPanel.ResultText(r) : "");
            _retreat.Disabled = true;
            return;
        }
        _retreat.Disabled = false;
        foreach (var side in new[] { BattleSide.Attacker, BattleSide.Defender })
        {
            var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _sides.AddChild(col);
            col.AddChild(new Label { Text = side == BattleSide.Attacker ? "Your army" : "Defenders", ThemeTypeVariation = "HeaderSmall" });
            foreach (var section in Sections.All)
            {
                var groups = _shown.GroupsOf(side).Where(g => g.Section == section).ToList();
                if (groups.Count == 0) continue;
                col.AddChild(new Label { Text = section, Modulate = new Color(1, 1, 1, 0.6f) });
                foreach (var g in groups)
                {
                    var name = new Label();
                    var hp = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14) };
                    hp.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = side == BattleSide.Attacker ? new Color(0.35f, 0.75f, 0.35f) : new Color(0.8f, 0.35f, 0.3f) });
                    col.AddChild(name);
                    col.AddChild(hp);
                    var cds = new List<ProgressBar>();
                    var row = new HBoxContainer();
                    foreach (var a in g.Unit.Actions)
                    {
                        row.AddChild(new Label { Text = a.Name, Modulate = new Color(1, 1, 1, 0.7f) });
                        var cd = new ProgressBar { MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(50, 6), SizeFlagsVertical = SizeFlags.ShrinkCenter, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                        row.AddChild(cd);
                        cds.Add(cd);
                    }
                    if (cds.Count > 0) col.AddChild(row);
                    _views.Add(new GroupView(g, name, hp, cds.ToArray()));
                }
            }
        }
        Update();
    }

    public override void _Process(double delta)
    {
        if (_shown is not null && IsVisibleInTree()) Update();
    }

    private void Update()
    {
        if (_shown is not { } b) return;
        _header.Text = $"Battle at {WorldManager.Instance.State?.BattleArea}: {b.NowSeconds:0.0}s / {b.TimeLimitMs / 1000}s.   " +
                       $"Your army {b.SideHp(BattleSide.Attacker):0}/{b.SideMaxHp(BattleSide.Attacker):0} hp, " +
                       $"defenders {b.SideHp(BattleSide.Defender):0}/{b.SideMaxHp(BattleSide.Defender):0} hp.";
        foreach (var v in _views)
        {
            var g = v.Group;
            v.Name.Text = $"{g.Unit.Name}  {DevFormat.Exact(g.Alive)}/{DevFormat.Exact(g.StartCount)}   {g.Hp:0}/{g.MaxHp:0} hp" + (g.Brace > 0 ? $"   braced -{g.Brace:P0}" : "");
            v.Name.Modulate = g.IsAlive ? Colors.White : new Color(1, 1, 1, 0.4f);
            v.Hp.Value = g.MaxHp > 0 ? g.Hp / g.MaxHp : 0;
            for (var i = 0; i < v.Cooldowns.Length; i++)
            {
                var cd = g.Unit.Actions[i].Cooldown * 1000;
                v.Cooldowns[i].Value = g.IsAlive ? 1 - System.Math.Clamp((g.NextFireMs[i] - b.NowMs) / cd, 0, 1) : 0;
            }
        }
    }

    private void Log(BattleAction a)
    {
        if (!IsVisibleInTree()) return;
        var who = a.Source.Side == BattleSide.Attacker ? "#9ecbff" : "#ff9e9e";
        var what = a.Action.Effect switch
        {
            "damage" => $"hits {a.Target?.Unit.Name} for {a.Amount:0.#}{(a.Braced ? " (braced, -10%)" : "")}",
            "reduce_next_incoming" => "braces",
            "heal" => $"heals {a.Target?.Unit.Name} for {a.Amount:0.#}",
            _ => a.Action.Effect,
        };
        _log.AppendText($"[color=#808080]{a.AtMs / 1000.0:0.0}s[/color] [color={who}]{DevFormat.Bb(a.Source.Unit.Name)} {DevFormat.Bb(a.Action.Name)}[/color] {DevFormat.Bb(what)}\n");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && IsVisibleInTree()) Rebuild();
    }
}
