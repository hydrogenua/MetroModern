using System;
using System.Collections.Generic;
using System.Linq;

namespace MetroCore
{
    /// <summary>
    /// Dijkstra over station/platform nodes. Weight = link time (seconds, already encodes
    /// dwell + motion) + penalty for starting a transfer chain (walk + typical wait).
    /// </summary>
    public sealed class Router
    {
        // Overhead of starting a transfer: walking to the other platform beyond the encoded
        // link time plus typical wait for the next train. Applied once per transfer chain
        // (consecutive transfer edges count as one transfer), not per edge.
        private const double TransferPenaltySeconds = 150;

        public sealed class RouteStep
        {
            public Edge Edge;          // edge traversed
            public Node From, To;
            public double CumulativeSeconds;
        }

        public sealed class RouteResult
        {
            public List<RouteStep> Steps = new List<RouteStep>();
            public double TotalSeconds;

            public List<RouteLeg> legs()
            {
                var legs = new List<RouteLeg>();
                foreach (var s in Steps)
                {
                    string line = s.Edge.PrimaryLineId;
                    bool isTransfer = s.Edge.IsTransfer;
                    var cur = legs.LastOrDefault();
                    if (cur != null && string.Equals(cur.LineId, isTransfer ? null : line, StringComparison.Ordinal)
                        && cur.IsTransfer == isTransfer)
                    {
                        cur.Seconds += s.Edge.Time;
                        cur.Nodes.Add(s.To);
                    }
                    else
                    {
                        var leg = new RouteLeg { LineId = isTransfer ? null : line, IsTransfer = isTransfer, Seconds = s.Edge.Time };
                        leg.Nodes.Add(s.To);
                        if (!isTransfer) leg.Nodes.Insert(0, s.From);
                        legs.Add(leg);
                    }
                }
                return legs;
            }

            public List<Station> StationsInOrder()
            {
                var list = new List<Station>();
                foreach (var s in Steps) if (!list.Contains(s.From.Station)) list.Add(s.From.Station);
                var last = Steps.LastOrDefault();
                if (last != null) list.Add(last.To.Station);
                return list;
            }
        }

        public sealed class RouteLeg
        {
            public string LineId;
            public bool IsTransfer;
            public double Seconds;
            public List<Node> Nodes = new List<Node>();
        }

        public RouteResult Find(MetroNetwork net, Station from, Station to)
        {
            if (from == null || to == null) return null;
            if (ReferenceEquals(from, to)) return new RouteResult { TotalSeconds = 0 };

            // try every platform pairing (stations like Технологический институт have 2 platforms)
            var targets = new HashSet<string>(to.Nodes.Select(n => n.Id));

            var dist = new Dictionary<string, double>();
            var prevEdge = new Dictionary<string, Edge>();
            var prevNode = new Dictionary<string, string>();
            var visited = new HashSet<string>();
            var heap = new MinHeap();

            foreach (var n in from.Nodes)
            {
                if (dist.ContainsKey(n.Id)) continue;
                dist[n.Id] = 0;
                heap.Push(0, n.Id);
            }

            string goal = null;

            while (heap.Count > 0)
            {
                var top = heap.Pop();
                if (visited.Contains(top.Id)) continue; // stale entry, already settled
                visited.Add(top.Id);

                if (targets.Contains(top.Id)) { goal = top.Id; break; }

                double d = dist[top.Id];
                Edge prevEdgeU;
                prevEdge.TryGetValue(top.Id, out prevEdgeU);

                List<Edge> edges;
                if (!net.Outgoing.TryGetValue(top.Id, out edges)) continue;

                foreach (var e in edges)
                {
                    if (visited.Contains(e.To.Id)) continue;

                    // penalty once per transfer chain: entering a transfer edge from a train edge
                    double penalty = 0;
                    if (e.IsTransfer && prevEdgeU != null && !prevEdgeU.IsTransfer)
                        penalty = TransferPenaltySeconds;

                    double nd = d + e.Time + penalty;
                    double cur;
                    if (dist.TryGetValue(e.To.Id, out cur) && cur <= nd) continue; // relaxation

                    dist[e.To.Id] = nd;
                    prevEdge[e.To.Id] = e;
                    prevNode[e.To.Id] = top.Id;
                    heap.Push(nd, e.To.Id);
                }
            }

            if (goal == null) return null;

            var result = new RouteResult();
            string curNodeId = goal;
            while (prevEdge.ContainsKey(curNodeId))
            {
                var e = prevEdge[curNodeId];
                result.Steps.Add(new RouteStep
                {
                    Edge = e,
                    From = e.From,
                    To = e.To,
                    CumulativeSeconds = dist[curNodeId]
                });
                curNodeId = prevNode[curNodeId];
            }
            result.Steps.Reverse();
            result.TotalSeconds = dist[goal];

            return result;
        }

        private sealed class MinHeap
        {
            private readonly List<(double, string)> _items = new List<(double, string)>();
            public int Count => _items.Count;
            public void Push(double d, string id) { _items.Add((d, id)); int i = _items.Count - 1; while (i > 0) { int p = (i - 1) / 2; if (_items[p].Item1 <= _items[i].Item1) break; var t = _items[i]; _items[i] = _items[p]; _items[p] = t; i = p; } }
            public (double D, string Id) Pop() { var first = _items[0]; var last = _items[_items.Count - 1]; _items.RemoveAt(_items.Count - 1); if (_items.Count > 0) { _items[0] = last; int i = 0; while (true) { int l = 2 * i + 1, r = l + 1, s = i; if (l < _items.Count && _items[l].Item1 < _items[s].Item1) s = l; if (r < _items.Count && _items[r].Item1 < _items[s].Item1) s = r; if (s == i) break; var t = _items[i]; _items[i] = _items[s]; _items[s] = t; i = s; } } return (first.Item1, first.Item2); }
        }
    }
}
