# Duty Tracker

Keep track of your FFXIV duties and the players you meet.

Duty Tracker records your runs, party members, deaths, and wipes. Look through old
runs, see who you've played with before, or check your stats over time.

The plugin started as Mentor Roulette Tracker. It now tracks other duty types too.

## Requirements

- FFXIV with XIVLauncher and Dalamud, using API 15.
- PvP Tracker (PvPStats) for PvP scoreboard deaths. Other tracking works without it.

## Installation

Duty Tracker is being prepared for its first public release. An installer
repository hasn't been published yet.

For a manual test build, extract the whole release archive into its own folder.
Keep the dependency DLLs and the `runtimes` folder alongside the plugin. Load
`MentorRouletteTracker.dll` as a development plugin in Dalamud. Open `/dt` once loaded.

## What's recorded

- Duty name, type, start time, and duration
- Whether you finished or joined in progress
- Wipes and Lalafell count
- Your job
- Party members: name, home world, job, race, gender, status, and recorded deaths

You can choose which duty types to record and whether to track alliance members.
PvP scoreboard deaths come from PvPStats. If Duty Tracker reloads before those
results arrive, it looks up unfinished scoreboards again. Recent matches keep
their 30-minute retry window; older matches get one check for saved results.

Interrupted duties stay pending while login and loading states settle. A pending
duty resumes only for the same character name and home world. If its recorder
can't be verified, it is kept as an incomplete record at its last checkpoint.
Resumed duty durations include time spent offline.

## Tabs

### Records

Browse completed and incomplete runs. Click a duty to see the party, add a note,
or edit the wipe count and joined-in-progress flag. You can delete records and
export your history to CSV.

### Statistics

See players grouped by race, gender, job, status, home world, and datacenter.
Tables show encounters, unique players, and death stats. Datacenter grouping
currently covers North American home worlds; other worlds show as Unknown.
Use Customize statistics to show or hide the overview, race, gender, job, status,
home world, and datacenter sections. Your choices are saved; all sections start visible.

### Duty Analysis

See completion times, wipes, duties by type, and your most common duty.
The day-and-time heatmap shows when you run duties or how long those runs take.

### Summary

See milestones across your completed duties: fastest clears, most wipes, player
records, streaks, and busy days. Click a linked result to open that duty.
Milestones use duty type, duty search, and date filters. Player filters don't apply.
Use Customize milestones to show or hide individual milestones by category.
Your choices are saved. Show all milestones restores everything.

### People

See who you've met, how often, and their recorded deaths. Click a player to see
your duties together.

### Settings

Choose what to record, when to show the duty timer, whether to track alliance
members, and whether to include yourself in stats.

## Duty timer

The optional timer window shows your current duty and its average completion
time from previous completed runs that weren't joined in progress. Click the time
to see the full runs used for that average. The average refreshes after record edits.
Choose which duty types show the window in Settings.

## Filters

Filter by duty type, duty name, player name or home world, job, race, gender,
status, and date. You can use a preset date range or enter your own dates.
Job and world dropdowns allow multiple selections. Choose a role or North American
datacenter to select its whole group, or pick individual jobs and home worlds.
Choose All Jobs or All Worlds to clear that selection.

Minimum encounters filters People and Statistics by how many matching completed
duties you've shared with each player. Encounters with unknown deaths still count.
The default is 1; Clear filters resets it to 1. Records, Duty Analysis, and Summary
aren't affected by this minimum.

Player filters must match the same player. Records and Duty Analysis keep duties
with at least one matching player; Statistics and People count matching players.
Statistics keeps duties without a match when calculating encounter droughts.
Summary uses duty and date filters only.

## Supported duties

Dungeons, guildhests, trials, raids, PvP, Deep Dungeons, Ultimates, V&C Dungeon
Finder content, and Chaotic Alliance Raids. Extreme Trials, Savage Raids, and
Alliance Raids have their own categories.

## Commands

| Command | What it does |
|---|---|
| `/dt` | Open or close Duty Tracker |
| `/dt arm` | Start recording duties |
| `/dt disarm` | Stop recording new duties |

## Saved data

Settings are saved in the plugin's JSON config. Duty history is saved in
`history.sqlite` in the plugin's config folder. New installs create the database
automatically. See [Storage](Storage/README.md) for backup and storage details.

There are no uploads.

## Things to know

- Death tracking can miss deaths when party or character data isn't available,
  especially outside your own party.
- Death totals include known counts only. Unknown counts aren't treated as confirmed
  zeros, and death averages leave them out.
- Your history only includes duties recorded while tracking was on.
- Jobs, statuses, and other player details reflect what the plugin saw during the run.

## Building

Open `MentorRouletteTracker.slnx` in Visual Studio with the .NET 10 SDK installed.
Build **Release / x64**. The build reads Dalamud reference assemblies from
`%APPDATA%\XIVLauncher\addon\Hooks\dev`, or from `DALAMUD_HOME` when set.

DalamudPackager creates a `MentorRouletteTracker` folder inside the build output.
It contains `latest.zip` and the generated manifest. Distribute that archive with
all of its contents; the native SQLite library is in `runtimes`.

Unload Duty Tracker before rebuilding into a folder that Dalamud is watching.
Back up your existing settings and history before testing a new release build.

## Credits

Duty Tracker is licensed under the [MIT License](LICENSE).

By **Adalith**. See [third-party notices](THIRD_PARTY_NOTICES.md) for dependencies
and borrowed code.

Duty Tracker is an unofficial plugin for Final Fantasy XIV. It is not affiliated
with or endorsed by Square Enix.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD.
