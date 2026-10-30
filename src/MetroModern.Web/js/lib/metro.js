/*
 * MetroModern — metro network model + router (port of MetroCore C#).
 * Target: EdgeHTML/Chakra on Windows 10 Mobile — ES2015 only, no modules.
 */
(function (global) {
    'use strict';

    /* ---------- network building (port of NetworkBuilder.cs) ---------- */

    function cleanLineIds(ids) {
        if (!ids) return null;
        var clean = ids.filter(function (x) { return typeof x === 'string' && x.length > 0; });
        return clean.length ? clean : null;
    }

    /// schemeJson: parsed { data: { schemeMeta, schemeData } }
    function buildNetwork(schemeJson) {
        var meta = schemeJson.data.schemeMeta;
        var sd = schemeJson.data.schemeData;

        var net = {
            schemeId: meta && meta.id,
            cityName: meta && meta.name && (meta.name.ru || meta.name.en) || 'Metro',
            size: (meta && meta.size) || { width: 1750, height: 1800 },
            view: (meta && meta.center && meta.scale) ? meta : null, // center {x,y}, scale {initial,min,max,overview,detailed}
            lines: {},            // lineId -> {id, shortName, name, color, interval}
            stations: [],         // {id, name, x, y, nodes[], lineIds[], labels:{in:[],out:[]}}
            stationsById: {},
            nodesById: {},
            edges: [],
            outgoing: {}          // nodeId -> [edges]
        };

        (sd.lines || []).forEach(function (l) {
            var a = (l.service && l.service.attributes) || {};
            net.lines[l.id] = {
                id: l.id,
                shortName: a.shortName || '',
                name: a.name || '',
                color: a.color || '#888888',
                interval: a.interval || null
            };
        });

        (sd.stations || []).forEach(function (s) {
            var st = {
                id: s.id,
                name: s.name || '',
                x: 0, y: 0,          // scheme pixel position (from marker graphics group)
                hasPos: false,
                nodes: [],
                lineIds: [],
                graphicsIds: s.graphicsIds || []
            };
            (s.stops || []).forEach(function (stop) {
                if (!stop.nodeId) return;
                var node = { id: stop.nodeId, lineId: stop.lineId, station: st };
                st.nodes.push(node);
                net.nodesById[node.id] = node;
                if (stop.lineId && st.lineIds.indexOf(stop.lineId) < 0) st.lineIds.push(stop.lineId);
            });
            net.stations.push(st);
            net.stationsById[s.id] = st;
        });

        (sd.links || []).forEach(function (link) {
            var from = net.nodesById[link.fromNodeId];
            var to = net.nodesById[link.toNodeId];
            if (!from || !to) return;
            var ids = cleanLineIds(link.lineIds);
            var e = {
                from: from, to: to,
                time: (link.time && link.time > 0) ? link.time : 60,
                lineIds: ids,
                isTransfer: !ids
            };
            net.edges.push(e);
            (net.outgoing[e.from.id] = net.outgoing[e.from.id] || []).push(e);
            // inject reverse when the data doesn't carry it
            var back = net.outgoing[e.to.id];
            var hasReverse = back && back.some(function (x) {
                return x.to.id === e.from.id && JSON.stringify(x.lineIds) === JSON.stringify(e.lineIds);
            });
            if (!hasReverse) {
                var r = { from: e.to, to: e.from, time: e.time, lineIds: e.lineIds, isTransfer: e.isTransfer };
                net.edges.push(r);
                (net.outgoing[r.from.id] = net.outgoing[r.from.id] || []).push(r);
            }
        });

        return net;
    }

    /* ---------- routing (port of Router.cs, fixed Dijkstra) ---------- */

    var TRANSFER_PENALTY_SECONDS = 150; // once per transfer chain

    function route(net, from, to) {
        if (!from || !to) return null;
        if (from.id === to.id) return { steps: [], totalSeconds: 0, legs: [] };

        var targets = {};
        to.nodes.forEach(function (n) { targets[n.id] = true; });

        var dist = {}, prevEdge = {}, prevNode = {}, visited = {};
        var heap = new MinHeap();

        from.nodes.forEach(function (n) {
            if (dist[n.id] === undefined) { dist[n.id] = 0; heap.push(0, n.id); }
        });

        var goal = null;
        while (heap.size() > 0) {
            var top = heap.pop();
            if (visited[top.id]) continue;
            visited[top.id] = true;
            if (targets[top.id]) { goal = top.id; break; }
            var d = dist[top.id];
            var prevEdgeU = prevEdge[top.id];
            var edges = net.outgoing[top.id];
            if (!edges) continue;
            for (var i = 0; i < edges.length; i++) {
                var e = edges[i];
                if (visited[e.to.id]) continue;
                var penalty = (e.isTransfer && prevEdgeU && !prevEdgeU.isTransfer) ? TRANSFER_PENALTY_SECONDS : 0;
                var nd = d + e.time + penalty;
                var cur = dist[e.to.id];
                if (cur !== undefined && cur <= nd) continue;
                dist[e.to.id] = nd;
                prevEdge[e.to.id] = e;
                prevNode[e.to.id] = top.id;
                heap.push(nd, e.to.id);
            }
        }
        if (goal === null) return null;

        var steps = [];
        var curId = goal;
        while (prevEdge[curId] !== undefined) {
            var edge = prevEdge[curId];
            steps.push({ edge: edge, from: edge.from, to: edge.to, cumulativeSeconds: dist[curId] });
            curId = prevNode[curId];
        }
        steps.reverse();

        var totalSeconds = dist[goal];

        // merge steps into legs (same line / transfer chains)
        var legs = [];
        steps.forEach(function (s) {
            var lineId = s.edge.isTransfer ? null : (s.edge.lineIds ? s.edge.lineIds[0] : null);
            var last = legs.length ? legs[legs.length - 1] : null;
            if (last && last.isTransfer === s.edge.isTransfer &&
                String(last.lineId) === String(lineId)) {
                last.seconds += s.edge.time;
                last.nodes.push(s.to);
            } else {
                var leg = { lineId: lineId, isTransfer: !!s.edge.isTransfer, seconds: s.edge.time, nodes: [s.to] };
                if (!leg.isTransfer) leg.nodes.unshift(s.from);
                legs.push(leg);
            }
        });

        return { steps: steps, totalSeconds: totalSeconds, legs: legs };
    }

    /* ---------- binary min-heap ---------- */

    function MinHeap() { this.a = []; }
    MinHeap.prototype.size = function () { return this.a.length; };
    MinHeap.prototype.push = function (d, id) {
        var a = this.a; a.push([d, id]);
        var i = a.length - 1;
        while (i > 0) {
            var p = (i - 1) >> 1;
            if (a[p][0] <= a[i][0]) break;
            var t = a[i]; a[i] = a[p]; a[p] = t; i = p;
        }
    };
    MinHeap.prototype.pop = function () {
        var a = this.a, first = a[0], last = a.pop();
        if (a.length) {
            a[0] = last;
            var i = 0;
            for (;;) {
                var l = 2 * i + 1, r = l + 1, s = i;
                if (l < a.length && a[l][0] < a[s][0]) s = l;
                if (r < a.length && a[r][0] < a[s][0]) s = r;
                if (s === i) break;
                var t = a[i]; a[i] = a[s]; a[s] = t; i = s;
            }
        }
        return { d: first[0], id: first[1] };
    };

    /* ---------- search ---------- */

    function normalizeName(s) {
        if (!s) return '';
        return String(s).toLowerCase().replace(/ё/g, 'е').replace(/[\s\-.«»"]/g, '');
    }

    function searchStations(net, query, limit) {
        limit = limit || 20;
        var q = normalizeName(query);
        var res = [];
        for (var i = 0; i < net.stations.length && res.length < limit; i++) {
            var st = net.stations[i];
            if (!q || normalizeName(st.name).indexOf(q) >= 0) res.push(st);
        }
        return res;
    }

    global.Metro = {
        buildNetwork: buildNetwork,
        route: route,
        searchStations: searchStations,
        normalizeName: normalizeName,
        TRANSFER_PENALTY_SECONDS: TRANSFER_PENALTY_SECONDS
    };
})(this);
