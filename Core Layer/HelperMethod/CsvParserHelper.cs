using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Core_Layer.HelperMethod
{
    public static class CsvParseHelper
    {
        private static readonly string[] ReportFmts =
            { "yyyy MMM dd hh:mm:ss tt", "yyyy MMM  d hh:mm:ss tt",
              "yyyy MMM d hh:mm:ss tt" };

        private static readonly Regex RangeRx =
            new(@"(\d{1,2}/\d{1,2}/\d{4}\s+\d{1,2}:\d{2})\s*-\s*\d{1,2}:\d{2}",
                RegexOptions.Compiled);

        private static readonly string[] CrimeFmts =
            { "MM/dd/yyyy HH:mm", "M/d/yyyy HH:mm", "MM/dd/yyyy H:mm" };

        public static DateTime? ParseReportDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            foreach (var f in ReportFmts)
                if (DateTime.TryParseExact(raw, f, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt)) return dt;
            return DateTime.TryParse(raw, out var fb) ? fb : null;
        }

        public static DateTime? ParseCrimeDateTime(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            var m = RangeRx.Match(raw);
            if (m.Success) raw = m.Groups[1].Value;
            foreach (var f in CrimeFmts)
                if (DateTime.TryParseExact(raw, f, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt)) return dt;
            return DateTime.TryParse(raw, out var fb) ? fb : null;
        }

        public static decimal? ParseDecimal(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return decimal.TryParse(raw.Trim(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        public static string[] SplitLine(string line)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQ = false;
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') { if (inQ && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; } else inQ = !inQ; }
                else if (c == ',' && !inQ) { result.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            result.Add(cur.ToString());
            return result.ToArray();
        }
    }
}
