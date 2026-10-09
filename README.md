# Mentor Roulette Tracker (Dalamud plugin)

Tracks, per duty run: duty name, duty type, whether it was already in
progress when you joined, wipe count, and how many Lalafell were in the
party. Exports to CSV so you can drop it into your spreadsheet.

## Setup

1. Install [XIVLauncher](https://goatcorp.github.io/) and enable Dalamud
   (you likely already have this if you use plugins).
2. Install Visual Studio 2022 (Community is fine) with the ".NET desktop
   development" workload.
3. Clone the **DalamudPluginProjectTemplate** or just drop these files into
   a new class library project targeting `net8.0-windows`.
4. Make sure the `DALAMUD_HOME` environment variable points at your
   Dalamud "dev" folder (usually
   `%AppData%\XIVLauncher\addon\Hooks\dev`). The `.csproj` here already
   falls back to that path if the env var isn't set.
5. Build in Release mode.
6. In-game, open the Dalamud plugin installer, go to "Dev Tools" /
   "Load from disk" (or add the build output folder as a custom repo),
   and load `MentorRouletteTracker.dll`.
7. Type `/mrt` to open the tracker window.

## Usage

- The plugin logs a record every time you enter and then leave/complete a
  duty while **armed**.
- Toggle arming with the checkbox in the window, or `/mrt arm` /
  `/mrt disarm` from chat. Arm it right before you queue, disarm it when
  you're done for the session if you don't want other duties logged.
- Click **Export CSV** to write a timestamped CSV file. It's saved to the
  plugin's config folder, and the full path is shown in the window after
  export (also logged via `/xllog` if you need to find it again).

## Known limitations (please read before relying on this)

- **"Joined in progress" detection**: this is inferred from whether the
  game's internal "duty started" flag is already true the instant you
  zone in. This is the standard signal for a rescue-queue join into an
  already-running duty, but it hasn't been tested against every duty
  type — spot-check the first few runs against what you actually saw.
- **Mentor Roulette specifically isn't auto-detected.** The game doesn't
  expose "which roulette you queued" in the stable Dalamud API in a
  reliable, version-safe way. Instead, the plugin logs *whatever duty
  you're in* while armed — arm it right before queuing Mentor Roulette
  and disarm it otherwise, and the DutyType column will still tell you
  Trial vs. Raid vs. Guildhest etc. for your own filtering.
- **Lalafell count** reads each party member's race byte from their
  character customization data. This works for members Dalamud can see
  as full character objects; in rare cases (e.g. a member not yet fully
  loaded when you zone in) it may undercount — the plugin waits a frame
  after you're marked as "bound by duty" to reduce this, but very slow
  loads could still be missed.
- **I couldn't compile or test this** in this environment (no access to
  the Dalamud SDK/NuGet feeds here), so treat it as a solid first draft.
  Dalamud's API shifts between API levels sometimes, so if something
  doesn't compile, the fix is almost always a renamed
  interface/property — check the current samples in
  `SamplePlugin` from the official
  `Dalamud-Plugin-Template` repo for the current names of
  `IDutyState`, `IPartyList`, `ICondition`, etc.

## Files

- `Plugin.cs` – entry point, wires up services and the `/mrt` command.
- `DutyTrackerService.cs` – the core event-driven tracking logic.
- `DutyRecord.cs` – the data model (matches your spreadsheet columns).
- `Configuration.cs` – persistence for all recorded runs.
- `CsvExporter.cs` – writes records out to CSV.
- `MainWindow.cs` – the in-game ImGui window (view, arm/disarm, export).
