using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private bool? settingsExpansion;
        private bool? milestoneExpansion;
        private bool openResetSettingsConfirmation;
        private bool appearanceSettingsDirty;

        private void SaveAppearanceChanges()
        {
            if (!appearanceSettingsDirty)
                return;
            config.Save();
            appearanceSettingsDirty = false;
        }

        private void DrawSettingsTab()
        {
            try
            {
                if (ImGui.BeginChild("SettingsContent", Vector2.Zero, false))
                    DrawSettingsContent();
            }
            finally
            {
                ImGui.EndChild();
            }
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
                openResetSettingsConfirmation = true;
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
                try
                {
                    var armed = tracker.Armed;

                    if (ImGui.Checkbox("Record duties", ref armed))
                    {
                        tracker.Armed = armed;
                    }

                    ImGui.PushTextWrapPos();
                    try
                    {
                        ImGui.TextDisabled(
                            "On by default and stays on until you turn it off. Your choice is saved when you reload or restart.");
                    }
                    finally
                    {
                        ImGui.PopTextWrapPos();
                    }

                    ImGui.Spacing();
                    ImGui.TextDisabled("Choose which duties to record.");
                    ImGui.Spacing();
                    foreach (var contentType in contentTypes)
                    {
                        var pvpDisabled = BeginPvpControl(contentType.Id == 6);
                        try
                        {
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
                        }
                        finally
                        {
                            EndPvpControl(pvpDisabled);
                        }
                    }
                }
                finally
                {
                    EndSettingsSection();
                }
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Player tracking", defaultOpen: true))
            {
                try
                {
                    bool includeSelf = config.IncludeSelfInStatistics;

                    if (ImGui.Checkbox(
                        "Include yourself in statistics",
                        ref includeSelf))
                    {
                        config.IncludeSelfInStatistics = includeSelf;
                        config.Save();
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Your data is still recorded when this is off. It just won't be included in statistics.");

                    bool trackAlliance = config.TrackAllianceMembers;

                    if (ImGui.Checkbox(
                        "Track alliance members",
                        ref trackAlliance))
                    {
                        config.TrackAllianceMembers = trackAlliance;
                        config.Save();
                    }
                }
                finally
                {
                    EndSettingsSection();
                }
            }

            ImGui.Spacing();
            if (BeginSettingsSection("PvP", defaultOpen: true))
            {
                try
                {
                    DrawPvpStatsStatus();
                    var pvpDisabled = BeginPvpControl();
                    try
                    {
                        var trackEnemies = config.TrackPvpEnemies;
                        if (ImGui.Checkbox("Track opposing members in PvP matches", ref trackEnemies))
                        {
                            config.TrackPvpEnemies = trackEnemies;
                            config.Save();
                        }
                    }
                    finally
                    {
                        EndPvpControl(pvpDisabled);
                    }
                    ImGui.TextDisabled("New matches only.");
                }
                finally
                {
                    EndSettingsSection();
                }
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Duty timer"))
            {
                try
                {
                    ImGui.TextDisabled("Choose which duties show the timer.");
                    ImGui.Spacing();
                    foreach (var contentType in contentTypes)
                    {
                        var pvpDisabled = BeginPvpControl(contentType.Id == 6);
                        try
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
                        finally
                        {
                            EndPvpControl(pvpDisabled);
                        }
                    }
                }
                finally
                {
                    EndSettingsSection();
                }
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Appearance"))
            {
                try
                {
                    DrawAppearanceSettings();
                }
                finally
                {
                    EndSettingsSection();
                }
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Statistics sections"))
            {
                try
                {
                    DrawStatisticsCustomization();
                }
                finally
                {
                    EndSettingsSection();
                }
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Table columns"))
            {
                try
                {
                    DrawTableColumnSettings();
                }
                finally
                {
                    EndSettingsSection();
                }
            }

            ImGui.Spacing();
            if (BeginSettingsSection("Milestones"))
            {
                try
                {
                    DrawMilestoneCustomization();
                }
                finally
                {
                    EndSettingsSection();
                }
            }

            ImGui.Spacing();
            if (BeginSettingsSection("History & exports"))
            {
                try
                {
                    DrawHistorySettings();
                }
                finally
                {
                    EndSettingsSection();
                }
            }

            settingsExpansion = null;
        }

        private bool BeginPvpControl(bool isPvp = true)
        {
            var disabled = isPvp && !tracker.PvpTrackingAvailable;
            if (disabled)
                ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.6f, 0.6f, 0.6f, 1f));
            ImGui.BeginDisabled(disabled);
            return disabled;
        }

        private static void EndPvpControl(bool disabled)
        {
            ImGui.EndDisabled();
            if (disabled)
                ImGui.PopStyleColor();
        }

        private void DrawPvpStatsStatus()
        {
            var enabled = tracker.PvpTrackingAvailable;
            ImGui.PushStyleColor(ImGuiCol.ChildBg, enabled ? new Vector4(0.06f, 0.20f, 0.09f, 1f) : new Vector4(0.24f, 0.06f, 0.05f, 1f));
            try
            {
                ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 4));
                try
                {
                    try
                    {
                        if (ImGui.BeginChild("PvpStatsStatus", new Vector2(0, ImGui.GetTextLineHeight() + 10), true,
                            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                            ImGui.TextColored(enabled ? new Vector4(0.45f, 1f, 0.55f, 1f) : new Vector4(1f, 0.45f, 0.40f, 1f),
                                enabled ? "PvP Tracker enabled" : "PvP Tracker unavailable");
                    }
                    finally
                    {
                        ImGui.EndChild();
                    }
                }
                finally
                {
                    ImGui.PopStyleVar();
                }
            }
            finally
            {
                ImGui.PopStyleColor();
            }
            ImGui.TextWrapped(enabled
                ? "PvP Tracker provides player rosters, match scores, and results."
                : "Enable PvP Tracker in /xlplugins to record PvP.");
            if (ImGui.SmallButton("PvP Tracker repository"))
                Dalamud.Utility.Util.OpenLink("https://github.com/wrath16/PvpStats");
            ImGui.Spacing();
        }

        private void DrawResetSettingsConfirmation()
        {
            const string title = "Reset all settings##ResetAllSettings";
            if (openResetSettingsConfirmation)
            {
                ImGui.OpenPopup(title);
                openResetSettingsConfirmation = false;
            }
            if (!ImGui.IsPopupOpen(title))
                return;
            const string message = "Reset settings, colors, columns, and filters to their defaults? Duty records and notes stay saved.";
            var style = ImGui.GetStyle();
            var width = ImGui.GetFontSize() * 28;
            var textHeight = ImGui.CalcTextSize(message, false, width - style.WindowPadding.X * 2).Y;
            var height = textHeight + style.WindowPadding.Y * 2 + style.ItemSpacing.Y * 3 +
                ImGui.GetFrameHeight() + ImGui.GetFrameHeightWithSpacing();
            ImGui.SetNextWindowSize(new Vector2(width, height), ImGuiCond.Always);
            ImGui.SetNextWindowPos(ImGui.GetWindowPos() + ImGui.GetWindowSize() * 0.5f,
                ImGuiCond.Always, new Vector2(0.5f));
            if (!ImGui.BeginPopupModal(title, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings))
                return;
            try
            {
                ImGui.TextWrapped(message);
                ImGui.Spacing();
                if (ImGui.Button("Reset settings"))
                {
                    ResetAllSettings();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel##ResetSettings"))
                    ImGui.CloseCurrentPopup();
            }
            finally
            {
                ImGui.EndPopup();
            }
        }

        private void ResetAllSettings()
        {
            config.ResetPreferences();
            ClearFilters();
            filtersExpanded = false;
            selectedHeatmapMetric = 0;
            settingsExpansion = false;
            milestoneExpansion = false;
            tableColumnsExpansion = false;
        }

        private void DrawHistorySettings()
        {
            UpdateCsvExport();
            UpdateHistoryBackup();
            ImGui.TextWrapped("Back up your history, export it to CSV, or clear saved records. Filters don't limit these actions.");
            ImGui.Spacing();
            ImGui.BeginDisabled(historyBackupTask != null);
            try
            {
                if (ImGui.Button("Create backup"))
                    StartHistoryBackup();
            }
            finally
            {
                ImGui.EndDisabled();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Saves duty history, player details, notes, and your current duty. Settings and CSV files aren't included.");
            ImGui.SameLine();
            if (ImGui.Button("Open backups folder"))
                OpenHistoryBackupsFolder();
            ImGui.Spacing();
            ImGui.BeginDisabled(historyBackupTask != null || !tracker.CanRestoreHistory);
            try
            {
                if (ImGui.Button("Restore backup..."))
                    OpenHistoryRestore();
            }
            finally { ImGui.EndDisabled(); }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Restore outside a duty. Your current history is backed up first. Settings stay as they are.");
            DrawHistoryRestore();
            if (!string.IsNullOrEmpty(lastBackupMessage))
            {
                ImGui.PushTextWrapPos();
                try
                {
                    ImGui.TextColored(backupFailed ? new Vector4(1f, 0.4f, 0.4f, 1f) : new Vector4(0.4f, 1f, 0.4f, 1f),
                        lastBackupMessage);
                }
                finally
                {
                    ImGui.PopTextWrapPos();
                }
            }
            ImGui.Spacing();
            ImGui.BeginDisabled(csvExportTask != null);
            try
            {
                if (ImGui.Button("Export CSV"))
                    StartCsvExport();
            }
            finally
            {
                ImGui.EndDisabled();
            }

            ImGui.SameLine();

            if (ImGui.Button("Clear all records"))
            {
                ImGui.OpenPopup("ConfirmClear");
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Clears saved history, even records hidden by filters. Your current duty, settings, exports, and backups stay.");

            if (ImGui.BeginPopup("ConfirmClear"))
            {
                try
                {
                    ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26);
                    try
                    {
                        ImGui.TextUnformatted("Clear all saved duty records?");
                        ImGui.TextWrapped("This removes saved duties, player details, and notes from your history. You can't undo it here.");
                        ImGui.Spacing();
                        ImGui.TextWrapped("Your current duty and settings stay. CSV exports, backups, and older recovery copies aren't deleted.");
                    }
                    finally
                    {
                        ImGui.PopTextWrapPos();
                    }

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
                }
                finally
                {
                    ImGui.EndPopup();
                }
            }

            if (!string.IsNullOrEmpty(lastExportMessage))
            {
                ImGui.Spacing();

                ImGui.TextColored(
                    exportFailed ? new Vector4(1f, 0.4f, 0.4f, 1f) : new Vector4(0.4f, 1f, 0.4f, 1f),
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
            try
            {
                ImGui.TextDisabled("Choose background colors below. Use Opacity to control transparency. Reset restores the default color.");
            }
            finally
            {
                ImGui.PopTextWrapPos();
            }
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
                appearanceSettingsDirty = true;
                SaveAppearanceChanges();
            }
            ImGui.Spacing();
            ImGui.TextDisabled("Table preview");
            if (BeginStyledTable("AppearancePreview", 2,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                try
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
                }
                finally
                {
                    EndStyledTable();
                }
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
            try
            {
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
                    appearanceSettingsDirty = true;
                    customized = true;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                    SaveAppearanceChanges();
                ImGui.SameLine();
                if (customized)
                {
                    if (ImGui.SmallButton("Reset"))
                    {
                        update(null);
                        appearanceSettingsDirty = true;
                        SaveAppearanceChanges();
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
                    appearanceSettingsDirty = true;
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                    SaveAppearanceChanges();
            }
            finally
            {
                ImGui.PopID();
            }
            ImGui.Spacing();
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
                try
                {
                    if (milestoneExpansion.HasValue)
                        ImGui.SetNextItemOpen(milestoneExpansion.Value, ImGuiCond.Always);
                    if (ImGui.TreeNodeEx(MilestoneTitle(section.Title), ImGuiTreeNodeFlags.SpanAvailWidth))
                    {
                        try
                        {
                            foreach (var row in section.Rows)
                            {
                                var key = MilestoneKey(section.Title, row.Label);
                                var visible = !config.HiddenMilestones.Contains(key);
                                var toggled = false;
                                var pvpDisabled = BeginPvpControl(section.Title is "PvP player stats" or "Your PvP results" || row.Label.Contains("(PvP)", StringComparison.Ordinal));
                                try
                                {
                                    toggled = ImGui.Checkbox(row.Label, ref visible);
                                }
                                finally
                                {
                                    EndPvpControl(pvpDisabled);
                                }
                                if (!toggled)
                                    continue;
                                if (visible)
                                    config.HiddenMilestones.Remove(key);
                                else
                                    config.HiddenMilestones.Add(key);
                                changed = true;
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
            milestoneExpansion = null;

            if (changed)
                config.Save();
        }

    }
}
