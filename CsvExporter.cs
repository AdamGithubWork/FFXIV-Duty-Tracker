using System.Globalization;
using System.Text;

namespace MentorRouletteTracker
{
    public static class CsvExporter
    {
        public static void Export(IEnumerable<DutyRecord> records, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,DutyName,DutyType,JoinedInProgress,WipeCount,LalafellCount,PlayerJob,TimeInDuty,Completed,Party");

            foreach (var r in records)
            {
                var partySummary = string.Join(" | ", r.Party.ConvertAll(m => $"{m.Name} ({m.Job}, {m.Race})"));

                sb.AppendLine(string.Join(",",
                    r.Timestamp.ToString("s", CultureInfo.InvariantCulture),
                    Escape(r.DutyName),
                    Escape(r.DutyType),
                    r.JoinedInProgress,
                    r.WipeCount,
                    r.LalafellCount,
                    Escape(r.PlayerJob),
                    Escape(r.TimeInDuty.ToString(@"hh\:mm\:ss")),
                    r.Completed,
                    Escape(partySummary)));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
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