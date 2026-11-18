using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private const float TableCellVerticalPadding = 3f;
        private int[]? configuredColumnOrder;
        private int[]? configuredColumnPositions;
        private string? draggedColumnTable;
        private string? draggedColumnId;
        private sealed record TableColumnOption(string Id, string Label, bool DefaultVisible = true, bool Required = false);

        private static readonly Dictionary<string, TableColumnOption[]> TableColumnOptions = new()
        {
            ["PvP records"] =
            [
                new("name", "Name", Required: true), new("world", "World"), new("job", "Job"),
                new("side", "Side"), new("damage", "Damage dealt"),
                new("kills", "Kills"), new("deaths", "Deaths"), new("assists", "Assists"),
                new("race", "Race"), new("gender", "Gender"), new("status", "Status", false),
                new("result", "Result"), new("taken", "Damage taken"), new("healing", "Healing done"),
                PvpDamagePerMinuteColumn("Damage / min"), ..PvpSupportRateColumns("Damage taken / min", "Healing / min")
            ],
            ["Records"] =
            [
                new("duty", "Duty", Required: true), new("type", "Type"), new("job", "Job"),
                new("started", "Started"), new("time", "Time"), new("joined", "Joined in progress"),
                new("wipes", "Wipes"), new("lalafell", "Lalafell"), new("completed", "Completed"),
                new("sprouts", "Sprouts seen", false), new("mentors", "Mentors seen", false),
                new("players", "Players seen", false), new("deaths", "Known deaths", false),
                new("pvpPlacement", "PvP placement", false), new("pvpDamage", "Your PvP damage", false),
                new("pvpKills", "Your PvP kills", false), new("pvpAssists", "Your PvP assists", false), ..PvpSupportColumns(),
                PvpDamagePerMinuteColumn("Your PvP damage / min"), ..PvpSupportRateColumns("Your PvP damage taken / min", "Your PvP healing / min")
            ],
            ["People"] =
            [
                new("name", "Name", Required: true), new("world", "World"), new("race", "Race"),
                new("gender", "Gender"), new("seen", "Times seen"), new("deaths", "Total deaths"),
                new("average", "Deaths / encounter"), new("first", "First seen", false),
                new("last", "Last seen", false), new("duties", "Different duties", false),
                new("known", "Known death encounters", false), new("deathless", "Deathless encounters", false),
                new("sprouts", "Sprout encounters", false), new("mentors", "Mentor encounters", false),
                new("pvpDamage", "Damage dealt (PvP)", false), new("pvpKills", "Kills (PvP)", false),
                new("pvpAssists", "Assists (PvP)", false), new("teammates", "Teammate encounters", false),
                new("enemies", "Enemy encounters", false), ..PvpResultColumns(), ..PvpSupportColumns(),
                PvpDamagePerMinuteColumn(), ..PvpSupportRateColumns()
            ],
            ["Duty Analysis"] =
            [
                new("duty", "Duty", Required: true), new("runs", "Runs"), new("deaths", "Deaths"),
                new("averageDeaths", "Deaths / run"), new("wipes", "Total wipes"),
                new("averageWipes", "Wipes / run"), new("time", "Average time"), new("joined", "Joined in progress"),
                new("sprouts", "Sprouts seen", false), new("mentors", "Mentors seen", false),
                new("players", "Players seen", false), new("lalafell", "Lalafell seen", false), ..PvpStatisticColumns()
            ],
            ["Statistics: Race"] = StatisticColumns("Race"),
            ["Statistics: Race and gender"] = StatisticColumns("Race"),
            ["Statistics: Gender"] = StatisticColumns("Gender"),
            ["Statistics: Job"] = StatisticColumns("Job"),
            ["Statistics: Status"] = StatisticColumns("Status", percentage: true),
            ["Statistics: World"] = StatisticColumns("World", percentage: true),
            ["Statistics: Datacenter"] = StatisticColumns("Datacenter", percentage: true)
        };

        private static TableColumnOption[] StatisticColumns(string name, bool percentage = false) =>
        [
            new("group", name, Required: true), new("detail", "Gender", Required: true),
            new("seen", "Encounters"), new("deaths", "Known deaths", !percentage),
            new("average", "Deaths / encounter"), new("percentage", "Percentage", percentage), ..PvpStatisticColumns(averages: true)
        ];

        private static string StatisticTableKey(PlayerStatistic statistic) =>
            "Statistics: " + (statistic == PlayerStatistic.RaceAndGender ? "Race and gender" : statistic.ToString());

        private bool BeginConfiguredTable(string table, string id, bool verticalScroll = false,
            float height = 0, bool showDetail = false)
        {
            var visibleCount = TableColumnOptions[table].Select((column, index) => index)
                .Count(index => IsTableColumnVisible(table, index) &&
                    (!table.StartsWith("Statistics: ", StringComparison.Ordinal) || index != 1 || showDetail));
            var availableWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
            var minimumWidth = ImGui.GetFontSize() * (9f + Math.Max(0, visibleCount - 1) * 4.25f);
            var horizontalScroll = minimumWidth > availableWidth;
            var flags = ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable |
                ImGuiTableFlags.SizingStretchProp;
            if (verticalScroll)
                flags |= ImGuiTableFlags.ScrollY;
            if (horizontalScroll)
                flags |= ImGuiTableFlags.ScrollX;

            var order = GetConfiguredColumnOrder(table);
            var tableId = order.Where((index, position) => index != position).Any()
                ? id + "##order:" + string.Join(',', order) : id;
            if (!BeginStyledTable(tableId, TableColumnOptions[table].Length, flags,
                new Vector2(0, height), horizontalScroll ? minimumWidth : 0))
                return false;
            configuredColumnOrder = order;
            configuredColumnPositions = new int[order.Length];
            for (var position = 0; position < order.Length; position++)
                configuredColumnPositions[order[position]] = position;
            return true;
        }

        private int[] GetConfiguredColumnOrder(string table)
        {
            var columns = TableColumnOptions[table];
            var byId = columns.Select((column, index) => (column.Id, index))
                .ToDictionary(entry => entry.Id, entry => entry.index, StringComparer.Ordinal);
            var saved = config.TableColumnOrder != null && config.TableColumnOrder.TryGetValue(table, out var order)
                ? order : null;
            return (saved ?? Enumerable.Empty<string>()).Where(id => id != null && byId.ContainsKey(id))
                .Select(id => byId[id]).Concat(GetDefaultColumnOrder(table)).Distinct().ToArray();
        }

        private static int[] GetDefaultColumnOrder(string table)
        {
            var columns = TableColumnOptions[table];
            if (table == "PvP records")
                return new[] { "name", "world", "job", "side", "damage", "pvpDamagePerMinute", "kills", "deaths", "assists",
                    "taken", "pvpDamageTakenPerMinute", "healing", "pvpHealingPerMinute", "result", "race", "gender", "status" }
                    .Select(id => Array.FindIndex(columns, column => column.Id == id)).ToArray();
            var order = Enumerable.Range(0, columns.Length).ToList();
            var assists = Array.FindIndex(columns, column => column.Id == "pvpAssists");
            if (assists >= 0)
            {
                var support = order.Where(index => columns[index].Id is "pvpDamageTaken" or "pvpHealingDone").ToArray();
                order.RemoveAll(index => support.Contains(index));
                order.InsertRange(order.IndexOf(assists) + 1, support);
            }
            foreach (var (scoreId, rateId) in new[]
                { ("pvpDamage", "pvpDamagePerMinute"), ("pvpDamageTaken", "pvpDamageTakenPerMinute"), ("pvpHealingDone", "pvpHealingPerMinute") })
            {
                var score = Array.FindIndex(columns, column => column.Id == scoreId);
                var rate = Array.FindIndex(columns, column => column.Id == rateId);
                if (score < 0 || rate < 0)
                    continue;
                order.Remove(rate);
                order.Insert(order.IndexOf(score) + 1, rate);
            }
            return order.ToArray();
        }

        private void MoveConfiguredColumn(string table, string sourceId, string targetId)
        {
            var columns = TableColumnOptions[table];
            var order = GetConfiguredColumnOrder(table).Select(index => columns[index].Id).ToList();
            var source = order.IndexOf(sourceId);
            var target = order.IndexOf(targetId);
            if (source < 0 || target < 0 || source == target)
                return;
            order.RemoveAt(source);
            order.Insert(target, sourceId);
            config.TableColumnOrder ??= new();
            config.TableColumnOrder[table] = order;
            config.Save();
        }

        private int ConfiguredSortColumn(int displayIndex) => configuredColumnOrder != null &&
            displayIndex >= 0 && displayIndex < configuredColumnOrder.Length ? configuredColumnOrder[displayIndex] : displayIndex;

        private bool SetConfiguredColumnIndex(int index) =>
            ImGui.TableSetColumnIndex(configuredColumnPositions == null ? index : configuredColumnPositions[index]);

        private bool BeginStyledTable(string id, int columns, ImGuiTableFlags flags,
            Vector2 size = default, float innerWidth = 0)
        {
            flags = (flags & ~ImGuiTableFlags.BordersInnerV) | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerH;
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(8, TableCellVerticalPadding));
            ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, config.TableHeaderBackgroundColor ??
                ImGui.ColorConvertFloat4ToU32(WindowAppearance.DefaultTableHeader));
            ImGui.PushStyleColor(ImGuiCol.TableRowBg, config.TableRowBackgroundColor ?? ImGui.GetColorU32(ImGuiCol.TableRowBg));
            ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, config.AlternateRowBackgroundColor ??
                ImGui.ColorConvertFloat4ToU32(WindowAppearance.DefaultAlternateRow));
            if (ImGui.BeginTable(id, columns, flags, size, innerWidth))
                return true;
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();
            return false;
        }

        private void EndStyledTable()
        {
            ImGui.EndTable();
            configuredColumnOrder = null;
            configuredColumnPositions = null;
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), 3f);
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();
        }

        private string TableHeaderLabel(string label, float availableWidth)
        {
            if (!config.WrapTableHeaders || availableWidth >= ImGui.GetFontSize() * 3f ||
                ImGui.CalcTextSize(label).X <= availableWidth)
                return label;

            return label switch
            {
                "Joined in progress" => "Joined in\nprogress",
                "Deaths / encounter" => "Deaths /\nencounter",
                "Known death encounters" => "Known death\nencounters",
                "Deathless encounters" => "Deathless\nencounters",
                "Sprout encounters" => "Sprout\nencounters",
                "Mentor encounters" => "Mentor\nencounters",
                "Different duties" => "Different\nduties",
                "Sprouts seen" => "Sprouts\nseen",
                "Mentors seen" => "Mentors\nseen",
                "Players seen" => "Players\nseen",
                "Lalafell seen" => "Lalafell\nseen",
                "Known deaths" => "Known\ndeaths",
                "Average time" => "Average\ntime",
                _ => label
            };
        }

        private bool IsTableColumnVisible(string table, int index)
        {
            var column = TableColumnOptions[table][index];
            if (column.Required)
                return true;
            return config.TableColumns != null && config.TableColumns.TryGetValue(table, out var selected)
                ? selected != null && selected.Contains(column.Id)
                : column.DefaultVisible;
        }

        private void SetupConfiguredColumns(string table, string? groupLabel = null, bool showDetail = false, bool scrollable = false)
        {
            var columns = TableColumnOptions[table];
            var labels = new string[columns.Length];
            foreach (var i in configuredColumnOrder ?? Enumerable.Range(0, columns.Length).ToArray())
            {
                var visible = IsTableColumnVisible(table, i) &&
                    (!table.StartsWith("Statistics: ", StringComparison.Ordinal) || i != 1 || showDetail);
                var flags = visible ? ImGuiTableColumnFlags.None : ImGuiTableColumnFlags.Disabled;
                flags |= ImGuiTableColumnFlags.WidthStretch;
                if (i == 0)
                    flags |= ImGuiTableColumnFlags.PreferSortAscending;
                if (table == "People" && columns[i].Id == "seen")
                    flags |= ImGuiTableColumnFlags.PreferSortDescending;
                var label = i == 0 && groupLabel != null ? groupLabel : columns[i].Label;
                if (table.StartsWith("Statistics: ", StringComparison.Ordinal) && i == 1)
                    label = "Gender";
                labels[i] = label;
                ImGui.TableSetupColumn(label, flags, i == 0 ? 2f : 1f, (uint)i + 1);
            }
            if (scrollable)
                ImGui.TableSetupScrollFreeze(1, 1);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            for (var i = 0; i < columns.Length; i++)
            {
                if (!SetConfiguredColumnIndex(i))
                    continue;
                ImGui.TableHeader(TableHeaderLabel(labels[i], ImGui.GetContentRegionAvail().X));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(columns[i].Id.EndsWith("PerMinute", StringComparison.Ordinal)
                        ? "Score divided by recorded duty minutes, including waiting time. Groups combine each player's score and time. Click to sort."
                        : labels[i] + " — click to sort");
            }
        }

        private void DrawTableCell(int index, string text)
        {
            if (SetConfiguredColumnIndex(index))
            {
                var width = ImGui.GetContentRegionAvail().X;
                ImGui.TextUnformatted(text);
                if (ImGui.IsItemHovered() && ImGui.CalcTextSize(text).X > width)
                    ImGui.SetTooltip(text);
            }
        }

        private static List<T> SortTableRows<T>(IEnumerable<T> rows, Func<T, IComparable?> value,
            bool ascending, Func<T, string> name)
        {
            var knownFirst = rows.OrderBy(row => value(row) == null);
            return (ascending ? knownFirst.ThenBy(value) : knownFirst.ThenByDescending(value))
                .ThenBy(name).ToList();
        }

        private readonly record struct DutyPlayerCounts(int Players, int Sprouts, int Mentors, int Lalafell, int? Deaths);

        private DutyPlayerCounts GetDutyPlayerCounts(DutyRecord record)
        {
            var localName = objectTable.LocalPlayer?.Name.TextValue;
            var players = record.Party.Where(player =>
                !string.IsNullOrWhiteSpace(player.Name) &&
                (config.IncludeSelfInStatistics || !IsRecordingPlayer(record, player, localName)) &&
                MatchesPlayerFilters(player)).ToList();
            return new DutyPlayerCounts(players.Count,
                players.Count(player => GetPlayerStatus(player) == "Sprout"),
                players.Count(IsMentor), players.Count(player => player.Race == "Lalafell"),
                TotalKnownDeaths(players.Select(player => EffectiveDeaths(record, player))));
        }

        private static bool IsMentor(PartyMemberSnapshot player) =>
            player.Status?.EndsWith("Mentor", StringComparison.OrdinalIgnoreCase) == true;

        private bool? tableColumnsExpansion;

        private void DrawTableColumnSettings()
        {
            config.TableColumns ??= new();
            ImGui.PushTextWrapPos();
            try
            {
                ImGui.TextDisabled("Check columns to show them. Drag a name to change its position.");
            }
            finally
            {
                ImGui.PopTextWrapPos();
            }
            foreach (var (table, columns) in TableColumnOptions)
            {
                ImGui.PushID(table);
                try
                {
                    if (tableColumnsExpansion.HasValue)
                        ImGui.SetNextItemOpen(tableColumnsExpansion.Value, ImGuiCond.Always);
                    if (ImGui.TreeNodeEx(table, ImGuiTreeNodeFlags.SpanAvailWidth))
                    {
                        try
                        {
                            if (ImGui.Button("Reset columns"))
                            {
                                config.TableColumns.Remove(table);
                                config.TableColumnOrder?.Remove(table);
                                config.Save();
                            }
                            foreach (var index in GetConfiguredColumnOrder(table))
                            {
                                var column = columns[index];
                                if (column.Id == "detail" && table != "Statistics: Race and gender")
                                    continue;
                                ImGui.PushID(column.Id);
                                try
                                {
                                    var visible = IsTableColumnVisible(table, index);
                                    var pvpDisabled = BeginPvpControl(table == "PvP records" || column.Id.StartsWith("pvp", StringComparison.Ordinal));
                                    try
                                    {
                                        ImGui.BeginDisabled(column.Required);
                                        try
                                        {
                                            if (ImGui.Checkbox("##visible", ref visible))
                                            {
                                                if (!config.TableColumns.TryGetValue(table, out var selected) || selected == null)
                                                {
                                                    selected = columns.Where(option => option.DefaultVisible).Select(option => option.Id).ToHashSet();
                                                    config.TableColumns[table] = selected;
                                                }
                                                if (visible)
                                                    selected.Add(column.Id);
                                                else
                                                    selected.Remove(column.Id);
                                                config.Save();
                                            }
                                        }
                                        finally
                                        {
                                            ImGui.EndDisabled();
                                        }
                                    }
                                    finally
                                    {
                                        EndPvpControl(pvpDisabled);
                                    }
                                    ImGui.SameLine();
                                    ImGui.Selectable($"{column.Label}{(column.Required ? " (always shown)" : "")}###dragColumn");
                                    if (ImGui.BeginDragDropSource())
                                    {
                                        try
                                        {
                                            draggedColumnTable = table;
                                            draggedColumnId = column.Id;
                                            ImGui.SetDragDropPayload("DUTY_TRACKER_COLUMN", BitConverter.GetBytes(index), ImGuiCond.Once);
                                            ImGui.TextUnformatted(column.Label);
                                        }
                                        finally
                                        {
                                            ImGui.EndDragDropSource();
                                        }
                                    }
                                    if (ImGui.BeginDragDropTarget())
                                    {
                                        try
                                        {
                                            var payload = ImGui.AcceptDragDropPayload("DUTY_TRACKER_COLUMN", ImGuiDragDropFlags.None);
                                            if (!payload.IsNull && payload.IsDelivery() && draggedColumnTable == table && draggedColumnId != null)
                                            {
                                                MoveConfiguredColumn(table, draggedColumnId, column.Id);
                                                draggedColumnTable = null;
                                                draggedColumnId = null;
                                            }
                                        }
                                        finally
                                        {
                                            ImGui.EndDragDropTarget();
                                        }
                                    }
                                }
                                finally
                                {
                                    ImGui.PopID();
                                }
                            }
                        }
                        finally
                        {
                            ImGui.TreePop();
                        }
                    }
                }
                finally
                {
                    ImGui.PopID();
                }
            }
            tableColumnsExpansion = null;
        }
    }
}
