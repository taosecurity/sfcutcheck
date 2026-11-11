// Reading a FormID the way a player types it (from xEdit, the console or a wiki) into a FormKey.
using System.Globalization;
using Mutagen.Bethesda.Plugins;

namespace SfCutCheck;

public static class FormIds
{
    /// <summary>
    /// Accepts "0012AB:Starfield.esm", "0x0012AB:Starfield.esm" and "Starfield.esm:0012AB", and a full load-order
    /// FormID with the plugin's name ("0100A1B2:MyMod.esm", "FE001800:MyLight.esm", "FD01ABCD:MyMedium.esm").
    /// In a full FormID the plugin's slot is part of the number: a full plugin uses the top two hex digits (local ID =
    /// the low 6), a light plugin "FE" plus three digits (local ID = the low 3), a medium plugin "FD" plus two digits
    /// (local ID = the low 4). The slot is dropped, because the plugin's name already says which plugin it is.
    /// </summary>
    public static bool TryParse(string text, out FormKey key)
    {
        key = FormKey.Null;
        var parts = text.Trim().Split(':', 2);
        if (parts.Length != 2) return false;
        var (id, plugin) = parts[1].Contains('.') ? (parts[0], parts[1]) : (parts[1], parts[0]);
        id = id.Trim();
        if (id.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) id = id[2..];
        if (id.Length == 0 || id.Length > 8 || !uint.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
            return false;
        uint local;
        if (id.Length == 8)
            local = (n >> 24) switch
            {
                0xFE => n & 0xFFF,     // light: FE + 3-digit slot + 3-digit ID
                0xFD => n & 0xFFFF,    // medium: FD + 2-digit slot + 4-digit ID
                _ => n & 0xFFFFFF,     // full: 2-digit slot + 6-digit ID
            };
        else if (id.Length == 7)
            local = n & 0xFFFFFF;      // "1000800" style: a full FormID with its leading zero dropped
        else
            local = n;
        return FormKey.TryFactory($"{local:X6}:{plugin.Trim()}", out key);
    }
}
