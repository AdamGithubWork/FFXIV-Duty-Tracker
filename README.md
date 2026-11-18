# Noumenon

A duty tracker for FFXIV. Keep your past runs, see who you've played with, and look
through your stats.

Still a beta I'm sharing with friends, so expect bugs. If something looks wrong,
let me know what happened.

## Install

1. Open `/xlsettings` and go to **Experimental**.
2. Add this under **Custom Plugin Repositories**:

   ```text
   https://raw.githubusercontent.com/AdalithCode/FFXIV-Duty-Tracker/main/repo.json
   ```

3. Save, open `/xlplugins`, and search for **Noumenon** to install it.

## Using it

Open with `/noumenon` or `/nom`.
Use `/lastduty` to open your latest saved duty directly.

Recording is on by default and stays on until you turn it off. Your choice is
saved between reloads and restarts. Change it in Settings, or use `/nom disarm`
and `/nom arm`.

- **Records:** past duties, party details, and notes.
- **People:** players you've run into and your history with them.
- **Statistics, Duty Analysis, and Summary:** stats, milestones, and filters.
- **Settings:** choose what to record, which columns to show, and how it looks.

## PvP

Install and enable [PvP Tracker](https://github.com/wrath16/PvpStats) for PvP
recording. Without it, PvP recording stays disabled. You don't need to untick
anything, and PvE still works.

PvP results can take a few minutes to appear. Damage, healing, damage taken, and
their per-minute columns are available in Settings.

## History and updates

History is stored locally. CSV export and **Clear all records** are under
**Settings → History & exports**. Clearing records keeps your current duty,
settings, CSV exports, and older backup/recovery copies.

Update through `/xlplugins`. If an update gets a file-in-use error, disable
Noumenon, restart the game, update it, then enable it again.
