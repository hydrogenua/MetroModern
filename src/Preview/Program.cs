using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MetroCore;

namespace MetroModern.Preview
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            // usage: metro-preview <city.json> [out.svg] [from|to] [--edges <station>]
            if (args.Length < 1)
            {
                Console.Error.WriteLine("usage: metro-preview <city.json> [out.svg] [from|to] [--edges <station>]");
                return 1;
            }

            var path = args[0];
            var json = File.ReadAllText(path);
            var env = SchemeLoader.Parse(json);

            Console.WriteLine($"scheme id={env.Data.Meta?.Id}, name={env.Data.Meta?.Name?["ru"] ?? "-"}, version={env.Data.Meta?.Version}");
            var net = SchemeLoader.BuildNetwork(env);

            Console.WriteLine($"stations={net.Stations.Count}, nodes={net.NodesById.Count}, edges={net.Edges.Count}, lines={net.LinesById.Count}");
            foreach (var l in net.LinesById.Values.OrderBy(x => x.ShortName))
                Console.WriteLine($"  line {l.Id} {l.ShortName,-6} {l.Name,-45} {l.Color}");

            // route test: from|to in form "Девяткино|Купчино"
            string fromName = "Девяткино", toName = "Купчино";
            if (args.Length >= 3 && args[2].Contains("|")) { var p = args[2].Split('|'); fromName = p[0]; toName = p[1]; }

            RouteFindTest(net, fromName, toName);
            RouteFindTest(net, "Терехово", "Ольховая");

            // --edges <station>: dump all outgoing edges of a station (graph QA)
            // --station <name>: station coordinates + its label elements (anchor QA)
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--edges") PrintEdges(net, args[i + 1]);
                if (args[i] == "--station") PrintStationInfo(env, args[i + 1]);
                if (args[i] == "--group") PrintGroup(env, args[i + 1]);
            }

            if (args.Length >= 2 && args[1].Length > 0 && env.Data._raw.ValueKind != System.Text.Json.JsonValueKind.Undefined)
            {
                var g = env.Data._raw;
                var svg = SvgSchemeLoader.FromRaw(g, env.Data.Meta.Int?.Width ?? 1800, env.Data.Meta.Int?.Height ?? 1800);
                var sb = new StringBuilder();
                // --crop "x y w h" exports a region (label/QA zoom-ins)
                string viewBox = null;
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "--crop") viewBox = args[i + 1];
                SvgExporter.Export(svg, sb, viewBox);
                File.WriteAllText(args[1], sb.ToString());
                Console.WriteLine($"svg written: {args[1]} ({sb.Length} bytes, groups={svg.Groups.Count}, viewBox={viewBox ?? "full"})");
            }

            return 0;
        }

        private static void PrintEdges(MetroNetwork net, string stationName)
        {
            string norm = Normalize(stationName);
            var matches = net.Stations.Where(s => Normalize(s.Name).Contains(norm)).ToList();
            if (matches.Count == 0) { Console.WriteLine($"EDGES: station '{stationName}' not found"); return; }
            foreach (var st in matches)
            {
                Console.WriteLine($"\nEDGES of {st.Name} id={st.Id} ({st.Nodes.Count} platform nodes):");
                foreach (var n in st.Nodes)
                {
                    Console.WriteLine($"  node {n.Id} (line {n.LineId}):");
                    List<Edge> edges;
                    if (!net.Outgoing.TryGetValue(n.Id, out edges)) { Console.WriteLine("    (none)"); continue; }
                    foreach (var e in edges.OrderBy(x => x.To.Station.Name))
                    {
                        LineInfo li = e.PrimaryLineId != null && net.LinesById.TryGetValue(e.PrimaryLineId, out var l) ? l : null;
                        Console.WriteLine(
                            $"    -> {e.To.Station.Name} [{li?.ShortName ?? (e.IsTransfer ? "transfer" : "?")}] {e.Time}s");
                    }
                }
            }
        }

        private static void PrintStationInfo(SchemeEnvelope env, string name)
        {
            var net = SchemeLoader.BuildNetwork(env);
            string norm = Normalize(name);
            foreach (var st in net.Stations.Where(s => Normalize(s.Name).Contains(norm)))
                Console.WriteLine($"STATION {st.Name}: x={st.X} y={st.Y} nodes={st.Nodes.Count}");

            // label elements from graphics
            var g = env.Data._raw;
            if (g.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var grp in g.EnumerateObject())
                {
                    if (grp.Value.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                    foreach (var el in grp.Value.EnumerateArray())
                    {
                        if (!el.TryGetProperty("type", out var t) || t.GetString() != "Text") continue;
                        if (!el.TryGetProperty("text", out var tx)) continue;
                        string txt = tx.GetString();
                        if (txt == null || Normalize(txt) != norm) continue;
                        double x = el.TryGetProperty("x", out var xv) && xv.TryGetDouble(out var xd) ? xd : 0;
                        double y = el.TryGetProperty("y", out var yv) && yv.TryGetDouble(out var yd) ? yd : 0;
                        string anchor = el.TryGetProperty("textAnchor", out var av) ? av.GetString() : "?";
                        double w = el.TryGetProperty("width", out var wv) && wv.TryGetDouble(out var wd) ? wd : 0;
                        double fs = el.TryGetProperty("fontSize", out var fv) && fv.TryGetDouble(out var fd) ? fd : 0;
                        Console.WriteLine($"  LABEL '{txt}' x={x} y={y} anchor={anchor} w={w} fontSize={fs} id={ (el.TryGetProperty("id", out var iv) ? iv.GetString() : "?")}");
                    }
                }
            }
        }

        private static void PrintGroup(SchemeEnvelope env, string groupId)
        {
            var g = env.Data._raw;
            if (g.ValueKind != System.Text.Json.JsonValueKind.Object) { Console.WriteLine("no graphics"); return; }
            foreach (var grp in g.EnumerateObject())
            {
                if (grp.Name != groupId) continue;
                Console.WriteLine($"GROUP {groupId}:");
                Console.WriteLine(grp.Value.GetRawText());
            }
        }

        private static void RouteFindTest(MetroNetwork net, string from, string to)
        {
            Station f = MatchStation(net, from), t = MatchStation(net, to);
            if (f == null || t == null) { Console.WriteLine($"ROUTE skip: '{from}' -> '{to}' (match miss: {(f == null ? "?" : "F")}, {(t == null ? "?" : "T")})"); return; }

            var router = new Router();
            var r = router.Find(net, f, t);
            if (r == null) { Console.WriteLine($"ROUTE {f.Name} -> {t.Name}: not found"); return; }

            Console.WriteLine($"\nROUTE {f.Name} -> {t.Name}: {TimeSpan.FromSeconds(r.TotalSeconds)} (steps={r.Steps.Count})");
            foreach (var leg in r.legs())
            {
                if (leg.IsTransfer) { Console.WriteLine($"   [transfer {TimeSpan.FromSeconds(leg.Seconds)}]"); }
                else if (leg.LineId == null) { Console.WriteLine("   [leg без lineId]"); }
                else
                {
                    net.LinesById.TryGetValue(leg.LineId, out var li);
                    leg.Nodes = leg.Nodes ?? null;
                    string seq = string.Join(" -> ", leg.Nodes.Select(n => n.Station.Name));
                    Console.WriteLine($"   [{li?.ShortName ?? leg.LineId} {li?.Color}] {leg.Nodes.Count} stops: {seq}");
                }
            }
            Console.WriteLine($"   total: {(int)(r.TotalSeconds / 60)} min");
        }

        private static Station MatchStation(MetroNetwork net, string name)
        {
            string norm = Normalize(name);
            var exact = net.Stations.FirstOrDefault(s => Normalize(s.Name) == norm);
            if (exact != null) return exact;
            return net.Stations.FirstOrDefault(s => Normalize(s.Name).Contains(norm));
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.ToLowerInvariant());
            for (int i = sb.Length - 1; i >= 0; i--)
            {
                char c = sb[i];
                switch (c)
                {
                    case 'ё': sb[i] = 'е'; break;
                    case '-': case ' ': case '.': case '"': case '«': case '»': sb.Remove(i, 1); break;
                }
            }
            return sb.ToString();
        }
    }
}
