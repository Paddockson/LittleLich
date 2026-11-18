using System;
using Godot;
using LittleLich.UI.Theme;

namespace LittleLich.UI.Components;

/// <summary>
/// A clickable, selectable card row (skills sidebar, Workshop buildings list, tome list, unit list).
/// Normal: BgCard with a soft border; selected: SelectedBg / SelectedBorder; hover brightens the border.
/// Put content in <see cref="Body"/>.
/// </summary>
public partial class NavItem : PanelContainer
{
    private bool _selected, _hover;
    private readonly int _padX, _padY;

    public NavItem(int padX = 9, int padY = 7, int gap = 4)
    {
        _padX = padX;
        _padY = padY;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        Body = Ui.Col(gap);
        Body.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(Body);
        MouseEntered += () => { _hover = true; Restyle(); };
        MouseExited += () => { _hover = false; Restyle(); };
        GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Pressed?.Invoke();
                AcceptEvent();
            }
        };
        Restyle();
    }

    public VBoxContainer Body { get; }

    public event Action? Pressed;

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Restyle(); }
    }

    /// <summary>Scripted click (navigation checks).</summary>
    public void Click() => Pressed?.Invoke();

    private void Restyle() => AddThemeStyleboxOverride("panel", _selected
        ? Tokens.Box(Tokens.SelectedBg, Tokens.SelectedBorder, Tokens.RadiusInset, _padX, _padY)
        : Tokens.Box(Tokens.BgCard, _hover ? Tokens.SelectedBorder : Tokens.BorderSkill, Tokens.RadiusInset, _padX, _padY));
}
