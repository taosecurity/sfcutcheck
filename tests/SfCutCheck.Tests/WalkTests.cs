// A made-up plugin (no game files) with one record for each verdict, built in a temp folder:
//   SCT_StartQuest   start-game quest; a script property points at SCT_WeaponByScript       -> both Reached
//                    its conditions test SCT_WeaponChecked (only) and SCT_WeaponByScript    -> Maybe (checked-only) / still Reached
//                    and compare SCT_GlobalInCheck (logic data, read by the check)          -> Reached
//   SCT_Scene        a scene stored inside SCT_StartQuest ("contains")                       -> Reached
//   SCT_Message      a message that names SCT_StartQuest as its owner ("owns")               -> Reached
//   SCT_List         a form list nothing uses, holding SCT_WeaponOrphan                       -> Unreferenced / Orphaned
//   SCT_WeaponAlone  a weapon nothing links to                                                -> Unreferenced
//   SCT_Recipe       a recipe that makes SCT_WeaponRecipe                                     -> both Maybe
//   SCT_Cell         an interior cell with a placed SCT_WeaponPlaced                          -> Reached
//   zzTestCell       an interior cell with a placed SCT_WeaponDev                             -> DevOnly
using BuildKit;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Noggog;
using SfCutCheck;
using Xunit;

namespace SfCutCheck.Tests;

public sealed class WalkTests : IDisposable
{
    const string PluginName = "SctTest.esm";
    readonly string dir = Directory.CreateTempSubdirectory("sfcutcheck-test-").FullName;
    readonly Walk walk;
    readonly Dictionary<string, FormKey> keys = new();

    public WalkTests()
    {
        var mod = Plugin.New(PluginName, PluginStyle.Full, "test", "made-up plugin for sfcutcheck tests");
        Weapon W(string edid) { var w = mod.Weapons.AddNew(edid); keys[edid] = w.FormKey; return w; }

        var byScript = W("SCT_WeaponByScript");
        var propAndChildCheck = W("SCT_WeaponPropChildCheck");
        var quest = mod.Quests.AddNew("SCT_StartQuest");
        quest.Data = new QuestData { Flags = Quest.Flag.StartGameEnabled };
        quest.VirtualMachineAdapter = new QuestAdapter
        {
            Scripts = new ExtendedList<ScriptEntry>
            {
                new ScriptEntry
                {
                    Name = "SCT_QuestScript",
                    Properties = new ExtendedList<ScriptProperty>
                    {
                        new ScriptObjectProperty { Name = "Weapon", Object = byScript.ToLink<IStarfieldMajorRecordGetter>() },
                        new ScriptObjectProperty { Name = "Other", Object = propAndChildCheck.ToLink<IStarfieldMajorRecordGetter>() },
                    },
                },
            },
        };
        keys["SCT_StartQuest"] = quest.FormKey;
        // Conditions: SCT_WeaponChecked is only tested; SCT_WeaponByScript is tested and also used.
        var checkedOnly = W("SCT_WeaponChecked");
        foreach (var target in new[] { checkedOnly, byScript })
        {
            var data = new GetIsIDConditionData();
            data.FirstParameter.Link.SetTo(target.FormKey);
            quest.DialogConditions.Add(new ConditionFloat { Data = data });
        }
        // A global compared in a condition is read by the game, so a check reaches it.
        var global = mod.Globals.AddNew("SCT_GlobalInCheck");
        keys["SCT_GlobalInCheck"] = global.FormKey;
        quest.DialogConditions.Add(new ConditionGlobal { ComparisonValue = global.ToLink<IGlobalGetter>(), Data = new GetIsIDConditionData() });

        // A response inside the quest only checks SCT_WeaponPropChildCheck; the quest's own script property still uses it.
        var childCheck = new GetIsIDConditionData();
        childCheck.FirstParameter.Link.SetTo(propAndChildCheck.FormKey);
        var topic = new DialogTopic(mod) { EditorID = "SCT_Topic" };
        topic.Quest.SetTo(quest);
        topic.Responses.Add(new DialogResponses(mod) { Conditions = new ExtendedList<Condition> { new ConditionFloat { Data = childCheck } } });
        quest.DialogTopics.Add(topic);

        var scene = new Scene(mod) { EditorID = "SCT_Scene" };
        scene.Quest.SetTo(quest);
        quest.Scenes.Add(scene);
        keys["SCT_Scene"] = scene.FormKey;

        var message = mod.Messages.AddNew("SCT_Message");
        message.OwnerQuest.SetTo(quest);
        keys["SCT_Message"] = message.FormKey;

        var orphan = W("SCT_WeaponOrphan");
        var list = mod.FormLists.AddNew("SCT_List");
        list.Items.Add(orphan.ToLink<IStarfieldMajorRecordGetter>());
        keys["SCT_List"] = list.FormKey;

        W("SCT_WeaponAlone");
        // The mod's own Gameplay Options group with its debug switch, as BuildKit makes them for our mods.
        var debug = BuildKit.GameplayOptions.Debug(mod, "SCT_DebugOption", "test lines");
        keys["SCT_DebugOption"] = debug.FormKey;
        keys["SCT_Options"] = BuildKit.GameplayOptions.Group(mod, "SCT_Options", "SCT Test", [debug]).FormKey;

        var made = W("SCT_WeaponRecipe");
        var recipe = mod.ConstructibleObjects.AddNew("SCT_Recipe");
        recipe.CreatedObject.SetTo(made.FormKey);
        keys["SCT_Recipe"] = recipe.FormKey;

        var placed = W("SCT_WeaponPlaced");
        var dev = W("SCT_WeaponDev");
        var cell = new Cell(mod) { EditorID = "SCT_Cell" };
        cell.Temporary.Add(new PlacedObject(mod) { Base = new FormLink<IPlaceableObjectGetter>(placed.FormKey) });
        var devCell = new Cell(mod) { EditorID = "zzTestCell" };
        devCell.Temporary.Add(new PlacedObject(mod) { Base = new FormLink<IPlaceableObjectGetter>(dev.FormKey) });
        mod.Cells.Add(new CellBlock { SubBlocks = new ExtendedList<CellSubBlock> { new CellSubBlock { Cells = new ExtendedList<Cell> { cell, devCell } } } });

        using (var g = new GameData(dir)) PluginWriter.Write(mod, dir, g, new WriteOptions { GapCheck = false, FixEffects = false });
        var index = RefIndex.Build(dir, [PluginName], new HashSet<string>(), _ => { });
        walk = new Walk(index);
    }

    Verdict V(string edid)
    {
        Assert.True(walk.Index.TryGetId(keys[edid], out var id), $"{edid} isn't in the index");
        return walk.VerdictOf(id);
    }

    [Theory]
    [InlineData("SCT_StartQuest", Verdict.Reached)]
    [InlineData("SCT_WeaponByScript", Verdict.Reached)]
    [InlineData("SCT_WeaponChecked", Verdict.Maybe)]
    [InlineData("SCT_WeaponPropChildCheck", Verdict.Reached)]
    [InlineData("SCT_GlobalInCheck", Verdict.Reached)]
    [InlineData("SCT_Scene", Verdict.Reached)]
    [InlineData("SCT_Message", Verdict.Reached)]
    [InlineData("SCT_WeaponPlaced", Verdict.Reached)]
    [InlineData("SCT_List", Verdict.Unreferenced)]
    [InlineData("SCT_WeaponOrphan", Verdict.Orphaned)]
    [InlineData("SCT_WeaponAlone", Verdict.Unreferenced)]
    [InlineData("SCT_Options", Verdict.Reached)]
    [InlineData("SCT_DebugOption", Verdict.Reached)]
    [InlineData("SCT_Recipe", Verdict.Maybe)]
    [InlineData("SCT_WeaponRecipe", Verdict.Maybe)]
    [InlineData("SCT_WeaponDev", Verdict.DevOnly)]
    public void Verdicts(string edid, Verdict expected) => Assert.Equal(expected, V(edid));

    [Fact]
    public void CheckedOnlyNamesItsOwnRule()
    {
        walk.Index.TryGetId(keys["SCT_WeaponChecked"], out var id);
        Assert.Same(Rules.CheckedOnly, walk.ReasonOf(id));
        Assert.Equal(keys["SCT_StartQuest"], walk.Index.KeyOf(walk.Pred[id]));
    }

    [Fact]
    public void PathNamesTheRootAndEveryStep()
    {
        walk.Index.TryGetId(keys["SCT_WeaponByScript"], out var id);
        var path = walk.PathTo(id);
        Assert.Equal(2, path.Count);
        Assert.Equal(keys["SCT_StartQuest"], walk.Index.KeyOf(path[0]));
        Assert.Same(Rules.StartGameQuest, walk.RootOf[path[0]]);
    }

    [Fact]
    public void ContainerLinksArentCountedTwice()
    {
        // The cell holds the placed weapon; the weapon must be linked from the placed reference, not from the cell.
        walk.Index.TryGetId(keys["SCT_WeaponPlaced"], out var id);
        var inbound = walk.Inbound(id).ToList();
        Assert.Single(inbound);
        Assert.Equal("PlacedObject", walk.Index.TypeOf(inbound[0].From));
    }

    [Theory]
    [InlineData("zzTestCell", true)]
    [InlineData("QA_Smoke", true)]
    [InlineData("DebugRoom", true)]
    [InlineData("CUT_Dialogue", true)]
    [InlineData("NewAtlantis", false)]
    [InlineData("Contest01", false)]
    [InlineData("DevilsTower", false)]
    [InlineData("OldMars", false)]
    [InlineData("CutsceneIntro", false)]
    [InlineData("QATestCell02", true)]
    [InlineData("MQ101_DevRoom", true)]
    [InlineData("PackInStorageCellDUPLICATE000", false)]
    [InlineData("BlueprintExteriorxDoNotUseLvlShipTridentLines", false)]
    public void DevNames(string edid, bool dev) => Assert.Equal(dev, Rules.IsDevName(edid));

    [Theory]
    [InlineData("xDoNotUse_EncShip_TridentLines_M01", "donotuse")]
    [InlineData("LL_Weapon_CutlassDUPLICATE000", "duplicate")]
    [InlineData("CUT_AmmoMag", "cut")]
    [InlineData("NewAtlantis", null)]
    public void HintLabels(string edid, string? hint) => Assert.Equal(hint, Rules.Hint(edid));

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
