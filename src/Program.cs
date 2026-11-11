// sfcutcheck: read-only finder of Starfield records the game can never reach.
//   sfcutcheck scan  [common options] [--plugin <file.esm>] [--types Weapon,Armor] [--all-types] [--out <folder>]
//   sfcutcheck why   <FormID:Plugin.esm | EditorID> [more...] [common options]
//   sfcutcheck find  <text> [more...] [--word] [--types ...] [--max N] [common options]
//   sfcutcheck spike [common options] [--skip Type,Type]     (index size, time and memory)
//   common options: --data <Data folder>  --plugins a.esm,b.esm  --verbose  --log <file>
// Exit codes: 0 done; 1 why found nothing for a record, or --plugin found records nothing reaches; 2 can't run (bad option, no Data folder, can't write).
using System.Diagnostics;
using Mutagen.Bethesda.Plugins;
using SfCutCheck;

const string Version = "0.1.6";
const string DefaultData = @"C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data";
const string Usage =
    "usage:\n" +
    "  sfcutcheck scan [--data <Data folder>] [--plugins a.esm,b.esm | --plugin <file.esm> [--masters-dir <folder>]...] [--types Weapon,Armor] [--all-types] [--out <folder>] [--verbose] [--log <file>]\n" +
    "  sfcutcheck why <FormID:Plugin.esm | EditorID> [more records...] [--data <Data folder>] [--plugins a.esm,b.esm] [--verbose] [--log <file>]\n" +
    "  sfcutcheck find <text> [more...] [--word] [--types Weapon,Armor] [--max N] [--data ...] [--plugins ...] [--verbose] [--log <file>]\n" +
    "  sfcutcheck spike [--data <Data folder>] [--plugins a.esm,b.esm] [--skip Type,Type]\n" +
    "  sfcutcheck --version";

// Attribution (user, 2026-11-11): one line on stderr at the start of every run, so stdout (the summary and RESULT)
// stays the same for scripts; --version prints it with the credits.
// Plain ASCII separators: older consoles (Windows PowerShell 5, cmd) can garble "·".
var byline = $"sfcutcheck {Version} by {About.Author} - {About.License} - {About.Url}";
if (args.Length == 1 && args[0] is "--version" or "-v")
{
    Console.WriteLine(byline);
    Console.WriteLine($"Reads plugins with {About.MutagenCredit}.");
    Console.WriteLine("Free software: you can share and change it under the GNU GPL version 3. It comes with no warranty.");
    return 0;
}
if (args.Length == 0 || args[0] is not ("scan" or "why" or "find" or "spike")) Console.Error.WriteLine(byline);
if (args.Length == 0 || args[0] is not ("scan" or "why" or "find" or "spike")) { Console.Error.WriteLine(Usage); return 2; }
var command = args[0];
var data = DefaultData;
List<string>? plugins = null;
var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
HashSet<string>? onlyTypes = null;
var allTypes = false;
var word = false;
var max = 40;
var outDir = Path.Combine("out", "scan");
var targets = new List<string>();
var sw = Stopwatch.StartNew();
var total = Stopwatch.StartNew();    // the whole run, for "time:" and the report
string? pluginPath = null;          // --plugin: report only the records this plugin adds
string? pluginName = null;
var extraDirs = new List<string>();   // --masters-dir folders, then --plugin's own folder
var dataGiven = false;
var noCache = false;                 // --no-cache: --plugin reads the official files again instead of the saved scan
OfficialCache? officialCache = null;
try { return Run(); }
catch (Exception e)
{
    Log.Write($"stopped: {e}");
    Console.Error.WriteLine($"sfcutcheck stopped: {e.GetType().Name}: {e.Message}");
    Console.Error.WriteLine("Run again with --log <file> and send that file with a bug report; it has the full error.");
    return 2;
}
finally { Log.Close(); }

int Run()
{
for (var i = 1; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    switch (args[i])
    {
        case "--verbose": Log.ToStderr(); break;
        case "--log":
            if (Log.ToFile(Next()) is { } logError) { Console.Error.WriteLine(logError); return 2; }
            break;
        case "--data": data = Next(); dataGiven = true; break;
        case "--masters-dir":
            var md = Next();
            if (!Directory.Exists(md)) { Console.Error.WriteLine($"no folder {Path.GetFullPath(md)} for --masters-dir. Give it a folder that holds the plugin's masters (for example an MO2 mod folder)."); return 2; }
            extraDirs.Add(Path.GetFullPath(md));
            break;
        case "--plugins": plugins = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); break;
        case "--skip": foreach (var s in Next().Split(',')) skip.Add(s.Trim()); break;
        case "--types": onlyTypes = new(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase); break;
        case "--all-types": allTypes = true; break;
        case "--plugin": pluginPath = Next(); break;
        case "--no-cache": noCache = true; break;
        case "--word": word = true; break;
        case "--max":
            if (!int.TryParse(Next(), out max) || max < 1) { Console.Error.WriteLine("--max needs a whole number, 1 or more (for example --max 40)"); return 2; }
            break;
        case "--out": outDir = Next(); break;
        default:
            if (command is "why" or "find" && !args[i].StartsWith("--")) { targets.Add(args[i]); break; }
            Console.Error.WriteLine($"unknown option {args[i]}\n{Usage}");
            return 2;
    }
}
if (command == "why" && targets.Count == 0) { Console.Error.WriteLine($"why needs a record: a FormID:Plugin.esm or an EditorID\n{Usage}"); return 2; }
if (command == "find" && targets.Count == 0) { Console.Error.WriteLine($"find needs some text to look for in EditorIDs\n{Usage}"); return 2; }

Console.Error.WriteLine(byline);
Log.Write($"sfcutcheck {Version}, command {command}, arguments: {string.Join(" ", args)}");
var dataWhy = dataGiven ? "from --data" : "Steam's usual folder, no --data given";
if (!dataGiven && !File.Exists(Path.Combine(data, "Starfield.esm")))
{
    // No --data and no Steam install in the usual place. First ask Mutagen's install finder (Steam's library list,
    // the Xbox app's records), then try Game Pass's usual folder on each drive (code review finding 9, 2026-11-11).
    string? found = null;
    try
    {
        if (Mutagen.Bethesda.Installs.GameLocations.TryGetDataFolder(Mutagen.Bethesda.GameRelease.Starfield, out var dp)
            && File.Exists(Path.Combine(dp.Path, "Starfield.esm")))
        { found = dp.Path; dataWhy = "found by Mutagen's install finder (no --data given, not in Steam's usual folder)"; }
    }
    catch (Exception e) { Log.Write($"Mutagen's install finder failed: {e.GetType().Name}: {e.Message}"); }
    found ??= DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed)
        .Select(d => Path.Combine(d.RootDirectory.FullName, "XboxGames", "Starfield", "Content", "Data"))
        .FirstOrDefault(p => File.Exists(Path.Combine(p, "Starfield.esm")));
    if (found != null)
    {
        if (!dataWhy.StartsWith("found")) dataWhy = "Game Pass's usual folder (no --data given, no Steam install found)";
        data = found;
    }
}
Log.Write($"Data folder: {Path.GetFullPath(data)} ({dataWhy})");
if (extraDirs.Count > 0) Log.Write($"--masters-dir folders, searched before the Data folder: {string.Join(", ", extraDirs)}");
if (!File.Exists(Path.Combine(data, "Starfield.esm")))
{
    Console.Error.WriteLine($"no Starfield.esm in {Path.GetFullPath(data)}. Give --data the game's Data folder: for Steam usually " +
        @"""C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data"", for Game Pass ""<drive>:\XboxGames\Starfield\Content\Data"".");
    Log.Write("stopped: no Starfield.esm in the Data folder");
    return 2;
}
Log.Write($"Starfield.esm: {Log.Describe(Path.Combine(data, "Starfield.esm"))}");

if (command == "scan")
{
    // Fail early, before the 2-minute read, if the reports can't be written.
    try { Directory.CreateDirectory(outDir); var probe = Path.Combine(outDir, ".sfcutcheck-write-test"); File.WriteAllText(probe, ""); File.Delete(probe); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        Console.Error.WriteLine($"can't write the reports to {Path.GetFullPath(outDir)}: {e.Message} Give --out a folder you can write to, such as one in your Documents folder.");
        Log.Write($"stopped: --out {outDir} isn't writable: {e.Message}");
        return 2;
    }
}

if (pluginPath != null)
{
    // A plugin name looks in the Data folder; a path can be anywhere (a build folder), and its folder is searched
    // for masters first.
    var full = Path.IsPathRooted(pluginPath) || pluginPath.Contains(Path.DirectorySeparatorChar) || pluginPath.Contains('/')
        ? Path.GetFullPath(pluginPath) : Path.Combine(data, pluginPath);
    if (!File.Exists(full))
    {
        Console.Error.WriteLine($"no plugin {full}. Give --plugin the plugin's file name (if it's in the Data folder) or its full path.");
        return 2;
    }
    pluginName = Path.GetFileName(full);
    // The plugin's own folder is searched first, so the file the user named is the one read, even when a
    // --masters-dir folder holds another copy (code review finding 2, 2026-11-11).
    var ownDir = Path.GetDirectoryName(full)!;
    foreach (var other in extraDirs.Where(d => !string.Equals(d, ownDir, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(d, pluginName))))
        Log.Write($"note: {other} also has a {pluginName}; reading {full}, the one --plugin names");
    extraDirs.RemoveAll(d => string.Equals(d, ownDir, StringComparison.OrdinalIgnoreCase));
    extraDirs.Insert(0, ownDir);
    // Context: the official files, then the plugin's own masters (and theirs) in load order, then the plugin.
    plugins ??= Official.Find(data);
    var order = new List<string>();
    var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    string? loop = null;
    void AddWithMasters(string name)
    {
        if (order.Contains(name, StringComparer.OrdinalIgnoreCase) || plugins.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        // A plugin that lists itself, or a circle of masters, would otherwise recurse forever (code review finding 3).
        if (!visiting.Add(name)) { loop ??= name; return; }
        var at = extraDirs.Append(data).Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
        if (at != null) foreach (var m in BuildKit.GameData.HeaderMasters(at)) AddWithMasters(m);
        order.Add(name);
    }
    AddWithMasters(pluginName);
    if (loop != null)
    {
        Console.Error.WriteLine($"{loop} is its own master (directly or through its masters), so it can't be loaded. Fix its master list in the Creation Kit or xEdit.");
        Log.Write($"stopped: master loop at {loop}");
        return 2;
    }
    Log.Write($"--plugin {full}: reporting only records it adds; its own non-official masters: {(order.Count > 1 ? string.Join(", ", order.Take(order.Count - 1)) : "none")}");
    // A mod's records count as used only if the game can reach them, often through Bethesda's own records (a leveled
    // list, a quest, a cell the mod adds to). So the official files must be scanned too. The saved result of that
    // scan (OfficialCache) is used when the game files haven't changed; otherwise they're read and the result saved.
    var officialList = plugins;
    if (command == "scan" && !noCache && !args.Contains("--plugins") && skip.Count == 0
        && !officialList.Contains(pluginName, StringComparer.OrdinalIgnoreCase))
    {
        var cacheDir = OfficialCache.DefaultDir;
        officialCache = OfficialCache.TryLoad(cacheDir, data, officialList);
        if (officialCache == null)
        {
            Console.Error.WriteLine($"Checking {pluginName}. A mod's records count as used only if the game can reach them, often through");
            Console.Error.WriteLine($"Bethesda's own records, so the {officialList.Count} official files are scanned first (about 2-3 minutes).");
            Console.Error.WriteLine("The result is saved, so later checks of any mod take seconds until the game files change.");
            sw.Restart();
            var officialIndex = RefIndex.Build(data, officialList, skip, msg => Console.Error.WriteLine($"  {sw.Elapsed.TotalSeconds,6:F1}s {msg}"));
            officialCache = OfficialCache.FromWalk(new Walk(officialIndex), data, officialList);
            officialCache.Save(cacheDir, officialList);
            Console.Error.WriteLine($"  {sw.Elapsed.TotalSeconds,6:F1}s official scan done and saved; now reading {pluginName}");
        }
        else
        {
            Console.Error.WriteLine($"Checking {pluginName} against the saved scan of the {officialList.Count} official files (made {officialCache.MadeUtc};");
            Console.Error.WriteLine("your game files haven't changed since). Reading only the mod. Add --no-cache to scan the official files again.");
        }
        plugins = order;
    }
    else
    {
        Console.Error.WriteLine($"Checking {pluginName}. The official files are read too, because a mod's records count as used only if the");
        Console.Error.WriteLine($"game can reach them{(noCache ? " (--no-cache: the saved scan isn't used)" : "")}.");
        plugins = plugins.Concat(order).ToList();
    }
}
plugins ??= Official.Find(data);
Log.Write($"plugins to read ({(args.Contains("--plugins") ? "from --plugins" : officialCache != null ? "the plugin and its non-official masters; the official files come from the saved scan" : "the official files found in the Data folder")}{(pluginName != null && officialCache == null ? ", then --plugin and its masters" : "")}): {string.Join(", ", plugins)}");
sw.Restart();
if (officialCache == null)
    Console.Error.WriteLine($"reading {plugins.Count} plugins from {data} (about 2 minutes for the whole official set)...");
var index = RefIndex.Build(data, plugins, skip, msg => Console.Error.WriteLine($"  {sw.Elapsed.TotalSeconds,6:F1}s {msg}"), extraDirs);
if (officialCache != null)
    Log.Write($"seeded {officialCache.SeedRoots(index):N0} official records from the saved scan");
if (pluginName != null && !index.PluginsRead.Contains(pluginName, StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"couldn't read {pluginName}: one of its masters is missing ({string.Join(", ", index.Skipped)}). Put the masters in the Data folder or next to the plugin.");
    return 2;
}
Log.Write($"index built in {sw.Elapsed.TotalSeconds:F1} s: {index.Defined.Count:N0} records, {index.EdgeCount:N0} links");
if (index.PluginsRead.Count == 0)
{
    Console.Error.WriteLine($"none of the plugins could be read from {Path.GetFullPath(data)}: {string.Join(", ", index.Skipped)}. Check the names in --plugins, and that each one's masters are in the Data folder.");
    return 2;
}

if (command == "spike") return Spike(index, sw);

var walk = new Walk(index);
Console.Error.WriteLine($"  {sw.Elapsed.TotalSeconds,6:F1}s walk done");
Log.Write($"walk done at {sw.Elapsed.TotalSeconds:F1} s");
// A plain scan of exactly the official files also saves the result for later --plugin runs.
if (command == "scan" && pluginName == null && !noCache && !args.Contains("--plugins") && skip.Count == 0)
    OfficialCache.FromWalk(walk, data, plugins).Save(OfficialCache.DefaultDir, plugins);
if (command == "scan") return Scan(walk);
if (command == "find") return Find(walk);
var worst = 0;
foreach (var t in targets) { worst = Math.Max(worst, Why(walk, t)); Console.WriteLine(); }
return worst;
}

int Scan(Walk w)
{
    var ix = w.Index;
    if (pluginName == null)
    {
        Console.WriteLine($"plugins: {string.Join(", ", ix.PluginsRead)}");
        Console.WriteLine($"records: {ix.Defined.Count:N0}; links: {w.LinkCount:N0}; roots: {ix.Roots.Count:N0}");
        foreach (var g in ix.Roots.GroupBy(r => r.Rule).OrderBy(g => g.Key.Tier))
            Console.WriteLine($"    root rule {g.Key.Name,-18} ({g.Key.Tier,-6}) {g.Count(),8:N0}  {g.Key.Means}");
    }
    else
        Console.WriteLine($"plugins read: {string.Join(", ", ix.PluginsRead)}{(officialCache != null ? $" (the official files from the saved scan, {officialCache.MadeUtc})" : "")}");

    bool Mine(int id) => pluginName == null || string.Equals(ix.KeyOf(id).ModKey.FileName, pluginName, StringComparison.OrdinalIgnoreCase);
    var rows = new List<(Verdict V, string Type, int Id)>();
    var verdictCounts = new Dictionary<Verdict, int>();
    foreach (var id in ix.Defined)
    {
        // --plugin: only the records the plugin adds (its overrides of other plugins' records aren't its to remove).
        if (!Mine(id)) continue;
        var type = ix.TypeOf(id);
        var v = w.VerdictOf(id);
        verdictCounts[v] = verdictCounts.GetValueOrDefault(v) + 1;
        if (v == Verdict.Reached) continue;
        if (onlyTypes != null ? !onlyTypes.Contains(type) : !allTypes && Rules.Structural.Contains(type)) continue;
        rows.Add((v, type, id));
    }
    Console.WriteLine();
    Console.WriteLine(pluginName == null ? "all records by verdict:" : $"records {pluginName} adds, by verdict:");
    foreach (var (v, n) in verdictCounts.OrderBy(kv => kv.Key)) Console.WriteLine($"    {v,-13} {n,10:N0}");

    Console.WriteLine();
    Console.WriteLine($"reported (not Reached{(onlyTypes != null ? ", --types" : allTypes ? "" : ", content types only")}): {rows.Count:N0}");
    if (rows.Count == 0) Console.WriteLine("    nothing to report");
    else
    {
        // The 20 types with the most rows; the full table is in the reports (user's test 2026-11-11: 130 rows was too long).
        var byType = rows.GroupBy(r => r.Type).OrderByDescending(g => g.Count()).ToList();
        Console.WriteLine($"    {"type",-28} {"Maybe",8} {"DevOnly",8} {"Orphaned",9} {"Unref",8} {"Cannot",7}");
        foreach (var g in byType.Take(20))
        {
            int C(Verdict v) => g.Count(r => r.V == v);
            Console.WriteLine($"    {g.Key,-28} {C(Verdict.Maybe),8:N0} {C(Verdict.DevOnly),8:N0} {C(Verdict.Orphaned),9:N0} {C(Verdict.Unreferenced),8:N0} {C(Verdict.CannotCheck),7:N0}");
        }
        if (byType.Count > 20)
            Console.WriteLine($"    ... and {byType.Count - 20} more types ({byType.Skip(20).Sum(g => g.Count()):N0} records): see report.html");
    }

    var reportRows = new List<ReportRow>();
    foreach (var (v, type, id) in rows.OrderBy(r => r.V).ThenBy(r => r.Type).ThenBy(r => ix.KeyOf(r.Id).ToString()))
    {
        // One entry per linking record (a quest that both contains and owns a topic is one record, not two).
        var inbound = w.Inbound(id).GroupBy(x => x.From).Select(g => g.First()).ToList();
        var p = w.PathTo(id);
        var rule = w.ReasonOf(id);
        var reason = rule == Rules.CheckedOnly && w.Pred[id] >= 0 ? $"checked-only: checked by {ix.Describe(w.Pred[id])}"
            // Short, so the report's Why column stays readable; the rule's full meaning is in the legend (and the
            // page's hover text). User's screenshot 2026-11-11: the long form squeezed the table.
            : p.Count > 0 ? $"via {rule!.Name}: {ix.Describe(p[0])}"
            : v == Verdict.CannotCheck ? "Mutagen couldn't read its links"
            : inbound.Count == 0 ? "nothing links to it" : "only records the game never reaches link to it";
        var key = ix.KeyOf(id);
        reportRows.Add(new ReportRow(v.ToString(), type, key.ToString(), key.ModKey.FileName, ix.EditorIdOf(id), ix.NameOf(id),
            Rules.Hint(ix.EditorIdOf(id)), reason, inbound.Count, inbound.Take(3).Select(x => ix.Describe(x.From)).ToArray()));
    }
    // The RESULT line (plugin mode) is worked out first, so the reports can show it too.
    static string P(int n, string one) => $"{n:N0} {one}{(n == 1 ? "" : "s")}";
    // "2026-11-11 22:17 UTC" -> "2026-11-11 at 22:17 UTC", for a sentence.
    static string Utc(string made) => made.Split(' ') is [var d, var t, ..] ? $"{d} at {t} UTC" : made;
    string? result = null;
    var dead = 0;
    if (pluginName != null)
    {
        var added = verdictCounts.Values.Sum();
        dead = rows.Count(r => r.V is Verdict.Orphaned or Verdict.Unreferenced);
        // Full sentences (user, 2026-11-11). The console line keeps "RESULT:" in front so scripts can find it.
        var maybe = rows.Count(r => r.V == Verdict.Maybe);
        var devOnly = rows.Count(r => r.V == Verdict.DevOnly);
        string Count(int n) => n == 0 ? "None is" : n == 1 ? "1 is" : $"{n:N0} are";
        result = dead == 0
            ? $"RESULT: {pluginName} adds {P(added, "record")}, and the game reaches all of them. " +
              $"{Count(maybe)} reached only through a recipe, research project or condition check" +
              (maybe == 0 ? ". " : maybe == 1 ? ", so it's marked Maybe. " : ", so they're marked Maybe. ") +
              $"{Count(devOnly)} reached only from test cells."
            : $"RESULT: {pluginName} adds {P(added, "record")}, and nothing in the game reaches {dead:N0} of them. " +
              "The reports list them as Orphaned or Unreferenced.";
        Log.Write($"--plugin result: {added} added, {dead} dead");
    }
    var filter = onlyTypes != null ? $"the types {string.Join(", ", onlyTypes)}" : allTypes ? "every record type" : "content record types (structure such as placed references, navmeshes and keywords is left out)";
    // "What was checked" at the top of the report, in full sentences (user's tests 2026-11-11: the reports didn't
    // say what was checked, and then the first version used fragments).
    var single = ix.PluginsRead.Count == 1;
    var checkedLines = new List<string>
    {
        pluginName != null
            ? $"This report checks the {P(verdictCounts.Values.Sum(), "record")} that {pluginName} adds, to find any that nothing in the game reaches."
            : $"This report checks every record in Starfield's official files ({P(ix.Defined.Count, "record")} in {P(ix.PluginsRead.Count, "plugin")}), to find what the game can never reach.",
        $"The game files are in {Path.GetFullPath(data)}.",
    };
    if (pluginName != null)
        checkedLines.Add(officialCache != null
            ? $"The official files come from the saved scan made on {Utc(officialCache.MadeUtc)}, and they haven't changed since."
            : "The official files were read in full for this run.");
    checkedLines.Add(single
        ? $"The plugin read was {ix.PluginsRead[0]}."
        : $"The plugins read were {string.Join(", ", ix.PluginsRead)}.");
    if (ix.Skipped.Count > 0)
        checkedLines.Add($"These plugins were skipped, because they or one of their masters weren't found: {string.Join(", ", ix.Skipped)}.");
    checkedLines.Add(onlyTypes != null || allTypes
        ? $"The table lists the records the game doesn't reach strongly, limited to {filter}."
        : "The table lists the records the game doesn't reach strongly, limited to content record types. Structure such as placed references, navmeshes and keywords is left out.");
    var secs = (int)Math.Round(total.Elapsed.TotalSeconds);
    checkedLines.Add($"sfcutcheck {Version} made this report on {DateTime.UtcNow:yyyy-MM-dd} at {DateTime.UtcNow:HH:mm} UTC in {secs} second{(secs == 1 ? "" : "s")}.");

    var run = new ReportRun("sfcutcheck", Version, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"), Path.GetFullPath(data),
        ix.PluginsRead.ToArray(), ix.Skipped.ToArray(), ix.Defined.Count, w.LinkCount,
        verdictCounts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value), filter,
        Rules.All.Concat(ix.Roots.Select(x => x.Rule).Distinct().Except(Rules.All))
            .Select(r => (r.Name, r.Tier.ToString(), ix.Roots.Count(x => x.Rule == r), r.Means)).ToArray(),
        pluginName ?? "Starfield's official files", checkedLines.ToArray(), result);
    var files = Report.WriteAll(outDir, run, reportRows);
    Console.WriteLine();
    Console.WriteLine("reports:");
    foreach (var f in files) Console.WriteLine($"    {Path.GetFullPath(f)}");
    if (ix.Skipped.Count > 0) Console.WriteLine($"skipped (not found, or a master missing): {string.Join(", ", ix.Skipped)}");
    // The whole run, including the official scan in a first --plugin run (user's test 2026-11-11 showed "time: 0 s").
    Console.WriteLine($"time: {total.Elapsed.TotalSeconds:F0} s");
    if (result != null)
    {
        Console.WriteLine();
        Console.WriteLine(result);
        return dead == 0 ? 0 : 1;
    }
    return 0;
}

// find <text>...: records whose EditorID contains the text (or, with --word, has it as a whole word), with their
// verdicts. Lists up to --max per text (default 40) and counts every match by verdict.
int Find(Walk w)
{
    var ix = w.Index;
    foreach (var text in targets)
    {
        var hits = ix.EditorIds
            .Where(kv => word ? Rules.Words(kv.Value).Contains(text.ToLowerInvariant()) : kv.Value.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Where(kv => onlyTypes == null || onlyTypes.Contains(ix.TypeOf(kv.Key)))
            .Select(kv => (Id: kv.Key, Edid: kv.Value, V: w.VerdictOf(kv.Key)))
            .OrderBy(h => h.V).ThenBy(h => h.Edid, StringComparer.OrdinalIgnoreCase).ToList();
        Console.WriteLine($"=== \"{text}\"{(word ? " (whole word)" : "")}: {hits.Count:N0} records; " +
            string.Join(", ", hits.GroupBy(h => h.V).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count():N0}")));
        // At most --max rows in all, shared between the verdicts present (code review finding 8, 2026-11-11).
        var groups = hits.GroupBy(h => h.V).OrderBy(g => g.Key).ToList();
        var shown = 0;
        for (var gi = 0; gi < groups.Count && shown < max; gi++)
        {
            var quota = Math.Max(1, (max - shown) / (groups.Count - gi));
            foreach (var h in groups[gi].Take(Math.Min(quota, max - shown)))
            {
                Console.WriteLine($"    {h.V,-12} {ix.TypeOf(h.Id),-22} {ix.KeyOf(h.Id),-28} {h.Edid}");
                shown++;
            }
        }
        if (shown < hits.Count) Console.WriteLine($"    ... {hits.Count - shown:N0} more (raise --max, or narrow with --types)");
        Console.WriteLine();
    }
    return 0;
}

int Why(Walk w, string what)
{
    var ix = w.Index;
    int id;
    if (what.Contains(':') && TryKey(what, out var key))
    {
        if (!ix.TryGetId(key, out id) || !ix.Defined.Contains(id)) { Console.WriteLine($"{key}: no such record in the plugins read"); return 1; }
    }
    else
    {
        var hits = ix.EditorIds.Where(kv => string.Equals(kv.Value, what, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
        if (hits.Count == 0) { Console.WriteLine($"no record has the EditorID {what} (placed references aren't searched by EditorID; give FormID:Plugin.esm)"); return 1; }
        id = hits[0];
        if (hits.Count > 1) Console.WriteLine($"({hits.Count} records have that EditorID; showing the first)");
    }

    var v = w.VerdictOf(id);
    Console.WriteLine($"{ix.Describe(id)}");
    Console.WriteLine($"verdict: {v}");
    if (Rules.Hint(ix.EditorIdOf(id)) is { } hint) Console.WriteLine($"hint: EditorID contains \"{hint}\"");
    var path = w.PathTo(id);
    if (path.Count > 0)
    {
        var root = path[0];
        var rule = w.ReasonOf(id)!;
        if (rule != w.RootOf[root]) Console.WriteLine($"verdict rule {rule.Name}: {rule.Means}");
        Console.WriteLine($"reached from root rule {w.RootOf[root]!.Name} ({w.RootOf[root]!.Means}):");
        Console.WriteLine($"    {ix.Describe(root)}");
        for (var i = 1; i < path.Count; i++)
            Console.WriteLine($"    {new string(' ', Math.Min(i, 12) * 2)}-{KindWord(w.KindBetween(path[i - 1], path[i]))}-> {ix.Describe(path[i])}");
    }
    var inbound = w.Inbound(id).ToList();
    var inboundRecords = inbound.Select(x => x.From).Distinct().Count();
    Console.WriteLine($"linked from {inboundRecords:N0} record(s){(inbound.Count > 20 ? " (first 20 links)" : "")}:");
    foreach (var (from, kind) in inbound.Take(20))
        Console.WriteLine($"    {KindWord(kind),-8} {ix.Describe(from)}  [{w.VerdictOf(from)}]");
    var outbound = w.Outbound(id).ToList();
    Console.WriteLine($"links to {outbound.Select(x => x.To).Distinct().Count():N0} record(s){(outbound.Count > 20 ? " (first 20 links)" : "")}:");
    foreach (var (to, kind) in outbound.Take(20))
        Console.WriteLine($"    {KindWord(kind),-8} {(ix.Defined.Contains(to) ? ix.Describe(to) + "  [" + w.VerdictOf(to) + "]" : ix.KeyOf(to) + "  [no plugin read defines it]")}");
    return 0;
}

static string KindWord(LinkKind k) => k switch { LinkKind.Contains => "contains", LinkKind.Owns => "owns", LinkKind.Checks => "checks", _ => "links" };

static bool TryKey(string s, out FormKey key) => FormIds.TryParse(s, out key);

static int Spike(RefIndex ix, Stopwatch sw)
{
    using var proc = Process.GetCurrentProcess();
    proc.Refresh();
    Console.WriteLine($"records: {ix.NodeCount:N0}; versions read: {ix.VersionsRead:N0}");
    Console.WriteLine($"links: {ix.EdgeCount:N0} (contains {ix.ContainsCount:N0}, owns {ix.OwnsCount:N0}); to undefined records: {ix.DanglingCount:N0}");
    Console.WriteLine($"failed to enumerate: {ix.Failed.Count}");
    foreach (var f in ix.Failed) Console.WriteLine($"    {f.Type} {f.Key}: {f.Error}");
    Console.WriteLine($"time: {sw.Elapsed.TotalSeconds:F1} s; managed heap: {GC.GetTotalMemory(true) / 1e9:F2} GB; peak working set: {proc.PeakWorkingSet64 / 1e9:F2} GB");
    Console.WriteLine("links to records no plugin defines, by source type (top 10), with 3 examples each:");
    // FormIDs below 0x800 in Starfield.esm are the engine's own hard-coded forms (PlayerRef, built-in actor values):
    // no plugin defines them, so links to them are expected. The rest may be leftovers or Mutagen misreading a field.
    var allDangling = Enumerable.Range(0, ix.EdgeCount).Where(e => !ix.Defined.Contains(ix.To[e])).ToList();
    bool Engine(int e) => ix.KeyOf(ix.To[e]) is var k && k.ID < 0x800 && string.Equals(k.ModKey.FileName, "Starfield.esm", StringComparison.OrdinalIgnoreCase);
    var dangling = allDangling.Where(e => !Engine(e)).ToList();
    Console.WriteLine($"    {allDangling.Count - dangling.Count:N0} go to hard-coded engine forms (below 0x800 in Starfield.esm); {dangling.Count:N0} go elsewhere:");
    foreach (var g in dangling.GroupBy(e => ix.TypeOf(ix.From[e])).OrderByDescending(g => g.Count()).Take(10))
    {
        Console.WriteLine($"    {g.Key,-28} {g.Count(),8:N0}");
        foreach (var e in g.Take(3)) Console.WriteLine($"        {ix.Describe(ix.From[e])} -> {ix.KeyOf(ix.To[e])}");
    }
    Console.WriteLine("links by source type (top 15):");
    foreach (var (type, n) in ix.LinksByType.OrderByDescending(kv => kv.Value).Take(15)) Console.WriteLine($"    {type,-28} {n,12:N0}");
    return 0;
}
