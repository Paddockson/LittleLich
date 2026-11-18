using System;
using System.Collections.Generic;
using Godot;

namespace LittleLich.UI.Shell;

/// <summary>A page shown in the shell's content area. Pages read state, call manager methods and refresh on events.</summary>
public interface IPage
{
    /// <summary>The control placed in the content area.</summary>
    Control Root { get; }

    /// <summary>Subscribe to manager events (called once, when the page is first created).</summary>
    void Bind();

    /// <summary>Redraw from current state (called on show and on events).</summary>
    void Refresh();
}

/// <summary>
/// Swaps the child of the content area. Pages are created on first use and kept (hidden) afterwards, so their
/// event subscriptions are made once. Keys: "Crypt/Workshop", "Crypt/Research", "Crypt/UnitBuilder",
/// "Crypt/SpellBook", "Crypt/Lich", "Skill/&lt;id&gt;", "Map", "Settings", "Battle".
/// </summary>
public sealed class PageRouter
{
    private readonly Control _host;
    private readonly Dictionary<string, Func<string, IPage>> _factories = new();
    private readonly Dictionary<string, IPage> _pages = new();

    public PageRouter(Control host) => _host = host;

    public string? Current { get; private set; }
    public IPage? CurrentPage => Current is null ? null : _pages.GetValueOrDefault(Current);

    /// <summary>Raised after the page changes (key).</summary>
    public event Action<string>? Changed;

    /// <summary>Register a page for a key, or for every key with a prefix ("Skill/").</summary>
    public void Register(string keyOrPrefix, Func<string, IPage> factory) => _factories[keyOrPrefix] = factory;

    public bool Has(string key) => FactoryFor(key) is not null;

    public void Show(string key)
    {
        if (!_pages.TryGetValue(key, out var page))
        {
            var factory = FactoryFor(key) ?? throw new ArgumentException($"No page for '{key}'.");
            page = factory(key);
            page.Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            page.Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            page.Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _host.AddChild(page.Root);
            page.Bind();
            _pages[key] = page;
        }
        foreach (var (k, p) in _pages) p.Root.Visible = k == key;
        Current = key;
        page.Refresh();
        Changed?.Invoke(key);
    }

    /// <summary>Refresh the visible page (e.g. after a game load).</summary>
    public void RefreshCurrent() => CurrentPage?.Refresh();

    private Func<string, IPage>? FactoryFor(string key)
    {
        if (_factories.TryGetValue(key, out var f)) return f;
        foreach (var (prefix, factory) in _factories)
            if (prefix.EndsWith('/') && key.StartsWith(prefix, StringComparison.Ordinal)) return factory;
        return null;
    }
}
