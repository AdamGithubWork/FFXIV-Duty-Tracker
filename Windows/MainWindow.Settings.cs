
using Dalamud.Bindings.ImGui;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawSettingsTab()
        {
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

            ImGui.Text("Duty recording");
            ImGui.TextDisabled(
                "Choose which duties to record.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            foreach (var contentType in contentTypes)
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

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Duty timer");
            ImGui.TextDisabled(
                "Choose which duties show the timer.");

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

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Player tracking");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

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
        }
    }
}
