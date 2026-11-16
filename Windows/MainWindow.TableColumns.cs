using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private const float TableCellVerticalPadding = 3f;
        private sealed record TableColumnOption(string Id, string Label, bool DefaultVisible = true, bool Required = false);

        private static readonly Dictionary<string, TableColumnOption[]> TableColumnOptions = new()
        {
            ["PvP players"] =
            [
                new("name", "Name", Required: true), new("world", "World"), new("job", "Job"),
                new("side", "Side"), new("damage", "Damage dealt"),
                new("kills", "Kills"), new("deaths", "Deaths"), new("assists", "Assists"),
                new("race", "Race", false), new("gender", "Gender", false), new("status", "Status", false),
                new("result", "Result"), new("taken", "Damage taken", false), new("healing", "Healing done", false)
            ],
            ["Records"] =
            [
                new("duty", "Duty", Required: true), new("type", "Type"), new("job", "Job"),
                new("started", "Started"), new("time", "Time"), new("joined", "Joined in progress"),
                new("wipes", "Wipes"), new("lalafell", "Lalafell"), new("completed", "Completed"),
                new("sprouts", "Sprouts seen", false), new("mentors", "Mentors seen", false),
                new("players", "Players seen", false), new("deaths", "Known deaths", false),
                new("pvpPlacement", "PvP placement", false), new("pvpDamage", "Your PvP damage", false),
                new("pvpKills", "Your PvP kills", false), new("pvpAssists", "Your PvP assists", false), ..PvpSupportColumns()
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
                new("enemies", "Enemy encounters", false), ..PvpResultColumns(), ..PvpSupportColumns()
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

            return BeginStyledTable(id, TableColumnOptions[table].Length, flags,
                new Vector2(0, height), horizontalScroll ? minimumWidth : 0);
        }

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

        private static void EndStyledTable()
        {
            ImGui.EndTable();
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
            for (var i = 0; i < columns.Length; i++)
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
                if (!ImGui.TableSetColumnIndex(i))
                    continue;
                ImGui.TableHeader(TableHeaderLabel(labels[i], ImGui.GetContentRegionAvail().X));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(labels[i] + " — click to sort");
            }
        }

        private static void DrawTableCell(int index, string text)
        {
            if (ImGui.TableSetColumnIndex(index))
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
            ImGui.TextDisabled("Choose which columns to show. Click a table header to sort.");
            ImGui.PopTextWrapPos();
            foreach (var (table, columns) in TableColumnOptions)
            {
                ImGui.PushID(table);
                if (tableColumnsExpansion.HasValue)
                    ImGui.SetNextItemOpen(tableColumnsExpansion.Value, ImGuiCond.Always);
                if (ImGui.TreeNodeEx(table, ImGuiTreeNodeFlags.SpanAvailWidth))
                {
                    if (ImGui.Button("Reset columns"))
                    {
                        config.TableColumns.Remove(table);
                        config.Save();
                    }
                    foreach (var (column, index) in columns.Select((column, index) => (column, index)))
                    {
                        if (column.Id == "detail" && table != "Statistics: Race and gender")
                            continue;
                        if (column.Required)
                        {
                            ImGui.TextDisabled($"{column.Label} (always shown)");
                            continue;
                        }
                        var visible = IsTableColumnVisible(table, index);
                        if (!ImGui.Checkbox(column.Label, ref visible))
                            continue;
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
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
            tableColumnsExpansion = null;
        }
    }
}
