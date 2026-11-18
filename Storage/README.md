# History storage

Duty history lives in `history.sqlite` in the plugin's config folder. Settings stay
in the JSON config. New installs create SQLite history automatically.

`HistorySession` owns the in-memory records used by the tracker, windows, exports,
and PvP updates. `SqliteHistoryStore` loads and saves them. There is one storage
backend; JSON is used for settings and recovery files.

## Clearing history and keeping copies

**Clear all records** clears saved duties, both completed and incomplete, along with
their player details and notes. It applies to the whole saved history, regardless
of the current filters. It keeps the duty currently being recorded and your
settings. That current duty can still be added to history when it ends.

Clearing history doesn't delete CSV exports, upgrade backups, archived recovery
copies, or abandoned temporary files. These copies can still contain the old
duties, player names, and notes. An export already running also keeps the snapshot
it started with.

| File | When it's kept or removed |
| --- | --- |
| `history.sqlite` | The database stays in place. Clearing records removes saved duty/player rows but keeps the current duty. |
| `backups/history-before-v*.sqlite` | Created before supported database upgrades. Kept until removed manually; there is no age limit or scheduled cleanup. These aren't regular, per-duty backups. |
| `history-recovery-*.json` | Written after a failed or timed-out save. Compatible recovery data is loaded at startup. The current session's copy is removed after a successful save; a successfully handled startup recovery is removed too. |
| `history-recovery-*.json.stale` | An older recovery copy, kept for review rather than restored over newer history. Not deleted by clearing records or on a schedule. |
| `history-recovery-*.json.tmp` | A recovery write that didn't finish. Not loaded as history or removed by clearing records. |
| `*.csv` | Separate exports. Clearing history doesn't change them. |
| `*.csv.*.tmp` | Temporary export output, normally removed when the export finishes or fails. A process interruption can leave one behind; clearing history doesn't remove it. |

The clear action can't be undone in the plugin. Keep recovery files if a save failed
or history won't load; they may contain edits that never reached the database.
Unload Noumenon before replacing the history database with a backup.

## Schema

New databases create schema 12 directly:

- `duty_runs`: duty records, including the pending duty and its checkpoint.
- `duty_players`: party snapshots in their recorded order.
- `history_state`: the write revision used to detect another database writer.

Duty IDs stay stable. Updating a duty keeps its row ID, and deleting a duty removes
its party rows. Timestamps are stored in UTC and loaded as local time for display.
Checkpoints stay in UTC. Durations use .NET ticks, and unknown scoreboard deaths
stay null rather than becoming zero.

`pvp_roster_complete` tracks whether a new PvP scoreboard roster was fully imported.
False imports can resume after reload. `pvp_combat_stats_complete` records whether
the full source scoreboard also supplied teams, damage, kills, and assists. Previously
imported full rosters can gain those fields on reload. Null keeps older records
unchanged unless they already need missing scoreboard results recovered.

Player snapshots store the team, enemy/teammate relationship to the recording
player, damage dealt, kills, and assists. Missing scores and sides stay null.
Each duty stores its enemy-tracking choice, PvP mode, and the recording team's
placement. Each player also stores their own team's placement, rather than
inheriting the recorder's result. `pvp_results_complete` tracks finished player
results separately from combat scores. Older complete scoreboards get another
lookup for missing results; absent results stay null.

## Development database transition

The owner's schema-6 through schema-11 databases upgrade to schema 12.
Before changing it, the plugin writes a complete SQLite backup to the `backups`
subfolder. If that backup fails, the transition stops.

For schemas 6 and 7, the transition removes the embedded JSON archive, import-origin columns, and any
unused upload queue. It keeps duty/player values, IDs, row order, and the write
revision. The backup retains the original database, including the old JSON archive.
Older development schemas and JSON history imports aren't supported by this build.
Schemas 6 through 8 also gain the nullable roster-import flag. All supported schemas
gain the PvP fields. Existing duty/player values and revisions stay unchanged.
Schema 10 also gains the mode, results-complete flag, and per-player placement.
Schema 11 gains nullable damage taken and healing done per player, plus
`pvp_extended_stats_complete` on each duty. The flag lets older completed imports
receive those scores without repeatedly importing completed scoreboards. Missing
scores stay null. Backups are named `history-before-v12-*.sqlite`.

Backups are separate files. Clearing records leaves those backup files in place.

## Loading and settings

An existing profile requires its history database. If history can't load, the
plugin opens a storage-error screen and pauses recording and PvP updates. History
controls stay hidden until the database can load. Restore a missing database while
the plugin is unloaded, then reload it. Only a new profile can create an empty
database automatically.

Only one loaded Noumenon instance can use a history database at a time. A second
game client shows a message asking you to unload it in the first client and reload
it in the second. The `history.sqlite.lock` file stays on disk, but ownership is
released when the session finishes or its process exits; the file does not need deleting.

Startup saves settings only for a new profile or when duplicate content-type IDs
were removed, and only after history loaded successfully.

Settings saves write only the JSON settings. Duty edits and tracking events save
through the history session.

## Saving and recovery

The session copies duty/player fields into typed rows, then writes them on one
background worker. Rows are compared by value. Full saves follow completed runs,
edits, notes, deletions, and PvP updates. The 15-second save copies only the pending
duty. Queued pending updates are merged into the worker's full recovery snapshot.

Writes use transactions, foreign keys, and `synchronous=FULL`. A revision check
catches writes from another database session. History loads directly during startup.

Shutdown waits up to five seconds for queued saves. If that wait expires, the
session writes a full recovery copy and cancels further database work. An in-flight
transaction finishes or rolls back before the worker records its final revision
in that copy. Writing recovery data also takes disk time.

Failed saves are logged and leave a recovery JSON file. A recovery from the current
database revision is applied at startup and removed after a successful save. Older
copies are kept as `.json.stale` files. Conflicting or unreadable recoveries need
review. A copy without a revision is removed if it matches the database; changed
contents need review until its revision is known.

## Development checks

`StorageDiagnostics` is compiled only in debug builds. There is no public storage
check command.
