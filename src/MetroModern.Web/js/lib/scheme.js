/*
 * MetroModern — scheme rendering: Yandex graphics JSON -> SVG DOM.
 *
 * KEY FACT (reverse-engineered): the graphics blob contains the whole map at
 * TWO zoom levels superimposed in one coordinate space:
 *   - detailed: thick lines (w>=5), big markers (mainCircle r>=4), fs>=15 labels
 *   - overview ("mini"): HALF-SCALE copy — mini = A + detailed/2, thin lines
 *     (w<=4), small markers (mainCircle r<=3), fs<15 labels
 * Rendering both at once looks "doubled". We classify every element into its
 * layer (markers by radius; lines/paths by the affine twin test; texts by font
 * size) and show exactly one layer per zoom level.
 */
(function (global) {
    'use strict';

    var SVG_NS = 'http://www.w3.org/2000/svg';

    function el(name, attrs, parent) {
        var e = document.createElementNS(SVG_NS, name);
        if (attrs) for (var k in attrs) if (attrs[k] !== null && attrs[k] !== undefined) e.setAttribute(k, attrs[k]);
        if (parent) parent.appendChild(e);
        return e;
    }

    function num(v, fallback) {
        var n = typeof v === 'number' ? v : parseFloat(v);
        return isNaN(n) ? (fallback || 0) : n;
    }

    /* Extracts station pixel positions from graphics groups (detailed layer). */
    function extractStationLayout(net, sd) {
        var graphics = sd.graphics || {};
        net.stations.forEach(function (st) {
            (st.graphicsIds || []).forEach(function (gid) {
                var arr = graphics[gid];
                if (!arr || !arr.length) return;
                for (var i = 0; i < arr.length; i++) {
                    var e = arr[i];
                    if (e.type === 'Circle' && e.id === 'background' && !st.hasPos) {
                        st.x = num(e.x); st.y = num(e.y); st.hasPos = true;
                    } else if (e.type === 'Text' && e.text) {
                        var bucket = num(e.fontSize, 12) >= 15 ? 'in' : 'out';
                        (st.labels = st.labels || { in: null, out: null, any: null });
                        if (st.labels[bucket] === null) st.labels[bucket] = e;
                        if (st.labels.any === null) st.labels.any = e;
                    }
                }
            });
        });
    }

    /* ---------- layer classification ---------- */

    function firstPathPoint(d) {
        if (!d) return null;
        var m = /([-\d.]+)[ ,]+([-\d.]+)/.exec(d);
        return m ? { x: parseFloat(m[1]), y: parseFloat(m[2]) } : null;
    }

    /*
     * Fits the mini->detailed affine offset: mini = A + detailed/2.
     * RANSAC over mini/detailed marker positions (mainCircle r<=3 vs r>=4).
     * Returns A or null when the data has no overview layer.
     */
    function fitMiniOffset(markersDet, markersMini) {
        if (!markersMini.length || !markersDet.length) return null;
        // hash detailed positions on a 2px grid for scoring
        function key(x, y, cell) { return Math.round(x / cell) + ':' + Math.round(y / cell); }
        var detSet = {};
        markersDet.forEach(function (p) { detSet[key(p.x, p.y, 2)] = true; });
        function nearDet(x, y) {
            var cx = Math.round(x / 2), cy = Math.round(y / 2);
            for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++)
                if (detSet[(cx + dx) + ':' + (cy + dy)]) return true;
            return false;
        }
        var best = null, bestScore = 0;
        for (var i = 0; i < markersMini.length; i++) {
            for (var j = 0; j < markersDet.length; j++) {
                var ax = markersMini[i].x - markersDet[j].x / 2;
                var ay = markersMini[i].y - markersDet[j].y / 2;
                // score: how many mini markers land on a detailed marker after up-scaling
                var score = 0;
                for (var k = 0; k < markersMini.length; k++) {
                    var px = (markersMini[k].x - ax) * 2, py = (markersMini[k].y - ay) * 2;
                    if (nearDet(px, py)) score++;
                }
                if (score > bestScore) { bestScore = score; best = { x: ax, y: ay }; }
                if (bestScore >= markersMini.length * 0.9) return best; // good enough, stop
            }
        }
        return bestScore >= markersMini.length * 0.5 ? best : null;
    }

    /*
     * Renders the scheme into `svg`:
     *   <g id="viewport">
     *     <g id="layer-det"/> <g id="labels-det"/>   detailed zoom
     *     <g id="layer-mini"/> <g id="labels-mini"/>  overview zoom
     *   </g>
     * Returns handles incl. `miniBounds` (bbox of the overview map) and `A`.
     */
    function renderScheme(net, sd, svg) {
        var w = net.size.width, h = net.size.height;
        svg.setAttribute('viewBox', '0 0 ' + w + ' ' + h);
        while (svg.firstChild) svg.removeChild(svg.firstChild);

        var viewport = el('g', { id: 'viewport' }, svg);
        var layerDet = el('g', { id: 'layer-det' }, viewport);
        var labelsDet = el('g', { id: 'labels-det' }, viewport);
        var layerMini = el('g', { id: 'layer-mini' }, viewport);
        var labelsMini = el('g', { id: 'labels-mini' }, viewport);

        var graphics = sd.graphics || {};

        /* flatten */
        var groups = [], lines = [], paths = [], texts = [];
        Object.keys(graphics).forEach(function (gid) {
            var arr = graphics[gid];
            if (!arr || !arr.length) return;
            var g = { els: arr, layer: null, mainR: null };
            for (var i = 0; i < arr.length; i++) {
                var e = arr[i];
                if (e.type === 'Line') lines.push({ e: e, g: g });
                else if (e.type === 'Path') paths.push({ e: e, g: g });
                else if (e.type === 'Text') texts.push({ e: e, g: g });
                if (e.type === 'Circle' && e.id === 'mainCircle') g.mainR = num(e.r, 0);
            }
            groups.push(g);
        });

        /* markers: group-level classification + collect positions for A-fit */
        var markersDet = [], markersMini = [];
        groups.forEach(function (g) {
            if (g.mainR !== null) {
                g.layer = g.mainR >= 4 ? 'det' : 'mini';
                for (var i = 0; i < g.els.length; i++) {
                    var c = g.els[i];
                    if (c.type === 'Circle' && c.id === 'mainCircle') {
                        var p = { x: num(c.x), y: num(c.y) };
                        (g.layer === 'det' ? markersDet : markersMini).push(p);
                    }
                }
            }
        });

        var A = fitMiniOffset(markersDet, markersMini);

        /* lines: twin test through the affine map */
        function lineKey(l) {
            return Math.round(l.x1 * 2) + ',' + Math.round(l.y1 * 2) + ',' +
                   Math.round(l.x2 * 2) + ',' + Math.round(l.y2 * 2);
        }
        var lineSet = {};
        lines.forEach(function (it) {
            lineSet[lineKey(it.e)] = true;
            // also reversed order
            lineSet[Math.round(it.e.x2 * 2) + ',' + Math.round(it.e.y2 * 2) + ',' +
                    Math.round(it.e.x1 * 2) + ',' + Math.round(it.e.y1 * 2)] = true;
        });
        if (A) {
            lines.forEach(function (it) {
                var l = it.e;
                // mini twin guess for a detailed line
                var t = { x1: A.x + l.x1 / 2, y1: A.y + l.y1 / 2, x2: A.x + l.x2 / 2, y2: A.y + l.y2 / 2 };
                var inv = { x1: (l.x1 - A.x) * 2, y1: (l.y1 - A.y) * 2, x2: (l.x2 - A.x) * 2, y2: (l.y2 - A.y) * 2 };
                if (lineSet[lineKey(inv)]) it.e.__layer = 'mini';   // its detailed twin exists
                else if (lineSet[lineKey(t)]) it.e.__layer = 'det'; // its mini twin exists
            });
        }

        /* texts: affine twin test (a text whose scaled position holds another
         * text belongs to the other layer) — badge digits pair up exactly;
         * distance test as fallback, fontSize as last resort */
        var textPos = {};
        texts.forEach(function (it) {
            var p = { x: num(it.e.x), y: num(it.e.y) };
            it.p0 = p;
            textPos[Math.round(p.x * 2) + ',' + Math.round(p.y * 2)] = true;
        });
        function textAt(x, y) {
            return textPos[Math.round(x * 2) + ',' + Math.round(y * 2)];
        }
        function classifyText(e) {
            if (e.__layer) return;
            var fs = num(e.fontSize, 12);
            if (!A) { e.__layer = fs >= 15 ? 'det' : 'mini'; return; }
            var x = num(e.x), y = num(e.y);
            var hasDown = textAt(A.x + x / 2, A.y + y / 2);   // my mini twin exists -> det
            var hasUp = textAt((x - A.x) * 2, (y - A.y) * 2); // my det twin exists -> mini
            if (hasDown && !hasUp) { e.__layer = 'det'; return; }
            if (hasUp && !hasDown) { e.__layer = 'mini'; return; }
            var dRaw = nearestDist({ x: x, y: y }, markersDet);
            var dScaled = nearestDist({ x: (x - A.x) * 2, y: (y - A.y) * 2 }, markersDet);
            if (Math.abs(dScaled - dRaw) < 8) e.__layer = fs >= 15 ? 'det' : 'mini';
            else e.__layer = dScaled < dRaw ? 'mini' : 'det';
        }

        /* junction endpoint sets for path classification */
        var detJunc = [], miniJunc = [];
        lines.forEach(function (it) {
            var layer = it.e.__layer || it.g.layer || 'det';
            var arr = layer === 'mini' ? miniJunc : detJunc;
            arr.push({ x: num(it.e.x1), y: num(it.e.y1) }, { x: num(it.e.x2), y: num(it.e.y2) });
        });

        /* paths: affine twin test over ALL d-points (hub pills etc. whose
         * start point touches no line endpoint and may be re-tessellated),
         * junction test as fallback */
        if (A) {
            function pathPoints(d) {
                var pts = [], re = /(-?\d+(?:\.\d+)?)[ ,]+(-?\d+(?:\.\d+)?)/g, m;
                while ((m = re.exec(d))) pts.push([parseFloat(m[1]), parseFloat(m[2])]);
                return pts;
            }
            function pathKey(pts, fn) {
                var ks = [];
                for (var i = 0; i < pts.length; i++) {
                    var t = fn(pts[i][0], pts[i][1]);
                    ks.push(Math.round(t[0]) + ':' + Math.round(t[1]));
                }
                return ks.sort().join('|');
            }
            var pk = {};
            paths.forEach(function (it) {
                it.pts = pathPoints(it.e.d);
                if (it.pts.length) pk[pathKey(it.pts, function (x, y) { return [x, y]; })] = true;
            });
            paths.forEach(function (it) {
                if (!it.pts.length || it.e.__layer) return;
                var up = pathKey(it.pts, function (x, y) { return [(x - A.x) * 2, (y - A.y) * 2]; });
                var down = pathKey(it.pts, function (x, y) { return [A.x + x / 2, A.y + y / 2]; });
                if (pk[up] && !pk[down]) it.e.__layer = 'mini';
                else if (pk[down]) it.e.__layer = 'det';
            });
        }
        function nearAny(p, set, eps2) {
            for (var i = 0; i < set.length; i++) {
                var dx = set[i].x - p.x, dy = set[i].y - p.y;
                if (dx * dx + dy * dy <= eps2) return true;
            }
            return false;
        }
        function nearestDist(p, set) {
            var best = 1e12;
            for (var i = 0; i < set.length; i++) {
                var dx = set[i].x - p.x, dy = set[i].y - p.y;
                var d = dx * dx + dy * dy;
                if (d < best) best = d;
            }
            return Math.sqrt(best);
        }

        /* render geometry */
        function renderLine(e, parent) {
            el('line', {
                x1: num(e.x1), y1: num(e.y1), x2: num(e.x2), y2: num(e.y2),
                stroke: e.stroke || '#000000',
                'stroke-width': num(e.strokeWidth, 1),
                'stroke-linecap': e.strokeLinecap || 'butt'
            }, parent);
        }
        function renderPath(e, parent) {
            var hasStroke = e.stroke && e.stroke !== 'none';
            var hasFill = e.fill && e.fill !== 'none';
            el('path', {
                d: e.d || '',
                fill: hasFill ? e.fill : 'none',
                stroke: hasStroke ? e.stroke : 'none',
                'stroke-width': hasStroke ? num(e.strokeWidth, 1) : null,
                'stroke-linecap': hasStroke ? (e.strokeLinecap || 'butt') : null,
                'stroke-linejoin': hasStroke ? (e.strokeLinejoin || null) : null
            }, parent);
        }
        function renderCircle(e, parent) {
            el('circle', {
                cx: num(e.x), cy: num(e.y), r: num(e.r, 3),
                fill: e.fill || '#000000'
            }, parent);
        }

        /* all circle positions (for standalone-circle twin test) */
        var circlePos = {};
        groups.forEach(function (g) {
            g.els.forEach(function (e) {
                if (e.type === 'Circle') circlePos[Math.round(num(e.x) * 2) + ',' + Math.round(num(e.y) * 2)] = true;
            });
        });
        function circleAt(x, y) {
            return circlePos[Math.round(x * 2) + ',' + Math.round(y * 2)];
        }

        var miniXs = [], miniYs = [];
        groups.forEach(function (g) {
            var els = g.els;
            for (var i = 0; i < els.length; i++) {
                var e = els[i];
                if (e.type === 'SVGImage' || e.type === 'Stylized') continue;
                if (num(e.opacity, 1) <= 0.001) continue;

                var layer = g.layer; // marker groups are pre-classified
                if (e.type === 'Text') { classifyText(e); renderText(e, labelsDet, labelsMini); continue; }
                if (e.type === 'Line' && !layer) layer = e.__layer || 'det';
                if (!layer) {
                    if (e.type === 'Line') layer = 'det';
                    else if (e.type === 'Path') {
                        if (e.__layer) layer = e.__layer;
                        else {
                            var p = firstPathPoint(e.d);
                            if (p && A) {
                                var isDet = nearAny(p, detJunc, 1.44);
                                var isMini = nearAny(p, miniJunc, 1.44);
                                if (isDet && !isMini) layer = 'det';
                                else if (isMini && !isDet) layer = 'mini';
                                else {
                                    // no junction anchor (hub pills): which layer's
                                    // markers does this shape belong to after scaling?
                                    var dRaw = nearestDist(p, markersDet);
                                    var dScaled = nearestDist({ x: (p.x - A.x) * 2, y: (p.y - A.y) * 2 }, markersDet);
                                    layer = dScaled < dRaw ? 'mini' : 'det';
                                    if (layer === 'mini') { miniXs.push(num(e.x)); miniYs.push(num(e.y)); }
                                }
                            } else layer = 'det';
                        }
                    } else if (e.type === 'Circle') {
                        var r = num(e.r, 0);
                        if (r >= 9) layer = 'det';
                        else if (r <= 3) layer = 'mini';
                        else {
                            // ambiguous band (mini hub rings r=6..8 vs det main r=5):
                            // a circle whose mini/detailed twin exists belongs to the other layer
                            var px = num(e.x), py = num(e.y);
                            if (A && circleAt((px - A.x) * 2, (py - A.y) * 2) &&
                                !circleAt(A.x + px / 2, A.y + py / 2)) layer = 'mini';
                            else if (A && circleAt(A.x + px / 2, A.y + py / 2)) layer = 'det';
                            else layer = r >= 6 ? 'det' : 'mini';
                        }
                        if (layer === 'mini') { miniXs.push(num(e.x)); miniYs.push(num(e.y)); }
                    } else layer = 'det';
                } else if (layer === 'mini' && e.type === 'Circle') {
                    miniXs.push(num(e.x)); miniYs.push(num(e.y));
                }

                if (layer === 'mini') {
                    if (e.type === 'Line') renderLine(e, layerMini);
                    else if (e.type === 'Path') renderPath(e, layerMini);
                    else if (e.type === 'Circle') renderCircle(e, layerMini);
                } else {
                    if (e.type === 'Line') renderLine(e, layerDet);
                    else if (e.type === 'Path') renderPath(e, layerDet);
                    else if (e.type === 'Circle') renderCircle(e, layerDet);
                }
            }
        });

        // overview map bounds (from its geometry; fallback: half-size centered)
        var mb = {
            x: miniXs.length ? Math.min.apply(null, miniXs) : w * 0.24,
            y: miniYs.length ? Math.min.apply(null, miniYs) : h * 0.25
        };
        mb.x2 = miniXs.length ? Math.max.apply(null, miniXs) : w * 0.74;
        mb.y2 = miniYs.length ? Math.max.apply(null, miniYs) : h * 0.75;
        mb.w = mb.x2 - mb.x; mb.h = mb.y2 - mb.y;

        return {
            viewport: viewport,
            layerDet: layerDet, labelsDet: labelsDet,
            layerMini: layerMini, labelsMini: labelsMini,
            miniBounds: mb, A: A
        };
    }

    /*
     * Yandex anchor codes (reverse-engineered):
     *   h = text after the point (start), y is the vertical CENTER
     *   f = text before the point (end),  y is the vertical CENTER
     *   a = text after the point (start), y is the baseline of the LAST line
     *   b = text before the point (end),  y is the baseline of the LAST line
     * Multi-line labels carry "\n"; SVG needs one node per line.
     */
    function textLines(e) {
        return String(e.text).split('\n');
    }

    function textBaselineY(e, i, count) {
        var fs = num(e.fontSize, 12);
        var lh = num(e.lineHeight, 0) || fs * 1.15;
        var y = num(e.y);
        if (e.textAnchor === 'h' || e.textAnchor === 'f') {
            return y + fs * 0.35 - ((count - 1) * lh) / 2 + i * lh;
        }
        return y - (count - 1 - i) * lh;
    }

    function anchorAttrs(e, y) {
        var fs = num(e.fontSize, 12);
        var a = {
            x: num(e.x),
            y: y,
            'font-size': fs,
            fill: e.color || '#12161a'
        };
        if (e.fontWeight === 'bold' || e.fontWeight === '500' || e.fontWeight === '600' || e.fontWeight === '700')
            a['font-weight'] = '600';
        switch (e.textAnchor) {
            case 'h': case 'a': a['text-anchor'] = 'start'; break;
            case 'f': case 'b': a['text-anchor'] = 'end'; break;
            default: a['text-anchor'] = 'start';
        }
        return a;
    }

    function renderText(e, labelsDet, labelsMini) {
        if (!e.text || num(e.opacity, 1) <= 0.001) return;
        var lines = textLines(e);
        var bucket = e.__layer === 'mini' ? labelsMini : labelsDet;
        for (var i = 0; i < lines.length; i++) {
            var attrs = anchorAttrs(e, textBaselineY(e, i, lines.length));
            if (e.id === 'textStroke') {
                var halo = el('text', attrs, bucket);
                halo.textContent = lines[i];
                halo.setAttribute('fill', e.color || '#ffffff');
                halo.setAttribute('stroke', e.stroke || '#ffffff');
                halo.setAttribute('stroke-width', num(e.strokeWidth, 2));
                if (e.strokeLinejoin) halo.setAttribute('stroke-linejoin', e.strokeLinejoin);
            } else {
                var t = el('text', attrs, bucket);
                t.textContent = lines[i];
            }
        }
    }

    global.MetroScheme = {
        renderScheme: renderScheme,
        extractStationLayout: extractStationLayout,
        el: el
    };
})(this);
