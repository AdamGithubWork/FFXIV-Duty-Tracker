using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MentorRouletteTracker.DutyTracking;
using System.Numerics;

namespace MentorRouletteTracker
{
    public sealed class DutyTimerWindow : Window
    {
        private readonly DutyTrackerService tracker;
        private readonly MainWindow mainWindow;
        private readonly Configuration config;
        private DutyRecord? cachedDuty;
        private int cachedRecordCount = -1;
        private int cachedRunCount;
        private string averageText = "No completed runs yet";

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

            if (!ReferenceEquals(cachedDuty, duty) || cachedRecordCount != config.History.Records.Count)
            {
                UpdateAverage(duty);
            }

            ImGui.TextUnformatted(duty.DutyName);
            ImGui.Separator();

            ImGui.TextUnformatted("Average clear time:");

            if (ImGui.Button(averageText, new Vector2(180, 0)))
            {
                mainWindow.OpenDutyHistory(duty.DutyName);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("View previous runs");
            }

            ImGui.TextDisabled($"Based on {cachedRunCount} full runs");
        }

        private void UpdateAverage(DutyRecord duty)
        {
            var completedRuns = config.History.Records
                .Where(record =>
                    record.Completed &&
                    !record.JoinedInProgress &&
                    record.TimeInDuty.TotalSeconds > 0 &&
                    string.Equals(
                        record.DutyName,
                        duty.DutyName,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            cachedDuty = duty;
            cachedRecordCount = config.History.Records.Count;
            cachedRunCount = completedRuns.Count;

            if (completedRuns.Count == 0)
            {
                averageText = "No completed runs yet";
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

        }
    }
}
