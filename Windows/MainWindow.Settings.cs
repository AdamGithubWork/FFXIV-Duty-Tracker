using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;

namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private void DrawSettingsTab()
        {
            ImGui.Text("Duty Tracking Options");
            ImGui.TextDisabled("Check each duty type you would like recorded");
            ImGui.Spacing();
            ImGui.Separator();
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

            foreach (var contentType in contentTypes)
            {
                var isTracked =
                    config.TrackedContentTypeIds.Contains(contentType.Id);

                if (ImGui.Checkbox(contentType.Name, ref isTracked))
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
            ImGui.Text("Player Tracking Options");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            bool includeSelf = config.IncludeSelfInStatistics;

            if (ImGui.Checkbox("Include yourself in statistics", ref includeSelf))
            {
                config.IncludeSelfInStatistics = includeSelf;
                config.Save();
            }
            bool trackAlliance = config.TrackAllianceMembers;

            if (ImGui.Checkbox("Track alliance members", ref trackAlliance))
            {
                config.TrackAllianceMembers = trackAlliance;
                config.Save();
            }
        }

    }
}
