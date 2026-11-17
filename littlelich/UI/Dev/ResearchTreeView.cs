using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LittleLich.Content.Definitions;
using LittleLich.Crypt.Research;

namespace LittleLich.UI.Dev;

/// <summary>
/// Draws one tome's nodes at their grid positions with links (dev UI: no art, no pan/zoom).
/// Done = green, researching = purple, available = gold, locked = grey; size by small/notable/keystone.
/// Click a node to select it.
/// </summary>
public partial class ResearchTreeView : Control
{
    private const float Cell = 78;
    private const float Margin = 40;

    private ResearchManager? _research;
    private ResearchTome? _tome;
    private readonly Dictionary<string, Vector2> _pos = new();
    private Vector2 _origin;

    public string? Selected { get; private set; }

    public event Action<string>? NodeSelected;

    public void Show(ResearchManager research, ResearchTome tome)
    {
        _research = research;
        _tome = tome;
        _pos.Clear();
        if (tome.Nodes.Count == 0) return;
        var minX = tome.Nodes.Min(n => n.Position[0]);
        var maxX = tome.Nodes.Max(n => n.Position[0]);
        var maxY = tome.Nodes.Max(n => n.Position[1]);
        var minY = tome.Nodes.Min(n => n.Position[1]);
        _origin = new Vector2((float)(-minX * Cell) + Margin + 40, (float)(-minY * Cell) + Margin);
        foreach (var n in tome.Nodes)
            _pos[n.Id] = _origin + new Vector2((float)(n.Position[0] * Cell), (float)(n.Position[1] * Cell));
        CustomMinimumSize = new Vector2((float)((maxX - minX) * Cell) + 2 * Margin + 80, (float)((maxY - minY) * Cell) + 2 * Margin + 20);
        QueueRedraw();
    }

    public void Select(string? nodeId)
    {
        Selected = nodeId;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_research is null || _tome is null) return;
        var font = ThemeDB.FallbackFont;
        foreach (var n in _tome.Nodes)
            foreach (var r in n.Requires)
                if (_pos.TryGetValue(r, out var from))
                {
                    var done = _tome.IsDone(r) && _tome.IsDone(n.Id);
                    var lit = _tome.IsDone(r);
                    DrawLine(from, _pos[n.Id], done ? new Color(0.4f, 0.85f, 0.4f) : lit ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.4f, 0.4f, 0.4f), done ? 3 : 2);
                }
        foreach (var n in _tome.Nodes)
        {
            var p = _pos[n.Id];
            var radius = n.Size switch { "keystone" => 26f, "notable" => 19f, _ => 14f };
            var color = _research.State(n.Id) switch
            {
                NodeState.Done => new Color(0.35f, 0.8f, 0.35f),
                NodeState.Researching => new Color(0.7f, 0.4f, 0.95f),
                NodeState.Available => new Color(0.95f, 0.8f, 0.3f),
                _ => new Color(0.35f, 0.35f, 0.35f),
            };
            DrawCircle(p, radius, color);
            if (n.Id == Selected) DrawArc(p, radius + 4, 0, Mathf.Tau, 32, Colors.White, 2);
            var size = font.GetStringSize(n.Name, HorizontalAlignment.Center, -1, 12);
            DrawString(font, p + new Vector2(-size.X / 2, radius + 14), n.Name, HorizontalAlignment.Left, -1, 12,
                _research.State(n.Id) == NodeState.Locked ? new Color(1, 1, 1, 0.5f) : Colors.White);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb || _tome is null) return;
        var hit = _pos.Where(kv => kv.Value.DistanceTo(mb.Position) <= 30).OrderBy(kv => kv.Value.DistanceTo(mb.Position)).Select(kv => kv.Key).FirstOrDefault();
        if (hit is null) return;
        Select(hit);
        NodeSelected?.Invoke(hit);
        AcceptEvent();
    }
}
