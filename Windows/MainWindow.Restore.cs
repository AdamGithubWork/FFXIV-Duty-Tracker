using Dalamud.Bindings.ImGui;
using MentorRouletteTracker.Storage;

namespace MentorRouletteTracker;

public partial class MainWindow
{
    private string[] restoreBackupFiles = [];
    private string? restoreBackupName;
    private Task<(HistoryData? Data, string? Error)>? restorePreviewTask;
    private HistoryData? restorePreview;
    private string restoreError = string.Empty;
    private bool restoreConfirmed;

    private void OpenHistoryRestore()
    {
        restoreBackupName = null;
        restorePreview = null;
        restorePreviewTask = null;
        restoreConfirmed = false;
        restoreError = string.Empty;
        try
        {
            restoreBackupFiles = Directory.Exists(config.History.BackupDirectory)
                ? Directory.GetFiles(config.History.BackupDirectory, "history-*.sqlite")
                    .Select(Path.GetFileName).OfType<string>().OrderDescending(StringComparer.Ordinal).ToArray()
                : [];
        }
        catch (Exception ex)
        {
            restoreBackupFiles = [];
            restoreError = $"Couldn't list backups: {ex.Message}";
        }
        ImGui.OpenPopup("Restore history backup");
    }

    private static async Task<(HistoryData? Data, string? Error)> ReadHistoryBackup(string path)
    {
        try { return (await Task.Run(() => SqliteHistoryStore.ReadBackup(path)).ConfigureAwait(false), null); }
        catch (Exception ex) { return (null, ex.Message); }
    }

    private void DrawHistoryRestore()
    {
        if (!ImGui.BeginPopupModal("Restore history backup", ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32);
            try
            {
                ImGui.TextWrapped("This replaces your saved history with the backup you choose. Your current history gets a safety backup first.");
                ImGui.TextWrapped("Restoring may take a moment for large histories.");
                ImGui.Spacing();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 32);
                if (ImGui.BeginCombo("##HistoryBackup", restoreBackupName ?? "Choose a backup"))
                {
                    try
                    {
                        foreach (var file in restoreBackupFiles)
                        {
                            if (ImGui.Selectable(file, file == restoreBackupName))
                            {
                                restoreBackupName = file;
                                restorePreview = null;
                                restoreConfirmed = false;
                                restoreError = string.Empty;
                                restorePreviewTask = ReadHistoryBackup(Path.Combine(config.History.BackupDirectory, file));
                            }
                        }
                    }
                    finally { ImGui.EndCombo(); }
                }
                if (restorePreviewTask is { IsCompleted: true } completed)
                {
                    var result = completed.GetAwaiter().GetResult();
                    restorePreview = result.Data;
                    restoreError = result.Error ?? string.Empty;
                    restorePreviewTask = null;
                }
                if (restoreBackupFiles.Length == 0)
                    ImGui.TextWrapped("No backups found. Use Create backup first, or put a Noumenon history backup in the backups folder.");
                if (restorePreviewTask != null)
                    ImGui.TextUnformatted("Checking backup...");
                if (restorePreview != null)
                {
                    ImGui.TextUnformatted($"{restorePreview.Records.Count:N0} saved duties in this backup.");
                    ImGui.TextWrapped("Any unfinished duty comes back as an incomplete record. Settings and CSV files stay as they are.");
                    ImGui.Checkbox("Replace my current history with this backup", ref restoreConfirmed);
                }
                if (!string.IsNullOrEmpty(restoreError))
                    ImGui.TextWrapped(restoreError);
                if (!tracker.CanRestoreHistory)
                    ImGui.TextWrapped("Leave your duty and wait for recording to finish before restoring.");
                ImGui.Spacing();
                ImGui.BeginDisabled(!restoreConfirmed || restorePreview == null || restorePreviewTask != null ||
                    historyBackupTask != null || !tracker.CanRestoreHistory);
                try
                {
                    if (ImGui.Button("Restore history"))
                    {
                        if (TryRestoreHistory())
                            ImGui.CloseCurrentPopup();
                    }
                }
                finally { ImGui.EndDisabled(); }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                    ImGui.CloseCurrentPopup();
            }
            finally { ImGui.PopTextWrapPos(); }
        }
        finally { ImGui.EndPopup(); }
    }

    private bool TryRestoreHistory()
    {
        if (!restoreConfirmed || restorePreview == null || historyBackupTask != null || !tracker.CanRestoreHistory)
            return false;
        try
        {
            var safetyBackup = config.History.RestoreBackup(restorePreview);
            selectedRecord = noteRecord = recordToEdit = recordPendingDeletion = null;
            showDetailWindow = showNoteWindow = openEditRecord = openDeleteConfirmation = false;
            backupFailed = false;
            lastBackupMessage = $"History restored. Your previous history is backed up at {safetyBackup}";
            restorePreview = null;
            restoreConfirmed = false;
            return true;
        }
        catch (Exception ex)
        {
            restoreError = $"Couldn't restore history: {ex.Message}";
            return false;
        }
    }
}
