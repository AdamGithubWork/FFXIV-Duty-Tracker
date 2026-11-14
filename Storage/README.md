# History storage

Duty history lives in `history.sqlite` in the plugin's config folder. Settings stay
in the JSON config. New installs create SQLite history automatically.

`HistorySession` owns the in-memory records used by the tracker, windows, exports,
and PvP updates. `SqliteHistoryStore` loads and saves them. There is one storage
backend; JSON is used for settings and recovery files.

## Schema

New databases create schema 8 directly:

- `duty_runs`: duty records, including the pending duty and its checkpoint.
- `duty_players`: party snapshots in their recorded order.
- `history_state`: the write revision used to detect another database writer.

Duty IDs stay stable. Updating a duty keeps its row ID, and deleting a duty removes
its party rows. Timestamps are stored in UTC and loaded as local time for display.
Checkpoints stay in UTC. Durations use .NET ticks, and unknown scoreboard deaths
stay null rather than becoming zero.

## Development database transition

There is one transition for the owner's existing schema-6 or schema-7 database.
Before changing it, the plugin writes a complete SQLite backup to the `backups`
subfolder. If that backup fails, the transition stops.

The transition removes the embedded JSON archive, import-origin columns, and any
unused upload queue. It keeps duty/player values, IDs, row order, and the write
revision. The backup retains the original database, including the old JSON archive.
Older development schemas and JSON history imports aren't supported by this build.
Once the owner's database is verified on schema 8, this transition can be retired.

Backups are separate files. Clearing records leaves those backup files in place.

## Loading and settings

An existing profile requires its history database. A missing database causes an
error instead of creating empty replacement history. Only a new profile can create
a new database automatically.

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

## SQLite check

`/dt storagecheck` checks SQLite with a temporary database. It doesn't open your
history database. Look for `Duty Tracker: SQLite check passed.` in Dalamud's log.
