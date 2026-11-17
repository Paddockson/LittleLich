using System;
using System.Collections.Generic;
using Godot;

namespace LittleLich.UI.Dev;

/// <summary>
/// A tab in the dev UI's main panel. Panels are Controls that read state, call manager methods and
/// refresh on manager signals (never every frame). They never change state directly.
/// </summary>
public interface IDevPanel
{
    string Title { get; }

    /// <summary>The control placed in the tab.</summary>
    Control Root { get; }

    /// <summary>Subscribe to manager events. Called once after the panel is in the tree.</summary>
    void Bind();

    /// <summary>Redraw from current state.</summary>
    void Refresh();
}

/// <summary>
/// The panel registry. A phase adds its panel with one class and one line here, in tab order.
/// </summary>
public static class DevPanelRegistry
{
    public static IEnumerable<Func<IDevPanel>> Panels => new Func<IDevPanel>[]
    {
        () => new LichPanel(),   // phase 2
        () => new SavePanel(),   // phase 1
        // Phase 5: () => new WorkshopPanel(),
        // Phase 6: () => new InventoryPanel(),
        // Phase 7: () => new ResearchPanel(),
        // Phase 8: () => new WorldMapPanel(), () => new BattlePanel(),
        // Phase 9: () => new UnitsPanel(),
        // Phase 10-11: () => new ContentBrowserPanel(),
    };
}
