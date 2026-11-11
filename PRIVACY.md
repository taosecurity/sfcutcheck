# Privacy

sfcutcheck runs on your own computer. It collects no data and sends nothing anywhere.

## What it does not do

- It makes no network connections.
- It has no telemetry, analytics, crash reporting or update checks.
- It never changes a plugin or any game file.

## What it reads

- The official plugins in the game's Data folder (Starfield.esm, the DLC and Bethesda's creations), from `--data`,
  Steam's usual folder, or Game Pass's usual folder.
- With `--plugin`, that plugin and its masters (from the plugin's own folder, any `--masters-dir` folders, and the
  Data folder).
- The game's string tables, to show in-game names in the reports.

## What it writes

- The reports `report.csv`, `report.json` and `report.html`, in the folder you give `--out` (`out\scan` under the
  current folder by default).
- With `--log <file>`, a troubleshooting log at that path. It holds the Data folder path, plugin names, sizes and
  dates, and counts. No personal data beyond the folder paths on your computer.

- The saved scan of the official files, `%LOCALAPPDATA%\sfcutcheck\official-<key>.txt` (about 5 MB): a list of
  official record IDs and how far the scan reached them, so `scan --plugin` can skip re-reading the game. It's made
  by a plain `scan` or the first `scan --plugin`.

Nothing else is written. Delete the report folder, the log file and the `%LOCALAPPDATA%\sfcutcheck` folder to remove
everything sfcutcheck made.
