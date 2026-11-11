// The saved result of a full scan of the official files, so `scan --plugin` doesn't have to read the whole game every
// time (user's test, 2026-11-11: "I asked to check one esm and it's doing the whole set again").
//
// What's saved: every official record the walk did NOT reach strongly, with its tier (Weak, Dev or None). Every other
// record an official plugin defines counts as Strong. That's about 180,000 lines instead of 7.6 million.
//
// When it's used: the key is each official plugin's name, size and modified time, plus the tool's rules version. A
// Starfield patch, or adding or removing a Creation, changes the key, and the scan runs again by itself.
//
// Why the answers match a full read: an official record can't link to a mod's records, so a mod's record is reached
// only through the mod itself (its own roots, or its versions of official records, which start from the official
// record's saved tier) or through its non-official masters, which are read in full. Tests compare both ways.
using System.Security.Cryptography;
using System.Text;
using Mutagen.Bethesda.Plugins;

namespace SfCutCheck;

public sealed class OfficialCache
{
    /// <summary>Change this whenever Rules.cs or the walk changes what counts as reached, so old caches are rebuilt.</summary>
    public const string RulesVersion = "rules-2026-11-11b";

    public string Key { get; }
    public string MadeUtc { get; }
    readonly Dictionary<FormKey, Tier> notStrong;
    public HashSet<string> Plugins { get; }

    OfficialCache(string key, string madeUtc, Dictionary<FormKey, Tier> notStrong, IEnumerable<string> plugins)
    {
        Key = key;
        MadeUtc = madeUtc;
        this.notStrong = notStrong;
        Plugins = new HashSet<string>(plugins, StringComparer.OrdinalIgnoreCase);
    }

    public int NotStrongCount => notStrong.Count;

    /// <summary>The tier the full official scan gave this record (Strong unless the cache lists it).</summary>
    public Tier TierOf(FormKey k) => notStrong.TryGetValue(k, out var t) ? t : Tier.Strong;

    public bool IsOfficial(FormKey k) => Plugins.Contains(k.ModKey.FileName);

    public static string DefaultDir => Environment.GetEnvironmentVariable("SFCUTCHECK_CACHE_DIR") is { Length: > 0 } d
        ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sfcutcheck");

    /// <summary>The key for the official plugins as they are on disk now.</summary>
    public static string KeyFor(string dataDir, IReadOnlyList<string> officialPlugins)
    {
        var sb = new StringBuilder(RulesVersion).Append('\n');
        foreach (var p in officialPlugins)
        {
            var fi = new FileInfo(Path.Combine(dataDir, p));
            sb.Append(p.ToLowerInvariant()).Append('|').Append(fi.Exists ? fi.Length : -1).Append('|')
              .Append(fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }

    static string PathFor(string dir, string key) => Path.Combine(dir, $"official-{key}.txt");

    /// <summary>The saved scan for these plugins as they are now, or null (with the reason in the log).</summary>
    public static OfficialCache? TryLoad(string dir, string dataDir, IReadOnlyList<string> officialPlugins)
    {
        var key = KeyFor(dataDir, officialPlugins);
        var path = PathFor(dir, key);
        if (!File.Exists(path))
        {
            Log.Write($"no saved official scan for the current game files (looked for {path})");
            return null;
        }
        try
        {
            var notStrong = new Dictionary<FormKey, Tier>();
            string made = "?";
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("# made ")) { made = line[7..]; continue; }
                if (line.Length == 0 || line[0] == '#') continue;
                var tab = line.IndexOf('\t');
                if (tab < 0 || !FormKey.TryFactory(line.Substring(0, tab), out var fk)) throw new InvalidDataException($"bad line: {line}");
                notStrong[fk] = line.Substring(tab + 1) switch { "Weak" => Tier.Weak, "Dev" => Tier.Dev, _ => Tier.None };
            }
            Log.Write($"loaded the saved official scan {Log.Describe(path)}: {notStrong.Count:N0} records not strongly reached");
            return new OfficialCache(key, made, notStrong, officialPlugins);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Write($"the saved official scan {path} can't be read ({e.Message}); scanning again");
            return null;
        }
    }

    /// <summary>Makes the cache from a walk over exactly the official plugins.</summary>
    public static OfficialCache FromWalk(Walk w, string dataDir, IReadOnlyList<string> officialPlugins)
    {
        var ix = w.Index;
        var notStrong = new Dictionary<FormKey, Tier>();
        foreach (var id in ix.Defined)
            if (w.TierOf[id] != Tier.Strong) notStrong[ix.KeyOf(id)] = w.TierOf[id];
        return new OfficialCache(KeyFor(dataDir, officialPlugins), DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC", notStrong, officialPlugins);
    }

    /// <summary>Writes the cache; a failure is logged, not fatal (the next run just scans again).</summary>
    public void Save(string dir, IReadOnlyList<string> officialPlugins)
    {
        var path = PathFor(dir, Key);
        try
        {
            Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            using (var wtr = new StreamWriter(tmp, false, new UTF8Encoding(false)))
            {
                wtr.WriteLine("# sfcutcheck: saved scan of the official files. Safe to delete; it's rebuilt when needed.");
                wtr.WriteLine($"# made {MadeUtc}");
                wtr.WriteLine($"# {RulesVersion}; plugins: {string.Join(", ", officialPlugins)}");
                wtr.WriteLine("# records not strongly reached (every other official record is): FormKey<TAB>tier");
                foreach (var (k, t) in notStrong.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal))
                    wtr.WriteLine($"{k}\t{(t == Tier.None ? "None" : t.ToString())}");
            }
            File.Move(tmp, path, overwrite: true);
            Log.Write($"saved the official scan to {Log.Describe(path)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"couldn't save the official scan to {path}: {e.Message} (the next --plugin run scans the game again)");
        }
    }

    /// <summary>Adds the official records a small index refers to as roots at their saved tiers.</summary>
    public int SeedRoots(RefIndex ix)
    {
        var n = 0;
        for (var id = 0; id < ix.NodeCount; id++)
        {
            var k = ix.KeyOf(id);
            if (!IsOfficial(k)) continue;
            var rule = TierOf(k) switch
            {
                Tier.Strong => Rules.OfficialStrong,
                Tier.Weak => Rules.OfficialWeak,
                Tier.Dev => Rules.OfficialDev,
                _ => null,
            };
            if (rule == null) continue;
            ix.Roots.Add((id, rule));
            n++;
        }
        return n;
    }
}
