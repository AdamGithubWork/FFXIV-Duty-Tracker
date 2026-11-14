using MentorRouletteTracker.DutyTracking;
using System.Globalization;
using System.Text;

namespace MentorRouletteTracker
{
    public static class CsvExporter
    {
        public static void Export(IEnumerable<DutyRecord> records, string filePath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Timestamp,DutyName,DutyType,JoinedInProgress,WipeCount,LalafellCount,PlayerJob,TimeInDuty,Completed,Party");

            foreach (var record in records)
            {
                var partySummary = string.Join(" | ", record.Party.ConvertAll(player => $"{player.Name} ({player.Job}, {player.Race})"));

                csv.AppendLine(string.Join(",",
                    record.Timestamp.ToString("s", CultureInfo.InvariantCulture),
                    Escape(record.DutyName),
                    Escape(record.DutyType),
                    record.JoinedInProgress,
                    record.WipeCount,
                    record.LalafellCount,
                    Escape(record.PlayerJob),
                    Escape(record.TimeInDuty.ToString(@"hh\:mm\:ss")),
                    record.Completed,
                    Escape(partySummary)));
            }

            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }

        private static string Escape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
