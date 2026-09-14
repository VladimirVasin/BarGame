using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Machine-readable output of the city audit probe: NDJSON findings,
    /// CSV registries, districts.json, run.json and the Markdown summary.
    /// Every number is written with the invariant culture (the host is
    /// ru-RU) and the findings stream is flushed after each check.
    /// </summary>
    public sealed partial class CityAuditProbeTests
    {
        internal sealed class Finding
        {
            public string Id = string.Empty;
            public string Category = "A";
            public string Subtype = string.Empty;
            public string Tag = "new";
            public double X;
            public double Y;
            public double Z;
            public bool HasY;
            public string Dir;
            public string Area = string.Empty;
            public string Cell = string.Empty;
            public string Surface = string.Empty;
            public string Note = string.Empty;
            public readonly List<string> Objects = new List<string>();
            public readonly List<KeyValuePair<string, double>> Metrics =
                new List<KeyValuePair<string, double>>();

            public Finding Metric(string name, double value)
            {
                Metrics.Add(new KeyValuePair<string, double>(name, value));
                return this;
            }

            public Finding Object(string path)
            {
                if (!string.IsNullOrEmpty(path) && !Objects.Contains(path))
                {
                    Objects.Add(path);
                }

                return this;
            }

            public Finding At(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
                HasY = !double.IsNaN(y) && !double.IsInfinity(y);
                return this;
            }

            public Finding AtXZ(double x, double z)
            {
                X = x;
                Z = z;
                HasY = false;
                return this;
            }

            public double PrimaryMetric =>
                Metrics.Count > 0 ? Metrics[0].Value : 0.0;

            public string PrimaryMetricName =>
                Metrics.Count > 0 ? Metrics[0].Key : string.Empty;
        }

        internal static Finding NewFinding(string category, string subtype, string tag)
        {
            return new Finding
            {
                Category = category,
                Subtype = subtype,
                Tag = tag
            };
        }

        internal static string WorstTag(string first, string second)
        {
            return TagRank(first) >= TagRank(second) ? first : second;
        }

        internal static int TagRank(string tag)
        {
            switch (tag)
            {
                case "new":
                    return 3;
                case "suspect":
                    return 2;
                default:
                    return 1;
            }
        }

        internal static class Json
        {
            public static string Str(string value)
            {
                if (value == null)
                {
                    return "null";
                }

                var builder = new StringBuilder(value.Length + 2);
                builder.Append('"');
                for (int index = 0; index < value.Length; index++)
                {
                    char c = value[index];
                    switch (c)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (c < 0x20)
                            {
                                builder.Append("\\u");
                                builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(c);
                            }

                            break;
                    }
                }

                builder.Append('"');
                return builder.ToString();
            }

            public static string Num(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    return "null";
                }

                return value.ToString("0.######", CultureInfo.InvariantCulture);
            }

            public static string Int(long value)
            {
                return value.ToString(CultureInfo.InvariantCulture);
            }

            public static string Bool(bool value)
            {
                return value ? "true" : "false";
            }
        }

        internal static string F(double value)
        {
            return Json.Num(value);
        }

        internal static string Csv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 ||
                value.IndexOf('\n') >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        internal sealed class AuditOutput
        {
            private readonly List<Finding> pending = new List<Finding>();
            private readonly List<Finding> all = new List<Finding>();
            private readonly Dictionary<string, int> idCounters =
                new Dictionary<string, int>(StringComparer.Ordinal);

            private StreamWriter findings;

            public AuditOutput(string directory)
            {
                Directory = directory;
                System.IO.Directory.CreateDirectory(directory);
                findings = new StreamWriter(
                    Path.Combine(directory, "findings.ndjson"),
                    false,
                    new UTF8Encoding(false));
                Started = DateTime.Now;
            }

            public string Directory { get; }
            public DateTime Started { get; }
            public int Seed;
            public string Label = string.Empty;
            public readonly List<string> ChecksRan = new List<string>();
            public readonly List<KeyValuePair<string, double>> Timings =
                new List<KeyValuePair<string, double>>();
            public readonly List<KeyValuePair<string, double>> Thresholds =
                new List<KeyValuePair<string, double>>();
            public readonly Dictionary<string, long> Counters =
                new Dictionary<string, long>(StringComparer.Ordinal);
            public readonly List<string> Notes = new List<string>();

            public IReadOnlyList<Finding> All => all;
            public int PendingCount => pending.Count;

            public List<Finding> PendingSnapshot()
            {
                return new List<Finding>(pending);
            }

            public void Add(Finding finding)
            {
                pending.Add(finding);
            }

            public void Count(string name, long amount = 1)
            {
                Counters.TryGetValue(name, out long current);
                Counters[name] = current + amount;
            }

            public void Timing(string check, double milliseconds)
            {
                Timings.Add(new KeyValuePair<string, double>(check, milliseconds));
            }

            public void Threshold(string name, double value)
            {
                Thresholds.Add(new KeyValuePair<string, double>(name, value));
            }

            public bool Ran(string check)
            {
                return ChecksRan.Contains(check);
            }

            /// <summary>
            /// Sorts the findings gathered since the previous flush by
            /// (category, subtype, x, z), assigns ids, appends them to the
            /// NDJSON stream and rewrites run.json and summary.md.
            /// </summary>
            public int FlushCheck(string check)
            {
                pending.Sort(CompareFindings);
                for (int index = 0; index < pending.Count; index++)
                {
                    Finding finding = pending[index];
                    idCounters.TryGetValue(finding.Category, out int counter);
                    counter++;
                    idCounters[finding.Category] = counter;
                    finding.Id = finding.Category + "-" +
                                 counter.ToString("000000", CultureInfo.InvariantCulture);
                    findings.WriteLine(FindingLine(finding));
                    all.Add(finding);
                }

                int flushed = pending.Count;
                pending.Clear();
                findings.Flush();
                if (!ChecksRan.Contains(check))
                {
                    ChecksRan.Add(check);
                }

                WriteRunJson();
                WriteSummary();
                return flushed;
            }

            private static int CompareFindings(Finding left, Finding right)
            {
                int result = string.CompareOrdinal(left.Category, right.Category);
                if (result != 0)
                {
                    return result;
                }

                result = string.CompareOrdinal(left.Subtype, right.Subtype);
                if (result != 0)
                {
                    return result;
                }

                result = left.X.CompareTo(right.X);
                if (result != 0)
                {
                    return result;
                }

                return left.Z.CompareTo(right.Z);
            }

            private static string FindingLine(Finding finding)
            {
                var builder = new StringBuilder(256);
                builder.Append('{');
                builder.Append("\"id\":").Append(Json.Str(finding.Id));
                builder.Append(",\"category\":").Append(Json.Str(finding.Category));
                builder.Append(",\"subtype\":").Append(Json.Str(finding.Subtype));
                builder.Append(",\"tag\":").Append(Json.Str(finding.Tag));
                builder.Append(",\"x\":").Append(Json.Num(finding.X));
                if (finding.HasY)
                {
                    builder.Append(",\"y\":").Append(Json.Num(finding.Y));
                }

                builder.Append(",\"z\":").Append(Json.Num(finding.Z));
                if (!string.IsNullOrEmpty(finding.Dir))
                {
                    builder.Append(",\"dir\":").Append(Json.Str(finding.Dir));
                }

                builder.Append(",\"area\":").Append(Json.Str(finding.Area ?? string.Empty));
                builder.Append(",\"cell\":").Append(Json.Str(finding.Cell ?? string.Empty));
                builder.Append(",\"surface\":").Append(Json.Str(finding.Surface ?? string.Empty));
                builder.Append(",\"objects\":[");
                for (int index = 0; index < finding.Objects.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(Json.Str(finding.Objects[index]));
                }

                builder.Append("],\"metrics\":{");
                for (int index = 0; index < finding.Metrics.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(Json.Str(finding.Metrics[index].Key));
                    builder.Append(':');
                    builder.Append(Json.Num(finding.Metrics[index].Value));
                }

                builder.Append("},\"note\":").Append(Json.Str(finding.Note ?? string.Empty));
                builder.Append('}');
                return builder.ToString();
            }

            public void WriteRenderers(RendererRegistry registry)
            {
                using (var writer = new StreamWriter(
                           Path.Combine(Directory, "renderers.csv"),
                           false,
                           new UTF8Encoding(false)))
                {
                    writer.WriteLine(
                        "index,path,class,readable,vertices,triangles,components,submesh,shader,renderQueue,writesDepth,cull,xMin,xMax,yMin,yMax,zMin,zMax");
                    foreach (RendererRecord record in registry.Records)
                    {
                        int submeshCount = record.ShaderNames.Length;
                        for (int submesh = 0; submesh < submeshCount; submesh++)
                        {
                            writer.WriteLine(string.Join(",", new[]
                            {
                                Json.Int(record.Index),
                                Csv(record.Path),
                                record.Class.ToString(),
                                Json.Bool(record.Data.Readable),
                                Json.Int(record.Data.Vertices.Length),
                                Json.Int(record.TriangleCount),
                                Json.Int(record.ComponentCount),
                                Json.Int(submesh),
                                Csv(record.ShaderNames[submesh]),
                                Json.Int(record.RenderQueues[submesh]),
                                Json.Bool(record.WritesDepth[submesh]),
                                F(record.Cull[submesh]),
                                F(record.Bounds.min.x),
                                F(record.Bounds.max.x),
                                F(record.Bounds.min.y),
                                F(record.Bounds.max.y),
                                F(record.Bounds.min.z),
                                F(record.Bounds.max.z)
                            }));
                        }
                    }
                }
            }

            public void WriteColliders(ColliderRegistry registry)
            {
                using (var writer = new StreamWriter(
                           Path.Combine(Directory, "colliders.csv"),
                           false,
                           new UTF8Encoding(false)))
                {
                    writer.WriteLine(
                        "index,path,type,isTrigger,layer,class,knownInvisible,visible,ownRenderer,proxy,xMin,xMax,yMin,yMax,zMin,zMax");
                    foreach (ColliderRecord record in registry.Records)
                    {
                        writer.WriteLine(string.Join(",", new[]
                        {
                            Json.Int(record.Index),
                            Csv(record.Path),
                            record.Type,
                            Json.Bool(record.IsTrigger),
                            Json.Int(record.Layer),
                            record.Class.ToString(),
                            Json.Bool(record.KnownInvisible),
                            Json.Bool(record.Visible),
                            Json.Bool(record.HasOwnRenderer),
                            Json.Bool(record.IsProxy),
                            F(record.Bounds.min.x),
                            F(record.Bounds.max.x),
                            F(record.Bounds.min.y),
                            F(record.Bounds.max.y),
                            F(record.Bounds.min.z),
                            F(record.Bounds.max.z)
                        }));
                    }
                }
            }

            public void WriteDistricts(IReadOnlyList<CityMapAreaRegion> regions)
            {
                var builder = new StringBuilder();
                builder.Append('[');
                bool first = true;
                if (regions != null)
                {
                    foreach (CityMapAreaRegion region in regions)
                    {
                        var box = new BoxXZ();
                        foreach (Rect rect in region.LandBounds)
                        {
                            box.Add(rect.xMin, rect.yMin);
                            box.Add(rect.xMax, rect.yMax);
                        }

                        if (!box.Any)
                        {
                            foreach (Rect rect in region.WaterBounds)
                            {
                                box.Add(rect.xMin, rect.yMin);
                                box.Add(rect.xMax, rect.yMax);
                            }
                        }

                        if (!first)
                        {
                            builder.Append(',');
                        }

                        first = false;
                        builder.Append("\n{\"areaId\":").Append(Json.Str(region.AreaId));
                        builder.Append(",\"archetype\":").Append(Json.Str(region.Archetype.ToString()));
                        builder.Append(",\"feature\":").Append(Json.Str(region.Feature.ToString()));
                        builder.Append(",\"xMin\":").Append(Json.Num(box.Any ? box.XMin : double.NaN));
                        builder.Append(",\"xMax\":").Append(Json.Num(box.Any ? box.XMax : double.NaN));
                        builder.Append(",\"zMin\":").Append(Json.Num(box.Any ? box.ZMin : double.NaN));
                        builder.Append(",\"zMax\":").Append(Json.Num(box.Any ? box.ZMax : double.NaN));
                        builder.Append('}');
                    }
                }

                builder.Append("\n]\n");
                File.WriteAllText(
                    Path.Combine(Directory, "districts.json"),
                    builder.ToString(),
                    new UTF8Encoding(false));
            }

            public void WriteRunJson()
            {
                var builder = new StringBuilder();
                builder.Append("{\n");
                builder.Append("  \"label\": ").Append(Json.Str(Label)).Append(",\n");
                builder.Append("  \"seed\": ").Append(Json.Int(Seed)).Append(",\n");
                builder.Append("  \"started\": ").Append(Json.Str(Started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(",\n");
                builder.Append("  \"checksRan\": [");
                for (int index = 0; index < ChecksRan.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(Json.Str(ChecksRan[index]));
                }

                builder.Append("],\n");
                builder.Append("  \"thresholds\": {");
                for (int index = 0; index < Thresholds.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(Json.Str(Thresholds[index].Key)).Append(": ").Append(Json.Num(Thresholds[index].Value));
                }

                builder.Append("},\n");
                builder.Append("  \"timingsMs\": {");
                for (int index = 0; index < Timings.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(Json.Str(Timings[index].Key)).Append(": ").Append(Json.Num(Timings[index].Value));
                }

                builder.Append("},\n");
                builder.Append("  \"counters\": {");
                bool first = true;
                var counterNames = new List<string>(Counters.Keys);
                counterNames.Sort(StringComparer.Ordinal);
                foreach (string name in counterNames)
                {
                    if (!first)
                    {
                        builder.Append(", ");
                    }

                    first = false;
                    builder.Append(Json.Str(name)).Append(": ").Append(Json.Int(Counters[name]));
                }

                builder.Append("},\n");
                builder.Append("  \"findings\": {");
                first = true;
                foreach (KeyValuePair<string, int> pair in CountBy(f => f.Category))
                {
                    if (!first)
                    {
                        builder.Append(", ");
                    }

                    first = false;
                    builder.Append(Json.Str(pair.Key)).Append(": ").Append(Json.Int(pair.Value));
                }

                builder.Append("},\n");
                builder.Append("  \"findingsBySubtype\": {");
                first = true;
                foreach (KeyValuePair<string, int> pair in CountBy(f => f.Category + "/" + f.Subtype))
                {
                    if (!first)
                    {
                        builder.Append(", ");
                    }

                    first = false;
                    builder.Append(Json.Str(pair.Key)).Append(": ").Append(Json.Int(pair.Value));
                }

                builder.Append("},\n");
                builder.Append("  \"notes\": [");
                for (int index = 0; index < Notes.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(Json.Str(Notes[index]));
                }

                builder.Append("]\n}\n");
                File.WriteAllText(
                    Path.Combine(Directory, "run.json"),
                    builder.ToString(),
                    new UTF8Encoding(false));
            }

            private List<KeyValuePair<string, int>> CountBy(Func<Finding, string> key)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (Finding finding in all)
                {
                    string k = key(finding);
                    counts.TryGetValue(k, out int current);
                    counts[k] = current + 1;
                }

                var result = new List<KeyValuePair<string, int>>(counts);
                result.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
                return result;
            }

            public string CountsLine()
            {
                var builder = new StringBuilder();
                foreach (KeyValuePair<string, int> pair in CountBy(f => f.Category + "/" + f.Subtype))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(pair.Key).Append('=').Append(Json.Int(pair.Value));
                }

                return builder.Length > 0 ? builder.ToString() : "no findings";
            }

            public string SummaryText()
            {
                var builder = new StringBuilder();
                builder.Append("# City audit summary\n\n");
                builder.Append("- label: ").Append(Label).Append('\n');
                builder.Append("- seed: ").Append(Json.Int(Seed)).Append('\n');
                builder.Append("- directory: ").Append(Directory).Append('\n');
                builder.Append("- started: ").Append(Started.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
                builder.Append("- checks: ").Append(string.Join(", ", ChecksRan)).Append('\n');
                builder.Append("- findings: ").Append(Json.Int(all.Count)).Append("\n\n");

                builder.Append("## Timings\n\n| check | ms |\n|---|---:|\n");
                foreach (KeyValuePair<string, double> timing in Timings)
                {
                    builder.Append("| ").Append(timing.Key).Append(" | ").Append(timing.Value.ToString("0", CultureInfo.InvariantCulture)).Append(" |\n");
                }

                builder.Append("\n## Counts by category / subtype / tag\n\n| category | subtype | new | suspect | by_design | total |\n|---|---|---:|---:|---:|---:|\n");
                var groups = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
                foreach (Finding finding in all)
                {
                    string key = finding.Category + "\t" + finding.Subtype;
                    if (!groups.TryGetValue(key, out int[] counts))
                    {
                        counts = new int[4];
                        groups.Add(key, counts);
                    }

                    switch (finding.Tag)
                    {
                        case "new":
                            counts[0]++;
                            break;
                        case "suspect":
                            counts[1]++;
                            break;
                        default:
                            counts[2]++;
                            break;
                    }

                    counts[3]++;
                }

                foreach (KeyValuePair<string, int[]> group in groups)
                {
                    string[] parts = group.Key.Split('\t');
                    builder.Append("| ").Append(parts[0]).Append(" | ").Append(parts[1]);
                    for (int index = 0; index < 4; index++)
                    {
                        builder.Append(" | ").Append(Json.Int(group.Value[index]));
                    }

                    builder.Append(" |\n");
                }

                builder.Append("\n## Counters\n\n| counter | value |\n|---|---:|\n");
                var counterNames = new List<string>(Counters.Keys);
                counterNames.Sort(StringComparer.Ordinal);
                foreach (string name in counterNames)
                {
                    builder.Append("| ").Append(name).Append(" | ").Append(Json.Int(Counters[name])).Append(" |\n");
                }

                builder.Append("\n## Top 20 per subtype (by primary metric)\n");
                var bySubtype = new SortedDictionary<string, List<Finding>>(StringComparer.Ordinal);
                foreach (Finding finding in all)
                {
                    string key = finding.Category + "/" + finding.Subtype;
                    if (!bySubtype.TryGetValue(key, out List<Finding> list))
                    {
                        list = new List<Finding>();
                        bySubtype.Add(key, list);
                    }

                    list.Add(finding);
                }

                foreach (KeyValuePair<string, List<Finding>> group in bySubtype)
                {
                    List<Finding> list = group.Value;
                    list.Sort((left, right) =>
                    {
                        int result = right.PrimaryMetric.CompareTo(left.PrimaryMetric);
                        return result != 0
                            ? result
                            : string.CompareOrdinal(left.Id, right.Id);
                    });
                    string metricName = list.Count > 0 ? list[0].PrimaryMetricName : "metric";
                    builder.Append("\n### ").Append(group.Key).Append(" (").Append(Json.Int(list.Count)).Append(")\n\n");
                    builder.Append("| id | tag | ").Append(string.IsNullOrEmpty(metricName) ? "metric" : metricName).Append(" | x | z | area | objects | note |\n|---|---|---:|---:|---:|---|---|---|\n");
                    int limit = Math.Min(20, list.Count);
                    for (int index = 0; index < limit; index++)
                    {
                        Finding finding = list[index];
                        builder.Append("| ").Append(finding.Id)
                            .Append(" | ").Append(finding.Tag)
                            .Append(" | ").Append(Json.Num(finding.PrimaryMetric))
                            .Append(" | ").Append(finding.X.ToString("0.00", CultureInfo.InvariantCulture))
                            .Append(" | ").Append(finding.Z.ToString("0.00", CultureInfo.InvariantCulture))
                            .Append(" | ").Append(finding.Area)
                            .Append(" | ").Append(string.Join("; ", finding.Objects).Replace("|", "/"))
                            .Append(" | ").Append((finding.Note ?? string.Empty).Replace("|", "/").Replace("\n", " "))
                            .Append(" |\n");
                    }
                }

                if (Notes.Count > 0)
                {
                    builder.Append("\n## Notes\n\n");
                    foreach (string note in Notes)
                    {
                        builder.Append("- ").Append(note).Append('\n');
                    }
                }

                return builder.ToString();
            }

            public void WriteSummary()
            {
                File.WriteAllText(
                    Path.Combine(Directory, "summary.md"),
                    SummaryText(),
                    new UTF8Encoding(false));
            }

            public void Close()
            {
                if (findings != null)
                {
                    findings.Flush();
                    findings.Dispose();
                    findings = null;
                }
            }
        }
    }
}
