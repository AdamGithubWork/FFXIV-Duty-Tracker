using Dalamud.Bindings.ImGui;

namespace MentorRouletteTracker;

public partial class MainWindow
{
    private static readonly (string Name, string[] Options)[] JobFilterGroups =
    [
        ("Tank", ["PLD", "WAR", "DRK", "GNB", "GLA", "MRD"]),
        ("Healer", ["WHM", "SCH", "AST", "SGE", "CNJ"]),
        ("Melee", ["MNK", "DRG", "NIN", "SAM", "RPR", "VPR", "PGL", "LNC", "ROG"]),
        ("Ranged", ["BRD", "MCH", "DNC", "ARC"]),
        ("Caster", ["BLM", "SMN", "RDM", "PCT", "BLU", "THM", "ACN"])
    ];

    private void DrawJobAndWorldFilters()
    {
        var worlds = new[] { "Aether", "Primal", "Dynamis", "Crystal" }
            .Select(datacenter => (Name: datacenter, Options: worldDatacenters
                .Where(pair => pair.Value == datacenter).Select(pair => pair.Key).Order().ToArray()))
            .ToArray();
        var players = config.History.Records.SelectMany(record => record.Party);
        var columns = ImGui.GetContentRegionAvail().X >= ImGui.GetFontSize() * 19f ? 2 : 1;
        if (ImGui.BeginTable("JobAndWorldFilters", columns, ImGuiTableFlags.SizingStretchSame))
        {
            try
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Job");
                ImGui.SetNextItemWidth(-1);
                DrawGroupedFilter("Jobs", "All Jobs", "Roles", selectedPlayerJobs, JobFilterGroups,
                    players.Select(player => player.Job));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Home world");
                ImGui.SetNextItemWidth(-1);
                DrawGroupedFilter("Worlds", "All Worlds", "Datacenters", selectedPlayerWorlds, worlds,
                    players.Select(player => player.World));
            }
            finally
            {
                ImGui.EndTable();
            }
        }
    }

    private static void DrawGroupedFilter(
        string id, string allLabel, string groupLabel, HashSet<string> selected,
        (string Name, string[] Options)[] groups, IEnumerable<string> recordedOptions)
    {
        var preview = selected.Count switch
        {
            0 => allLabel,
            1 => selected.Single(),
            _ => groups.FirstOrDefault(group => selected.SetEquals(group.Options)).Name
                ?? $"{selected.Count} {id.ToLowerInvariant()}"
        };
        if (!ImGui.BeginCombo($"##{id}Filter", preview, ImGuiComboFlags.HeightLarge))
            return;

        try
        {
            ImGui.PushID(id);
            try
            {
                if (ImGui.Selectable(allLabel, selected.Count == 0))
                    selected.Clear();

                ImGui.Separator();
                ImGui.TextDisabled(groupLabel);
                foreach (var group in groups)
                {
                    var wholeGroup = group.Options.All(selected.Contains);
                    var groupCount = group.Options.Count(selected.Contains);
                    var label = groupCount > 0 && !wholeGroup
                        ? $"{group.Name} ({groupCount}/{group.Options.Length})" : group.Name;
                    if (ImGui.Checkbox(label + $"###Group{group.Name}", ref wholeGroup))
                        SelectFilterGroup(selected, group.Options, wholeGroup);
                }

                foreach (var group in groups)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled(group.Name);
                    ImGui.Indent();
                    try
                    {
                        foreach (var option in group.Options)
                            DrawFilterOption(option, selected);
                    }
                    finally
                    {
                        ImGui.Unindent();
                    }
                }

                var groupedOptions = groups.SelectMany(group => group.Options).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var otherOptions = recordedOptions.Concat(selected)
                    .Where(option => !string.IsNullOrWhiteSpace(option) && !groupedOptions.Contains(option))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
                if (otherOptions.Count > 0)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled("Other");
                    foreach (var option in otherOptions)
                        DrawFilterOption(option, selected);
                }
            }
            finally
            {
                ImGui.PopID();
            }
        }
        finally
        {
            ImGui.EndCombo();
        }
    }

    private static void SelectFilterGroup(HashSet<string> selected, IEnumerable<string> options, bool select)
    {
        if (select)
            selected.UnionWith(options);
        else
            selected.ExceptWith(options);
    }

    private static void DrawFilterOption(string option, HashSet<string> selected)
    {
        var isSelected = selected.Contains(option);
        if (!ImGui.Checkbox(option, ref isSelected))
            return;
        if (isSelected)
            selected.Add(option);
        else
            selected.Remove(option);
    }
}
