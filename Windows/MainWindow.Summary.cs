
using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        // Rolling date filters can change even when no history is saved.
        private DateTime nextMilestoneRefresh = DateTime.MinValue;
        private long milestoneHistoryVersion = -1;
        private int milestoneRecordCount = -1;
        private string? milestoneFilterKey;
        private string? milestonePlayerName;
        private bool milestonesIncludeSelf;

        private sealed record MilestoneResult(string Text, DutyRecord? Record = null)
        {
            public static implicit operator MilestoneResult(string text) => new(text);
        }

        private readonly List<(string Title, List<(string Label, MilestoneResult Value)> Rows)> milestoneSections = new();

        private void RefreshMilestonesIfNeeded(string? localName)
        {
            var dutyFilterKey = string.Join("|",
                string.Join(",", selectedDutyTypes.OrderBy(x => x, StringComparer.Ordinal)),
                string.Join(",", selectedPvpModes.Order()),
                globalDutySearchText.Trim(),
                selectedTimeRange.ToString(),
                customStartDate.Date.ToString("yyyyMMdd"),
                customEndDate.Date.ToString("yyyyMMdd"), EncounterTargetRace, EncounterTargetGender);

            if (milestoneHistoryVersion != config.History.ChangeVersion ||
                milestoneRecordCount != config.History.Records.Count ||
                (selectedTimeRange is >= 1 and <= 3 && DateTime.UtcNow >= nextMilestoneRefresh) ||
                dutyFilterKey != milestoneFilterKey ||
                localName != milestonePlayerName ||
                milestonesIncludeSelf != config.IncludeSelfInStatistics)
            {
                RebuildMilestones(localName);
                nextMilestoneRefresh = DateTime.UtcNow.AddSeconds(1);
                milestoneFilterKey = dutyFilterKey;
                milestonePlayerName = localName;
                milestonesIncludeSelf = config.IncludeSelfInStatistics;
                milestoneHistoryVersion = config.History.ChangeVersion;
                milestoneRecordCount = config.History.Records.Count;
            }
        }

        private void DrawSummaryTab()
        {
            config.HiddenMilestones ??= new();
            RefreshMilestonesIfNeeded(objectTable.LocalPlayer?.Name.TextValue);

            try
            {
                if (ImGui.BeginChild("SummaryContent", Vector2.Zero, false,
                        ImGuiWindowFlags.HorizontalScrollbar))
                {
                    DrawSectionHeading("Milestones");
                    ImGui.TextDisabled("Your PvP stats cover all dates. Duty filters still apply.");
                    ImGui.Separator();

                    if (milestoneSections.Count == 0)
                        ImGui.TextDisabled("No completed duties with these filters.");
                    else
                    {
                        var anyVisible = false;
                        foreach (var section in milestoneSections)
                        {
                            var visibleRows = section.Rows.Where(row =>
                                !config.HiddenMilestones.Contains(MilestoneKey(section.Title, row.Label))).ToList();
                            if (visibleRows.Count == 0)
                                continue;
                            anyVisible = true;
                            ImGui.Spacing();
                            if (ImGui.CollapsingHeader($"{MilestoneTitle(section.Title)}##Lifetime{section.Title}",
                                    ImGuiTreeNodeFlags.DefaultOpen))
                            {
                                if (BeginStyledTable($"LifetimeTable##{section.Title}", 2,
                                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable))
                                {
                                    try
                                    {
                                        ImGui.TableSetupColumn("Record");
                                        ImGui.TableSetupColumn("Result");
                                        ImGui.TableHeadersRow();

                                        foreach (var row in visibleRows)
                                        {
                                            ImGui.TableNextRow();
                                            ImGui.TableSetColumnIndex(0);
                                            ImGui.TextUnformatted(EncounterMilestoneLabel(row.Label));
                                            ImGui.TableSetColumnIndex(1);

                                            if (row.Value.Record is DutyRecord record)
                                            {
                                                ImGui.PushID(row.Label);
                                                try
                                                {
                                                    if (ImGui.Selectable(row.Value.Text + "###ViewDuty"))
                                                    {
                                                        selectedRecord = record;
                                                        showDetailWindow = true;
                                                    }
                                                    if (ImGui.IsItemHovered())
                                                        ImGui.SetTooltip("View duty details");
                                                }
                                                finally
                                                {
                                                    ImGui.PopID();
                                                }
                                            }
                                            else
                                            {
                                                ImGui.TextUnformatted(row.Value.Text);
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        EndStyledTable();
                                    }
                                }
                            }
                        }
                        if (!anyVisible)
                            ImGui.TextDisabled("No milestones selected. Choose which ones to show in Settings.");
                    }
                }
            }
            finally
            {
                ImGui.EndChild();
            }
        }

        private static string MilestoneTitle(string title) => title == "PvP player stats" ? "Your lifetime PvP stats" : title;

        private static string MilestoneKey(string category, string label) => $"{category}/{label}";

        private void RebuildMilestones(string? localName)
        {
            milestoneSections.Clear();

            var records = FilterDutyRecords(applyPlayerFilters: false)
                .OrderBy(r => r.Timestamp)
                .ToList();

            var lifetimePvpRecords = config.History.Records.Where(record => record.Completed && record.DutyType == "PvP" && MatchesDutyCategoryFilters(record)).ToList();
            var personalPvp = lifetimePvpRecords
                .Select(record => (Record: record, Player: RecorderPvpPlayer(record)))
                .Where(entry => entry.Player != null)
                .Select(entry => new PlayerEncounter { Record = entry.Record, Player = entry.Player! }).ToList();
            var lifetimePvpTotals = BuildPvpStatistics(personalPvp);
            MilestoneResult PersonalBest(Func<PartyMemberSnapshot, long?> score, string unit)
            {
                var best = personalPvp.Where(entry => score(entry.Player).HasValue).MaxBy(entry => score(entry.Player));
                return best == null ? new MilestoneResult("N/A") : new MilestoneResult(
                    $"{score(best.Player):N0} {unit} — {best.Player.Name}@{best.Player.World} — {best.Record.DutyName} — {best.Record.Timestamp:yyyy-MM-dd HH:mm}", best.Record);
            }
            MilestoneResult PersonalRateBest(Func<PartyMemberSnapshot, long?> score, string unit)
            {
                var best = personalPvp.Where(entry => PvpRatePerMinute(entry.Record, score(entry.Player)).HasValue)
                    .MaxBy(entry => PvpRatePerMinute(entry.Record, score(entry.Player)));
                return best == null ? new MilestoneResult("N/A") : new MilestoneResult(
                    $"{PvpRatePerMinute(best.Record, score(best.Player)):N0} {unit} / min — {best.Player.Name}@{best.Player.World} — {best.Record.DutyName} — {best.Record.Timestamp:yyyy-MM-dd HH:mm}", best.Record);
            }
            var lifetimePvpRows = new List<(string Label, MilestoneResult Value)>
            {
                ("Damage dealt (PvP)", PvpStatisticText(lifetimePvpTotals, 0)),
                ("Damage / min (PvP)", PvpStatisticText(lifetimePvpTotals, 14)),
                ("Kills (PvP)", PvpStatisticText(lifetimePvpTotals, 1)),
                ("Assists (PvP)", PvpStatisticText(lifetimePvpTotals, 2)),
                ("Deaths (PvP)", lifetimePvpTotals.Deaths?.ToString("N0") ?? "-"),
                ("Damage taken (PvP)", PvpStatisticText(lifetimePvpTotals, 12)),
                ("Damage taken / min (PvP)", PvpStatisticText(lifetimePvpTotals, 15)),
                ("Healing done (PvP)", PvpStatisticText(lifetimePvpTotals, 13)),
                ("Healing / min (PvP)", PvpStatisticText(lifetimePvpTotals, 16)),
                ("Highest damage dealt in one match (PvP)", PersonalBest(player => player.PvPDamageDealt, "damage")),
                ("Highest damage / min in one match (PvP)", PersonalRateBest(player => player.PvPDamageDealt, "damage")),
                ("Most kills in one match (PvP)", PersonalBest(player => player.PvPKills, "kills")),
                ("Most assists in one match (PvP)", PersonalBest(player => player.PvPAssists, "assists")),
                ("Highest damage taken in one match (PvP)", PersonalBest(player => player.PvPDamageTaken, "damage taken")),
                ("Highest damage taken / min in one match (PvP)", PersonalRateBest(player => player.PvPDamageTaken, "damage taken")),
                ("Most healing in one match (PvP)", PersonalBest(player => player.PvPHealingDone, "healing")),
                ("Highest healing / min in one match (PvP)", PersonalRateBest(player => player.PvPHealingDone, "healing"))
            };
            if (records.Count == 0)
            {
                if (lifetimePvpRecords.Count > 0)
                    milestoneSections.Add(("PvP player stats", lifetimePvpRows));
                return;
            }

            var encounters = records
                .SelectMany(r => r.Party
                    .Where(p => config.IncludeSelfInStatistics || !IsRecordingPlayer(r, p, localName))
                    .Select(p => new MilestoneEncounter(r, p, EffectiveDeaths(r, p))))
                .ToList();

            Dictionary<DutyRecord, List<MilestoneEncounter>> byRecord =
                new(ReferenceEqualityComparer.Instance);

            foreach (var r in records)
                byRecord[r] = new List<MilestoneEncounter>();

            foreach (var e in encounters)
                byRecord[e.Record].Add(e);

            Dictionary<DutyRecord, int?> deathsByRecord = new(ReferenceEqualityComparer.Instance);
            foreach (var r in records)
                deathsByRecord[r] = TotalKnownDeaths(byRecord[r].Select(e => e.KnownDeaths));

            int? TotalDeaths(DutyRecord r) => deathsByRecord[r];

            var knownDeathEncounters = encounters.Where(e => e.KnownDeaths.HasValue).ToList();
            var dutiesWithKnownDeaths = records.Where(r =>
                byRecord[r].Count > 0 && byRecord[r].All(e => e.KnownDeaths.HasValue)).ToList();

            var completed = records.Where(r => r.Completed).ToList();

            string Duty(DutyRecord r) => $"{r.DutyName} — {r.Timestamp:yyyy-MM-dd HH:mm}";
            string Player(MilestoneEncounter e) => $"{e.Player.Name}@{e.Player.World}";
            bool MatchesStatus(PartyMemberSnapshot player, string status) =>
                status == "Mentor"
                    ? player.Status?.EndsWith("Mentor", StringComparison.OrdinalIgnoreCase) == true
                    : string.Equals(player.Status, status, StringComparison.OrdinalIgnoreCase);

            int CountStatus(DutyRecord r, string value) => byRecord[r].Count(e =>
                MatchesStatus(e.Player, value));

            int CountTargetPlayers(DutyRecord r) => byRecord[r].Count(e =>
                MatchesEncounterTarget(e.Player));

            void Add(string title, List<(string Label, MilestoneResult Value)> rows) =>
                milestoneSections.Add((title, rows));

            string NoData = "N/A";

            MilestoneResult DutyExt(
                IEnumerable<DutyRecord> candidates,
                Func<DutyRecord, double> metric,
                bool maximum,
                Func<DutyRecord, string> metricText)
            {
                var list = candidates.ToList();
                if (list.Count == 0) return NoData;
                var chosen = maximum ? list.MaxBy(metric) : list.MinBy(metric);

                return chosen == null
                    ? NoData
                    : new MilestoneResult($"{metricText(chosen)} — {Duty(chosen)}", chosen);
            }

            MilestoneResult PlayerExt(
                IEnumerable<MilestoneEncounter> candidates,
                Func<MilestoneEncounter, double> metric,
                Func<MilestoneEncounter, string> value)
            {
                var chosen = candidates.MaxBy(metric);

                return chosen == null
                    ? NoData
                    : new MilestoneResult(
                        $"{value(chosen)} — {Player(chosen)} — {Duty(chosen.Record)}",
                        chosen.Record);
            }

            string NamedGroupExt<TKey>(
                IEnumerable<IGrouping<TKey, MilestoneEncounter>> groups,
                Func<IGrouping<TKey, MilestoneEncounter>, double> metric,
                Func<IGrouping<TKey, MilestoneEncounter>, string> value)
            {
                var chosen = groups.MaxBy(metric);
                return chosen == null ? NoData : value(chosen);
            }

            var totalDuration = TimeSpan.FromTicks(records.Sum(r => r.TimeInDuty.Ticks));

            Add("Overall stats", new()
            {
                ("Total duties recorded", records.Count.ToString()),
                ("Total completed duties", completed.Count.ToString()),
                ("Total time spent in duties", FormatMilestoneDuration(totalDuration)),
                ("Total player encounters", encounters.Count.ToString()),
                ("Total unique players", encounters.Select(e => (e.Player.Name, e.Player.World)).Distinct().Count().ToString()),
                ("Total unique worlds", encounters.Select(e => e.Player.World)
                    .Where(w => !string.IsNullOrWhiteSpace(w) && w != "Unknown").Distinct().Count().ToString()),
                ("Total deaths observed", TotalKnownDeaths(encounters.Select(e => e.KnownDeaths))?.ToString() ?? "-"),
                ("Total wipes", records.Sum(r => r.WipeCount).ToString()),
                ("Total duties joined in progress", records.Count(r => r.JoinedInProgress).ToString()),
                ("Different duties completed", records.Select(r => r.DutyName).Distinct().Count().ToString())
            });

            if (lifetimePvpRecords.Count > 0)
                Add("PvP player stats", lifetimePvpRows);
            if (records.Any(record => record.DutyType == "PvP"))
            {
                var yourPvpResults = BuildPvpStatistics(records.Where(record => record.DutyType == "PvP")
                    .Select(record => (Record: record, Player: RecorderPvpPlayer(record)))
                    .Where(entry => entry.Player != null)
                    .Select(entry => new PlayerEncounter { Record = entry.Record, Player = entry.Player! }));
                Add("Your PvP results", new()
                {
                    ("Wins (PvP)", PvpStatisticText(yourPvpResults, 3)),
                    ("Losses (PvP)", PvpStatisticText(yourPvpResults, 4)),
                    ("Win rate (PvP)", PvpStatisticText(yourPvpResults, 5)),
                    ("1st (Frontline)", PvpStatisticText(yourPvpResults, 6)),
                    ("1st % (Frontline)", PvpStatisticText(yourPvpResults, 7)),
                    ("2nd (Frontline)", PvpStatisticText(yourPvpResults, 8)),
                    ("2nd % (Frontline)", PvpStatisticText(yourPvpResults, 9)),
                    ("3rd (Frontline)", PvpStatisticText(yourPvpResults, 10)),
                    ("3rd % (Frontline)", PvpStatisticText(yourPvpResults, 11))
                });
            }

            Add("Duty Records", new()
            {
                ("Fastest completed duty", DutyExt(completed, r => r.TimeInDuty.TotalSeconds, false,
                    r => FormatMilestoneDuration(r.TimeInDuty))),
                ("Longest completed duty", DutyExt(completed, r => r.TimeInDuty.TotalSeconds, true,
                    r => FormatMilestoneDuration(r.TimeInDuty))),
                ("Most deaths in a single duty", DutyExt(dutiesWithKnownDeaths, r => TotalDeaths(r).GetValueOrDefault(), true,
                    r => $"{TotalDeaths(r)} deaths")),
                ("Highest deaths per minute in a duty", DutyExt(dutiesWithKnownDeaths.Where(r => r.TimeInDuty.TotalMinutes > 0),
                    r => TotalDeaths(r).GetValueOrDefault() / r.TimeInDuty.TotalMinutes, true,
                    r => $"{TotalDeaths(r) / r.TimeInDuty.TotalMinutes:F2} deaths/min")),
                ("Most wipes in a single duty", DutyExt(records, r => r.WipeCount, true,
                    r => $"{r.WipeCount} wipes")),
                ("Most Lalafell in one duty", DutyExt(records, r => CountTargetPlayers(r), true,
                    r => $"{CountTargetPlayers(r)} {EncounterTargetLabel}")),
                ("Most sprouts in one duty", DutyExt(records, r => CountStatus(r, "Sprout"), true,
                    r => $"{CountStatus(r, "Sprout")} sprouts")),
                ("Most mentors in one duty", DutyExt(records, r => CountStatus(r, "Mentor"), true,
                    r => $"{CountStatus(r, "Mentor")} mentors")),
                ("Most different races in one duty", DutyExt(records,
                    r => byRecord[r].Select(e => e.Player.Race)
                        .Where(s => !string.IsNullOrWhiteSpace(s) && s != "Unknown")
                        .Distinct().Count(), true,
                    r => $"{byRecord[r].Select(e => e.Player.Race).Where(s => !string.IsNullOrWhiteSpace(s) && s != "Unknown").Distinct().Count()} races")),
                ("Most wipes in a completed duty", DutyExt(completed,
                    r => r.WipeCount, true, r => $"{r.WipeCount} wipes"))
            });

            var playersByIdentity = encounters
                .GroupBy(e => (e.Player.Name, e.Player.World))
                .ToList();

            var playersWithKnownDeaths = knownDeathEncounters
                .GroupBy(e => (e.Player.Name, e.Player.World))
                .ToList();

            Add("Individual Player Records", new()
            {
                ("Player seen most often", NamedGroupExt(playersByIdentity,
                    g => g.Count(),
                    g => $"{g.Key.Name}@{g.Key.World} — {g.Count()} encounters")),
                ("Most deaths by one player in a duty", PlayerExt(knownDeathEncounters,
                    e => e.Deaths, e => $"{e.Deaths} deaths")),
                ("Highest deaths per minute by one player", PlayerExt(
                    knownDeathEncounters.Where(e => e.Record.TimeInDuty.TotalMinutes > 0),
                    e => e.Deaths / e.Record.TimeInDuty.TotalMinutes,
                    e => $"{e.Deaths / e.Record.TimeInDuty.TotalMinutes:F2} deaths/min ({e.Deaths} deaths)")),
                ("Most total deaths by one player", NamedGroupExt(playersWithKnownDeaths,
                    g => g.Sum(e => e.Deaths),
                    g => $"{g.Key.Name}@{g.Key.World} — {g.Sum(e => e.Deaths)} deaths")),
                ("Highest average deaths per player encounter", NamedGroupExt(playersWithKnownDeaths,
                    g => g.Average(e => e.Deaths),
                    g => $"{g.Key.Name}@{g.Key.World} — {g.Average(e => e.Deaths):F2} deaths/encounter ({g.Count()} known encounters)")),
                ("Most different duties with one player", NamedGroupExt(playersByIdentity,
                    g => g.Select(e => e.Record.DutyName).Distinct().Count(),
                    g => $"{g.Key.Name}@{g.Key.World} — {g.Select(e => e.Record.DutyName).Distinct().Count()} duties")),
                ("Most deathless encounters with one player", NamedGroupExt(playersByIdentity.Where(g => g.Any(e => e.IsConfirmedDeathless)),
                    g => g.Count(e => e.IsConfirmedDeathless),
                    g => $"{g.Key.Name}@{g.Key.World} — {g.Count(e => e.IsConfirmedDeathless)} deathless encounters"))
            });

            string Streak(Func<DutyRecord, bool> qualifies)
            {
                var best = 0;
                var current = 0;
                DateTime bestStart = default;
                DateTime bestEnd = default;
                DateTime currentStart = default;

                foreach (var r in records)
                {
                    if (qualifies(r))
                    {
                        if (current == 0) currentStart = r.Timestamp;
                        current++;

                        if (current > best)
                        {
                            best = current;
                            bestStart = currentStart;
                            bestEnd = r.Timestamp;
                        }
                    }
                    else
                    {
                        current = 0;
                    }
                }

                return best == 0
                    ? NoData
                    : $"{best} duties — {bestStart:yyyy-MM-dd} to {bestEnd:yyyy-MM-dd}";
            }

            string RepeatedDutyStreak()
            {
                var best = 0;
                var current = 0;
                string? previous = null;
                string? winningDuty = null;
                DateTime winningStart = default;
                DateTime start = default;
                DateTime end = default;

                foreach (var r in records)
                {
                    if (r.DutyName == previous)
                        current++;
                    else
                    {
                        current = 1;
                        start = r.Timestamp;
                    }

                    if (current > best)
                    {
                        best = current;
                        winningDuty = r.DutyName;
                        winningStart = start;
                        end = r.Timestamp;
                    }

                    previous = r.DutyName;
                }

                return best == 0
                    ? NoData
                    : $"{best} duties — {winningDuty} — {winningStart:yyyy-MM-dd} to {end:yyyy-MM-dd}";
            }

            Add("Streaks and Droughts", new()
            {
                ("Longest streak without a wipe", Streak(r => r.WipeCount == 0)),
                ("Longest streak with wipes", Streak(r => r.WipeCount > 0)),
                ("Longest streak without a player death", Streak(r =>
                    byRecord[r].Count > 0 && byRecord[r].All(e => e.IsConfirmedDeathless))),
                ("Longest streak with deaths", Streak(r => TotalDeaths(r) > 0)),
                ("Longest streak without a Lalafell", Streak(r => CountTargetPlayers(r) == 0)),
                ("Longest streak with a Lalafell", Streak(r => CountTargetPlayers(r) > 0)),
                ("Longest streak without a sprout", Streak(r => CountStatus(r, "Sprout") == 0)),
                ("Longest streak without a mentor", Streak(r => CountStatus(r, "Mentor") == 0)),
                ("Longest streak of the same duty", RepeatedDutyStreak()),
                ("Longest streak of clears under 10 minutes",
                    Streak(r => r.Completed && r.TimeInDuty.TotalMinutes < 10)),
                ("Longest streak of runs over 30 minutes",
                    Streak(r => r.TimeInDuty.TotalMinutes > 30))
            });

            string TopPeriod<TKey>(
                IEnumerable<IGrouping<TKey, DutyRecord>> groups,
                Func<IGrouping<TKey, DutyRecord>, double> metric,
                Func<IGrouping<TKey, DutyRecord>, string> label)
            {
                var best = groups.MaxBy(metric);
                return best == null ? NoData : label(best);
            }

            var byDay = records.GroupBy(r => r.Timestamp.Date).ToList();
            var byWeek = records.GroupBy(r => StartOfWeek(r.Timestamp)).ToList();
            var byMonth = records.GroupBy(r =>
                new DateTime(r.Timestamp.Year, r.Timestamp.Month, 1)).ToList();

            var playerByDay = encounters
                .GroupBy(e => e.Record.Timestamp.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            List<MilestoneEncounter> DayPlayers(DateTime day) =>
                playerByDay.TryGetValue(day, out var items) ? items : new();

            int? DayDeaths(DateTime day) => TotalKnownDeaths(DayPlayers(day).Select(e => e.KnownDeaths));
            int DayTargetPlayers(DateTime day) =>
                DayPlayers(day).Count(e => MatchesEncounterTarget(e.Player));

            int DayStatus(DateTime day, string status) =>
                DayPlayers(day).Count(e =>
                    MatchesStatus(e.Player, status));

            int DayUniquePlayers(DateTime day) =>
                DayPlayers(day).Select(e => (e.Player.Name, e.Player.World)).Distinct().Count();

            string ConsecutiveDays()
            {
                var days = byDay.Select(g => g.Key).OrderBy(d => d).ToList();
                var best = 0;
                var current = 0;
                DateTime start = default;
                DateTime bestStart = default;
                DateTime bestEnd = default;
                DateTime previous = default;

                foreach (var day in days)
                {
                    if (current > 0 && day == previous.AddDays(1))
                        current++;
                    else
                    {
                        current = 1;
                        start = day;
                    }

                    if (current > best)
                    {
                        best = current;
                        bestStart = start;
                        bestEnd = day;
                    }

                    previous = day;
                }

                return best == 0
                    ? NoData
                    : $"{best} days — {bestStart:yyyy-MM-dd} to {bestEnd:yyyy-MM-dd}";
            }

            string LongestBreak()
            {
                if (records.Count < 2) return NoData;

                var max = TimeSpan.Zero;
                DutyRecord? before = null;
                DutyRecord? after = null;

                for (int i = 1; i < records.Count; i++)
                {
                    var gap = records[i].Timestamp - records[i - 1].Timestamp;
                    if (gap > max)
                    {
                        max = gap;
                        before = records[i - 1];
                        after = records[i];
                    }
                }

                return before == null || after == null
                    ? NoData
                    : $"{FormatMilestoneDuration(max)} — {before.Timestamp:yyyy-MM-dd HH:mm} to {after.Timestamp:yyyy-MM-dd HH:mm} (between duty starts)";
            }

            Add("Activity Records", new()
            {
                ("Most duties recorded in one day", TopPeriod(byDay,
                    g => g.Count(), g => $"{g.Count()} duties — {g.Key:yyyy-MM-dd}")),
                ("Most duties recorded in one week", TopPeriod(byWeek,
                    g => g.Count(), g => $"{g.Count()} duties — week of {g.Key:yyyy-MM-dd}")),
                ("Most duties recorded in one month", TopPeriod(byMonth,
                    g => g.Count(), g => $"{g.Count()} duties — {g.Key:yyyy-MM}")),
                ("Most time spent in duties in one day", TopPeriod(byDay,
                    g => g.Sum(r => r.TimeInDuty.Ticks),
                    g => $"{FormatMilestoneDuration(TimeSpan.FromTicks(g.Sum(r => r.TimeInDuty.Ticks)))} — {g.Key:yyyy-MM-dd}")),
                ("Most deaths observed in one day", TopPeriod(byDay.Where(g => DayDeaths(g.Key).HasValue),
                    g => DayDeaths(g.Key).GetValueOrDefault(),
                    g => $"{DayDeaths(g.Key)} deaths — {g.Key:yyyy-MM-dd}")),
                ("Most wipes in one day", TopPeriod(byDay,
                    g => g.Sum(r => r.WipeCount),
                    g => $"{g.Sum(r => r.WipeCount)} wipes — {g.Key:yyyy-MM-dd}")),
                ("Most unique players in one day", TopPeriod(byDay,
                    g => DayUniquePlayers(g.Key),
                    g => $"{DayUniquePlayers(g.Key)} players — {g.Key:yyyy-MM-dd}")),
                ("Most Lalafell in one day", TopPeriod(byDay,
                    g => DayTargetPlayers(g.Key),
                    g => $"{DayTargetPlayers(g.Key)} {EncounterTargetLabel} — {g.Key:yyyy-MM-dd}")),
                ("Most sprouts in one day", TopPeriod(byDay,
                    g => DayStatus(g.Key, "Sprout"),
                    g => $"{DayStatus(g.Key, "Sprout")} sprouts — {g.Key:yyyy-MM-dd}")),
                ("Most mentors in one day", TopPeriod(byDay,
                    g => DayStatus(g.Key, "Mentor"),
                    g => $"{DayStatus(g.Key, "Mentor")} mentors — {g.Key:yyyy-MM-dd}")),
                ("Most days in a row recording duties", ConsecutiveDays()),
                ("Longest break between recorded duties", LongestBreak())
            });
        }

        private sealed record MilestoneEncounter(
            DutyRecord Record,
            PartyMemberSnapshot Player,
            int? KnownDeaths)
        {
            public int Deaths => KnownDeaths.GetValueOrDefault();
            public bool IsConfirmedDeathless => KnownDeaths == 0;
        }

        private static DateTime StartOfWeek(DateTime date)
        {
            int daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-daysSinceMonday);
        }

        private static string FormatMilestoneDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";

            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";

            return $"{duration.Minutes}m {duration.Seconds}s";
        }

        private static void DrawStatisticRow(string statistic, string value)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(statistic);
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(value);
        }
    }
}
