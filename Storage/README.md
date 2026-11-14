# SQLite foundation

`SqliteHistoryStore` creates/validates the schema and loads/saves history. `HistorySession`
owns the runtime records shared by tracking, windows, exports, and PvP enrichment.
Its default backend remains the legacy JSON configuration; SQLite mode is **off by default**.
Existing profiles are not automatically activated. No uploader is implemented.

With `UseSqliteHistory` activated in a separately approved profile, the plugin loads
`history.sqlite` from its configuration directory. It refuses a missing database, an
unapplied recovery file, or first-activation history that differs from the legacy data
still in the profile. There is no automatic empty fallback or automatic migration.
SQLite-mode settings serialization omits Records, SeenPlayers, and PendingDuty.

- `duty_runs`: one stable ID per run, all existing duty fields, and pending/checkpoint state.
- `duty_players`: ordered snapshots per run; identities are not deduplicated across history.
- `upload_queue`: reserved for future explicitly opted-in, name-free duty summaries.
- `PRAGMA user_version`: schema version. Unsupported databases are rejected rather than overwritten.
- `legacy_imports`: full original backup bytes, checksum, and counts; schema v2 adds this
  archive and import-origin columns without rebuilding existing history tables.
- `history_state`: schema v3 preserves the legacy seen-player cache and a write revision;
  the same migration adds a stable history ordering column to duty rows.

Timestamps are reserved for round-trip strings and durations for .NET ticks so a future
import can preserve original values. Nullable scoreboard deaths distinguish missing data from zero.
Initialization and the version marker are committed in one transaction. Foreign keys protect
relationships, and reopening a supported database does not recreate its tables.

Importing requires an expected SHA-256 checksum. Each duty gets an ID based on the source
checksum and source position so identical-looking duties are not collapsed. All rows and the
source archive are committed in one transaction after comparison against the source values.
A repeat of the exact same backup verifies the rows and inserts nothing; a different snapshot
or nonempty unrelated history is rejected. Missing or edited imported rows fail verification
rather than being silently repaired. Original unknown fields, settings, and the SeenPlayers
cache are preserved in the byte-for-byte source archive.

Before connecting this to tracking, verify a trial import of the user's backup. Do not remove
legacy history until the imported copy has been checked and a separate switch-over approved.
Tracking and UI accesses now use the shared history session. Live activation still requires
an up-to-date verified import, native-library testing inside Dalamud, and explicit approval.

SQLite writes run on one background worker using immutable snapshots. Pending writes are
coalesced; changed duty rows are upserted in transactions, deleted records cascade to their
snapshots, and shutdown flushes outstanding edits. Revision checks reject a stale writer.
An I/O failure is logged and creates a separately named recovery JSON file; an unresolved
failure is surfaced by Flush. A successful retry removes only its own recovery copy.

History is currently loaded into memory and captured on the caller's state thread. SQL
pagination/aggregation and further snapshot optimizations remain future performance work.
The original import archive is intentionally retained even after normal history deletion;
full backup/archive erasure needs an explicit policy before public release.

Run `/dt storagecheck` in-game to test the bundled SQLite runtime against a disposable
database. Look in Dalamud's log for `SQLite storage check passed`. The command never opens
the live history database or changes the configured storage mode.

