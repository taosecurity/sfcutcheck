// FormIDs as players type them (code review finding 1, 2026-11-11: light and medium load-order FormIDs used to keep
// part of the plugin's slot as the record ID).
using SfCutCheck;
using Xunit;

namespace SfCutCheck.Tests;

public sealed class FormIdTests
{
    [Theory]
    [InlineData("28AE15:Starfield.esm", "28AE15:Starfield.esm")]
    [InlineData("0x28AE15:Starfield.esm", "28AE15:Starfield.esm")]
    [InlineData("Starfield.esm:28AE15", "28AE15:Starfield.esm")]
    [InlineData("8AE15:Starfield.esm", "08AE15:Starfield.esm")]
    [InlineData("0128AE15:Mod.esm", "28AE15:Mod.esm")]         // full plugin in slot 01
    [InlineData("FE001800:Light.esm", "000800:Light.esm")]      // light plugin slot 001, record 800
    [InlineData("FE01A812:Light.esm", "000812:Light.esm")]      // light plugin slot 01A, record 812
    [InlineData("FD01ABCD:Medium.esm", "00ABCD:Medium.esm")]    // medium plugin slot 01, record ABCD
    [InlineData("0xFD020800:Medium.esm", "000800:Medium.esm")]
    public void Parses(string text, string expected)
    {
        Assert.True(FormIds.TryParse(text, out var key));
        Assert.Equal(expected, key.ToString());
    }

    [Theory]
    [InlineData("CUT_Skill_Mindfulness")]
    [InlineData("XYZ:Starfield.esm")]
    [InlineData("123456789:Starfield.esm")]
    [InlineData(":Starfield.esm")]
    public void RejectsWhatIsntAFormId(string text) => Assert.False(FormIds.TryParse(text, out _));
}
