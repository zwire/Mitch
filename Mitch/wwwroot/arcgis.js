///
/// ArcGIS API Wrapper
///


// ---------- fields ---------- //

let _center = [141.35, 43.07];
let _zoom = 16;
let _pointGraphics = {};
let _lineGraphics = {};
let arc_clear = null;
let arc_wtog = null;
let arc_gtow = null;
let arc_format = null;
let arc_makePoly = null;
let arc_createPoints = null;
let arc_createLine = null;
let arc_setCenter = null;
const _app = 'Mitch';
const notify = () => DotNet.invokeMethodAsync(_app, 'Notify');

function clearMap() {

    _pointGraphics = {};
    _lineGraphics = {};
    arc_clear();
    notify();

}

function setPath(points, code, color) {

    const lonlats = [];
    for (let j = 0; j < points.length; j++) {
        const point = points[j];
        const lat = point.latitude;
        const lon = point.longitude;
        lonlats.push([lon, lat]);
    }
    const line = arc_makePoly(lonlats);
    arc_createLine(line, code, color);
    arc_createPoints(line, code, color);
    notify();

}

function removePath(code) {

    arc_remove(code);
    notify();

}

function setCenter(lon, lat) {

    arc_setCenter(lon, lat);
    notify();

}


// ---------- constructor ---------- //

function initialize() {

    require([
        "esri/Map",
        "esri/Graphic",
        "esri/layers/GraphicsLayer",
        "esri/views/MapView",
        "esri/widgets/BasemapToggle",
        "esri/geometry/Polyline",
        "esri/geometry/support/webMercatorUtils",
        "esri/widgets/Search",
        "esri/geometry/coordinateFormatter",
        "esri/widgets/Fullscreen",
    ], (Map, Graphic, GraphicsLayer, MapView, BasemapToggle, Polyline, webMercatorUtils, Search, coordinateFormatter, Fullscreen) => {

        coordinateFormatter.load();

        // ---------- register public functions ---------- //

        arc_clear = () => view.graphics.removeAll();

        arc_wtog = w => webMercatorUtils.webMercatorToGeographic(w);

        arc_gtow = g => webMercatorUtils.geographicToWebMercator(g);

        arc_makePoly = p => new Polyline({ paths: p });

        arc_format = (lat_dmm, lon_dmm) => {
            const str_lat = lat_dmm.split(".");
            const d_lat = Math.floor(Number(str_lat[0]) / 100);
            const m_lat = str_lat[0].substr(-2) + "." + str_lat[1];
            const input_lat = d_lat + "~" + m_lat + "'";
            const str_lon = lon_dmm.split(".");
            const d_lon = Math.floor(Number(str_lon[0]) / 100);
            const m_lon = str_lon[0].substr(-2) + "." + str_lon[1];
            const input_lon = d_lon + "~" + m_lon + "'";
            const input = input_lat + "|" + input_lon;
            const result = coordinateFormatter.fromLatitudeLongitude(input);
            return [result.longitude, result.latitude];
        }

        arc_createPoints = (line, code, color) => {
            const simpleMarkerSymbol = {
                type: "simple-marker",
                color: color,
                outline: {
                    color: color,
                    width: 0.1
                }
            };
            const simpleMarkerSymbolS = {
                type: "simple-marker",
                color: [50, 50, 50],
                width: 0.1,
                outline: {
                    color: color,
                    width: 0.1
                }
            };
            _pointGraphics[code] = [];
            const path = line.paths[0];
            for (let j = 0; j < path.length; j++) {
                const point = {
                    type: "point",
                    longitude: path[j][0],
                    latitude: path[j][1]
                };
                if (j == 0) {
                    const pointGraphic = new Graphic({
                        layer: graphicsLayer,
                        geometry: point,
                        symbol: simpleMarkerSymbolS,
                    });
                    _pointGraphics[code].push(pointGraphic);
                    view.graphics.add(pointGraphic);
                }
                else {
                    const pointGraphic = new Graphic({
                        layer: graphicsLayer,
                        geometry: point,
                        symbol: simpleMarkerSymbol,
                    });
                    _pointGraphics[code].push(pointGraphic);
                    view.graphics.add(pointGraphic);
                }
            }
        }

        arc_createLine = (line, code, color) => {
            const polylineSymbol = {
                type: "simple-line",
                color: color,
                width: 2,
            }
            const polylineGraphic = new Graphic({
                layer: graphicsLayer,
                geometry: line,
                symbol: polylineSymbol,
            });
            _lineGraphics[code] = polylineGraphic;
            view.graphics.add(polylineGraphic);
        }


        arc_remove = code => {
            if (_pointGraphics[code])
                for (let i = 0; i < _pointGraphics[code].length; i++)
                    view.graphics.remove(_pointGraphics[code][i]);
            if (_lineGraphics[code])
                view.graphics.remove(_lineGraphics[code]);
        }

        arc_setCenter = (lon, lat) => {
            _center = [lon, lat];
            view.center = _center;
        }

        arc_setZoom = zoom => {
            view.zoom = zoom;
        }

        // ---------- register private functions ---------- //

        const graphicsLayer = new GraphicsLayer();

        const map = new Map({
            basemap: "topo-vector",
            layers: [graphicsLayer]
        });

        const view = new MapView({
            container: "viewDiv",
            map: map,
            zoom: _zoom,
            center: _center
        });

        const toggle = new BasemapToggle({
            view: view,
            nextBasemap: "satellite"
        });

        const searchWidget = new Search({ view: view });

        const fullscreen = new Fullscreen({ view: view });

        view.when(() => {
            view.ui.add(fullscreen, "bottom-right");
            view.ui.add(toggle, "bottom-right");
            view.ui.add(searchWidget, "top-right");
        });

        view.on("blur", function (e) {
            _center = view.center;
        });

        view.on("mouse-wheel", function (e) {
            _zoom = view.zoom;
        });

    });

}