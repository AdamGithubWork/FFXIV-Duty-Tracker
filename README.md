# Duty Tracker

A Dalamud plugin for **Final Fantasy XIV** that records your duty history, tracks player encounters and deaths, and provides detailed statistics about the duties you've completed and the players you've encountered.

Originally developed as Mentor Roulette Tracker, Duty Tracker has expanded into a general-purpose duty tracking and analysis plugin supporting dungeons, trials, raids, PvP, and other content.

## Features

### Duty Tracking

Automatically records information about your duties, including:

- Duty name and type
- Start time and time spent in duty
- Completion status
- Whether you joined in progress
- Number of wipes
- Number of Lalafell encountered
- Your selected job
- Party members and their information

Party member information includes:

- Character name and home world
- Job
- Race and gender
- Player status (mentor, sprout, returner, etc.)
- Recorded deaths

PvP support includes death statistics obtained from match scoreboards.

### Records

Browse your complete duty history in a sortable table.

- Sort by duty, type, job, date, duration, wipes, and more
- Click a duty to view detailed party information
- Add notes to individual duties
- Edit recorded wipe counts and join-in-progress status
- Delete individual records
- Export your history to CSV

### Statistics

Analyze the players you've encountered across your duties.

Available breakdowns include:

- Race statistics
- Gender statistics
- Combined race and gender statistics
- Job statistics
- World statistics
- Player status statistics
- Total encounters and unique players
- Death totals and deaths per encounter

Statistics can be filtered by duty type, date range, and player characteristics.

### Duty Analysis

Explore your duty history through aggregated statistics.

Includes:

- Total completed duties
- Average completion time
- Average wipes per duty
- Duties joined in progress
- Duties completed by content type
- Most frequently encountered duty
- Detailed comparisons between individual duties

**Duties by Day and Time** provides a heatmap showing when you complete duties, with options to display duty counts or average completion times.

### Summary & Milestones

Track up to **50 milestones and records** across your duty history.

Milestone categories include:

**Duty Records**
- Fastest and longest completed duty
- Most deaths and wipes in a duty
- Highest deaths per minute
- Most Lalafell, sprouts, and mentors encountered
- Most different races in a single duty

**Player Records**
- Most frequently encountered player
- Most deaths by an individual player in one duty
- Highest deaths per minute by one player
- Most cumulative deaths by a player
- Most deathless encounters

**Streaks & Droughts**
- Longest streak without a wipe
- Longest streak without an observed death
- Longest streak without encountering a Lalafell
- Consecutive duties containing wipes or deaths
- Consecutive duties below or above certain durations

**Activity Records**
- Most duties completed in a day, week, or month
- Most time spent in duties in one day
- Most player deaths or wipes observed in one day
- Most unique players encountered in one day
- Longest streak of active days

Duty-related filters allow you to view milestones for specific content, such as PvP-only duties.

Milestones associated with individual duty records can be clicked to open their full details.

### People

Browse individual players you've encountered throughout your duty history.

- Character name, world, race, and gender
- Total encounters
- Total recorded deaths
- Average deaths per encounter
- Sortable player statistics
- Click a player to view your shared duty history
- Open individual duty records directly from player history

### Duty Timer Overlay

An optional floating window appears during selected duty types.

- Displays the current duty name
- Shows your historical average completion time
- Uses completed duty records to calculate averages
- Click the average time to open previous runs of that duty
- Configure which duty types display the timer

### Filtering & Configuration

Global filters include:

- Duty type
- Duty name search
- Player name or world search
- Race
- Gender
- Player status
- Date range, including custom dates

Additional settings allow you to:

- Choose which duty types to record
- Configure duty timer visibility by content type
- Include or exclude your own character from statistics
- Enable or disable alliance member tracking

## Supported Content

Duty Tracker supports recording configured content types, including:

- Dungeons
- Guildhests
- Trials
- Raids
- PvP
- Deep Dungeons
- Ultimate Raids
- Variant and Criterion content through V&C Dungeon Finder
- Chaotic Alliance Raids

Some duty categories, including Extreme Trials, Savage Raids, and Alliance Raids, receive additional classification.

## Commands

| Command | Description |
|---|---|
| `/dt` | Open or close Duty Tracker |
| `/dt arm` | Enable duty recording |
| `/dt disarm` | Disable duty recording |

## Technical Details

Duty Tracker is developed in **C# and .NET 10** using the Dalamud plugin framework and ImGui.

It uses Dalamud events and game-state information to monitor duty activity, player encounters, and death-state transitions.

Historical records and settings are persisted between sessions. Statistical calculations use filtering, grouping, and indexed lookups to support analysis of recorded encounters.

## Limitations

- Death tracking depends on the availability of party and character state information. Some deaths may not be captured, particularly for players outside your immediate party.
- Historical statistics reflect only duties recorded while tracking was enabled.
- Player status and other character information depend on what was available when the encounter was recorded.
- Results should be treated as observational statistics rather than definitive measurements of player skill.

## Development

Duty Tracker is an ongoing personal project. Additional improvements and features may be added over time.

## Disclaimer

Duty Tracker is an unofficial third-party plugin for Final Fantasy XIV. It is not affiliated with or endorsed by Square Enix.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD.

## Author

**Adalith**
