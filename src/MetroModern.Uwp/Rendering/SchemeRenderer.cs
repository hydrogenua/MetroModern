using System;
using System.Collections.Generic;
using System.Globalization;
using MetroCore;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace MetroModern.Uwp.Rendering
{
    /// <summary>
    /// Draws an SvgScheme into a Canvas using native Shape objects:
    /// texts, circles, lines, paths (via SvgPathParser) and image placeholders.
    /// </summary>
    public static class SchemeRenderer
    {
        public static void Render(SvgScheme scheme, Canvas canvas, Dictionary<string, Windows.UI.Xaml.Shapes.Path> highlightStations = null)
        {
            foreach (var g in scheme.Groups)
            {
                foreach (var el in g.Elements)
                {
                    if (el.Opacity <= 0.001) continue;   // state-toggled elements (closed/warning/selected)
                    UIElement uie = Element(el);
                    if (uie == null) continue;
                    canvas.Children.Add(uie);
                }
            }
            if (highlightStations != null) highlightStations.Clear(); // player: overlay built on demand (v2)
        }

        private static UIElement Element(SvgElement el)
        {
            if (el is SvgLine l)
            {
                var sh = new Line
                {
                    X1 = l.X1, Y1 = l.Y1, X2 = l.X2, Y2 = l.Y2,
                    StrokeThickness = l.StrokeWidth,
                    Stroke = new SolidColorBrush(ParseColor(l.Stroke, Color.FromArgb(255, 0, 0, 0)))
                };
                PenLineCap cap = PenLineCap.Flat;
                if (l.LineCap == "round") cap = PenLineCap.Round;
                else if (l.LineCap == "square") cap = PenLineCap.Square;
                sh.StrokeStartLineCap = cap;
                sh.StrokeEndLineCap = cap;
                return sh;
            }

            if (el is SvgCircle c)
            {
                // data keeps the point in x/y (cx/cy are usually 0)
                var sh = new Ellipse
                {
                    Width = c.R * 2,
                    Height = c.R * 2,
                    Fill = new SolidColorBrush(ParseColor(c.Fill, Color.FromArgb(255, 0, 0, 0)))
                };
                Canvas.SetLeft(sh, c.Cx - c.R);
                Canvas.SetTop(sh, c.Cy - c.R);
                return sh;
            }

            if (el is SvgText t)
            {
                if (t.Content == null) return null;
                var blk = new TextBlock
                {
                    Text = t.Content,
                    FontSize = t.FontSize > 0 ? t.FontSize : 10,
                    FontWeight = IsBold(t.Font) ? Windows.UI.Text.FontWeights.SemiBold : Windows.UI.Text.FontWeights.Normal,
                    Foreground = new SolidColorBrush(ParseColor(t.Color, Color.FromArgb(255, 0, 0, 0))),
                    TextAlignment = TextAlignment.Left
                };
                // anchor point handling approximated: middle/end shift back
                double dx = 0;
                Canvas.SetLeft(blk, t.X - dx);
                Canvas.SetTop(blk, t.Y - t.FontSize);
                return blk;
            }

            if (el is SvgPath p)
            {
                var geometry = BuildGeometry(p.Data);
                if (geometry == null) return null;
                var sh = new Windows.UI.Xaml.Shapes.Path { Data = geometry };

                bool hasStroke = !string.IsNullOrEmpty(p.Stroke) && p.Stroke != "none";
                bool hasFill = !string.IsNullOrEmpty(p.Fill) && p.Fill != "none";

                if (hasStroke)
                {
                    sh.StrokeThickness = p.StrokeWidth;
                    sh.Stroke = new SolidColorBrush(ParseColor(p.Stroke, Color.FromArgb(255, 0, 0, 0)));
                    PenLineCap cap = PenLineCap.Flat;
                    if (p.LineCap == "round") cap = PenLineCap.Round;
                    else if (p.LineCap == "square") cap = PenLineCap.Square;
                    sh.StrokeStartLineCap = cap;
                    sh.StrokeEndLineCap = cap;
                    if (p.LineJoin == "round") sh.StrokeLineJoin = PenLineJoin.Round;
                    else if (p.LineJoin == "bevel") sh.StrokeLineJoin = PenLineJoin.Bevel;
                }
                sh.Fill = hasFill ? (Brush)new SolidColorBrush(ParseColor(p.Fill, Color.FromArgb(255, 0, 0, 0))) : null;
                return sh;
            }

            if (el is SvgImage img)
            {
                // real resource SVGs come from getSchemeResourcesBySchemeId (planned v2)
                var ph = new Rectangle
                {
                    Width = 7,
                    Height = 7,
                    Fill = new SolidColorBrush(Color.FromArgb(160, 180, 180, 180))
                };
                double dx = 0, dy = 0;
                if (img.Transform != null)
                {
                    // translate(-6 -6) style shift
                    var parts = img.Transform.Split(new[] { ' ' });
                    if (parts.Length >= 3)
                    {
                        double.TryParse(parts[1].TrimStart('-'), NumberStyles.Float, CultureInfo.InvariantCulture, out dx);
                        double.TryParse(parts[2].TrimStart('-'), NumberStyles.Float, CultureInfo.InvariantCulture, out dy);
                        dx = -dx; dy = -dy;
                    }
                }
                Canvas.SetLeft(ph, img.X + dx - 3.5);
                Canvas.SetTop(ph, img.Y + dy - 3.5);
                return ph;
            }

            return null;
        }

        private static bool IsBold(string fontWeight)
        {
            if (string.IsNullOrEmpty(fontWeight)) return false;
            if (fontWeight == "bold" || fontWeight == "700" || fontWeight == "600" || fontWeight == "500") return true;
            int n;
            if (int.TryParse(fontWeight, out n)) return n >= 500;
            return false;
        }

        internal static Geometry BuildGeometry(string d)
        {
            var figures = SvgPathParser.Parse(d);
            if (figures.Count == 0) return null;
            var geo = new PathGeometry();
            foreach (var f in figures)
            {
                if (f.Commands.Count == 0) continue;
                var pf = new Windows.UI.Xaml.Media.PathFigure { IsClosed = f.IsClosed, IsFilled = f.IsClosed };
                bool startSet = false;
                foreach (var c in f.Commands)
                {
                    switch (c.Kind)
                    {
                        case 'L':
                            if (c.Args.Length < 2) continue;
                            if (!startSet)
                            {
                                pf.StartPoint = new Point(c.Args[0], c.Args[1]);
                                startSet = true;
                            }
                            else
                            {
                                pf.Segments.Add(new LineSegment { Point = new Point(c.Args[0], c.Args[1]) });
                            }
                            break;

                        case 'C':
                            if (c.Args.Length < 6) continue;
                            if (!startSet)
                            {
                                pf.StartPoint = new Point(c.Args[0], c.Args[1]);
                                startSet = true;
                                pf.Segments.Add(new BezierSegment
                                {
                                    Point1 = new Point(c.Args[0], c.Args[1]),
                                    Point2 = new Point(c.Args[2], c.Args[3]),
                                    Point3 = new Point(c.Args[4], c.Args[5])
                                });
                            }
                            else
                            {
                                pf.Segments.Add(new BezierSegment
                                {
                                    Point1 = new Point(c.Args[0], c.Args[1]),
                                    Point2 = new Point(c.Args[2], c.Args[3]),
                                    Point3 = new Point(c.Args[4], c.Args[5])
                                });
                            }
                            break;

                        case 'Q':
                            if (c.Args.Length < 4) continue;
                            if (!startSet)
                            {
                                pf.StartPoint = new Point(c.Args[0], c.Args[1]);
                                startSet = true;
                            }
                            else
                            {
                                pf.Segments.Add(new QuadraticBezierSegment
                                {
                                    Point1 = new Point(c.Args[0], c.Args[1]),
                                    Point2 = new Point(c.Args[2], c.Args[3])
                                });
                            }
                            break;

                        case 'A':
                            // args: rx ry rot largeArc sweep ex ey
                            if (c.Args.Length < 7) continue;
                            if (!startSet)
                            {
                                // arc as first command has no start point: degenerate line to endpoint
                                pf.StartPoint = new Point(c.Args[5], c.Args[6]);
                                startSet = true;
                                break;
                            }
                            pf.Segments.Add(new ArcSegment
                            {
                                Size = new Size(Math.Abs(c.Args[0]), Math.Abs(c.Args[1])),
                                RotationAngle = c.Args[2],
                                IsLargeArc = Math.Abs(c.Args[3]) > 0.5,
                                SweepDirection = Math.Abs(c.Args[4]) > 0.5
                                    ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                                Point = new Point(c.Args[5], c.Args[6])
                            });
                            break;
                    }
                }
                if (startSet) geo.Figures.Add(pf);
            }
            return geo;
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (hex[0] != '#') return fallback;
            string rest = hex.Substring(1);
            try
            {
                if (rest.Length == 6)
                    return Color.FromArgb(255,
                        byte.Parse(rest.Substring(0, 2), NumberStyles.HexNumber),
                        byte.Parse(rest.Substring(2, 2), NumberStyles.HexNumber),
                        byte.Parse(rest.Substring(4, 2), NumberStyles.HexNumber));
                if (rest.Length == 8)
                    return Color.FromArgb(
                        byte.Parse(rest.Substring(0, 2), NumberStyles.HexNumber),
                        byte.Parse(rest.Substring(2, 2), NumberStyles.HexNumber),
                        byte.Parse(rest.Substring(4, 2), NumberStyles.HexNumber),
                        byte.Parse(rest.Substring(6, 2), NumberStyles.HexNumber));
                if (rest.Length == 3)
                {
                    // #rgb
                    Func<char, byte> d = ch => (byte)(ParseHex(ch) * 17);
                    return Color.FromArgb(255, d(rest[0]), d(rest[1]), d(rest[2]));
                }
            }
            catch { }
            return fallback;
        }

        private static int ParseHex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }
    }
}
