// The reference index: every record (FormKey) gets a small integer id; every link from one record to another is
// kept as a pair of ids plus a kind. Records themselves are not kept, so memory grows with the link count only.
//
// Two things Mutagen does that the index undoes (found in the step 1 spike, 2026-11-11):
//  - A record that holds other records (a worldspace its cells, a cell its placed references and navmeshes, a quest
//    its dialogue topics, a topic its responses) reports all of their links as its own. The index keeps a container's
//    own links only (its links minus those of the records inside it) and adds one "contains" link to each record
//    directly inside it. A container link that a record inside it also has is dropped; reachability doesn't change
//    (the inner record is reached through "contains"), only which record is named as evidence.
//  - Surface trees repeat the same pattern many times. Each record's links are de-duplicated.
// Links that name an owner (Rules.OwnersOf) are also added in the other direction, as "owns".
// Overrides: links from every version of a record are kept (union).
using BuildKit;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Records;

namespace SfCutCheck;

/// <summary>Ref: a normal link. Contains: a record stored inside another. Owns: the reverse of an owner field.
/// Checks: the link appears only in the record's conditions; the walk doesn't follow it.</summary>
public enum LinkKind : byte { Ref = 0, Contains = 1, Owns = 2, Checks = 3 }

public sealed class RefIndex
{
    readonly Dictionary<FormKey, int> ids = new();
    readonly List<FormKey> keys = new();
    readonly List<byte> typeOf = new();
    readonly List<string> typeNames = new();
    readonly Dictionary<string, byte> typeIds = new();
    public readonly List<int> From = new();
    public readonly List<int> To = new();
    public readonly List<LinkKind> Kind = new();
    public readonly List<(FormKey Key, string Type, string Error)> Failed = new();
    public readonly HashSet<int> FailedIds = new();
    public readonly Dictionary<string, long> LinksByType = new();
    public readonly Dictionary<int, string> EditorIds = new();
    public readonly Dictionary<int, string> Names = new();
    public int NameFailures;
    public string? NameOf(int id) => Names.GetValueOrDefault(id);
    public readonly HashSet<int> Defined = new();
    /// <summary>Records that are roots by themselves (worldspaces and interior cells are added by AddWorldRoots).</summary>
    public readonly List<(int Id, RootRule Rule)> Roots = new();
    public readonly List<string> PluginsRead = new();
    public readonly List<string> Skipped = new();

    public int NodeCount => keys.Count;
    public int EdgeCount => From.Count;
    public long ContainsCount { get; private set; }
    public long OwnsCount { get; private set; }
    public long VersionsRead { get; private set; }
    public long DanglingCount { get; private set; }

    public FormKey KeyOf(int id) => keys[id];
    public string TypeOf(int id) => typeOf[id] == 255 ? "?" : typeNames[typeOf[id]];
    public string? EditorIdOf(int id) => EditorIds.GetValueOrDefault(id);
    public bool TryGetId(FormKey k, out int id) => ids.TryGetValue(k, out id);

    /// <summary>"Weapon 0012AB:Starfield.esm EditorID" for messages.</summary>
    public string Describe(int id) => $"{TypeOf(id)} {KeyOf(id)}{(EditorIdOf(id) is { } e ? " " + e : "")}";

    int Id(FormKey k)
    {
        if (ids.TryGetValue(k, out var id)) return id;
        id = keys.Count;
        ids[k] = id;
        keys.Add(k);
        typeOf.Add(255);
        return id;
    }

    byte TypeId(string name)
    {
        if (typeIds.TryGetValue(name, out var t)) return t;
        if (typeNames.Count >= 255) throw new InvalidOperationException("more than 254 record types");
        t = (byte)typeNames.Count;
        typeNames.Add(name);
        typeIds[name] = t;
        return t;
    }

    public static string TypeName(IMajorRecordGetter r) => r.GetType().Name.Replace("BinaryOverlay", "");

    static bool IsPlaced(string type) => type.StartsWith("Placed", StringComparison.Ordinal);

    public long CheckLinksFound { get; private set; }

    /// <summary>A record's links as ids, without repeats and without links to itself. Null if Mutagen failed. With
    /// checks != null, links that occur only inside the record's conditions go there instead.</summary>
    HashSet<int>? LinksOf(IMajorRecordGetter r, string type, int self, bool record, HashSet<int>? checks = null)
    {
        var total = new Dictionary<FormKey, int>();
        try
        {
            foreach (var l in r.EnumerateFormLinks())
                if (!l.FormKey.IsNull) total[l.FormKey] = total.GetValueOrDefault(l.FormKey) + 1;
        }
        catch (Exception e)
        {
            if (record && FailedIds.Add(self)) Failed.Add((r.FormKey, type, $"{e.GetType().Name}: {e.Message}"));
            return null;
        }
        Dictionary<FormKey, int>? inConditions = null;
        if (checks != null)
        {
            try { inConditions = Conditions.LinkCounts(r); }
            catch (Exception e) { if (record) Failed.Add((r.FormKey, type, $"conditions: {e.GetType().Name}: {e.Message}")); }
        }
        var set = new HashSet<int>();
        foreach (var (k, n) in total)
        {
            var id = Id(k);
            if (id == self) continue;
            // Every occurrence is inside a condition: the record only tests it.
            if (inConditions != null && inConditions.GetValueOrDefault(k) >= n) { checks!.Add(id); if (record) CheckLinksFound++; }
            else set.Add(id);
        }
        return set;
    }

    void Add(int from, int to, LinkKind kind, string type)
    {
        From.Add(from);
        To.Add(to);
        Kind.Add(kind);
        if (kind == LinkKind.Contains) ContainsCount++;
        else if (kind == LinkKind.Owns) OwnsCount++;
        else LinksByType[type] = LinksByType.GetValueOrDefault(type) + 1;
    }

    public static RefIndex Build(string dataDir, IReadOnlyList<string> plugins, ISet<string> skipTypes, Action<string> progress,
        IEnumerable<string>? extraDirs = null)
    {
        var ix = new RefIndex();
        using var game = new GameData(dataDir, extraDirs);
        foreach (var p in plugins)
        {
            var path = game.Locate(p);
            Log.Write($"plugin {p}: {(path == null ? "not found in the Data folder" : Log.Describe(path))}");
            var mod = game.TryOpen(p);
            if (mod == null)
            {
                ix.Skipped.Add(p);
                progress($"{p}: not found (or one of its masters is missing), skipped");
                Log.Write($"skipped {p}: {(path == null ? "the file isn't there" : "one of its masters isn't there: " + string.Join(", ", GameData.HeaderMasters(path).Where(m => game.Locate(m) == null)))}");
                continue;
            }
            ix.PluginsRead.Add(p);
            long n = 0, before = ix.EdgeCount;
            foreach (var r in mod.EnumerateMajorRecords())
            {
                var type = TypeName(r);
                if (skipTypes.Contains(type)) continue;
                n++;
                var self = ix.Id(r.FormKey);
                ix.Defined.Add(self);
                ix.typeOf[self] = ix.TypeId(type);
                if (!IsPlaced(type) && r.EditorID is { Length: > 0 } edid) ix.EditorIds[self] = edid;
                // In-game names (from the string tables) for content records only, for the reports.
                if (!IsPlaced(type) && !Rules.Structural.Contains(type) && r is INamedGetter named)
                {
                    try { if (named.Name is { Length: > 0 } nm) ix.Names[self] = nm; }
                    catch (Exception e) { ix.NameFailures++; if (ix.NameFailures <= 5) Log.Write($"name of {r.FormKey} ({type}) unreadable: {e.GetType().Name}: {e.Message}"); }
                }
                if (Rules.RootOf(r) is { } rule) ix.Roots.Add((self, rule));
                foreach (var owner in Rules.OwnersOf(r))
                    if (!owner.IsNull) ix.Add(ix.Id(owner), self, LinkKind.Owns, type);

                // If Mutagen fails on the record's links (often because of one navmesh inside a cell), keep going with
                // no links of its own, so the records inside it still get their "contains" links.
                var checks = new HashSet<int>();
                var own = ix.LinksOf(r, type, self, record: true, checks) ?? new HashSet<int>();

                if (r is IMajorRecordGetterEnumerable holder)
                {
                    // Everything stored inside this record, at any depth.
                    var inside = holder.EnumerateMajorRecords().Where(c => !ReferenceEquals(c, r) && c.FormKey != r.FormKey).ToList();
                    if (inside.Count > 0)
                    {
                        var insideIds = new HashSet<int>();
                        var deeper = new HashSet<int>();   // records inside a record that is itself inside this one
                        foreach (var c in inside)
                        {
                            var cid = ix.Id(c.FormKey);
                            insideIds.Add(cid);
                            own.Remove(cid);
                            // Drop from the container only what the inner record really uses. A link the inner
                            // record merely checks stays the container's own (step 3 fix, 2026-11-11: DialogueFCNeon's
                            // script property for FFNeonZ08 was lost because its responses check FFNeonZ08).
                            var childChecks = new HashSet<int>();
                            var cl = ix.LinksOf(c, TypeName(c), cid, record: false, childChecks);
                            if (cl != null) { own.ExceptWith(cl); checks.ExceptWith(cl); checks.ExceptWith(childChecks); }
                            if (c is IMajorRecordGetterEnumerable inner)
                                foreach (var g in inner.EnumerateMajorRecords())
                                    if (g.FormKey != c.FormKey) deeper.Add(ix.Id(g.FormKey));
                        }
                        foreach (var cid in insideIds)
                            if (!deeper.Contains(cid)) ix.Add(self, cid, LinkKind.Contains, type);
                    }
                }
                foreach (var to in own) ix.Add(self, to, LinkKind.Ref, type);
                foreach (var to in checks) if (!own.Contains(to)) ix.Add(self, to, LinkKind.Checks, type);
            }
            ix.VersionsRead += n;
            progress($"{p}: {n:N0} records, {ix.EdgeCount - before:N0} links");
            Log.Write($"read {p}: {n:N0} records, {ix.EdgeCount - before:N0} links; {ix.Failed.Count} records Mutagen failed on so far");
        }
        foreach (var k in Rules.PlayerForms)
            if (ix.ids.TryGetValue(k, out var pid) && ix.Defined.Contains(pid)) ix.Roots.Add((pid, Rules.Player));
        ix.AddWorldRoots();
        // A record overridden by the DLC or a creation meets its root rule once per version: count it once (code
        // review finding 4, 2026-11-11). Overrides are still combined: a later version that drops the rule (for
        // example clears a quest's start-game flag) doesn't remove the root. README "Limits" says so.
        var distinct = ix.Roots.Distinct().ToList();
        ix.Roots.Clear();
        ix.Roots.AddRange(distinct);
        foreach (var f in ix.Failed) Log.Write($"Mutagen failed on {f.Type} {f.Key}: {f.Error} (its own links are left out; see MUTAGEN-GAPS.md)");
        foreach (var g in ix.Roots.GroupBy(r => r.Rule)) Log.Write($"root rule {g.Key.Name}: {g.Count():N0} roots");
        Log.Write($"names read: {ix.Names.Count:N0}; unreadable: {ix.NameFailures}");
        ix.DanglingCount = ix.To.LongCount(t => !ix.Defined.Contains(t));
        return ix;
    }

    /// <summary>Worldspaces, and interior cells (cells no worldspace holds), are roots: "world" normally, "dev-world"
    /// when their EditorID marks them as test, debug or cut content.</summary>
    void AddWorldRoots()
    {
        var inWorld = new HashSet<int>();
        for (var i = 0; i < From.Count; i++)
            if (Kind[i] == LinkKind.Contains && TypeOf(From[i]) == "Worldspace") inWorld.Add(To[i]);
        foreach (var id in Defined)
        {
            var t = TypeOf(id);
            if (t == "Worldspace" || (t == "Cell" && !inWorld.Contains(id)))
                Roots.Add((id, Rules.IsDevName(EditorIdOf(id)) ? Rules.DevWorld : Rules.World));
        }
    }

    public IEnumerable<(string Type, int Count)> CountByType() =>
        typeOf.Where(t => t != 255).GroupBy(t => t).Select(g => (typeNames[g.Key], g.Count())).OrderByDescending(x => x.Item2);
}
