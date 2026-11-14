
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
        private int? SummaryDeaths(
            PartyMemberSnapshot player,
            Dictionary<PartyMemberSnapshot, DutyRecord> playerRecords)
        {
            return playerRecords.TryGetValue(player, out var record)
                ? EffectiveDeaths(record, player)
                : null;
        }

        // Refresh once a second to avoid rebuilding milestones every frame.
        private DateTime nextMilestoneRefresh = DateTime.MinValue;
        private string? milestoneFilterKey;
        private string? milestonePlayerName;
        private bool milestonesIncludeSelf;

        private sealed record MilestoneResult(string Text, DutyRecord? Record = null)
        {
            public static implicit operator MilestoneResult(string text) => new(text);
        }

        private readonly List<(string Title, List<(string Label, MilestoneResult Value)> Rows)> milestoneSections = new();

        private void DrawSummaryTab()
        {
            var localName = objectTable.LocalPlayer?.Name.TextValue;

            var dutyFilterKey = string.Join("|",
                string.Join(",", selectedDutyTypes.OrderBy(x => x, StringComparer.Ordinal)),
                globalDutySearchText.Trim(),
                selectedTimeRange.ToString(),
                customStartDate.Date.ToString("yyyyMMdd"),
                customEndDate.Date.ToString("yyyyMMdd"));

            if (DateTime.UtcNow >= nextMilestoneRefresh ||
                dutyFilterKey != milestoneFilterKey ||
                localName != milestonePlayerName ||
                milestonesIncludeSelf != config.IncludeSelfInStatistics)
            {
                RebuildMilestones(localName);
                nextMilestoneRefresh = DateTime.UtcNow.AddSeconds(1);
                milestoneFilterKey = dutyFilterKey;
                milestonePlayerName = localName;
                milestonesIncludeSelf = config.IncludeSelfInStatistics;
            }

            if (ImGui.BeginChild("SummaryContent", Vector2.Zero, false,
                    ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.Text("Milestones");
                ImGui.TextDisabled("Completed duties only. Uses duty type, duty search, and date filters. Player filters don't apply.");
                ImGui.Separator();

                if (milestoneSections.Count == 0)
                    ImGui.TextDisabled("No completed duties match your duty filters.");
                else
                {
                    foreach (var section in milestoneSections)
                    {
                        ImGui.Spacing();
                        if (ImGui.CollapsingHeader($"{section.Title}##Lifetime{section.Title}",
                                ImGuiTreeNodeFlags.DefaultOpen))
                        {
                            if (ImGui.BeginTable($"LifetimeTable##{section.Title}", 2,
                                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable))
                            {
                                ImGui.TableSetupColumn("Record");
                                ImGui.TableSetupColumn("Result");
                                ImGui.TableHeadersRow();

                                foreach (var row in section.Rows)
                                {
                                    ImGui.TableNextRow();
                                    ImGui.TableSetColumnIndex(0);
                                    ImGui.TextUnformatted(row.Label);
                                    ImGui.TableSetColumnIndex(1);

                                    if (row.Value.Record is DutyRecord record)
                                    {
                                        ImGui.PushID(row.Label);
                                        if (ImGui.Selectable(row.Value.Text))
                                        {
                                            selectedRecord = record;
                                            showDetailWindow = true;
                                        }
                                        if (ImGui.IsItemHovered())
                                            ImGui.SetTooltip("View duty details");
                                        ImGui.PopID();
                                    }
                                    else
                                    {
                                        ImGui.TextUnformatted(row.Value.Text);
                                    }
                                }

                                ImGui.EndTable();
                            }
                        }
                    }
                }
            }

            ImGui.EndChild();
        }

        private void RebuildMilestones(string? localName)
        {
            milestoneSections.Clear();

            var now = DateTime.Now;
            var records = config.History.Records
                .Where(r => r.Completed)
                .Where(r => selectedDutyTypes.Count == 0 || selectedDutyTypes.Contains(r.DutyType))
                .Where(r => MatchesGlobalDutySearch(r))
                .Where(r => selectedTimeRange switch
                {
                    1 => r.Timestamp >= now.AddDays(-7),
                    2 => r.Timestamp >= now.AddDays(-30),
                    3 => r.Timestamp >= now.AddDays(-90),
                    4 => r.Timestamp >= customStartDate.Date &&
                         r.Timestamp < customEndDate.Date.AddDays(1),
                    _ => true
                })
                .OrderBy(r => r.Timestamp)
                .ToList();

            if (records.Count == 0)
                return;

            var encounters = records
                .SelectMany(r => r.Party
                    .Where(p => config.IncludeSelfInStatistics || !IsRecordingPlayer(r, p, localName))
                    .Select(p => new MilestoneEncounter(r, p, EffectiveDeaths(r, p) ?? 0)))
                .ToList();

            Dictionary<DutyRecord, List<MilestoneEncounter>> byRecord =
                new(ReferenceEqualityComparer.Instance);

            foreach (var r in records)
                byRecord[r] = new List<MilestoneEncounter>();

            foreach (var e in encounters)
                byRecord[e.Record].Add(e);

            int TotalDeaths(DutyRecord r) => byRecord[r].Sum(e => e.Deaths);

            var completed = records.Where(r => r.Completed).ToList();
            var positiveDuration = records.Where(r => r.TimeInDuty.TotalMinutes > 0).ToList();

            string Duty(DutyRecord r) => $"{r.DutyName} — {r.Timestamp:yyyy-MM-dd HH:mm}";
            string Player(MilestoneEncounter e) => $"{e.Player.Name}@{e.Player.World}";
            bool MatchesStatus(PartyMemberSnapshot player, string status) =>
                status == "Mentor"
                    ? player.Status?.EndsWith("Mentor", StringComparison.OrdinalIgnoreCase) == true
                    : string.Equals(player.Status, status, StringComparison.OrdinalIgnoreCase);

            int CountStatus(DutyRecord r, string value) => byRecord[r].Count(e =>
                MatchesStatus(e.Player, value));

            int CountLalafell(DutyRecord r) => byRecord[r].Count(e =>
                e.Player.Race == "Lalafell");

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
                ("Total deaths observed", encounters.Sum(e => e.Deaths).ToString()),
                ("Total wipes", records.Sum(r => r.WipeCount).ToString()),
                ("Total duties joined in progress", records.Count(r => r.JoinedInProgress).ToString()),
                ("Different duties completed", records.Select(r => r.DutyName).Distinct().Count().ToString())
            });

            Add("Duty Records", new()
            {
                ("Fastest completed duty", DutyExt(completed, r => r.TimeInDuty.TotalSeconds, false,
                    r => FormatMilestoneDuration(r.TimeInDuty))),
                ("Longest completed duty", DutyExt(completed, r => r.TimeInDuty.TotalSeconds, true,
                    r => FormatMilestoneDuration(r.TimeInDuty))),
                ("Most deaths in a single duty", DutyExt(records, r => TotalDeaths(r), true,
                    r => $"{TotalDeaths(r)} deaths")),
                ("Highest deaths per minute in a duty", DutyExt(positiveDuration,
                    r => TotalDeaths(r) / r.TimeInDuty.TotalMinutes, true,
                    r => $"{TotalDeaths(r) / r.TimeInDuty.TotalMinutes:F2} deaths/min")),
                ("Most wipes in a single duty", DutyExt(records, r => r.WipeCount, true,
                    r => $"{r.WipeCount} wipes")),
                ("Most Lalafell in one duty", DutyExt(records, r => CountLalafell(r), true,
                    r => $"{CountLalafell(r)} Lalafell")),
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

            var playersWithKnownDeaths = encounters
                .Where(e => EffectiveDeaths(e.Record, e.Player).HasValue)
                .GroupBy(e => (e.Player.Name, e.Player.World))
                .ToList();

            Add("Individual Player Records", new()
            {
                ("Player seen most often", NamedGroupExt(playersByIdentity,
                    g => g.Count(),
                    g => $"{g.Key.Name}@{g.Key.World} — {g.Count()} encounters")),
                ("Most deaths by one player in a duty", PlayerExt(encounters,
                    e => e.Deaths, e => $"{e.Deaths} deaths")),
                ("Highest deaths per minute by one player", PlayerExt(
                    encounters.Where(e => e.Record.TimeInDuty.TotalMinutes > 0),
                    e => e.Deaths / e.Record.TimeInDuty.TotalMinutes,
                    e => $"{e.Deaths / e.Record.TimeInDuty.TotalMinutes:F2} deaths/min ({e.Deaths} deaths)")),
                ("Most total deaths by one player", NamedGroupExt(playersByIdentity,
                    g => g.Sum(e => e.Deaths),
                    g => $"{g.Key.Name}@{g.Key.World} — {g.Sum(e => e.Deaths)} deaths")),
                ("Highest average deaths per player encounter", NamedGroupExt(playersWithKnownDeaths,
                    g => g.Average(e => e.Deaths),
                    g => $"{g.Key.Name}@{g.Key.World} — {g.Average(e => e.Deaths):F2} deaths/encounter ({g.Count()} known encounters)")),
                ("Most different duties with one player", NamedGroupExt(playersByIdentity,
                    g => g.Select(e => e.Record.DutyName).Distinct().Count(),
                    g => $"{g.Key.Name}@{g.Key.World} — {g.Select(e => e.Record.DutyName).Distinct().Count()} duties")),
                ("Most deathless encounters with one player", NamedGroupExt(playersByIdentity,
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
                ("Longest streak without a Lalafell", Streak(r => CountLalafell(r) == 0)),
                ("Longest streak with a Lalafell", Streak(r => CountLalafell(r) > 0)),
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

            int DayDeaths(DateTime day) => DayPlayers(day).Sum(e => e.Deaths);
            int DayLalafell(DateTime day) =>
                DayPlayers(day).Count(e => e.Player.Race == "Lalafell");

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
                ("Most deaths observed in one day", TopPeriod(byDay,
                    g => DayDeaths(g.Key),
                    g => $"{DayDeaths(g.Key)} deaths — {g.Key:yyyy-MM-dd}")),
                ("Most wipes in one day", TopPeriod(byDay,
                    g => g.Sum(r => r.WipeCount),
                    g => $"{g.Sum(r => r.WipeCount)} wipes — {g.Key:yyyy-MM-dd}")),
                ("Most unique players in one day", TopPeriod(byDay,
                    g => DayUniquePlayers(g.Key),
                    g => $"{DayUniquePlayers(g.Key)} players — {g.Key:yyyy-MM-dd}")),
                ("Most Lalafell in one day", TopPeriod(byDay,
                    g => DayLalafell(g.Key),
                    g => $"{DayLalafell(g.Key)} Lalafell — {g.Key:yyyy-MM-dd}")),
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
            int Deaths)
        {
            public bool IsConfirmedDeathless => EffectiveDeaths(Record, Player) == 0;
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
