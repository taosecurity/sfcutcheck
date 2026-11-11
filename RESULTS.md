# What the results mean

sfcutcheck sorts every record it reports into one **verdict**. This page explains each verdict in plain words,
with a real example from Starfield, what it means for people playing the game, and what a mod author can do with
it. The examples come from Starfield's official files as of November 2026, and `sfcutcheck why <EditorID>` shows the
evidence for any of them.

First, a word about how the tool thinks. The game starts using content from a few places: quests that start when
you start a new game, the story manager (which starts quests when things happen, such as arriving in a city), the
worlds and rooms you can walk into, the menus, and so on. sfcutcheck starts from those places and follows every link
from record to record: a vendor's container leads to its leveled lists, a leveled list leads to weapons, a weapon
leads to its ammo. **Anything it can't reach that way is something the game, as shipped, has no way to show you.**

## The verdicts

### Reached

**What it means:** a starting point leads to it. This is the normal case, so these records aren't listed in the
reports (they're counted in the summary).

**Example:** the EquiNOX laser rifle. A quest that starts with the game leads to it.

**In the game:** you can meet it in normal play.

### Maybe

**What it means:** it's reached, but only in a way the tool can't confirm from the files alone. Either it comes
from a crafting, building or modding recipe (available only when its workbench and conditions allow), from a
research project, or from a record the engine picks by its conditions (load screens, idle animations), or a
running record checks for it in a condition but nothing in the files gives it. The last case usually means a script
hands it out.

**Examples:**
- The outpost solar array ("Solar Array") is reached only through its building recipe. It's in the game, built
  from the outpost menu.
- `ArtifactPowerGravDash_Spell`, the Starborn power Grav Dash. The game checks whether you have it, but no record in
  the files gives it: a script does.

**In the game:** most Maybe records are in the game. Treat Maybe as "probably fine; check before calling it cut".

**For modders:** if you're hunting cut content, look elsewhere first. If you're checking your own mod, Maybe on
your recipes and craftable items is expected.

### DevOnly

**What it means:** it's reached only from Bethesda's test and debug areas: cells whose names say `QA`, `Test`,
`Debug` or `zz`, which players can't walk into in normal play.

**Example:** `AmmoPlaceholderBullet`, placed only in the `QASmoke` test cell, a developer test room.

**In the game:** not in normal play. You could visit a test cell with the console (`coc QASmoke`), but it's a
workshop, not content.

**For modders:** usually placeholders or test copies. They're useful as reference, but rarely worth restoring as
they are.

### Orphaned

**What it means:** something links to it, but only things the game itself never reaches. It belongs to a
disconnected group: it may be part of a finished piece of content whose top was never hooked up.

**Examples:**
- `COM_Companion_SamCoe_Convo_Neon01`, a conversation Sam Coe would have when you arrive in Neon. The quest, its
  dialogue lines and its scene are all there, linked to each other. But no story manager node starts it, while
  Andreja's and Sarah's matching chats are started. Eighteen such companion location chats (Barrett, Sam Coe and one
  of Sarah's) are in the same state.
- `CUT_AmmoMag`, a cut ammo type. One record links to it, but the game never reaches that record either.

**In the game:** you'll never see or hear it.

**For modders:** often the most rewarding find, because a whole group of records is ready and only the top link is
missing. `sfcutcheck why <record>` shows the group ("linked from" walks up the chain). Find its top record and
connect that one thing. For Sam's chat, that means adding the quest to the story manager the way Andreja's chats
are added.

### Unreferenced

**What it means:** nothing in the game files links to it at all. It's a complete record that's simply never used.

**Example:** `CUT_Skill_Mindfulness`, a skill named "Mindfulness". It's a full skill record, but no skill list,
quest or item gives it, so it never appears in the skill screen. Bethesda's own `CUT_` prefix agrees.

**In the game:** you'll never see it. It costs nothing but a little file space.

**For modders:** this is the cleanest kind of cut content. The record is finished, and it's just not connected.
Restoring it means adding one link from something the game uses (see "Using cut content" below).

### CannotCheck

**What it means:** the plugin reader couldn't read this record's links, so the tool doesn't know.

**In the game:** unknown. **Never treat it as cut.**

## Hints

A **hint** appears when an EditorID carries a word Bethesda uses for unfinished or cut work: `CUT`, `zz`, `Unused`,
`Deprecated`, `Test`, `Debug`, `DoNotUse`, `DUPLICATE`. It's a clue, not a verdict. Some hinted records still count as used (scenes named
`zzLegacy...` sit inside a quest that runs), and many cut records have no hint at all.

## What the results don't mean

- **Not every Orphaned or Unreferenced record is "cut content".** Some are leftovers: an old copy of a record,
  a list nothing needs any more, a duplicate made in the editor. Some are building blocks (statics, pack-ins) only
  the editor used.
- **Scripts aren't read.** A record that a script fetches by its ID looks unused. The tool reads script
  *properties*, which covers most scripted content, but not every case.
- **Nothing in your game changes.** sfcutcheck only reads files. Cut content doesn't slow your game or cause bugs
  by being there.

## Using cut content in a mod

1. **Understand it.** Run `sfcutcheck why <EditorID>`. It shows what the record links to (its parts: models, sounds,
   other records) and what links to it, with each one's verdict. For an Orphaned record, follow "linked from"
   upward to find the top of its group.
2. **Check that the pieces exist.** Records point at models, textures and voice files, and sfcutcheck doesn't check
   those. Look for them in the game's archives (an archive browser, or xEdit's asset checks) before planning a
   restoration. Cut content is sometimes cut because its art or voice was never finished.
3. **Try it in the game first.** For an item, use the console: `help <name> 4` finds its full ID, then
   `player.additem <ID> 1`. For a quest, `startquest <EditorID>`. If it works, you know what you're restoring.
4. **Connect it with your own records, not by editing Bethesda's.** Put the item in your own leveled list, vendor or
   quest reward, or add it to a list with a script at run time. That avoids conflicts with other mods that edit the
   same vanilla records.
5. **Check your mod.** `sfcutcheck scan --plugin YourMod.esm` confirms that the records you added (and the restored
   content they reach) are now reached.
6. **Say what you restored.** Players like to know what's original Bethesda content and what's new. Your mod page
   can list the EditorIDs from the report.

## Reading the report page

- **The buttons at the top** show how many records have each verdict. Click one to show only those.
- **The type list and the search box** narrow the table. Search covers EditorIDs, in-game names, FormIDs and the
  Why text.
- **Why** gives the rule that decided the verdict. Hover over it for the rule's full meaning.
- **Linked from** lists up to three records that link to it, with the total.
- **The `report.csv` file** has the same rows for a spreadsheet, and `report.json` adds what was checked and the
  rules used.
