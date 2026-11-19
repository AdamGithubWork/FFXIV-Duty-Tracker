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
        private DutyRecord? lastDuty;
        private DutyRecord? cachedDuty;
        private int cachedRecordCount = -1;
        private long cachedChangeVersion = -1;
        private int cachedRunCount;
        private string averageText = "No full runs yet";
        private int appearanceColorCount;

        public DutyTimerWindow(
            DutyTrackerService tracker,
            MainWindow mainWindow,
            Configuration config)
            : base("Duty Timer###MentorRouletteDutyTimer",
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoCollapse)
        {
            this.tracker = tracker;
            this.mainWindow = mainWindow;
            this.config = config;

            IsOpen = true;
            RespectCloseHotkey = false;
        }

        public override void PreOpenCheck()
        {
            var duty = tracker.ActiveDuty;
            if (!ReferenceEquals(lastDuty, duty))
            {
                lastDuty = duty;
                IsOpen = true;
            }
        }

        public override void PreDraw()
        {
            appearanceColorCount = WindowAppearance.Push(config);
        }

        public override void PostDraw()
        {
            if (appearanceColorCount > 0)
                ImGui.PopStyleColor(appearanceColorCount);
            appearanceColorCount = 0;
        }

        public override bool DrawConditions()
        {
            var contentTypeId = tracker.ActiveContentTypeId;
            return tracker.ActiveDuty != null &&
                contentTypeId.HasValue &&
                config.TimerVisibleContentTypeIds.Contains(contentTypeId.Value);
        }

        public override void Draw()
        {
            var duty = tracker.ActiveDuty;

            if (duty == null)
                return;

            RefreshAverage(duty);

            ImGui.TextUnformatted(duty.DutyName);
            ImGui.Separator();

            ImGui.TextUnformatted("Average clear time:");

            if (ImGui.Button(averageText + "###AverageClearTime", new Vector2(180, 0)))
            {
                mainWindow.OpenDutyHistory(duty.DutyName, fullRunsOnly: true);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Click to view the full runs used for this average");
            }

            ImGui.TextDisabled($"Based on {cachedRunCount} full runs");
        }

        internal static bool IsFullRunForDuty(DutyRecord record, string dutyName) =>
            record.Completed && !record.JoinedInProgress && record.TimeInDuty > TimeSpan.Zero &&
            string.Equals(record.DutyName, dutyName, StringComparison.OrdinalIgnoreCase);

        private void RefreshAverage(DutyRecord duty)
        {
            if (!ReferenceEquals(cachedDuty, duty) || cachedRecordCount != config.History.Records.Count ||
                cachedChangeVersion != config.History.ChangeVersion)
                UpdateAverage(duty);
        }

        private void UpdateAverage(DutyRecord duty)
        {
            var completedRuns = config.History.Records
                .Where(record => IsFullRunForDuty(record, duty.DutyName))
                .ToList();

            cachedDuty = duty;
            cachedRecordCount = config.History.Records.Count;
            cachedChangeVersion = config.History.ChangeVersion;
            cachedRunCount = completedRuns.Count;

            if (completedRuns.Count == 0)
            {
                averageText = "No full runs yet";
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
