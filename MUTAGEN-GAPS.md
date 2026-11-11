# What the plugin reader can't read, or doesn't tell us

sfcutcheck reads plugins with Mutagen.Bethesda.Starfield 0.54.4 (https://github.com/Mutagen-Modding/Mutagen). This
page lists what that leaves out on the official files (18 plugins from the Steam version, checked 2026-11-11), and
what could be improved. It names FormIDs and EditorIDs only, never record contents.

Run `sfcutcheck spike` to see the same lists on your copy of the game.

## 1. Records whose links can't be read (7)

`EnumerateFormLinks()` throws on these records, all in Starfield.esm:

| Record | Error |
|---|---|
| Cell 000025 | ArgumentOutOfRangeException |
| NavigationMesh 110AD3 | ArgumentOutOfRangeException |
| NavigationMesh 14FC69 | OverflowException |
| NavigationMesh 17FEC6 | OverflowException |
| NavigationMesh 1BA29E | ArgumentOutOfRangeException |
| NavigationMesh 1BA29F | OverflowException |
| NavigationMesh 2F01CA | ArgumentOutOfRangeException |

**Effect:** small.
- The cell probably fails because one of its navmeshes does: a cell reports its contents' links as its own.
  sfcutcheck keeps the records inside a failed record linked, so nothing placed in Cell 000025 loses its path.
- Navmesh links only matter for navmesh neighbours and doors.
- All 7 records are inside reached cells, so none shows as CannotCheck in the report. If one ever does, the report
  says so; it never calls it cut.

**To improve:** find the field that throws (it's in navmesh data) by reading one of these navmeshes with the Mutagen
source in a debugger, then report it to Mutagen if it isn't fixed in a later version.

## 2. Links to records no plugin defines (214, plus 11,065 expected)

- **11,065 expected:** these links go to FormIDs below 0x800 in Starfield.esm, the engine's own hard-coded forms
  (PlayerRef 000014, built-in actor values and keywords). No plugin defines them, so they're expected.
- **214 go elsewhere:**
  - 160 come from one Terran Armada cell, `SFTERAssaultShip001DestroyedCreatedExterior` (0AB27A:SFBGS050.esm), and
    point at SFBGS050 IDs that don't exist.
  - 28 come from quests (for example UC08 and SE_TradeShip_LIST).
  - 17 come from SFBGS00D navmeshes.
  - The rest are single links: GenericBaseForm MQ_PlayerStarbornShip, SurfacePattern PatternVecteraMineVideoWorld,
    and Planet AlgorabIIPlanetData pointing into SFBGS050.
- **Likely cause:** leftovers in Bethesda's data (records deleted but still linked). A plugin reader mistaking a
  field for a FormID is possible but not shown.
- **To improve:** read the Terran Armada cell's raw subrecords and check whether those 160 values are really FormID
  fields. If they aren't, it's a field definition to report to Mutagen.

## 3. What the link list doesn't tell us

`EnumerateFormLinks()` gives the linked FormKey only, not the field it came from.

1. **Conditions:** handled. sfcutcheck finds each record's conditions by reflection (`src/Conditions.cs`), and links
   found only in conditions become "checks". A check on content (a quest, item, NPC, spell...) gives Maybe. A check
   on logic data (a condition record, global, form list...) reaches it, because the game reads that data to run the
   check.
2. **Owner fields point up:** handled by hand. A scene names its quest, and a story manager node names its parent;
   `Rules.OwnersOf` adds the reverse link for 13 such fields. A field that's missing from that list makes a whole
   subtree look unreached. For example, the location parent field was missing at first, and 3,340 locations came out
   unreferenced until it was added.
3. **Script code isn't read.** Links made only inside Papyrus (`Game.GetFormFromFile`, forms found at run time) are
   invisible. Script properties are seen. A later version could scan compiled scripts for FormID constants.

## 4. Fields Mutagen 0.54.4 doesn't read

Mutagen 0.54.4 is known to lose some Starfield fields when writing plugins. sfcutcheck only reads, so the question is
whether a field it skips holds a link. None of the test answers (known cut items, and items known to be in the game)
depended on one. Check again if a known cut or reachable item comes out wrong.
