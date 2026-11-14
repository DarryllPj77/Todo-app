using System.Globalization;
using System.Text;

namespace TodoApp.Domain.Calendar;

// RFC 5545 data exchange only: not an invitation, subscription, or sync protocol.
public static class CalendarFile
{
    public static string Export(IEnumerable<TimeBlock> source)
    {
        var blocks = source.OrderBy(b => b.StartUtc).ThenBy(b => b.Id).ToArray();
        if (blocks.Length == 0) throw new ArgumentException("Save a time block before exporting.");
        if (blocks.Select(b => b.Scope).Distinct(StringComparer.Ordinal).Count() != 1 || blocks.Select(b => b.Id).Distinct().Count() != blocks.Length)
            throw new ArgumentException("Export one workspace with unique block IDs.");
        var output = new StringBuilder();
        void Line(string value)
        {
            var octets = 0;
            foreach (var rune in value.EnumerateRunes())
            {
                if (octets + rune.Utf8SequenceLength > 75) { output.Append("\r\n "); octets = 1; }
                output.Append(rune.ToString()); octets += rune.Utf8SequenceLength;
            }
            output.Append("\r\n");
        }
        Line("BEGIN:VCALENDAR"); Line("VERSION:2.0"); Line("PRODID:-//TodoApp//Time blocks//EN"); Line("CALSCALE:GREGORIAN");
        foreach (var block in blocks)
        {
            if (block.Id == Guid.Empty || block.Revision is < 1 or > int.MaxValue || block.StartUtc >= block.EndUtc)
                throw new ArgumentException("Refresh invalid calendar blocks before exporting.");
            Line("BEGIN:VEVENT"); Line($"UID:{block.Id:N}@todoapp.local");
            Line("DTSTAMP:" + Utc(block.UpdatedUtc)); Line("LAST-MODIFIED:" + Utc(block.UpdatedUtc));
            Line("SEQUENCE:" + (block.Revision - 1).ToString(CultureInfo.InvariantCulture));
            Line("DTSTART:" + Utc(block.StartUtc)); Line("DTEND:" + Utc(block.EndUtc));
            Line("SUMMARY:" + Escape(block.Title)); Line("CLASS:PRIVATE"); Line("TRANSP:OPAQUE"); Line("END:VEVENT");
        }
        Line("END:VCALENDAR"); return output.ToString();
    }

    private static string Utc(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n")
        .Replace("\n", "\\n").Replace(";", "\\;").Replace(",", "\\,");
}
