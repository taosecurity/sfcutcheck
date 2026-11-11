namespace SfCutCheck;

/// <summary>The official plugins (the game, its DLC and Bethesda's own creations) present in a Data folder, in the
/// order the game loads them.</summary>
public static class Official
{
    static readonly string[] Fixed =
    [
        "Starfield.esm", "Constellation.esm", "OldMars.esm", "BlueprintShips-Starfield.esm", "ShatteredSpace.esm",
    ];

    public static List<string> Find(string dataDir)
    {
        var found = Fixed.Where(f => File.Exists(Path.Combine(dataDir, f))).ToList();
        // Bethesda's creations: SFBGS###.esm, their ship blueprints, and Trackers Alliance (sfta##.esm).
        var extra = Directory.EnumerateFiles(dataDir, "*.esm")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(n => n.StartsWith("SFBGS", StringComparison.OrdinalIgnoreCase)
                     || n.StartsWith("BlueprintShips-SFBGS", StringComparison.OrdinalIgnoreCase)
                     || System.Text.RegularExpressions.Regex.IsMatch(n, @"^sfta\d\d\.esm$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .OrderBy(n => n.StartsWith("Blueprint", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase);
        found.AddRange(extra);
        return found;
    }
}
