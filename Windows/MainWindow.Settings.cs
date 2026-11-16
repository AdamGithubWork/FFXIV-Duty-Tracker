using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private bool? settingsExpansion;
        private bool? milestoneExpansion;

        private void DrawSettingsTab()
        {
            if (ImGui.BeginChild("SettingsContent", Vector2.Zero, false))
                DrawSettingsContent();
            ImGui.EndChild();
        }

        private void DrawSettingsContent()
        {
            if (ImGui.Button("Collapse all##Settings"))
            {
                settingsExpansion = false;
                milestoneExpansion = false;
                tableColumnsExpansion = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Expand all##Settings"))
            {
                settingsExpansion = true;
                milestoneExpansion = true;
                tableColumnsExpansion = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("Reset all settings"))
                ImGui.OpenPopup("ResetAllSettings");
            if (ImGui.BeginPopup("ResetAllSettings"))
            {
                ImGui.TextWrapped("Reset settings, colors, columns, and filters to their defaults? Duty records and notes stay saved.");
                if (ImGui.Button("Reset settings"))
                {
                    ResetAllSettings();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel##ResetSettings"))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            ImGui.Spacing();

            var contentTypes = new (uint Id, string Name)[]
            {
                (2, "Dungeons"),
                (3, "Guildhests"),
                (4, "Trials"),
                (5, "Raids"),
                (6, "PvP"),
                (21, "Deep Dungeons"),
                (28, "Ultimate Raids"),
                (30, "V&C Dungeon Finder"),
                (37, "Chaotic Alliance Raid")
            };

            if (BeginSettingsSection("Duty recording", defaultOpen: true))
            {
                var armed = tracker.Armed;

                if (ImGui.Checkbox("Record duties", ref armed))
                {
                    tracker.Armed = armed;
                }

                ImGui.PushTextWrapPos();
                ImGui.TextDisabled(
                    "Turn this off before duties you don't want recorded.");
                ImGui.PopTextWrapPos();

                ImGui.Spacing();
                ImGui.TextDisabled("Choose which duties to record.");
                ImGui.Spacing();
                foreach (var contentType in contentTypes)
                {
                    var missingPvpStats = contentType.Id == 6 && !tracker.PvpTrackingAvailable;
                    ImGui.BeginDisabled(missingPvpStats);
                    var isTracked =
                        config.TrackedContentTypeIds.Contains(contentType.Id);

                    if (ImGui.Checkbox(
                        $"{contentType.Name}##DutyTracking",
                        ref isTracked))
                    {
                        if (isTracked)
                            config.TrackedContentTypeIds.Add(contentType.Id);
                        else
                            config.TrackedContentTypeIds.Remove(contentType.Id);

                        config.Save();
                    }
                    ImGui.EndDisabled();
                    if (missingPvpStats)
                        ImGui.TextWrapped("Enable PvP Tracker (PvPStats) in /xlplugins to record PvP duties.");
                }
                EndSettingsSection();
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Player tracking", defaultOpen: true))
            {
                bool includeSelf = config.IncludeSelfInStatistics;

                if (ImGui.Checkbox(
                    "Include yourself in statistics",
                    ref includeSelf))
                {
                    config.IncludeSelfInStatistics = includeSelf;
                    config.Save();
                }

                bool trackAlliance = config.TrackAllianceMembers;

                if (ImGui.Checkbox(
                    "Track alliance members",
                    ref trackAlliance))
                {
                    config.TrackAllianceMembers = trackAlliance;
                    config.Save();
                }
                EndSettingsSection();
            }

            ImGui.Spacing();
            if (BeginSettingsSection("PvP", defaultOpen: true))
            {
                ImGui.TextWrapped(tracker.PvpTrackingAvailable
                    ? "PvPStats is enabled. PvP recording is available."
                    : "PvP recording is disabled. Enable PvP Tracker (PvPStats) in /xlplugins first.");
                var trackEnemies = config.TrackPvpEnemies;
                if (ImGui.Checkbox("Track opposing members in PvP matches", ref trackEnemies))
                {
                    config.TrackPvpEnemies = trackEnemies;
                    config.Save();
                }
                ImGui.PushTextWrapPos();
                ImGui.TextDisabled("Applies to new duties. Turn this off to record teammates only. Existing history stays saved.");
                ImGui.TextDisabled("PvPStats adds teammate/enemy labels, damage dealt, kills, deaths, assists, and placement after a match. Teammates include your whole team, even when alliance tracking is off.");
                ImGui.TextDisabled("Nearby players can have race, gender, and status recorded. Hidden identities and missing scores stay unknown.");
                ImGui.PopTextWrapPos();
                EndSettingsSection();
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Duty timer"))
            {
                ImGui.TextDisabled("Choose which duties show the timer.");
                ImGui.Spacing();
                foreach (var contentType in contentTypes)
                {
                    var showTimer =
                        config.TimerVisibleContentTypeIds.Contains(contentType.Id);

                    if (ImGui.Checkbox(
                        $"{contentType.Name}##TimerVisibility",
                        ref showTimer))
                    {
                        if (showTimer)
                            config.TimerVisibleContentTypeIds.Add(contentType.Id);
                        else
                            config.TimerVisibleContentTypeIds.Remove(contentType.Id);

                        config.Save();
                    }
                }
                EndSettingsSection();
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Appearance"))
            {
                DrawAppearanceSettings();
                EndSettingsSection();
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Statistics sections"))
            {
                DrawStatisticsCustomization();
                EndSettingsSection();
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Table columns"))
            {
                DrawTableColumnSettings();
                EndSettingsSection();
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Milestones"))
            {
                DrawMilestoneCustomization();
                EndSettingsSection();
            }

            ImGui.Spacing();
            if (BeginSettingsSection("History & exports"))
            {
                DrawHistorySettings();
                EndSettingsSection();
            }

            settingsExpansion = null;
        }

        private void ResetAllSettings()
        {
            config.ResetPreferences();
            tracker.Armed = config.StorageAvailable;
            ClearFilters();
            selectedHeatmapMetric = 0;
            settingsExpansion = false;
            milestoneExpansion = false;
            tableColumnsExpansion = false;
        }

        private void DrawHistorySettings()
        {
            ImGui.TextWrapped("Export all saved records to CSV, or clear saved duty history. Filters don't limit these actions.");
            ImGui.Spacing();
            if (ImGui.Button("Export CSV"))
            {
                var path = Path.Combine(
                    exportDirectory,
                    $"mentor_roulette_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                CsvExporter.Export(config.History.Records, path);

                lastExportMessage =
                    $"Exported {config.History.Records.Count} records to {path}";
            }

            ImGui.SameLine();

            if (ImGui.Button("Clear all records"))
            {
                ImGui.OpenPopup("ConfirmClear");
            }

            if (ImGui.BeginPopup("ConfirmClear"))
            {
                ImGui.Text(
                    "Clear all duty records? You can't undo this.");

                ImGui.Spacing();

                if (ImGui.Button("Yes, clear"))
                {
                    config.History.Records.Clear();
                    config.History.Save();

                    selectedRecord = null;
                    showDetailWindow = false;

                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            if (!string.IsNullOrEmpty(lastExportMessage))
            {
                ImGui.Spacing();

                ImGui.TextColored(
                    new Vector4(0.4f, 1f, 0.4f, 1f),
                    lastExportMessage);
            }

            ImGui.Spacing();
        }

        private bool BeginSettingsSection(string title, bool defaultOpen = false)
        {
            if (settingsExpansion.HasValue)
                ImGui.SetNextItemOpen(settingsExpansion.Value, ImGuiCond.Always);
            var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            if (!ImGui.CollapsingHeader(title, flags))
                return false;
            ImGui.Indent();
            ImGui.Spacing();
            return true;
        }

        private static void EndSettingsSection()
        {
            ImGui.Spacing();
            ImGui.Unindent();
        }

        private void DrawAppearanceSettings()
        {
            ImGui.PushTextWrapPos();
            ImGui.TextDisabled("Choose background colors below. Use Opacity to control transparency. Reset restores the default color.");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            DrawBackgroundColor("Window background", config.WindowBackgroundColor,
                ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.WindowBg)), value => config.WindowBackgroundColor = value);
            DrawBackgroundColor("Panel background", config.PanelBackgroundColor,
                ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.ChildBg)), value => config.PanelBackgroundColor = value);
            DrawBackgroundColor("Table headers", config.TableHeaderBackgroundColor,
                WindowAppearance.DefaultTableHeader, value => config.TableHeaderBackgroundColor = value);
            DrawBackgroundColor("Table rows", config.TableRowBackgroundColor,
                ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.TableRowBg)), value => config.TableRowBackgroundColor = value);
            DrawBackgroundColor("Alternate table rows", config.AlternateRowBackgroundColor,
                WindowAppearance.DefaultAlternateRow, value => config.AlternateRowBackgroundColor = value);

            if (ImGui.Button("Reset colors"))
            {
                config.WindowBackgroundColor = null;
                config.PanelBackgroundColor = null;
                config.TableHeaderBackgroundColor = null;
                config.TableRowBackgroundColor = null;
                config.AlternateRowBackgroundColor = null;
                config.Save();
            }
            ImGui.SameLine();
            if (ImGui.Button("Make colors opaque"))
                MakeBackgroundColorsOpaque();
            ImGui.Spacing();
            ImGui.TextDisabled("Table preview");
            if (BeginStyledTable("AppearancePreview", 2,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Row");
                ImGui.TableSetupColumn("Example");
                ImGui.TableHeadersRow();
                for (var i = 0; i < 3; i++)
                {
                    ImGui.TableNextRow();
                    DrawTableCell(0, $"Row {i + 1}");
                    DrawTableCell(1, i % 2 == 0 ? "Table row color" : "Alternate row color");
                }
                EndStyledTable();
            }
            ImGui.Spacing();
            var wrap = config.WrapTableHeaders;
            if (ImGui.Checkbox("Wrap headers in very narrow columns", ref wrap))
            {
                config.WrapTableHeaders = wrap;
                config.Save();
            }
        }

        private void DrawBackgroundColor(string label, uint? saved, Vector4 fallback, Action<uint?> update)
        {
            ImGui.PushID(label);
            ImGui.TextUnformatted(label);
            var color = saved.HasValue ? ImGui.ColorConvertU32ToFloat4(saved.Value) : fallback;
            var customized = saved.HasValue;
            var resetWidth = ImGui.CalcTextSize("Default").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetNextItemWidth(Math.Max(1, Math.Min(280, ImGui.GetContentRegionAvail().X - resetWidth)));
            if (ImGui.ColorEdit4("##Color", ref color, ImGuiColorEditFlags.NoAlpha))
            {
                if (!customized)
                    color.W = 1f;
                update(ImGui.ColorConvertFloat4ToU32(color));
                config.Save();
                customized = true;
            }
            ImGui.SameLine();
            if (customized)
            {
                if (ImGui.SmallButton("Reset"))
                {
                    update(null);
                    config.Save();
                }
            }
            else
            {
                ImGui.TextDisabled("Default");
            }
            var opacity = color.W * 100f;
            var opacityLabelWidth = ImGui.CalcTextSize("Opacity").X + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetNextItemWidth(Math.Max(1, Math.Min(280, ImGui.GetContentRegionAvail().X - opacityLabelWidth)));
            if (ImGui.SliderFloat("Opacity", ref opacity, 0, 100, "%.0f%%"))
            {
                color.W = opacity / 100f;
                update(ImGui.ColorConvertFloat4ToU32(color));
                config.Save();
            }
            ImGui.PopID();
            ImGui.Spacing();
        }

        private void MakeBackgroundColorsOpaque()
        {
            static uint? Opaque(uint? color) => color.HasValue ? color.Value | 0xFF000000u : null;
            config.WindowBackgroundColor = Opaque(config.WindowBackgroundColor);
            config.PanelBackgroundColor = Opaque(config.PanelBackgroundColor);
            config.TableHeaderBackgroundColor = Opaque(config.TableHeaderBackgroundColor);
            config.TableRowBackgroundColor = Opaque(config.TableRowBackgroundColor);
            config.AlternateRowBackgroundColor = Opaque(config.AlternateRowBackgroundColor);
            config.Save();
        }

        private void DrawStatisticsCustomization()
        {
            var changed = false;
            void Option(string label, bool visible, Action<bool> update)
            {
                if (ImGui.Checkbox(label, ref visible))
                {
                    update(visible);
                    changed = true;
                }
            }

            Option("Overview", config.ShowStatisticsOverview, value => config.ShowStatisticsOverview = value);
            Option("Race", config.ShowRaceStatistics, value => config.ShowRaceStatistics = value);
            Option("Gender", config.ShowGenderStatistics, value => config.ShowGenderStatistics = value);
            Option("Job", config.ShowJobStatistics, value => config.ShowJobStatistics = value);
            Option("Status", config.ShowStatusStatistics, value => config.ShowStatusStatistics = value);
            Option("Home world", config.ShowWorldStatistics, value => config.ShowWorldStatistics = value);
            Option("Datacenter", config.ShowDatacenterStatistics, value => config.ShowDatacenterStatistics = value);

            if (changed)
                config.Save();
        }

        private void DrawMilestoneCustomization()
        {
            config.HiddenMilestones ??= new();
            RefreshMilestonesIfNeeded(objectTable.LocalPlayer?.Name.TextValue);

            var changed = false;
            if (ImGui.Button("Show all milestones") && config.HiddenMilestones.Count > 0)
            {
                config.HiddenMilestones.Clear();
                changed = true;
            }
            ImGui.Separator();

            if (milestoneSections.Count == 0)
                ImGui.TextWrapped("Complete a duty or clear the duty filters to see the milestone options.");
            foreach (var section in milestoneSections)
            {
                ImGui.PushID(section.Title);
                if (milestoneExpansion.HasValue)
                    ImGui.SetNextItemOpen(milestoneExpansion.Value, ImGuiCond.Always);
                if (ImGui.TreeNodeEx(section.Title, ImGuiTreeNodeFlags.SpanAvailWidth))
                {
                    foreach (var row in section.Rows)
                    {
                        var key = MilestoneKey(section.Title, row.Label);
                        var visible = !config.HiddenMilestones.Contains(key);
                        if (!ImGui.Checkbox(row.Label, ref visible))
                            continue;
                        if (visible)
                            config.HiddenMilestones.Remove(key);
                        else
                            config.HiddenMilestones.Add(key);
                        changed = true;
                    }
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
            milestoneExpansion = null;

            if (changed)
                config.Save();
        }

    }
}
