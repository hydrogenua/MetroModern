using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace MetroCore
{
    public abstract class SvgElement
    {
        public string Id;
        public double Opacity = 1;
    }

    public sealed class SvgLine : SvgElement
    {
        public double X1, Y1, X2, Y2, StrokeWidth = 1;
        public string Stroke, LineCap;
    }

    public sealed class SvgCircle : SvgElement
    {
        public double Cx, Cy, R = 1;
        public string Fill;
    }

    public sealed class SvgText : SvgElement
    {
        public double X, Y, FontSize = 10;
        public string Font, Color, Anchor, Content;
    }

    public sealed class SvgPath : SvgElement
    {
        public string Data;   // raw svg "d", passthrough for exporters
        public string Stroke, Fill, LineCap, LineJoin;
        public double StrokeWidth = 1;
    }

    public sealed class SvgImage : SvgElement
    {
        public double X, Y;
        public string Resource; // icon key, may require resources endpoint
        public string Transform;
    }

    public sealed class SvgGroup
    {
        public string Id;
        public double ZoomH; // heuristic zoom bucket
        public List<SvgElement> Elements = new List<SvgElement>();
    }

    public sealed class SvgScheme
    {
        public double Width, Height;
        public List<SvgGroup> Groups = new List<SvgGroup>();
    }

    public static class SvgSchemeLoader
    {
        public static SvgScheme FromRaw(JsonElement graphics, double width, double height)
        {
            var scheme = new SvgScheme { Width = width, Height = height };
            if (graphics.ValueKind != JsonValueKind.Object) return scheme;

            foreach (var groupProp in graphics.EnumerateObject())
            {
                var group = new SvgGroup { Id = groupProp.Name };
                if (groupProp.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in groupProp.Value.EnumerateArray())
                    {
                        SvgElement built = BuildElement(el);
                        if (built != null) group.Elements.Add(built);
                    }
                }
                scheme.Groups.Add(group);
            }
            return scheme;
        }

        private static SvgElement BuildElement(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            string type = null;
            if (el.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String) type = t.GetString();
            if (type == null) return null;

            switch (type)
            {
                case "Line":
                    {
                        var e = new SvgLine { Id = Str(el, "id") };
                        e.X1 = Num(el, "x1"); e.Y1 = Num(el, "y1"); e.X2 = Num(el, "x2"); e.Y2 = Num(el, "y2");
                        e.Stroke = Str(el, "stroke"); e.StrokeWidth = Num(el, "strokeWidth"); e.LineCap = Str(el, "strokeLinecap");
                        e.Opacity = Num(el, "opacity", 1); return e;
                    }
                case "Circle":
                    {
                        var e = new SvgCircle { Id = Str(el, "id"), Fill = Str(el, "fill") };
                        e.Cx = Num(el, "x"); e.Cy = Num(el, "y"); e.R = Num(el, "r");
                        // NOTE: circular station glyphs carry x/y (not cx/cy) in this data; cx/cy usually 0
                        e.Opacity = Num(el, "opacity", 1); return e;
                    }
                case "Text":
                    {
                        var e = new SvgText { Id = Str(el, "id"), Color = Str(el, "color"), Anchor = Str(el, "textAnchor"), Content = Str(el, "text") };
                        e.X = Num(el, "x"); e.Y = Num(el, "y"); e.FontSize = Num(el, "fontSize");
                        e.Font = Str2(el, "fontWeight");
                        e.Opacity = Num(el, "opacity", 1); return e;
                    }
                case "Path":
                    {
                        var e = new SvgPath { Id = Str(el, "id"), Data = Str(el, "d"), Stroke = Str(el, "stroke"), Fill = Str(el, "fill"), LineCap = Str(el, "strokeLinecap"), LineJoin = Str(el, "strokeLinejoin") };
                        e.StrokeWidth = Num(el, "strokeWidth");
                        e.Opacity = Num(el, "opacity", 1); return e;
                    }
                case "SVGImage":
                    {
                        var e = new SvgImage { Id = Str(el, "id"), Resource = Str(el, "resource"), Transform = Str(el, "transform") };
                        e.X = Num(el, "x"); e.Y = Num(el, "y");
                        e.Opacity = Num(el, "opacity", 1); return e;
                    }
                default:
                    return null;
            }
        }

        private static string Str(JsonElement el, string prop)
        {
            JsonElement v;
            if (el.TryGetProperty(prop, out v) && v.ValueKind == JsonValueKind.String) return v.GetString();
            return null;
        }

        private static string Str2(JsonElement el, string prop) => Str(el, prop);

        private static double Num(JsonElement el, string prop, double fallback = 0)
        {
            JsonElement v;
            if (el.TryGetProperty(prop, out v))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
                if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds)) return ds;
            }
            return fallback; // absent opacity must default to visible (1), not 0
        }
    }
}
