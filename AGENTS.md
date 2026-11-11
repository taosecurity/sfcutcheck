# sfcutcheck: instructions for AI assistants

These steps are for an AI assistant (or anyone) helping a Starfield player or mod author use sfcutcheck. Every
command here was run once on sfcutcheck 0.1.6 before release. The [README](README.md) has the full description.

## 1. What it is for

sfcutcheck reads Starfield's plugins and reports records the game can never use: cut content in the official files,
or records a mod adds that nothing uses. It starts from the places the game begins using content (quests that start
with the game, the story manager, worlds and cells, default objects, Gameplay Options groups, planets, the player),
follows every link, and lists what it can't reach, each with a verdict and a reason.

Use it:
- when someone asks what was cut from Starfield, or whether a record is used;
- when a mod author wants to find unused records in their own plugin (`scan --plugin`);
- to explain why one record is or isn't used (`why`).

Don't use it:
- to check a plugin for errors (use a plugin checker such as sfmodcheck or xEdit's Check for Errors);
- to find conflicts between mods;
- to fix or clean a plugin (it never changes files);
- for games other than Starfield.

## 2. Prerequisites

1. Windows 10 or 11 (x64) for the stand-alone exe; nothing else is needed, because .NET is built in. Or, to build from
   source on Windows or Linux, the .NET SDK 9.0 or later (`dotnet --version` prints 9.x or higher).
2. Starfield installed, so its `Data` folder (the one holding `Starfield.esm`) can be read. sfcutcheck only reads it.
3. About 3 GB of free memory. A run takes 2 to 3 minutes.

## 3. Install

Pick one. Files come from https://github.com/taosecurity/sfcutcheck/releases (version 0.1.6 shown here).

1. **Stand-alone exe (Windows):** download `sfcutcheck-0.1.6-win-x64.zip` and its `.sha256` file, check the checksum,
   and unzip it:

       certutil -hashfile sfcutcheck-0.1.6-win-x64.zip SHA256
       mkdir C:\Tools\sfcutcheck
       tar -xf sfcutcheck-0.1.6-win-x64.zip -C C:\Tools\sfcutcheck

   Compare the printed hash with the one in `sfcutcheck-0.1.6-win-x64.zip.sha256`. The program is then
   `C:\Tools\sfcutcheck\sfcutcheck.exe`.

2. **From source (Windows or Linux):**

       git clone https://github.com/taosecurity/sfcutcheck
       cd sfcutcheck
       dotnet build src -c Release
       dotnet test tests/SfCutCheck.Tests

   The program is then `src/bin/Release/net9.0/sfcutcheck` (`sfcutcheck.exe` on Windows). The tests need no game
   files: 56 pass.

## 4. Prove the install works

    sfcutcheck --version

Its first line is `sfcutcheck 0.1.6 by Richard Bejtlich - GPL-3.0 - https://github.com/taosecurity/sfcutcheck`
(two credit lines follow). Every other command prints that same first line on stderr. Then run the first real
command (step 5.1) to prove it can read the game.

## 5. Common tasks

Below, `sfcutcheck` stands for the full path of the program, for example `C:\Tools\sfcutcheck\sfcutcheck.exe`. Add
`--data "<Data folder>"` to any command when the game isn't found by itself (step 6). The `C:\Temp\...` folders in
the examples are only examples: `--out` and `--log` take any folder the user can write to, and `--out` makes the
folder if it doesn't exist.

1. **List cut and unused content in the official files:**

       sfcutcheck scan --out C:\Temp\cutscan

   It writes `report.html` (open it in a browser), `report.csv` and `report.json` to `C:\Temp\cutscan`, and prints a
   summary by verdict and record type.

2. **Only some record types:**

       sfcutcheck scan --types "Weapon,Armor,Quest" --out C:\Temp\cutscan-items

   Quote the list in PowerShell. Type names are Mutagen's: Weapon, Armor, Quest, Npc, Book, MiscItem, DialogTopic,
   Scene, Spell, Perk, LeveledItem and so on. The summary table shows the names used.

3. **Find unused records in a mod (mod authors):**

       sfcutcheck scan --plugin MyMod.esm --out C:\Temp\mymod-cut

   Give a plugin name from the Data folder, or a full path to a plugin anywhere (a build folder). For Mod Organizer 2,
   give the full path and add `--masters-dir "<mod folder>"` for each folder that holds one of its masters. The last
   line starts `RESULT:`. Exit code 1 means some records the mod adds are Orphaned or Unreferenced; 0 means none.

   Tell the user that the official files are scanned too, because a mod's records are usually reached through
   Bethesda's records. The first run takes 2-3 minutes and saves that scan. Later runs for any mod take seconds while
   the official files are unchanged. The console says "against the saved scan" when it's used. `--no-cache` scans the
   official files again.

4. **Explain one record or several:**

       sfcutcheck why CUT_Skill_Mindfulness 0DF320:Starfield.esm

   These are two different records: a skill given by its EditorID (CUT_Skill_Mindfulness, FormID 28AE15), and one of
   Sam Coe's companion chat quests given by its FormID (0DF320). Give EditorIDs, or FormIDs as `FormID:Plugin.esm`.
   A full load-order FormID as the console shows it works too when the plugin name is given: `0128AE15:Starfield.esm`
   for a full plugin, `FE001800:MyLight.esm` for a light one, `FD01ABCD:MyMedium.esm` for a medium one.

   For each record it prints the verdict, the path from a starting point (if reached), the records that link to it
   (with their verdicts), and the records it links to (with theirs), up to 20 of each.

5. **Search by EditorID:**

       sfcutcheck find Flamethrower Trident --max 40
       sfcutcheck find cut unused --word --types "Weapon,Ammunition"

   It lists matching records grouped by verdict, with counts. `--word` matches whole words in the EditorID.

6. **Make a troubleshooting log:**

       sfcutcheck scan --out C:\Temp\cutscan --log C:\Temp\sfcutcheck.log

   `--verbose` prints the same lines in the terminal instead, each starting with `log:`.

7. **Read only some plugins** (for example a quick test on one official creation):

       sfcutcheck scan --plugins "Starfield.esm,SFBGS007.esm" --out C:\Temp\cut-sfbgs007

## 6. Inputs

- **The Data folder:**
  - `--data`, or if it isn't given, Steam's usual folder
    `C:\Program Files (x86)\Steam\steamapps\common\Starfield\Data`, then wherever Mutagen's install finder says the
    game is (other Steam libraries, the Xbox app), then Game Pass's usual `<drive>:\XboxGames\Starfield\Content\Data`
    on each drive. The `--log` file says which folder was used and why.
  - Vortex links mods into the real Data folder, so `--plugin MyMod.esm` works as is.
  - Mod Organizer 2 keeps mods in their own folders: see task 5.3.
- **The official plugins:** Starfield.esm, Constellation.esm, OldMars.esm, BlueprintShips-Starfield.esm,
  ShatteredSpace.esm, SFBGS*.esm, BlueprintShips-SFBGS*.esm and sfta##.esm, whichever are in the Data folder. Use
  `--plugins a.esm,b.esm` to read others instead.
- **Options:**
  - `--types`: limit the report to these record types.
  - `--all-types`: also report structure records (placed references, navmeshes, keywords).
  - `--out <folder>`: where the reports go, `out\scan` under the current folder by default.
  - `--max N`: the most rows `find` lists for each text (shared between the verdicts; default 40).
  - `--word`: whole-word matching in `find`.
  - `--skip Type,Type`: leave record types out of the index.
  - `--no-cache`: with `scan --plugin`, scan the official files again instead of using the saved scan.
- **The saved scan of the official files:** `%LOCALAPPDATA%\sfcutcheck\official-<key>.txt` (or the folder in the
  `SFCUTCHECK_CACHE_DIR` environment variable). It holds about 180,000 lines (FormID and tier). It's safe to delete,
  and it's rebuilt when the official files' sizes or dates change.

## 7. Outputs

- **`report.html`**, **`report.csv`**, **`report.json`** in `--out`. One row per reported record:
  - `verdict`, `type`, `formkey` (`FormID:Plugin.esm`), `plugin`, `editorid`, `name` (in-game name, English);
  - `hint` (a Bethesda marker word in the EditorID, such as cut or zz);
  - `reason` (the rule that decides the verdict);
  - `linked_from` (how many records link to it) and `first_links_from` (up to 3 of them).
- **The verdicts:**
  - **Unreferenced:** nothing links to it.
  - **Orphaned:** only unreached records link to it.
  - **DevOnly:** only test or debug cells lead to it.
  - **Maybe:** reached only through a recipe, research project, engine-picked record or condition check.
  - **CannotCheck:** its links couldn't be read. Never treat this as cut.
- **Reached records** are counted in the summary but not listed.
- **Explaining results to the user:** [RESULTS.md](RESULTS.md) has each verdict in plain words with a real example,
  what it means for players, and the steps for using cut content in a mod. Quote from it, and check one record
  with `why` before telling the user it's cut.
- **The console** shows a summary: plugins read, root counts per rule, records by verdict, and the reported records
  by type and verdict, then the report paths. With `--plugin`, a `RESULT:` line comes last.
- **Exit codes:**
  - 0: done.
  - 1: `why` couldn't find a record, or `scan --plugin` found records nothing reaches.
  - 2: couldn't run. The message on stderr says what to change: an unknown option, no Starfield.esm in the Data
    folder, an `--out` or `--log` folder it can't write to, or a plugin or master not found.
- **The log:** only with `--log <file>` (written there, with times) or `--verbose` (stderr). It never changes stdout.

## 8. Limits

- **It never** changes, cleans or writes any plugin or game file, connects to the internet, or uploads anything.
- **Scripts aren't read.** A record only a script fetches by ID (`Game.GetFormFromFile`) looks unused. Script
  properties are read. Treat Orphaned or Unreferenced as "nothing in the plugins uses it", not proof that no script
  does.
- **Scenes and topics:** every one inside a running quest counts as used.
- **Interior cells** count as starting points, so a cut interior with no door isn't found.
- **Overrides:** links from every version of a record count.
- **The reports** list FormIDs, EditorIDs and names from Bethesda's plugins. They're made from the user's own copy
  of the game; suggest the user doesn't post the whole files publicly.
- **Mutagen.Bethesda.Starfield 0.54.4** can't read a handful of official records; see
  [MUTAGEN-GAPS.md](MUTAGEN-GAPS.md).
- **Diagnostics:** `sfcutcheck spike` prints the index size, the time, the memory, and the records it couldn't read.
- **Starfield only.**
