using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ouroboros.Core
{
    /// <summary>One line of a death recap: who or what hurt you, with what, how much and how many times.</summary>
    public struct DamageEntry
    {
        public string Source;
        public string Means;
        public float Total;
        public int Hits;
        /// <summary>Counts up with every hit, so the most recent source can be told from the largest.</summary>
        public int LastOrder;
    }

    /// <summary>What a player is shown when they die: the final blow and everything that led to it.</summary>
    public sealed class DeathReport
    {
        public string Killer = "";
        public string KillerMeans = "";
        public readonly List<DamageEntry> Entries = new List<DamageEntry>();
        public float Total;
    }

    /// <summary>
    /// Damage taken during one life, grouped by source (v0.14). The host keeps one per player and sends it to
    /// the victim when they die. Pure: no Unity or Fusion types, unit-tested in Tools/tests.
    ///
    /// It holds at most <see cref="MaxEntries"/> sources; past that, the smallest is folded into "Other", so the
    /// encoded report always fits in one RPC.
    /// </summary>
    public sealed class DamageLog
    {
        public const int MaxEntries = 6;
        public const int MaxLabelLength = 24;
        public const string OtherSource = "Other";
        private const char FieldSeparator = '|';
        private const char LineSeparator = '\n';

        private readonly List<DamageEntry> entries = new List<DamageEntry>(MaxEntries + 1);
        private int order;

        public int Count => entries.Count;
        public float Total { get; private set; }
        public IReadOnlyList<DamageEntry> Entries => entries;

        public void Clear()
        {
            entries.Clear();
            Total = 0f;
            order = 0;
        }

        public void Add(string source, string means, float amount)
        {
            if (amount <= 0f) return;
            source = Clean(source, "Unknown");
            means = Clean(means, "");
            Total += amount;
            order++;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Source != source || entries[i].Means != means) continue;
                var e = entries[i];
                e.Total += amount; e.Hits++; e.LastOrder = order;
                entries[i] = e;
                return;
            }

            entries.Add(new DamageEntry { Source = source, Means = means, Total = amount, Hits = 1, LastOrder = order });
            if (entries.Count > MaxEntries) FoldSmallest();
        }

        /// <summary>Merges the two smallest sources into one "Other" line.</summary>
        private void FoldSmallest()
        {
            entries.Sort((a, b) => b.Total.CompareTo(a.Total));
            var last = entries[entries.Count - 1];
            var prev = entries[entries.Count - 2];
            entries.RemoveRange(entries.Count - 2, 2);
            entries.Add(new DamageEntry
            {
                Source = OtherSource, Means = "",
                Total = last.Total + prev.Total, Hits = last.Hits + prev.Hits,
                LastOrder = Math.Max(last.LastOrder, prev.LastOrder)
            });
        }

        /// <summary>Entries from the most damage to the least.</summary>
        public List<DamageEntry> Sorted()
        {
            var sorted = new List<DamageEntry>(entries);
            sorted.Sort((a, b) => b.Total.CompareTo(a.Total));
            return sorted;
        }

        // ------------------------------------------------------------------
        // Wire format: "killer|means" on the first line, then "source|means|total|hits" per entry.

        public string Encode(string killer, string killerMeans)
        {
            var sb = new StringBuilder(160);
            sb.Append(Clean(killer, "Unknown")).Append(FieldSeparator).Append(Clean(killerMeans, ""));
            foreach (var e in Sorted())
            {
                sb.Append(LineSeparator).Append(e.Source).Append(FieldSeparator).Append(e.Means).Append(FieldSeparator)
                  .Append(((int)Math.Round(e.Total)).ToString(CultureInfo.InvariantCulture)).Append(FieldSeparator)
                  .Append(e.Hits.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static bool TryDecode(string encoded, out DeathReport report)
        {
            report = new DeathReport();
            if (string.IsNullOrEmpty(encoded)) return false;
            string[] lines = encoded.Split(LineSeparator);
            string[] head = lines[0].Split(FieldSeparator);
            report.Killer = head.Length > 0 ? head[0] : "";
            report.KillerMeans = head.Length > 1 ? head[1] : "";

            for (int i = 1; i < lines.Length && report.Entries.Count < MaxEntries; i++)
            {
                string[] f = lines[i].Split(FieldSeparator);
                if (f.Length < 4) continue;
                if (!int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int total)) continue;
                int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hits);
                report.Entries.Add(new DamageEntry { Source = f[0], Means = f[1], Total = Math.Max(0, total), Hits = Math.Max(1, hits) });
                report.Total += Math.Max(0, total);
            }
            return report.Killer.Length > 0;
        }

        /// <summary>Labels travel in a separator-delimited string, so separators and control characters are removed.</summary>
        public static string Clean(string label, string fallback)
        {
            if (string.IsNullOrEmpty(label)) return fallback;
            var sb = new StringBuilder(Math.Min(label.Length, MaxLabelLength));
            foreach (char c in label)
            {
                if (sb.Length >= MaxLabelLength) break;
                if (c == FieldSeparator || c == LineSeparator || char.IsControl(c)) continue;
                sb.Append(c);
            }
            string cleaned = sb.ToString().Trim();
            return cleaned.Length == 0 ? fallback : cleaned;
        }
    }
}
