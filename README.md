# Duty Tracker

Keep track of your FFXIV duties and the players you meet.

Duty Tracker records your runs, party members, deaths, and wipes. Look through old
runs, see who you've played with before, or check your stats over time.

The plugin started as Mentor Roulette Tracker. It now tracks other duty types too.

## Requirements

- FFXIV with XIVLauncher and Dalamud, using API 15.
- PvP Tracker (PvPStats) for full PvP rosters and scoreboard deaths. Other tracking works without it.

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
PvP recording requires PvP Tracker (PvPStats) to be enabled in Dalamud. Having an
old PvPStats database isn't enough. Settings shows whether PvP recording is available;
PvE recording works without PvPStats. If PvPStats unloads during a recorded duty,
player capture pauses and the duty already in progress is kept.

PvPStats results add the full scoreboard roster: up to 72 players in Frontline,
10 in Crystalline Conflict, and 48 in Rival Wings. Names, home worlds, jobs,
damage dealt, kills, deaths, assists, and teams are imported after the match.
Duty details show whether each player was a teammate or enemy, with your team's
placement when available. Each player keeps their own team's result, including
opponents. Damage is the scoreboard's total damage dealt, including
damage to non-player targets where the mode counts it. Players already recorded keep
their observed race, gender, and status. During PvP, Duty Tracker also checks nearby
players once a second for those details when their full name and home world are
available. Hidden identities are skipped. Players who weren't observed use Unknown
for those details; the scoreboard doesn't provide them. This also applies to older
matches, which can't have appearance details filled in afterward.
Observed players absent from a final result
are kept, so replacements or leavers can make a recorded roster larger.

Duty Tracker checks for saved results when a PvP duty finishes, including PvPStats
versions without the match-saved callback. First checks are after about one minute
for CC and five minutes for Frontline/Rival Wings, then every 30 seconds if needed.
Recent matches keep their 30-minute retry window; older unfinished imports get one
check after reload. Partial results add available players without marking the
roster complete. Fully imported scoreboards aren't imported again after reload.
Previously imported full rosters get another lookup to add missing combat scores,
teams, and player results when PvPStats still has them.
Older records with known deaths aren't bulk-backfilled automatically.

Settings > PvP has **Track opposing members in PvP matches**, enabled by default.
Turn it off before entering a duty to capture and import teammates only. Players
whose side can't be confirmed are skipped while it's off. The choice is saved with
each duty, so delayed imports use that duty's setting. Existing history isn't deleted.
PvPStats imports your whole team regardless of the alliance-tracking setting.

Choose scoreboard columns under Settings > Table columns > PvP players. Records
also has optional columns for your PvP scores and placement. People has optional
PvP damage, kills, assists, and teammate/enemy encounter totals. Those totals use
the current filters and only include known scores; '-' means unknown.

Statistics tables, People, and Duty Analysis have optional PvP columns under
Settings > Table columns. Choose damage dealt, kills, assists, wins, losses, win
rate, and separate Frontline 1st/2nd/3rd counts and percentages. Statistics and
People count each matching player encounter. Duty Analysis counts your result and
scores once per matching duty. These are results from recorded encounters, not a
player's lifetime win rate.

Statistics uses average damage, kills, and assists for every group, including
race, gender, job, status, home world, and datacenter. Each average combines the
group's scores and divides by encounters with a known score. Missing scores are
skipped; zero scores count. Damage and combat averages are numbers, while win rate
and placement rates are percentages. People, Duty Analysis, and Summary keep totals.

1st place counts as a win. Frontline 2nd and 3rd count as losses for win rate while
keeping their own counts and percentages. Win rate uses known PvP results only;
Frontline placement percentages use known Frontline results only. Missing results
don't count as losses. CC and Rival Wings show Win/Loss in duty details.

Summary adds toggleable PvP damage, kill, and assist milestones, plus your win rate
and Frontline placements. Choose them under Settings > Milestones. Damage dealt,
kills, and assists under Your lifetime PvP stats show your personal totals and
single-match bests across completed recorded PvP duties. Lifetime deaths, damage
taken, and healing are available too. Duty type, PvP mode, and duty-name filters
apply; date ranges and Include yourself do not. They use each duty's recording character and home world, not
teammates or enemies. Other milestones use duty/date filters, and your results
count once per duty.

Damage taken and Healing done are optional columns alongside damage dealt in
Records, PvP details, People, Duty Analysis, and grouped Statistics. Statistics
uses averages; the other tables use totals. Healing uses PvPStats' HP restored
score. Missing scores stay unknown and don't count as zero. Older imported matches
get another lookup for these scores when PvPStats still has them.

Interrupted duties stay pending while login and loading states settle. A pending
duty resumes only for the same character name and home world. If its recorder
can't be verified, it is kept as an incomplete record at its last checkpoint.
Resumed duty durations include time spent offline.

## Tabs

### Records

Browse completed and incomplete runs. Click a duty to see the party, add a note,
or edit the wipe count and joined-in-progress flag. You can delete individual
records here. Export CSV and Clear all records are in Settings > History & exports.
Both actions cover all saved records regardless of filters. Clearing records still
requires confirmation.

### Statistics

See players grouped by race, gender, job, status, home world, and datacenter.
Tables show encounters, unique players, and death stats. Datacenter grouping
currently covers North American home worlds; other worlds show as Unknown.
Use Settings > Statistics sections to show or hide the overview, race, gender, job, status,
home world, and datacenter sections. Your choices are saved; all sections start visible.

### Duty Analysis

See completion times, wipes, duties by type, and your most common duty.
The day-and-time heatmap shows when you run duties or how long those runs take.

### Summary

See milestones across your completed duties: fastest clears, most wipes, player
records, streaks, and busy days. Click a linked result to open that duty.
Milestones use duty type, duty search, and date filters. Player filters don't apply.
Use Settings > Milestones to show or hide individual milestones by category.
Your choices are saved. Show all milestones restores everything.

### People

See who you've met, how often, and their recorded deaths. Click a player to see
your duties together.

### Settings

Turn duty recording on or off, choose what to record and when to show the duty
timer, and set player tracking preferences. Choose your visible statistics and
milestones here. The heatmap metric selector is in Duty Analysis.

Reset all settings restores recording preferences, colors, columns, milestones,
and filters after confirmation. Completed records, pending duties, and notes stay
saved. PvP recording still requires PvPStats to be enabled after a reset.

PvP duty details show teammate/enemy labels and results without team names such as
Astra or Umbra. Duty tables size themselves to their rows, with scrolling for
larger lists.

Settings > Appearance lets you change window and panel backgrounds, table headers,
and row colors, with a separate Opacity slider and a live table preview. New custom
colors start fully opaque. Make colors opaque keeps the selected colors and sets
their opacity to 100%. Reset a single color or use Reset colors to restore the
defaults. Appearance choices are saved. Headers stay on one line until
columns are very narrow; you can also turn wrapping off. Hover a clipped header
or table value to read its full text.

Settings > Table columns controls the visible, sortable columns in Records, People,
Stats by duty, and each grouped Statistics table. Existing columns start visible;
new optional columns start hidden. Use Reset columns under a table to restore its
defaults. The identifying duty, player, or group columns stay visible.

Records and Stats by duty can show sprouts, mentors, players, and known deaths.
Player-based counts follow player filters and Include yourself; mentor counts
include Trade Mentors. People can also show first/last seen, different duties,
known-death and deathless encounters, and sprout/mentor encounters. These values
use encounters matching the current filters. Unknown deaths don't count as deathless.
Column choices are saved in settings. Damage and DPS columns aren't available yet.

## Duty timer

The optional timer window shows your current duty and its average completion
time from previous completed runs that weren't joined in progress. Click the time
to see the full runs used for that average. The average refreshes after record edits.
Choose which duty types show the window in Settings.

## Filters

Expand the Filters panel below the active data tab. Duty/date and player controls
are grouped separately, with multi-select dropdowns for categories. The panel is
hidden in Settings. Summary shows only the duty and date controls it uses.
A colored end-of-filters bar stays below the panel, outside its scrolling area.

Filter by duty type, duty name, player name or home world, job, race, gender,
status, and date. You can use a preset date range or enter your own dates.
Under Duty types > PvP, select Frontline, Crystalline Conflict, or Rival Wings.
Multiple modes can be selected, alongside other duty types. The PvP checkbox selects
all modes; its indented checkboxes narrow the selection. Known Frontline and Rival
Wings map names identify existing records before a scoreboard is imported; remaining
named PvP duties without an imported mode are treated as CC. Clear filters resets
both duty types and modes.

Duty types start with Dungeons, Trials, Extreme Trials, Raids, Savage Raids,
Ultimate Raids, Alliance Raids, PvP, and Guildhests. Additional recorded categories
follow those. Names containing (Ultimate) match Ultimate Raids even in older records.
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
Summary uses duty and date filters. Personal lifetime PvP stats use duty filters
across all dates.

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
