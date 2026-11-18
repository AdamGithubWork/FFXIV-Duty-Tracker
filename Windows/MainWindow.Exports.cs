namespace MentorRouletteTracker
{
    public partial class MainWindow
    {
        private string lastExportMessage = string.Empty;
        private bool exportFailed;
        private Task<string?>? csvExportTask;
        private string csvExportPath = string.Empty;
        private int csvExportCount;

        private void StartCsvExport()
        {
            try
            {
                var snapshot = CsvExporter.Capture(config.History.Records);
                var path = Path.Combine(exportDirectory, $"noumenon_export_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv");
                csvExportPath = path;
                csvExportCount = snapshot.Length;
                exportFailed = false;
                lastExportMessage = $"Exporting {csvExportCount} records...";
                csvExportTask = Task.Run(() =>
                {
                    try
                    {
                        CsvExporter.ExportSnapshot(snapshot, path);
                        return (string?)null;
                    }
                    catch (Exception ex)
                    {
                        return ex.Message;
                    }
                });
            }
            catch (Exception ex)
            {
                exportFailed = true;
                lastExportMessage = $"Couldn't export CSV: {ex.Message}";
            }
        }

        private void UpdateCsvExport()
        {
            if (csvExportTask is not { IsCompleted: true } completed)
                return;
            csvExportTask = null;
            try
            {
                var error = completed.GetAwaiter().GetResult();
                exportFailed = error != null;
                lastExportMessage = error == null
                    ? $"Exported {csvExportCount} records to {csvExportPath}"
                    : $"Couldn't export CSV: {error}";
            }
            catch (Exception ex)
            {
                exportFailed = true;
                lastExportMessage = $"Couldn't export CSV: {ex.Message}";
            }
        }
    }
}
