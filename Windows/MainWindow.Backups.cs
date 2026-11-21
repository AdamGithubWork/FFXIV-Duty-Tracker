using System.Diagnostics;

namespace MentorRouletteTracker;

public partial class MainWindow
{
    private Task<(string? Path, string? Error)>? historyBackupTask;
    private string lastBackupMessage = string.Empty;
    private bool backupFailed;

    private void StartHistoryBackup()
    {
        if (historyBackupTask != null)
            return;
        try
        {
            historyBackupTask = ObserveHistoryBackup(config.History.CreateBackupAsync());
            backupFailed = false;
            lastBackupMessage = "Creating backup...";
        }
        catch (Exception ex)
        {
            backupFailed = true;
            lastBackupMessage = $"Couldn't create backup: {ex.Message}";
        }
    }

    private static async Task<(string? Path, string? Error)> ObserveHistoryBackup(Task<string> backup)
    {
        try { return (await backup.ConfigureAwait(false), null); }
        catch (Exception ex) { return (null, ex.Message); }
    }

    private void UpdateHistoryBackup()
    {
        if (historyBackupTask is not { IsCompleted: true } completed)
            return;
        historyBackupTask = null;
        var result = completed.GetAwaiter().GetResult();
        backupFailed = result.Error != null;
        lastBackupMessage = result.Error == null
            ? $"Backup saved to {result.Path}"
            : $"Couldn't create backup: {result.Error}";
    }

    private void OpenHistoryBackupsFolder()
    {
        try
        {
            var directory = config.History.BackupDirectory;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            backupFailed = true;
            lastBackupMessage = $"Couldn't open backups folder: {ex.Message}";
        }
    }
}
