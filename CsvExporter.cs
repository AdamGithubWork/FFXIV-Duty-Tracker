using MentorRouletteTracker.DutyTracking;
using System.Globalization;
using System.Text;

namespace MentorRouletteTracker
{
    public static class CsvExporter
    {
        public static void Export(IEnumerable<DutyRecord> records, string filePath)
        {
            ExportSnapshot(Capture(records), filePath);
        }

        internal sealed record Row(DateTime Timestamp, string DutyName, string DutyType,
            bool JoinedInProgress, int WipeCount, int LalafellCount, string PlayerJob,
            TimeSpan TimeInDuty, bool Completed, (string Name, string Job, string Race)[] Party);

        internal static Row[] Capture(IEnumerable<DutyRecord> records) => records.Select(record => new Row(
            record.Timestamp, record.DutyName, record.DutyType, record.JoinedInProgress,
            record.WipeCount, record.LalafellCount, record.PlayerJob, record.TimeInDuty,
            record.Completed, record.Party.Select(player => (player.Name, player.Job, player.Race)).ToArray())).ToArray();

        internal static void ExportSnapshot(IEnumerable<Row> records, string filePath)
        {
            var destination = Path.GetFullPath(filePath);
            var temporaryPath = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.WriteLine("Timestamp,DutyName,DutyType,JoinedInProgress,WipeCount,LalafellCount,PlayerJob,TimeInDuty,Completed,Party");
                    foreach (var record in records)
                    {
                        var partySummary = string.Join(" | ", record.Party.Select(player => $"{player.Name} ({player.Job}, {player.Race})"));
                        writer.WriteLine(string.Join(",",
                            record.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                            Escape(record.DutyName),
                            Escape(record.DutyType),
                            record.JoinedInProgress,
                            record.WipeCount.ToString(CultureInfo.InvariantCulture),
                            record.LalafellCount.ToString(CultureInfo.InvariantCulture),
                            Escape(record.PlayerJob),
                            Escape(record.TimeInDuty.ToString("c", CultureInfo.InvariantCulture)),
                            record.Completed,
                            Escape(partySummary)));
                    }
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, destination, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string Escape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
