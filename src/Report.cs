// The reports written by `scan` into --out: report.csv (one row per reported record), report.json (the same rows
// plus the run's settings, rules and counts) and report.html (one self-contained page with filters; no internet
// needed). Every value that comes from a plugin is escaped: CSV quoting, JSON escaping (System.Text.Json escapes
// < > & too, so the JSON can sit inside the HTML page), and the page builds its table with textContent only.
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SfCutCheck;

public sealed record ReportRow(
    string Verdict, string Type, string FormKey, string Plugin, string? EditorId, string? Name, string? Hint,
    string Reason, int LinkedFrom, string[] FirstLinksFrom);

/// <param name="Target">What the report is about, for the title: a plugin's file name, or "Starfield's official files".</param>
/// <param name="Checked">"What was checked", one line each, shown at the top of report.html and kept in report.json
/// (user's test 2026-11-11: the reports didn't say what was checked).</param>
/// <param name="Result">The RESULT line of a --plugin check, or null.</param>
public sealed record ReportRun(
    string Tool, string Version, string GeneratedUtc, string DataFolder, string[] Plugins, string[] Skipped,
    long Records, long Links, Dictionary<string, int> AllVerdicts, string Filter,
    (string Name, string Tier, int Roots, string Means)[] Rules,
    string Target = "Starfield's official files", string[]? Checked = null, string? Result = null);

public static class Report
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Default,   // escapes < > & ' as \uXXXX
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public static List<string> WriteAll(string outDir, ReportRun run, IReadOnlyList<ReportRow> rows)
    {
        Directory.CreateDirectory(outDir);
        var written = new List<string>();
        var csv = Path.Combine(outDir, "report.csv");
        File.WriteAllText(csv, Csv(rows), new UTF8Encoding(true));   // BOM so Excel reads UTF-8
        written.Add(csv);
        var json = Path.Combine(outDir, "report.json");
        File.WriteAllText(json, JsonText(run, rows), new UTF8Encoding(false));
        written.Add(json);
        var html = Path.Combine(outDir, "report.html");
        File.WriteAllText(html, Html(run, rows), new UTF8Encoding(false));
        written.Add(html);
        foreach (var f in written) Log.Write($"wrote {Log.Describe(f)}");
        return written;
    }

    static string Q(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // A cell starting with = + - @ (or tab / carriage return) runs as a formula in Excel and other spreadsheets:
        // a mod could plant one in a name. A leading apostrophe makes it plain text (code review finding 7).
        if (s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static string Csv(IReadOnlyList<ReportRow> rows)
    {
        var sb = new StringBuilder("verdict,type,formkey,plugin,editorid,name,hint,reason,linked_from,first_links_from\r\n");
        foreach (var r in rows)
            sb.Append(string.Join(",", Q(r.Verdict), Q(r.Type), Q(r.FormKey), Q(r.Plugin), Q(r.EditorId), Q(r.Name), Q(r.Hint),
                Q(r.Reason), r.LinkedFrom.ToString(), Q(string.Join(" | ", r.FirstLinksFrom)))).Append("\r\n");
        return sb.ToString();
    }

    static object RunObject(ReportRun run) => new
    {
        run.Tool, run.Version, Author = About.Author, License = About.License, Url = About.Url,
        run.GeneratedUtc, run.Target, Checked = run.Checked ?? [], run.Result,
        run.DataFolder, run.Plugins, run.Skipped, run.Records, run.Links, run.AllVerdicts, run.Filter,
        Rules = run.Rules.Select(r => new { r.Name, r.Tier, r.Roots, r.Means }),
    };

    public static string JsonText(ReportRun run, IReadOnlyList<ReportRow> rows) =>
        JsonSerializer.Serialize(new { Run = RunObject(run), Records = rows }, Json);

    public static string Html(ReportRun run, IReadOnlyList<ReportRow> rows)
    {
        var data = JsonSerializer.Serialize(new { Run = RunObject(run), Records = rows }, new JsonSerializerOptions(Json) { WriteIndented = false });
        return Template.Replace("/*DATA*/null", data);
    }

    // The page: tokens for light and dark, summary first, then filters and the table. Shows up to 500 matching rows at
    // a time (the official set has about 24,000), with a "show more" button.
    const string Template = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>sfcutcheck</title>
<style>
/* Layout: a summary band, a filter bar, then one wide table that scrolls inside its own box. */
:root {
  --bg: #f6f7f9; --panel: #ffffff; --fg: #1d232b; --muted: #5d6874; --line: #d9dee4; --accent: #2f5f8a;
  --reached: #2e7d4f; --maybe: #9a6b00; --dev: #6b4fa0; --orphan: #b5542a; --unref: #a8323e; --cannot: #5d6874;
  --mono: ui-monospace, "Cascadia Mono", Consolas, monospace; --sans: "Segoe UI", system-ui, -apple-system, sans-serif;
  color-scheme: light;
}
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) {
  --bg: #12161b; --panel: #1a2027; --fg: #e3e8ee; --muted: #9aa5b1; --line: #2c343d; --accent: #7fb0dc;
  --reached: #6cc28f; --maybe: #e0b44c; --dev: #b49be0; --orphan: #ec8c5e; --unref: #ef7f8a; --cannot: #9aa5b1;
  color-scheme: dark; } }
:root[data-theme="dark"] {
  --bg: #12161b; --panel: #1a2027; --fg: #e3e8ee; --muted: #9aa5b1; --line: #2c343d; --accent: #7fb0dc;
  --reached: #6cc28f; --maybe: #e0b44c; --dev: #b49be0; --orphan: #ec8c5e; --unref: #ef7f8a; --cannot: #9aa5b1;
  color-scheme: dark; }
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--fg); font: 14px/1.45 var(--sans);
  padding-block: 20px 40px; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); }
h1 { font-size: 22px; margin: 0 0 2px; overflow-wrap: anywhere; }
.byline { margin: 0 0 10px; font-size: 12.5px; color: var(--muted); }
.byline a, .about a { color: var(--accent); }
.about { margin-top: 22px; padding-top: 12px; border-top: 1px solid var(--line); font-size: 12.5px; color: var(--muted); }
.about p { margin: 4px 0; }
.result { margin: 0 0 10px; }
.result b { margin-right: 4px; }
.checked { margin: 0 0 16px; font-size: 13px; color: var(--muted); line-height: 1.55; overflow-wrap: anywhere; }
td.links div + div { margin-top: 4px; }
td.links small { display: block; font-family: var(--mono); font-size: 11.5px; color: var(--muted); }
.counts { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 16px; }
.count { background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: 8px 12px; min-width: 120px; cursor: pointer; }
.count[aria-pressed="true"] { outline: 2px solid var(--accent); }
.count b { display: block; font-size: 18px; font-variant-numeric: tabular-nums; }
.count span { font-size: 12px; text-transform: uppercase; letter-spacing: .04em; color: var(--muted); }
.filters { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-bottom: 10px; }
.filters input, .filters select { font: inherit; color: var(--fg); background: var(--panel); border: 1px solid var(--line); border-radius: 4px; padding: 6px 8px; }
.filters input { flex: 1 1 260px; min-width: 0; }
.shown { color: var(--muted); font-size: 13px; }
.tablebox { overflow-x: auto; background: var(--panel); border: 1px solid var(--line); border-radius: 6px; }
/* Fixed column widths that add up to the table's width, so no column pushes the others off the right edge; long
   FormIDs, EditorIDs and reasons wrap inside their own column. Below 760px the box scrolls sideways instead. */
table { border-collapse: collapse; width: 100%; min-width: 760px; table-layout: fixed; }
col.c-verdict { width: 8%; } col.c-type { width: 10%; } col.c-formid { width: 13%; } col.c-edid { width: 21%; }
col.c-name { width: 13%; } col.c-why { width: 20%; } col.c-links { width: 15%; }
th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid var(--line); vertical-align: top; overflow-wrap: anywhere; }
th { font-size: 12px; text-transform: uppercase; letter-spacing: .04em; color: var(--muted); position: sticky; top: 0; background: var(--panel); }
td.mono { font-family: var(--mono); font-size: 12.5px; }
td.why { color: var(--muted); font-size: 13px; }
td.why[title] { cursor: help; }
.v { font-size: 12px; font-weight: 600; padding: 2px 6px; border-radius: 3px; border: 1px solid currentColor; white-space: nowrap; }
.v-Reached { color: var(--reached); } .v-Maybe { color: var(--maybe); } .v-DevOnly { color: var(--dev); }
.v-Orphaned { color: var(--orphan); } .v-Unreferenced { color: var(--unref); } .v-CannotCheck { color: var(--cannot); }
.hint { font-size: 11px; color: var(--muted); border: 1px dashed var(--line); padding: 0 4px; border-radius: 3px; margin-left: 4px; }
button.more { margin-top: 10px; font: inherit; color: var(--accent); background: none; border: 1px solid var(--accent); border-radius: 4px; padding: 6px 12px; cursor: pointer; }
details { margin-top: 18px; color: var(--muted); }
details li { margin: 2px 0; }
details.guide { margin: 0 0 16px; color: var(--fg); background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: 10px 14px; }
details.guide > summary { cursor: pointer; font-weight: 600; }
details.guide p { margin: 8px 0; font-size: 13.5px; }
.cards { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 12px; }
@media (max-width: 1100px) { .cards { grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); } }
/* overflow-wrap: long EditorIDs such as COM_Companion_SamCoe_Convo_Neon01 spilled into the next card (screenshot
   2026-11-11 18:13). */
.card { border-top: 1px solid var(--line); padding-top: 4px; min-width: 0; overflow-wrap: anywhere; }
.card p { margin: 6px 0; }
.card code { font-family: var(--mono); font-size: 12px; }
:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
</style>
</head>
<body>
<h1 id="h1">sfcutcheck report</h1>
<p class="byline" id="byline"></p>
<p class="result" id="result" hidden></p>
<p class="checked" id="checked"></p>
<details class="guide" open><summary>What the verdicts mean</summary>
  <p>sfcutcheck starts from the places where the game begins using content (quests that start with a new game, the story manager, the worlds and rooms you can enter, the menus) and follows every link from record to record. A record it can't reach that way is one the game, as shipped, has no way to show you. Links made only inside script code aren't seen.</p>
  <div class="cards">
    <div class="card"><p><span class="v v-Maybe">Maybe</span> It's reached only in a way the files can't confirm: a crafting or building recipe, a research project, a record the engine picks by its conditions, or a check for it that nothing in the files satisfies.</p><p><b>Example:</b> the outpost Solar Array comes only from its building recipe, and the Starborn power Grav Dash is checked for but given by a script.</p><p><b>In the game:</b> most Maybe records are in the game. <b>For modders:</b> check before calling one cut.</p></div>
    <div class="card"><p><span class="v v-DevOnly">DevOnly</span> Only Bethesda's test and debug cells lead to it.</p><p><b>Example:</b> AmmoPlaceholderBullet is placed only in QASmoke, a developer test room.</p><p><b>In the game:</b> not in normal play. <b>For modders:</b> usually placeholders; useful as reference rather than for restoring.</p></div>
    <div class="card"><p><span class="v v-Orphaned">Orphaned</span> Something links to it, but only records the game itself never reaches.</p><p><b>Example:</b> Sam Coe's Neon conversation (COM_Companion_SamCoe_Convo_Neon01) has its quest, lines and scene, but nothing starts the quest, while Andreja's matching chat is started.</p><p><b>In the game:</b> you'll never hear it. <b>For modders:</b> often a whole finished group; use <code>sfcutcheck why</code> to find the top record and connect that one.</p></div>
    <div class="card"><p><span class="v v-Unreferenced">Unreferenced</span> Nothing in the game files links to it at all.</p><p><b>Example:</b> CUT_Skill_Mindfulness is a finished skill called "Mindfulness", but no skill list, quest or item gives it, so it never appears in the skill screen.</p><p><b>In the game:</b> you'll never see it. <b>For modders:</b> the cleanest find; one new link from something the game uses brings it in.</p></div>
    <div class="card"><p><span class="v v-CannotCheck">CannotCheck</span> The plugin reader couldn't read its links.</p><p><b>In the game:</b> unknown. Never treat it as cut.</p><p>Records the game does reach aren't listed; the counts below cover only what's left.</p></div>
  </div>
  <p>A <span class="hint">hint</span> marks an EditorID with a word Bethesda uses for unfinished work (cut, zz, unused, test, debug, DoNotUse). It's a clue, not a verdict. Not every Orphaned or Unreferenced record is cut content: some are leftovers or editor building blocks. RESULTS.md, next to sfcutcheck.exe, explains each verdict in more detail and how to use cut content in a mod.</p>
</details>
<div class="counts" id="counts"></div>
<div class="filters">
  <input id="q" type="search" placeholder="Search EditorID, name, FormID or reason" aria-label="Search">
  <select id="type" aria-label="Record type"><option value="">All types</option></select>
  <select id="hint" aria-label="Hint"><option value="">Any hint</option><option value="*">Has a hint</option></select>
  <span class="shown" id="shown"></span>
</div>
<div class="tablebox"><table>
  <colgroup><col class="c-verdict"><col class="c-type"><col class="c-formid"><col class="c-edid"><col class="c-name"><col class="c-why"><col class="c-links"></colgroup>
  <thead><tr><th>Verdict</th><th>Type</th><th>FormID</th><th>EditorID</th><th>Name</th><th>Why</th><th>Linked from</th></tr></thead>
  <tbody id="rows"></tbody>
</table></div>
<button class="more" id="more" hidden>Show 500 more</button>
<footer class="about" id="about"></footer>
<details><summary>The starting-point rules used</summary>
  <p>The game starts using content from these places. Everything sfcutcheck can reach from them, by following links from record to record, counts as used.</p>
  <ul id="rules"></ul>
</details>
<script id="data" type="application/json">/*DATA*/null</script>
<script>
(function () {
  var d = JSON.parse(document.getElementById('data').textContent);
  var run = d.run, recs = d.records, order = ['Maybe', 'DevOnly', 'Orphaned', 'Unreferenced', 'CannotCheck'];
  var state = { verdict: '', type: '', hint: '', q: '', limit: 500 };
  function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
  // Attribution: a byline under the title and a footer (added 2026-11-11 at the author's request).
  function link(href, text) { var a = el('a', null, text); a.href = href; a.target = '_blank'; a.rel = 'noopener'; return a; }
  var by = document.getElementById('byline');
  by.appendChild(document.createTextNode('Made with sfcutcheck ' + run.version + ' by ' + run.author + ' · '));
  by.appendChild(link(run.url, run.url.replace(/^https:\/\//, '')));
  var about = document.getElementById('about');
  var p1 = el('p', null, 'sfcutcheck ' + run.version + ' by ' + run.author + '. Free software under ' + run.license + ': ');
  p1.appendChild(link(run.url, 'source and releases'));
  p1.appendChild(document.createTextNode('. Spotted a mistake? '));
  p1.appendChild(link(run.url + '/issues', 'Report it on GitHub'));
  p1.appendChild(document.createTextNode('.'));
  about.appendChild(p1);
  var p2 = el('p', null, 'Plugins are read with ');
  p2.appendChild(link('https://github.com/Mutagen-Modding/Mutagen', 'Mutagen'));
  p2.appendChild(document.createTextNode(', by Noggog and the Mutagen contributors. The idea came from georgearhitektive\'s '));
  p2.appendChild(link('https://www.nexusmods.com/starfield/mods/17708', 'Bethesda Archaeology Studio Cut Content Analyzer'));
  p2.appendChild(document.createTextNode('; sfcutcheck is separate work and uses none of its code.'));
  about.appendChild(p2);
  about.appendChild(el('p', null, 'This page was made on your computer from your own copy of the game. sfcutcheck sends nothing anywhere.'));
  document.title = 'sfcutcheck: ' + run.target;
  document.getElementById('h1').textContent = 'sfcutcheck: ' + run.target;
  if (run.result) {
    var res = document.getElementById('result');
    res.appendChild(el('b', null, 'Result:'));
    res.appendChild(document.createTextNode(run.result.replace(/^RESULT:\s*/, '')));
    res.hidden = false;
  }
  // What was checked, as one short paragraph of full sentences.
  document.getElementById('checked').textContent = (run.checked || []).join(' ');
  var counts = document.getElementById('counts');
  order.forEach(function (v) {
    var n = recs.filter(function (r) { return r.verdict === v; }).length;
    var b = el('button', 'count'); b.setAttribute('aria-pressed', 'false'); b.dataset.v = v;
    b.appendChild(el('b', 'v-' + v, n.toLocaleString())); b.appendChild(el('span', null, v));
    b.onclick = function () { state.verdict = state.verdict === v ? '' : v; state.limit = 500; draw(); };
    counts.appendChild(b);
  });
  var types = {}; recs.forEach(function (r) { types[r.type] = (types[r.type] || 0) + 1; });
  var ts = document.getElementById('type');
  Object.keys(types).sort().forEach(function (t) { var o = el('option', null, t + ' (' + types[t] + ')'); o.value = t; ts.appendChild(o); });
  var hs = document.getElementById('hint'), hints = {};
  recs.forEach(function (r) { if (r.hint) hints[r.hint] = 1; });
  Object.keys(hints).sort().forEach(function (h) { var o = el('option', null, h); o.value = h; hs.appendChild(o); });
  var means = {}; run.rules.forEach(function (r) { means[r.name] = r.means; });
  var rl = document.getElementById('rules');
  run.rules.forEach(function (r) { rl.appendChild(el('li', null, r.name + ' (' + r.tier + ', ' + r.roots.toLocaleString() + ' roots): ' + r.means)); });
  ts.onchange = function () { state.type = ts.value; state.limit = 500; draw(); };
  hs.onchange = function () { state.hint = hs.value; state.limit = 500; draw(); };
  var q = document.getElementById('q'), timer;
  q.oninput = function () { clearTimeout(timer); timer = setTimeout(function () { state.q = q.value.trim().toLowerCase(); state.limit = 500; draw(); }, 150); };
  document.getElementById('more').onclick = function () { state.limit += 500; draw(); };
  function match(r) {
    if (state.verdict && r.verdict !== state.verdict) return false;
    if (state.type && r.type !== state.type) return false;
    if (state.hint === '*' ? !r.hint : state.hint && r.hint !== state.hint) return false;
    if (state.q) {
      var s = (r.formKey + ' ' + (r.editorId || '') + ' ' + (r.name || '') + ' ' + r.reason).toLowerCase();
      if (s.indexOf(state.q) < 0) return false;
    }
    return true;
  }
  function draw() {
    Array.prototype.forEach.call(counts.children, function (b) { b.setAttribute('aria-pressed', String(b.dataset.v === state.verdict)); });
    var hits = recs.filter(match), body = document.getElementById('rows');
    body.textContent = '';
    hits.slice(0, state.limit).forEach(function (r) {
      var tr = el('tr'), v = el('td'); v.appendChild(el('span', 'v v-' + r.verdict, r.verdict)); tr.appendChild(v);
      tr.appendChild(el('td', null, r.type));
      tr.appendChild(el('td', 'mono', r.formKey));
      var e = el('td', 'mono', r.editorId || ''); if (r.hint) e.appendChild(el('span', 'hint', r.hint)); tr.appendChild(e);
      tr.appendChild(el('td', null, r.name || ''));
      var why = el('td', 'why', r.reason || (r.linkedFrom ? '' : 'nothing links to it'));
      var rule = (r.reason || '').match(/^(?:via )?([a-z-]+)[:;]/);
      if (rule && means[rule[1]]) why.title = rule[1] + ': ' + means[rule[1]];
      tr.appendChild(why);
      // One referrer per line: "Type EditorID", with the FormID small underneath ("and N more" past three).
      var lt = el('td', 'why links');
      r.firstLinksFrom.forEach(function (d) {
        var p = d.split(' '), row = el('div', null, p[0] + ' ' + (p.slice(2).join(' ') || ''));
        row.appendChild(el('small', null, p[1] || ''));
        lt.appendChild(row);
      });
      if (r.linkedFrom > r.firstLinksFrom.length) lt.appendChild(el('div', null, 'and ' + (r.linkedFrom - r.firstLinksFrom.length) + ' more'));
      tr.appendChild(lt);
      body.appendChild(tr);
    });
    document.getElementById('shown').textContent = 'Showing ' + Math.min(hits.length, state.limit).toLocaleString() + ' of ' + hits.length.toLocaleString();
    document.getElementById('more').hidden = hits.length <= state.limit;
  }
  draw();
})();
</script>
</body>
</html>
""";
}
