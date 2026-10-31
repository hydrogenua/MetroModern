/*
 * MetroModern — app UI. Modern Yandex.Metro-style scheme viewer for W10M.
 * ES2015 only (EdgeHTML/Chakra on Windows 10 Mobile).
 */
(function () {
    'use strict';

    /* ---------------- state ---------------- */

    // v1.0.7 ships Moscow + SPb data only: W10M was crashing with the other
    // cities' schemes in the package (their dumps stay in ../data for later)
    var CITIES = [
        { name: 'Москва', alias: 'moscow' },
        { name: 'Санкт-Петербург', alias: 'saint-petersburg' }
    ];

    var state = {
        city: null,
        net: null,
        sd: null,
        handles: null,      // { viewport, labelsIn, labelsOut }
        from: null,
        to: null,
        route: null,
        view: { tx: 0, ty: 0, s: 1 },   // screen = tx + s * scheme
        activeSheet: null,
        editTarget: null    // 'from' | 'to' while search sheet is open
    };

    /* ---------------- helpers ---------------- */

    function $(id) { return document.getElementById(id); }

    // EdgeHTML 14 on early W10M builds lacks Element.closest — walk manually
    function closestClass(node, cls) {
        while (node && node !== document) {
            if (node.classList && node.classList.contains(cls)) return node;
            node = node.parentNode;
        }
        return null;
    }

    function xhrText(url) {
        return new Promise(function (resolve, reject) {
            var x = new XMLHttpRequest();
            x.open('GET', url, true);
            x.onload = function () {
                if (x.status === 200 || x.status === 0) resolve(x.responseText);
                else reject(new Error('HTTP ' + x.status + ' ' + url));
            };
            x.onerror = function () { reject(new Error('network error ' + url)); };
            x.send();
        });
    }

    function loadCity(cityId, cb) {
        var city = null;
        for (var i = 0; i < CITIES.length; i++) if (CITIES[i].id === cityId) city = CITIES[i];
        if (!city) city = CITIES[0];
        xhrText('js/data/' + city.file + '.json').then(function (text) {
            var json = JSON.parse(text);
            state.city = city.id;
            state.sd = json.data.schemeData;
            state.net = Metro.buildNetwork(json);
            MetroScheme.extractStationLayout(state.net, state.sd);
            state.from = null; state.to = null; state.route = null;
            cb(null);
        }, function (err) { cb(err); });
    }

    /* ---------------- scheme view (pan/zoom) ---------------- */

    var MIN_S = 0.5, MAX_S = 6;
    var mapRect = { left: 0, top: 0, w: 0, h: 0 };
    var viewScheduled = false;

    function refreshRect() {
        var r = $('map').getBoundingClientRect();
        mapRect.left = r.left; mapRect.top = r.top;
        mapRect.w = r.width; mapRect.h = r.height;
        if (r.width > 0 && r.height > 0)
            $('scheme').setAttribute('viewBox', '0 0 ' + r.width + ' ' + r.height);
    }
    window.addEventListener('resize', function () { refreshRect(); applyView(); });

    function wholeFitScale() {
        if (!mapRect.w) return 0.3;
        return Math.min(mapRect.w / state.net.size.width, mapRect.h / state.net.size.height);
    }

    /* schedule one transform update per animation frame (touchmove storms) */
    function applyView() {
        if (viewScheduled) return;
        viewScheduled = true;
        requestAnimationFrame(function () { viewScheduled = false; applyViewNow(); });
    }

    function applyViewNow() {
        var v = state.view;
        var meta = state.net && state.net.view && state.net.view.scale;
        var metaMin = meta ? (meta.min || 0.5) : 0.5;
        MAX_S = meta ? (meta.max || 6) : 6;
        // overview (mini) map must be reachable when zooming out
        var miniFit = 0;
        if (state.handles && state.handles.miniBounds && state.handles.miniBounds.w > 0)
            miniFit = Math.min(mapRect.w / state.handles.miniBounds.w, mapRect.h / state.handles.miniBounds.h);
        MIN_S = Math.min(metaMin, miniFit || wholeFitScale());
        // while a route is shown its fit scale may go below the usual floor
        var floor = MIN_S;
        if (state.route && state.routeFitScale) floor = Math.min(floor, state.routeFitScale);
        v.s = Math.min(MAX_S, Math.max(floor, v.s));
        state.handles.viewport.setAttribute('transform',
            'translate(' + v.tx + ' ' + v.ty + ') scale(' + v.s + ')');
        // two full map layers ship in the data (detailed + half-size overview);
        // show exactly one
        var showDet = v.s >= 1.35;
        if (state.handles.layerDet) {
            state.handles.layerDet.style.display = showDet ? '' : 'none';
            state.handles.labelsDet.style.display = showDet ? '' : 'none';
            state.handles.layerMini.style.display = showDet ? 'none' : '';
            state.handles.labelsMini.style.display = showDet ? 'none' : '';
            if (state.showingDet !== undefined && state.showingDet !== showDet) {
                if (!showDet) {
                    // crossing into the overview layer: center the mini map once
                    var mb = state.handles.miniBounds;
                    if (mb && mb.w > 0) {
                        v.tx = mapRect.w / 2 - v.s * (mb.x + mb.w / 2);
                        v.ty = mapRect.h / 2 - v.s * (mb.y + mb.h / 2);
                        state.handles.viewport.setAttribute('transform',
                            'translate(' + v.tx + ' ' + v.ty + ') scale(' + v.s + ')');
                    }
                }
                // route overlay lives in layer coordinates — redraw it for the new layer
                if (state.route) drawRoute(state.route);
            }
        }
        state.showingDet = showDet;
    }

    function screenToScheme(px, py) {
        return { x: (px - mapRect.left - state.view.tx) / state.view.s,
                 y: (py - mapRect.top - state.view.ty) / state.view.s };
    }

    function zoomAround(px, py, factor) {
        var v = state.view;
        var cx = px - mapRect.left, cy = py - mapRect.top;
        var ns = Math.min(MAX_S, Math.max(MIN_S, v.s * factor));
        var k = ns / v.s;
        v.tx = cx - (cx - v.tx) * k;
        v.ty = cy - (cy - v.ty) * k;
        v.s = ns;
        applyView();
    }

    function centerOn(sx, sy, scale) {
        var v = state.view;
        var rect = $('map').getBoundingClientRect();
        v.s = scale || v.s;
        v.tx = rect.width / 2 - v.s * sx;
        v.ty = rect.height / 2 - v.s * sy;
        applyView();
    }

    function fitRoute(bbox) {
        var v = state.view;
        var pad = 50;
        var reserve = 280; // route card height + margin
        var availH = Math.max(120, mapRect.h - reserve);
        var sx = (mapRect.w - pad * 2) / Math.max(40, bbox.w);
        var sy = availH / Math.max(40, bbox.h);
        var s = Math.min(Math.min(sx, sy), MAX_S);
        state.routeFitScale = s; // applyViewNow allows zooming this low while the route is shown
        v.s = s;
        v.tx = mapRect.w / 2 - s * (bbox.x + bbox.w / 2);
        v.ty = availH / 2 - s * (bbox.y + bbox.h / 2);
        applyView();
    }

    /* ---------------- pointer handling ---------------- */

    var pointers = {};      // id -> {x, y}
    var gesture = null;     // { type:'pan'|'pinch', ... }

    function bindMapEvents() {
        var map = $('map');

        // sheets/cards live inside #map — touches on them must not start gestures
        // or close the sheet before its own click handlers fire (touch devices!)
        function insideSheet(t) {
            while (t && t !== map) {
                if (t.id === 'searchSheet' || (t.classList && t.classList.contains('sheet'))) return true;
                t = t.parentNode;
            }
            return false;
        }

        map.addEventListener('pointerdown', function (e) {
            if (insideSheet(e.target)) return;
            pointers[e.pointerId] = { x: e.clientX, y: e.clientY };
            map.setPointerCapture && map.setPointerCapture(e.pointerId);
            var ids = Object.keys(pointers);
            closeSheets(); // tap outside sheets closes them (also fires before tap logic)
            if (ids.length === 1) {
                gesture = { type: 'pan', x0: e.clientX, y0: e.clientY, moved: 0, t0: Date.now() };
            } else if (ids.length === 2) {
                var a = pointers[ids[0]], b = pointers[ids[1]];
                gesture = { type: 'pinch', d0: dist2d(a, b), s0: state.view.s,
                            cx0: (a.x + b.x) / 2, cy0: (a.y + b.y) / 2 };
            }
            e.preventDefault();
        });

        map.addEventListener('pointermove', function (e) {
            if (!pointers[e.pointerId]) return;
            var prev = pointers[e.pointerId];
            var dx = e.clientX - prev.x, dy = e.clientY - prev.y;
            pointers[e.pointerId] = { x: e.clientX, y: e.clientY };

            if (gesture && gesture.type === 'pan') {
                gesture.moved += Math.abs(dx) + Math.abs(dy);
                state.view.tx += dx; state.view.ty += dy;
                applyView();
            } else if (gesture && gesture.type === 'pinch') {
                var ids = Object.keys(pointers);
                if (ids.length >= 2) {
                    var a = pointers[ids[0]], b = pointers[ids[1]];
                    var d = dist2d(a, b);
                    var cx = (a.x + b.x) / 2, cy = (a.y + b.y) / 2;
                    var ns = Math.min(MAX_S, Math.max(MIN_S, gesture.s0 * (d / Math.max(1, gesture.d0))));
                    var k = ns / state.view.s;
                    state.view.tx = (cx - mapRect.left) - ((cx - mapRect.left) - state.view.tx) * k;
                    state.view.ty = (cy - mapRect.top) - ((cy - mapRect.top) - state.view.ty) * k;
                    state.view.s = ns;
                    applyView();
                }
            }
        });

        function endPointer(e) {
            if (!pointers[e.pointerId]) return;
            delete pointers[e.pointerId];
            if (gesture && gesture.type === 'pan') {
                var dt = Date.now() - gesture.t0;
                if (gesture.moved < 12 && dt < 600) handleTap(e.clientX, e.clientY);
            }
            var ids = Object.keys(pointers);
            if (ids.length === 1) {
                var p = pointers[ids[0]];
                gesture = { type: 'pan', x0: p.x, y0: p.y, moved: 999, t0: Date.now() };
            } else if (ids.length === 0) {
                gesture = null;
            }
        }
        map.addEventListener('pointerup', endPointer);
        map.addEventListener('pointercancel', endPointer);

        map.addEventListener('wheel', function (e) {
            var f = e.deltaY < 0 ? 1.2 : 1 / 1.2;
            zoomAround(e.clientX, e.clientY, f);
            e.preventDefault();
        });

        // double tap zoom
        var lastTap = { t: 0, x: 0, y: 0 };
        map.addEventListener('pointerup', function (e) {
            var now = Date.now();
            if (now - lastTap.t < 350 &&
                Math.abs(e.clientX - lastTap.x) < 40 && Math.abs(e.clientY - lastTap.y) < 40) {
                zoomAround(e.clientX, e.clientY, 1.8);
                lastTap.t = 0;
            } else {
                lastTap = { t: now, x: e.clientX, y: e.clientY };
            }
        });
    }

    function dist2d(a, b) {
        var dx = a.x - b.x, dy = a.y - b.y;
        return Math.sqrt(dx * dx + dy * dy);
    }

    function handleTap(px, py) {
        if (!state.net) return;
        var p = screenToScheme(px, py);
        var st;
        if (!state.showingDet && state.handles.A) {
            // overview layer active: mini coords -> detailed coords
            p = { x: state.handles.A.x + p.x * 2, y: state.handles.A.y + p.y * 2 };
            st = nearestStation(p.x, p.y, 13 / state.view.s + 6);
        } else {
            st = nearestStation(p.x, p.y, 26 / state.view.s + 8);
        }
        if (st) openStationSheet(st);
    }

    function nearestStation(x, y, radius) {
        var best = null, bestD = radius * radius;
        var stations = state.net.stations;
        for (var i = 0; i < stations.length; i++) {
            var st = stations[i];
            if (!st.hasPos) continue;
            var dx = st.x - x, dy = st.y - y;
            var d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = st; }
        }
        return best;
    }

    /* ---------------- route overlay ---------------- */

    /* station position in the coordinate space of the VISIBLE map layer */
    function routePt(st) {
        if (!state.showingDet && state.handles.A) {
            return { x: state.handles.A.x + st.x / 2, y: state.handles.A.y + st.y / 2 };
        }
        return { x: st.x, y: st.y };
    }

    /* dim everything except the route (like the real app) */
    function setDim(on) {
        var op = on ? '0.16' : '';
        ['layerDet', 'labelsDet', 'layerMini', 'labelsMini'].forEach(function (k) {
            if (state.handles[k]) state.handles[k].style.opacity = op;
        });
    }

    function clearOverlay() {
        var g = $('overlay');
        while (g.firstChild) g.removeChild(g.firstChild);
        var sel = $('selection');
        while (sel.firstChild) sel.removeChild(sel.firstChild);
        setDim(false);
    }

    function routeBbox(legs) {
        var x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
        legs.forEach(function (leg) {
            leg.nodes.forEach(function (n) {
                var st = n.station;
                if (!st.hasPos) return;
                var p = routePt(st);
                if (p.x < x0) x0 = p.x; if (p.x > x1) x1 = p.x;
                if (p.y < y0) y0 = p.y; if (p.y > y1) y1 = p.y;
            });
        });
        return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
    }

    function drawRoute(r) {
        clearOverlay();
        setDim(true);
        var overlay = $('overlay');
        var selection = $('selection');

        r.legs.forEach(function (leg) {
            var pts = leg.nodes.map(function (n) {
                if (!n.station.hasPos) return null;
                var p = routePt(n.station);
                return p.x + ',' + p.y;
            }).filter(function (p) { return p !== null; });
            if (pts.length < 1) return;

            if (leg.isTransfer) {
                overlay.appendChild(MetroScheme.el('polyline', {
                    points: pts.join(' '), fill: 'none',
                    stroke: '#8a9096', 'stroke-width': 5,
                    'stroke-dasharray': '1 9', 'stroke-linecap': 'round', 'stroke-linejoin': 'round'
                }));
            } else {
                var line = state.net.lines[leg.lineId];
                var color = (line && line.color) || '#5a6167';
                overlay.appendChild(MetroScheme.el('polyline', {
                    points: pts.join(' '), fill: 'none',
                    stroke: '#ffffff', 'stroke-width': 15, 'stroke-linecap': 'round', 'stroke-linejoin': 'round'
                }));
                overlay.appendChild(MetroScheme.el('polyline', {
                    points: pts.join(' '), fill: 'none',
                    stroke: color, 'stroke-width': 9, 'stroke-linecap': 'round', 'stroke-linejoin': 'round'
                }));
            }
        });

        // bright labels for every station along the route (halo pass, then text pass)
        var seen = {};
        var names = [];
        r.legs.forEach(function (leg) {
            leg.nodes.forEach(function (n) {
                var st = n.station;
                if (!st.hasPos || seen[st.id]) return;
                seen[st.id] = true;
                names.push({ p: routePt(st), name: st.name });
            });
        });
        names.forEach(function (o) {
            var halo = MetroScheme.el('text', {
                x: o.p.x, y: o.p.y - 13, 'text-anchor': 'middle',
                'font-size': 13, fill: '#ffffff', stroke: '#ffffff', 'stroke-width': 3,
                'stroke-linejoin': 'round', 'font-weight': '600'
            }, overlay);
            halo.textContent = o.name;
        });
        names.forEach(function (o) {
            var t = MetroScheme.el('text', {
                x: o.p.x, y: o.p.y - 13, 'text-anchor': 'middle',
                'font-size': 13, fill: '#12161a', 'font-weight': '600'
            }, overlay);
            t.textContent = o.name;
        });

        // endpoint markers A/B
        [state.from, state.to].forEach(function (st, idx) {
            if (!st || !st.hasPos) return;
            var p = routePt(st);
            var color = idx === 0 ? '#2b7cff' : '#e02b2b';
            selection.appendChild(MetroScheme.el('circle', {
                cx: p.x, cy: p.y, r: 10, fill: color, stroke: '#ffffff', 'stroke-width': 3
            }));
            var letter = MetroScheme.el('text', {
                x: p.x, y: p.y + 4, 'text-anchor': 'middle',
                'font-size': 12, fill: '#ffffff', 'font-weight': '700'
            }, selection);
            letter.textContent = idx === 0 ? '\u0410' : '\u0411';
        });
    }

    /* ---------------- UI: chips, sheets, cards ---------------- */

    function lineDotsHtml(st) {
        return (st.lineIds || []).slice(0, 4).map(function (lid) {
            var l = state.net.lines[lid];
            return '<span class="dot" style="background:' + ((l && l.color) || '#999') + '"></span>';
        }).join('');
    }

    function setChip(which, st) {
        var chip = which === 'from' ? $('chipFrom') : $('chipTo');
        var text = chip.querySelector('.chip-text');
        var marker = chip.querySelector('.chip-mark');
        if (st) {
            text.textContent = st.name;
            chip.classList.add('filled');
            if (marker) {
                marker.style.display = '';
                if (which === 'from') {
                    var l = st.lineIds && st.lineIds.length ? state.net.lines[st.lineIds[0]] : null;
                    marker.style.background = (l && l.color) || '#999';
                }
            }
        } else {
            text.textContent = which === 'from' ? 'Откуда' : 'До:';
            chip.classList.remove('filled');
            if (marker) marker.style.display = 'none';
        }
    }

    function setFrom(st) {
        state.from = st; setChip('from', st); updateRoute();
    }
    function setTo(st) {
        state.to = st; setChip('to', st); updateRoute();
    }

    function updateRoute() {
        persistSelection();
        if (!state.from || !state.to) {
            clearOverlay();
            $('routeCard').classList.add('hidden');
            state.routeFitScale = null;
            return;
        }
        var r = Metro.route(state.net, state.from, state.to);
        state.route = r;
        if (!r) {
            $('routeLegs').innerHTML = '<div class="muted">Маршрут не найден</div>';
            $('routeTotal').textContent = '';
            $('routeCard').classList.remove('hidden');
            clearOverlay();
            return;
        }
        drawRoute(r);
        renderRouteCard(r);
        fitRoute(routeBbox(r.legs));
    }

    function fmtTime(sec) {
        var m = Math.round(sec / 60);
        return m + ' мин';
    }

    function renderRouteCard(r) {
        var html = '';
        r.legs.forEach(function (leg, i) {
            if (leg.isTransfer) {
                html += '<div class="leg transfer"><span class="walk">⇢</span> Пересадка · ' +
                    fmtTime(leg.seconds) + '</div>';
            } else {
                var line = state.net.lines[leg.lineId];
                var color = (line && line.color) || '#888';
                var name = (line && (line.name || ('Линия ' + line.shortName))) || 'Линия';
                var stops = Math.max(0, leg.nodes.length - 1);
                var first = leg.nodes[0] ? leg.nodes[0].station.name : '';
                var last = leg.nodes.length ? leg.nodes[leg.nodes.length - 1].station.name : '';
                html += '<div class="leg"><span class="bar" style="background:' + color + '"></span>' +
                    '<div><div class="leg-line" style="color:' + color + '">' + name + '</div>' +
                    '<div class="leg-dirs">' + first + ' → ' + last + '</div>' +
                    '<div class="muted">' + stops + ' ст. · ' + fmtTime(leg.seconds) + '</div></div></div>';
            }
        });
        $('routeLegs').innerHTML = html;
        $('routeTotal').textContent = fmtTime(r.totalSeconds);
        $('routeCard').classList.remove('hidden');
    }

    /* ---------------- sheets ---------------- */

    function closeSheets() {
        ['searchSheet', 'stationSheet'].forEach(function (id) {
            $(id).classList.add('hidden');
        });
        state.activeSheet = null;
    }

    function openSearchSheet(target) {
        state.editTarget = target;
        var sheet = $('searchSheet');
        sheet.classList.remove('hidden');
        state.activeSheet = 'search';
        var input = $('searchInput');
        input.value = '';
        renderSearchResults('');
        setTimeout(function () { input.focus(); }, 50);
    }

    function renderSearchResults(q) {
        var list = $('searchResults');
        var items = Metro.searchStations(state.net, q, 30);
        var html = '';
        items.forEach(function (st) {
            html += '<div class="result" data-sid="' + st.id + '">' +
                lineDotsHtml(st) + '<span class="rname">' + escapeHtml(st.name) + '</span></div>';
        });
        list.innerHTML = html || '<div class="muted" style="padding:12px">Ничего не найдено</div>';
    }

    function escapeHtml(s) {
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    /* ---------------- geolocation: nearest station ---------------- */

    function distGeoKm(a, b) {
        var R = 6371, toRad = Math.PI / 180;
        var dLat = (b.lat - a.lat) * toRad, dLon = (b.lon - a.lon) * toRad;
        var s = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(a.lat * toRad) * Math.cos(b.lat * toRad) * Math.sin(dLon / 2) * Math.sin(dLon / 2);
        return R * 2 * Math.atan2(Math.sqrt(s), Math.sqrt(1 - s));
    }

    function nearestStationGeo(lat, lon) {
        var best = null, bestD = 1e12;
        var stations = state.net.stations;
        for (var i = 0; i < stations.length; i++) {
            var st = stations[i];
            if (st.lat === null) continue;
            var d = distGeoKm({ lat: lat, lon: lon }, { lat: st.lat, lon: st.lon });
            if (d < bestD) { bestD = d; best = st; }
        }
        return best;
    }

    /* pre-fills the route start with the closest station. User-triggered only:
     * geolocation at launch crashes WWAHost on W10M sideloaded apps. */
    function locateNearest(target) {
        target = target || 'from';
        if (target === 'from' && state.from) return;
        var finished = false;
        function apply(pos) {
            if (finished) return;
            finished = true;
            try {
                var st = nearestStationGeo(pos.coords.latitude, pos.coords.longitude);
                if (!st || st.lat === null) return;
                if (target === 'to') { setTo(st); } else { setFrom(st); }
                persistSelection();
                updateRoute();
            } catch (e) { }
        }
        function fail() { finished = true; }
        try {
            if (window.Windows && Windows.Devices && Windows.Devices.Geolocation) {
                var loc = new Windows.Devices.Geolocation.Geolocator();
                loc.desiredAccuracy = Windows.Devices.Geolocation.PositionAccuracy.Default;
                loc.getGeopositionAsync().then(apply, function () { finished = true; });
            } else if (navigator.geolocation) {
                navigator.geolocation.getCurrentPosition(apply, function () { finished = true; },
                    { timeout: 9000, maximumAge: 120000, enableHighAccuracy: false });
            }
        } catch (e) { finished = true; }
    }

    /* ---------------- init ---------------- */

    function openStationSheet(st) {
        var sheet = $('stationSheet');
        var lineNames = (st.lineIds || []).map(function (lid) {
            var l = state.net.lines[lid];
            if (!l) return '';
            return '<span class="line-badge" style="background:' + l.color + '">' + escapeHtml(l.shortName || '') + '</span>';
        }).join(' ');
        $('stTitle').textContent = st.name;
        $('stLines').innerHTML = lineNames;
        sheet.classList.remove('hidden');
        state.activeSheet = 'station';
        state.sheetStation = st;
    }

    function persistSelection() {
        try {
            localStorage.setItem('mm.city', state.city);
            localStorage.setItem('mm.from.' + state.city, state.from ? state.from.name : '');
            localStorage.setItem('mm.to.' + state.city, state.to ? state.to.name : '');
        } catch (e) { /* private mode etc. */ }
    }

    function restoreSelection() {
        var fromName = null, toName = null;
        try {
            fromName = localStorage.getItem('mm.from.' + state.city);
            toName = localStorage.getItem('mm.to.' + state.city);
        } catch (e) { }
        if (fromName) {
            var f = Metro.searchStations(state.net, fromName, 1)[0];
            if (f && f.name === fromName) { state.from = f; setChip('from', f); }
        }
        if (toName) {
            var t = Metro.searchStations(state.net, toName, 1)[0];
            if (t && t.name === toName) { state.to = t; setChip('to', t); }
        }
        if (state.from && state.to) updateRoute();
    }

    /* ---------------- init ---------------- */

    function renderCitySelect() {
        var sel = $('citySelect');
        if (!sel) return;
        // never innerHTML a <select>: legacy EdgeHTML crashes WWAHost on it
        while (sel.options.length) sel.remove(0);
        CITIES.forEach(function (c) {
            var o = new Option(c.name, c.id, c.id === state.city, c.id === state.city);
            sel.add(o);
        });
    }

    function init() {
        // surface unexpected errors on screen (no dev console on the phone)
        window.onerror = function (msg, src, line, col) {
            var elTxt = $('loadingText');
            $('loading').classList.remove('hidden');
            elTxt.style.color = '#da2128';
            elTxt.textContent = 'Ошибка: ' + msg + ' @' + (line || '?') + ':' + (col || '?');
            return false;
        };

        // theme
        var theme = null;
        try { theme = localStorage.getItem('mm.theme'); } catch (e) { }
        if (theme === 'dark') document.body.classList.add('dark');
        $('themeBtn').addEventListener('click', function () {
            var dark = !document.body.classList.contains('dark');
            document.body.classList.toggle('dark', dark);
            this.innerHTML = dark ? '\u2600' : '\u263E';
            try { localStorage.setItem('mm.theme', dark ? 'dark' : 'light'); } catch (e) { }
        });
        if (theme === 'dark') $('themeBtn').innerHTML = '\u2600';

        // restore city
        var saved = null;
        try { saved = localStorage.getItem('mm.city'); } catch (e) { }
        var cityId = saved || CITIES[0].id;

        renderCitySelect();

        $('citySelect').addEventListener('change', function () {
            if (this.value === state.city) return;
            switchCity(this.value);
        });

        bindMapEvents();

        $('chipFrom').addEventListener('click', function (e) {
            if (closestClass(e.target, 'chip-x')) { setFrom(null); return; }
            openSearchSheet('from');
        });
        $('chipTo').addEventListener('click', function (e) {
            if (closestClass(e.target, 'chip-x')) { setTo(null); return; }
            openSearchSheet('to');
        });
        $('swapBtn').addEventListener('click', function () {
            var f = state.from; state.from = state.to; state.to = f;
            setChip('from', state.from); setChip('to', state.to);
            updateRoute();
        });

        $('searchInput').addEventListener('input', function () {
            renderSearchResults(this.value);
        });
        $('searchBack').addEventListener('click', closeSheets);
        $('locBtn').addEventListener('click', function () {
            var target = state.editTarget || 'from';
            $('searchResults').innerHTML = '<div class="muted" style="padding:12px">Определяю расположение…</div>';
            locateNearest(target);
            setTimeout(closeSheets, 6000);
        });
        $('searchResults').addEventListener('click', function (e) {
            var item = closestClass(e.target, 'result');
            if (!item) return;
            var st = state.net.stationsById[item.getAttribute('data-sid')];
            if (!st) return;
            if (state.editTarget === 'from') setFrom(st); else setTo(st);
            closeSheets();
        });

        $('stClose').addEventListener('click', closeSheets);
        $('stFrom').addEventListener('click', function () {
            if (state.sheetStation) setFrom(state.sheetStation);
            closeSheets();
        });
        $('stTo').addEventListener('click', function () {
            if (state.sheetStation) setTo(state.sheetStation);
            closeSheets();
        });

        $('routeClose').addEventListener('click', function () {
            setFrom(null); setTo(null);
        });

        switchCity(cityId, true);
    }

    function switchCity(cityId, first) {
        $('loading').classList.remove('hidden');
        $('loadingText').textContent = 'Загрузка схемы…';
        loadCity(cityId, function (err) {
            if (err) {
                $('loading').classList.add('hidden');
                $('loadingText').textContent = 'Ошибка загрузки: ' + err.message;
                return;
            }
            renderCitySelect();
            var svg = $('scheme');
            state.handles = MetroScheme.renderScheme(state.net, state.sd, svg);

            // overlay + selection layers: above geometry, below station labels
            var vp = state.handles.viewport;
            var overlay = MetroScheme.el('g', { id: 'overlay' });
            var selection = MetroScheme.el('g', { id: 'selection' });
            vp.insertBefore(overlay, state.handles.labelsDet);
            vp.insertBefore(selection, state.handles.labelsDet);

            // initial view after layout settles (flex sizes may not be final yet)
            requestAnimationFrame(function () {
                refreshRect();
                // reset per-city UI (chips/card may hold the previous city's state)
                setChip('from', null);
                setChip('to', null);
                $('routeCard').classList.add('hidden');

                var meta = state.net.view;
                if (meta && meta.center && meta.scale) {
                    state.view.s = meta.scale.initial || 2;
                    state.view.tx = mapRect.w / 2 - state.view.s * meta.center.x;
                    state.view.ty = mapRect.h / 2 - state.view.s * meta.center.y;
                } else {
                    state.view.s = Math.max(MIN_S, Math.min(
                        mapRect.w / state.net.size.width, mapRect.h / state.net.size.height));
                    state.view.tx = (mapRect.w - state.view.s * state.net.size.width) / 2;
                    state.view.ty = (mapRect.h - state.view.s * state.net.size.height) / 2;
                }
                applyViewNow();
                restoreSelection();
                // deferred: auto-fill the route start with the nearest station
                $('loading').classList.add('hidden');
            });
        });
    }

    document.addEventListener('DOMContentLoaded', init);

    // QA hook (desktop testing): window.__mm.state / .net
    window.__mm = state;
})();
