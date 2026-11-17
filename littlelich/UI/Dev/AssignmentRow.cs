using System;
using Godot;
using LittleLich.Managers;

namespace LittleLich.UI.Dev;

/// <summary>
/// Generic necromancer assignment control: name, assigned count (short, exact in tooltip), optional rate,
/// and -All, -, +, +10, +100, +1K, +Max buttons. "+" buttons are disabled when no necromancer is free.
///
/// It only calls the delegates it is given, so phase 4 can pass e.g. MiningManager.AddNecromancer.
/// By default it calls DevAssign (through the building or skill that owns the row, so slot limits and locks apply).
/// </summary>
public partial class AssignmentRow : HBoxContainer
{
    private readonly Func<long> _current;
    private readonly Func<long, long> _add;
    private readonly Func<long, long> _remove;
    private readonly Func<string>? _rate;

    private readonly Label _count;
    private readonly Label? _rateLabel;
    private readonly Button[] _plus;
    private readonly Button[] _minus;

    public AssignmentRow(string ownerId, string activityId, string? label = null, Func<string>? rate = null)
        : this(label ?? $"{ownerId} / {activityId}",
               () => LichManager.Instance.GetAssigned(ownerId, activityId),
               n => DevAssign.Add(ownerId, activityId, n),
               n => DevAssign.Remove(ownerId, activityId, n),
               rate)
    { }

    public AssignmentRow(string label, Func<long> current, Func<long, long> add, Func<long, long> remove, Func<string>? rate = null)
    {
        _current = current;
        _add = add;
        _remove = remove;
        _rate = rate;

        AddChild(new Label
        {
            Text = label,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            TooltipText = label,
            MouseFilter = MouseFilterEnum.Pass,
            CustomMinimumSize = new Vector2(140, 0),
        });
        _count = DevFormat.CountLabel(0);
        _count.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_count);
        if (rate is not null)
        {
            _rateLabel = new Label { CustomMinimumSize = new Vector2(110, 0), HorizontalAlignment = HorizontalAlignment.Right };
            AddChild(_rateLabel);
        }

        _minus = new[]
        {
            Btn("-All", () => _remove(long.MaxValue)),
            Btn("-", () => _remove(1)),
        };
        _plus = new[]
        {
            Btn("+", () => _add(1)),
            Btn("+10", () => _add(10)),
            Btn("+100", () => _add(100)),
            Btn("+1K", () => _add(1_000)),
            Btn("+Max", () => _add(long.MaxValue)),
        };
        Refresh();
    }

    private Button Btn(string text, Action press)
    {
        var b = new Button { Text = text };
        // Commands change state through managers; managers raise events, and the owner panel calls Refresh.
        b.Pressed += press;
        AddChild(b);
        return b;
    }

    /// <summary>Locked rows (level too low) cannot take necromancers but can still release them.</summary>
    public bool Locked { get; set; }

    public void Refresh()
    {
        var n = _current();
        DevFormat.SetCount(_count, n);
        if (_rateLabel is not null) _rateLabel.Text = _rate!();
        var free = LichManager.Instance.State.Pool.Free;
        foreach (var b in _plus) b.Disabled = free <= 0 || Locked;
        foreach (var b in _minus) b.Disabled = n <= 0;
    }
}
