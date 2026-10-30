using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetroCore
{
    /* ---------- Raw API shapes (subset we consume) ---------- */

    public sealed class SchemeEnvelope
    {
        [JsonPropertyName("data")] public SchemeData Data { get; set; }
        [JsonPropertyName("error")] public ApiError Error { get; set; }
    }

    public sealed class ApiError
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
    }

    public sealed class SchemeData
    {
        [JsonPropertyName("schemeMeta")] public SchemeMeta Meta { get; set; }
        [JsonPropertyName("schemeData")] public SchemeBody Body { get; set; }
        // graphics/resources are loaded lazily from raw JSON to save memory
        [JsonIgnore] public JsonElement _raw;
    }

    public sealed class SchemeMeta
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("name")] public Dictionary<string, string> Name { get; set; }
        [JsonPropertyName("size")] public Vec2 Int { get; set; }
        [JsonPropertyName("aliases")] public string[] Aliases { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("countryCode")] public string CountryCode { get; set; }
        [JsonPropertyName("defaultAlias")] public string DefaultAlias { get; set; }
    }

    public sealed class Vec2
    {
        [JsonPropertyName("width")] public double Width { get; set; }
        [JsonPropertyName("height")] public double Height { get; set; }
    }

    public sealed class SchemeBody
    {
        [JsonPropertyName("stations")] public ApiStation[] Stations { get; set; }
        [JsonPropertyName("links")] public ApiLink[] Links { get; set; }
        [JsonPropertyName("transferHubs")] public ApiHub[] TransferHubs { get; set; }
        [JsonPropertyName("lines")] public ApiLine[] Lines { get; set; }
    }

    public sealed class ApiStation
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("nkId")] public string NkId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("stops")] public ApiStop[] Stops { get; set; }
        [JsonPropertyName("coordinates")] public double[] Coordinates { get; set; }
    }

    public sealed class ApiStop
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("nodeId")] public string NodeId { get; set; }
        [JsonPropertyName("lineId")] public string LineId { get; set; }
    }

    public sealed class ApiLink
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("fromNodeId")] public string FromNodeId { get; set; }
        [JsonPropertyName("toNodeId")] public string ToNodeId { get; set; }
        [JsonPropertyName("time")] public double Time { get; set; }
        [JsonPropertyName("lineIds")] public string[] LineIds { get; set; }
    }

    public sealed class ApiHub
    {
        [JsonPropertyName("id")] public string Id { get; set; }
    }

    public sealed class ApiLine
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("service")] public ApiService Service { get; set; }
    }

    public sealed class ApiService
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("attributes")] public ApiServiceAttrs Attributes { get; set; }
    }

    public sealed class ApiServiceAttrs
    {
        [JsonPropertyName("shortName")] public string ShortName { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("color")] public string Color { get; set; }
        [JsonPropertyName("interval")] public double? Interval { get; set; }
    }

    /* ---------- Normalized model used by router + renderers ---------- */

    public sealed class LineInfo
    {
        public string Id;
        public string ShortName;
        public string Name;
        public string Color; // hex like #da2128
        public double? Interval;
    }

    public sealed class Node
    {
        public string Id;
        public string LineId;
        public Station Station;
    }

    public sealed class Station
    {
        public string Id;
        public string Name;
        public double X, Y;
        public List<Node> Nodes = new List<Node>();
        public string LineIdsCsv; // cheap first-line for search UI
    }

    public sealed class Edge
    {
        public Node From, To;
        public double Time; // seconds
        public string[] LineIds;
        public bool IsTransfer;

        public string PrimaryLineId => (LineIds != null && LineIds.Length > 0) ? LineIds[0] : null;
    }

    public sealed class MetroNetwork
    {
        public string SchemeId;
        public string CityRuName;

        public Dictionary<string, Station> StationsById = new Dictionary<string, Station>();
        public Dictionary<string, Node> NodesById = new Dictionary<string, Node>();
        public Dictionary<string, LineInfo> LinesById = new Dictionary<string, LineInfo>();

        public List<Station> Stations = new List<Station>();
        public List<Edge> Edges = new List<Edge>();

        public Dictionary<string, List<Edge>> Outgoing = new Dictionary<string, List<Edge>>();
    }

    public sealed class NetworkBuilder
    {
        public MetroNetwork Build(SchemeEnvelope env)
        {
            var net = new MetroNetwork
            {
                SchemeId = env.Data.Meta?.Id,
                CityRuName = env.Data.Meta?.Name != null && env.Data.Meta.Name.ContainsKey("ru") ? env.Data.Meta.Name["ru"] : "Metro"
            };

            var body = env.Data.Body;
            if (body == null) throw new InvalidOperationException("schemeData missing from response");

            foreach (var l in body.Lines ?? Array.Empty<ApiLine>())
            {
                var a = l.Service?.Attributes;
                net.LinesById[l.Id] = new LineInfo
                {
                    Id = l.Id,
                    ShortName = a?.ShortName,
                    Name = a?.Name,
                    Color = a?.Color,
                    Interval = a?.Interval
                };
            }

            foreach (var s in body.Stations ?? Array.Empty<ApiStation>())
            {
                var st = new Station { Id = s.Id, Name = s.Name };
                if (s.Coordinates != null && s.Coordinates.Length >= 2) { st.X = s.Coordinates[0]; st.Y = s.Coordinates[1]; }
                foreach (var stop in s.Stops ?? Array.Empty<ApiStop>())
                {
                    if (string.IsNullOrEmpty(stop.NodeId)) continue;
                    var n = new Node { Id = stop.NodeId, LineId = stop.LineId, Station = st };
                    st.Nodes.Add(n);
                }
                net.Stations.Add(st);
                net.StationsById[s.Id] = st;
            }

            foreach (var st in net.Stations) { net.StationsById[st.Id] = st; foreach (var n in st.Nodes) net.NodesById[n.Id] = n; }

            foreach (var link in body.Links ?? Array.Empty<ApiLink>())
            {
                Node from, to;
                if (!net.NodesById.TryGetValue(link.FromNodeId, out from)) continue;
                if (!net.NodesById.TryGetValue(link.ToNodeId, out to)) continue;
                var e = new Edge
                {
                    From = from,
                    To = to,
                    Time = link.Time <= 0 ? 60 : link.Time,
                    LineIds = CleanLineIds(link.LineIds)
                };
                e.IsTransfer = e.LineIds == null || e.LineIds.Length == 0; // data has "lineIds":[null] for transfers
                net.Edges.Add(e);
                List<Edge> list;
                if (!net.Outgoing.TryGetValue(e.From.Id, out list)) { list = new List<Edge>(); net.Outgoing[e.From.Id] = list; }
                list.Add(e);
                // presumed symmetric twin missing? keep one-way unless reverse exists; inject reverse when absent
                bool hasReverse = false;
                List<Edge> rev;
                if (net.Outgoing.TryGetValue(e.To.Id, out rev))
                    hasReverse = rev.Any(x => x.From.Id == e.To.Id && x.To.Id == e.From.Id && SameLines(x, e));
                if (!hasReverse)
                {
                    var back = new Edge { From = to, To = from, Time = e.Time, LineIds = e.LineIds, IsTransfer = e.IsTransfer };
                    net.Edges.Add(back);
                    List<Edge> listBack;
                    if (!net.Outgoing.TryGetValue(to.Id, out listBack)) { listBack = new List<Edge>(); net.Outgoing[to.Id] = listBack; }
                    listBack.Add(back);
                }
            }

            return net;
        }

        /// <summary>Drops null/empty entries; API uses "lineIds":[null] for transfer links.</summary>
        private static string[] CleanLineIds(string[] ids)
        {
            if (ids == null) return null;
            var clean = ids.Where(x => !string.IsNullOrEmpty(x)).ToArray();
            return clean.Length == 0 ? null : clean;
        }

        private static bool SameLines(Edge a, Edge b)
        {
            if (a.LineIds == null || b.LineIds == null) return a.LineIds == null && b.LineIds == null;
            return a.LineIds.SequenceEqual(b.LineIds);
        }
    }
}
