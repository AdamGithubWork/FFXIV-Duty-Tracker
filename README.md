# Duty Tracker

Keep track of your FFXIV duties and the players you meet.

Duty Tracker records your runs, party members, deaths, and wipes. Look through old
runs, see who you've played with before, or check your stats over time.

The plugin started as Mentor Roulette Tracker. It now tracks other duty types too.

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

## Tabs

### Records

Browse completed and incomplete runs. Click a duty to see the party, add a note,
or edit the wipe count and joined-in-progress flag. You can delete records and
export your history to CSV.

### Statistics

See players grouped by race, gender, job, status, home world, and datacenter.
Tables show encounters, unique players, and death stats. Datacenter grouping
currently covers North American home worlds; other worlds show as Unknown.

### Duty Analysis

See completion times, wipes, duties by type, and your most common duty.
The day-and-time heatmap shows when you run duties or how long those runs take.

### Summary

See milestones across your completed duties: fastest clears, most wipes, player
records, streaks, and busy days. Click a linked result to open that duty.
Milestones use duty type, duty search, and date filters. Player filters don't apply.

### People

See who you've met, how often, and their recorded deaths. Click a player to see
your duties together.

### Settings

Choose what to record, when to show the duty timer, whether to track alliance
members, and whether to include yourself in stats.

## Duty timer

The optional timer window shows your current duty and its average completion
time from previous completed runs that weren't joined in progress. Click the time to see those runs.
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
- Unknown death counts are left out of the player death averages that support them.
  Some other views still treat missing counts as zero.
- Your history only includes duties recorded while tracking was on.
- Jobs, statuses, and other player details reflect what the plugin saw during the run.

## Development

Built with C#, .NET 10, Dalamud, and ImGui. This is still a personal project in
development.

## Credits

By **Adalith**. See [third-party notices](THIRD_PARTY_NOTICES.md) for dependencies
and borrowed code.

Duty Tracker is an unofficial plugin for Final Fantasy XIV. It is not affiliated
with or endorsed by Square Enix.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD.
