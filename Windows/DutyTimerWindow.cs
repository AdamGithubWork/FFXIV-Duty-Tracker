
using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MentorRouletteTracker
{
    public sealed class DutyTimerWindow : Window
    {
        private readonly DutyTrackerService tracker;
        private readonly MainWindow mainWindow;
        private readonly Configuration config;

        public DutyTimerWindow(
            DutyTrackerService tracker,
            MainWindow mainWindow,
            Configuration config)
            : base("Duty Timer###MentorRouletteDutyTimer",
                ImGuiWindowFlags.NoTitleBar |
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoCollapse)
        {
            this.tracker = tracker;
            this.mainWindow = mainWindow;
            this.config = config;

            IsOpen = false;
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        public override void Draw()
        {
            var duty = tracker.ActiveDuty;

            if (duty == null)
                return;

            var completedRuns = config.Records
                .Where(record =>
                    record.Completed &&
                    record.TimeInDuty.TotalSeconds > 0 &&
                    string.Equals(
                        record.DutyName,
                        duty.DutyName,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            string averageText;

            if (completedRuns.Count == 0)
            {
                averageText = "No previous data";
            }
            else
            {
                double averageSeconds = completedRuns.Average(
                    record => record.TimeInDuty.TotalSeconds);

                var average = TimeSpan.FromSeconds(averageSeconds);

                averageText =
                    $"{(int)average.TotalHours:00}:" +
                    $"{average.Minutes:00}:" +
                    $"{average.Seconds:00}";
            }

            ImGui.TextUnformatted(duty.DutyName);
            ImGui.Separator();

            ImGui.TextUnformatted("Average completion time:");

            if (ImGui.Button(averageText, new Vector2(180, 0)))
            {
                mainWindow.OpenDutyHistory(duty.DutyName);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Open the history of this duty");
            }

            ImGui.TextDisabled(
                $"Based on {completedRuns.Count} completed runs");
        }
    }
}
