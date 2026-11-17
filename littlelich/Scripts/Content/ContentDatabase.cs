using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleLich.Content;

/// <summary>
/// Every content definition, loaded once at startup. Read-only after load.
/// Ids are unique per family (the abstract class directly under <see cref="Definition"/>).
/// </summary>
public sealed class ContentDatabase
{
    // family type -> id -> definition (insertion order kept for stable UI lists)
    private readonly Dictionary<Type, Dictionary<string, Definition>> _byFamily = new();
    private readonly Dictionary<Type, List<Definition>> _ordered = new();

    internal ContentDatabase() { }

    public static ContentDatabase Empty { get; } = new();

    public int Count => _ordered.Values.Sum(l => l.Count);

    /// <summary>Gets a definition by id. Throws <see cref="KeyNotFoundException"/> with a clear message if missing.</summary>
    public T Get<T>(string id) where T : Definition =>
        TryGet<T>(id, out var def)
            ? def
            : throw new KeyNotFoundException($"No {typeof(T).Name} with id '{id}'.");

    public bool TryGet<T>(string id, out T def) where T : Definition
    {
        if (_byFamily.TryGetValue(FamilyOf(typeof(T)), out var map) &&
            map.TryGetValue(id, out var d) && d is T typed)
        {
            def = typed;
            return true;
        }
        def = null!;
        return false;
    }

    public bool Contains<T>(string id) where T : Definition => TryGet<T>(id, out _);

    /// <summary>All definitions assignable to T, in load order.</summary>
    public IEnumerable<T> All<T>() where T : Definition =>
        _ordered.TryGetValue(FamilyOf(typeof(T)), out var list) ? list.OfType<T>() : Enumerable.Empty<T>();

    /// <summary>Adds a definition. Returns false if the id is already used in its family.</summary>
    internal bool TryAdd(Definition def)
    {
        var family = FamilyOf(def.GetType());
        if (!_byFamily.TryGetValue(family, out var map))
        {
            map = new Dictionary<string, Definition>(StringComparer.Ordinal);
            _byFamily[family] = map;
            _ordered[family] = new List<Definition>();
        }
        if (!map.TryAdd(def.Id, def)) return false;
        _ordered[family].Add(def);
        return true;
    }

    internal IEnumerable<Definition> Everything() => _ordered.Values.SelectMany(l => l);

    internal Definition? Find(Type family, string id) =>
        _byFamily.TryGetValue(family, out var map) && map.TryGetValue(id, out var d) ? d : null;

    /// <summary>The class directly below <see cref="Definition"/> in T's hierarchy.</summary>
    public static Type FamilyOf(Type t)
    {
        if (!typeof(Definition).IsAssignableFrom(t) || t == typeof(Definition))
            throw new ArgumentException($"{t.Name} is not a Definition subclass.");
        while (t.BaseType != typeof(Definition)) t = t.BaseType!;
        return t;
    }
}
