# History storage

`HistorySession` holds the history used by the tracker, UI, CSV export, and PvP
updates. `SqliteHistoryStore` reads and writes the database.

## Storage modes

JSON is still the default. Existing profiles aren't switched to SQLite automatically.
With `UseSqliteHistory` enabled, history loads from `history.sqlite` in the plugin's
config folder. Settings stay in JSON, without Records, SeenPlayers, or PendingDuty.

SQLite loading fails if the database is missing, a recovery file needs review, or
the first load doesn't match history still in the JSON config. There is no automatic
migration or fallback yet.

## Tables

- `duty_runs`: duty records, including the pending duty and a checkpoint column.
  The tracker doesn't write checkpoints yet.
- `duty_players`: party members for each duty, kept in their original order.
- `history_state`: the seen-player cache and write revision.
- `legacy_imports`: the original JSON backup, its checksum, and import counts.
- `upload_queue`: unused. No uploader is implemented.

Schema v2 added the import archive and source-record IDs. Version 3 added
`history_state` and `history_order`. `PRAGMA user_version` tracks the version.

Timestamps use round-trip strings; durations use .NET ticks. Missing scoreboard
death counts are stored as null, separate from zero.

## Importing JSON

The offline importer takes a backup and its expected SHA-256 checksum. Duty IDs
include the backup's checksum and record position, so identical-looking runs stay
separate. It checks the imported values before committing the transaction.

Importing the same backup again checks the existing rows without adding duplicates.
A different backup, missing imported rows, or edited imported values cause the
import to fail. The original JSON bytes are also stored, including settings and
fields the importer doesn't use.

Before switching a JSON profile to SQLite, import a current backup and check it.
The current code requires this manual step.

## Saving

The session copies history into a snapshot on the calling thread, then writes it
on one background worker. If another save is queued, the newer snapshot replaces it.
Changed duties are updated in a transaction. Deleting a duty also deletes its party
rows. A revision check catches writes from another database session.

Shutdown waits for queued saves. If a database save fails, the session logs the
error and writes a recovery JSON file. A successful retry removes that file.
Otherwise, the recovery file needs review before the next load.

## Current limits

All history is loaded into memory, and each save captures the whole history.
The pending-duty save runs every 15 seconds. Reducing that work is still on the list.

Clearing records doesn't remove the original JSON archive in `legacy_imports`.
That needs to be addressed before public release.

## SQLite check

Run `/dt storagecheck` in game. It checks loading, saving, editing, and deleting
using a temporary database, then removes the temporary files. It doesn't use your
history database or change storage modes. Look for `Duty Tracker: SQLite check passed.`
in Dalamud's log.
