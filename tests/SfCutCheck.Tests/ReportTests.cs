// The reports must survive hostile text from a plugin: an EditorID or name with commas, quotes, new lines or HTML.
using System.Text.Json;
using SfCutCheck;
using Xunit;

namespace SfCutCheck.Tests;

public sealed class ReportTests
{
    static readonly ReportRow Nasty = new("Orphaned", "Weapon", "000801:Bad.esm", "Bad.esm",
        "Evil,\"Edid\"", "</script><img src=x onerror=alert(1)>\nline two", null,
        "nothing links to it", 0, []);

    static readonly ReportRun Run = new("sfcutcheck", "test", "2026-11-11 00:00", @"C:\Data", ["Bad.esm"], [], 1, 0,
        new() { ["Orphaned"] = 1 }, "every record type", [("world", "Strong", 0, "a world")]);

    [Fact]
    public void CsvQuotesCommasQuotesAndNewLines()
    {
        var line = Report.Csv([Nasty]).Split("\r\n")[1];
        Assert.Contains("\"Evil,\"\"Edid\"\"\"", line);
        Assert.Contains("\"</script><img src=x onerror=alert(1)>\nline two\"", Report.Csv([Nasty]));
    }

    [Fact]
    public void CsvDefusesFormulas()
    {
        var row = Nasty with { EditorId = "=HYPERLINK(\"http://x\",\"click\")", Name = "+SUM(1,2)" };
        var line = Report.Csv([row]).Split("\r\n")[1];
        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\",\"\"click\"\")\"", line);
        Assert.Contains("\"'+SUM(1,2)\"", line);
        Assert.DoesNotContain(",=", line);
    }

    [Fact]
    public void ReportsSayWhatWasChecked()
    {
        var run = Run with { Target = "MyMod.esm", Checked = ["Checked: the records MyMod.esm adds", "Game files: C:\\Data"], Result = "RESULT: fine" };
        using var doc = JsonDocument.Parse(Report.JsonText(run, [Nasty]));
        var r = doc.RootElement.GetProperty("run");
        Assert.Equal("MyMod.esm", r.GetProperty("target").GetString());
        Assert.Equal("Checked: the records MyMod.esm adds", r.GetProperty("checked")[0].GetString());
        Assert.Equal("RESULT: fine", r.GetProperty("result").GetString());
        Assert.Equal(About.Author, r.GetProperty("author").GetString());
        Assert.Equal(About.Url, r.GetProperty("url").GetString());
        var html = Report.Html(run, [Nasty]);
        Assert.Contains("MyMod.esm", html);
        Assert.Contains("id=\"checked\"", html);
    }

    [Fact]
    public void VerdictCardsFollowTheCountsOrder()
    {
        // The guide cards must be in the same order as the count buttons under them (user, 2026-11-11).
        var html = Report.Html(Run, [Nasty]);
        string[] order = ["Maybe", "DevOnly", "Orphaned", "Unreferenced", "CannotCheck"];
        Assert.Contains("order = ['Maybe', 'DevOnly', 'Orphaned', 'Unreferenced', 'CannotCheck']", html);
        var positions = order.Select(v => html.IndexOf($"<div class=\"card\"><p><span class=\"v v-{v}\"", StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void JsonRoundTrips()
    {
        using var doc = JsonDocument.Parse(Report.JsonText(Run, [Nasty]));
        var rec = doc.RootElement.GetProperty("records")[0];
        Assert.Equal(Nasty.EditorId, rec.GetProperty("editorId").GetString());
        Assert.Equal(Nasty.Name, rec.GetProperty("name").GetString());
    }

    [Fact]
    public void HtmlCantBeBrokenOutOf()
    {
        var html = Report.Html(Run, [Nasty]);
        // The data sits inside a <script type="application/json"> block: no raw "<" from the plugin may appear.
        Assert.DoesNotContain("</script><img", html);
        Assert.DoesNotContain("onerror=alert(1)>", html.Replace("\\u003E", ""));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "</script>\\s*<script>"));
    }
}
