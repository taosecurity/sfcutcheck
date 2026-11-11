// Finds every condition inside a record, at any depth (quest stages and log entries, dialogue responses, packages,
// perk entries, recipe conditions, ...). A link that appears only inside conditions is a "check", not a use: the
// game tests the record but doesn't start, place or give it (step 3 finding, 2026-11-11: CUT_FFWaggonerFarm01 was
// "reached" only through a condition in a start-game quest).
//
// Mutagen's EnumerateFormLinks() doesn't say which field a link came from, so this walks the record's properties by
// reflection. Which properties can hold conditions is worked out once per type from the declared property types
// (Mutagen getter interfaces), so records that can't hold conditions (placed references, navmeshes) cost nothing.
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;

namespace SfCutCheck;

public static class Conditions
{
    static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropsByType = new();
    static readonly ConcurrentDictionary<Type, bool> MayHold = new();
    static readonly Assembly StarfieldAssembly = typeof(IConditionGetter).Assembly;

    /// <summary>How many times each FormKey is linked from inside the record's conditions.</summary>
    public static Dictionary<FormKey, int> LinkCounts(IMajorRecordGetter record)
    {
        var counts = new Dictionary<FormKey, int>();
        foreach (var c in Find(record, 0))
            foreach (var l in c.EnumerateFormLinks())
                if (!l.FormKey.IsNull) counts[l.FormKey] = counts.GetValueOrDefault(l.FormKey) + 1;
        return counts;
    }

    static IEnumerable<IConditionGetter> Find(object obj, int depth)
    {
        if (depth > 12) yield break;
        if (obj is IConditionGetter cond) { yield return cond; yield break; }
        foreach (var p in Props(obj.GetType()))
        {
            object? v;
            try { v = p.GetValue(obj); } catch { continue; }
            if (v == null) continue;
            if (v is IConditionGetter c) { yield return c; continue; }
            if (v is IEnumerable list and not string)
            {
                foreach (var item in list)
                    if (item != null)
                        foreach (var x in Find(item, depth + 1)) yield return x;
                continue;
            }
            foreach (var x in Find(v, depth + 1)) yield return x;
        }
    }

    /// <summary>The properties of a runtime type that can hold a condition somewhere below them.</summary>
    static PropertyInfo[] Props(Type t) => PropsByType.GetOrAdd(t, type =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && MayHoldConditions(p.PropertyType))
            .ToArray());

    /// <summary>Whether a declared property type can hold a condition: a condition itself, a list of something that
    /// can, or a Mutagen Starfield getter type with such a property.</summary>
    static readonly HashSet<Type> InProgress = new();
    static readonly object Gate = new();

    static bool MayHoldConditions(Type t)
    {
        lock (Gate) return Work(t);
    }

    // A type met again while it's still being worked out answers "no" for that path only. A "yes" is always final
    // and cached. A "no" is cached only when no other type was in progress, because a "no" reached through a cycle
    // may turn into "yes" once the outer type finishes (code review finding 6, 2026-11-11).
    static bool Work(Type t)
    {
        if (MayHold.TryGetValue(t, out var known)) return known;
        if (typeof(IConditionGetter).IsAssignableFrom(t)) return MayHold[t] = true;
        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t.IsValueType && !t.IsGenericType) return MayHold[t] = false;
        if (typeof(IFormLinkGetter).IsAssignableFrom(t)) return MayHold[t] = false;
        if (!(t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t)) && t.Assembly != StarfieldAssembly) return MayHold[t] = false;
        if (!InProgress.Add(t)) return false;
        bool result;
        try
        {
            if (t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t))
                result = t.GetGenericArguments().Any(Work);
            else
            {
                var props = t.IsInterface
                    ? t.GetInterfaces().Append(t).SelectMany(i => i.GetProperties())
                    : t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                result = props.Where(p => p.GetIndexParameters().Length == 0).Any(p => Work(p.PropertyType));
            }
        }
        finally { InProgress.Remove(t); }
        if (result || InProgress.Count == 0) MayHold[t] = result;
        return result;
    }
}
