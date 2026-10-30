using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MetroCore
{
    /// <summary>Exports an SvgScheme into a standalone SVG asset (desktop preview / QA).</summary>
    public static class SvgExporter
    {
        public static void Export(SvgScheme scheme, StringBuilder sb, string viewBox = null)
        {
            sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"" +
                (viewBox ?? ("0 0 " + scheme.Width + " " + scheme.Height)) + "\" font-family=\"Arial\" style=\"background:#ffffff\">");
            double[] vb;
            if (viewBox != null && (vb = viewBox.Split(' ').Select(s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray()).Length == 4)
                sb.AppendLine($"<rect x=\"{vb[0]}\" y=\"{vb[1]}\" width=\"{vb[2]}\" height=\"{vb[3]}\" fill=\"#ffffff\"/>");

            foreach (var g in scheme.Groups)
            {
                foreach (var el in g.Elements)
                {
                    if (el.Opacity <= 0.001) continue; // state elements toggled later (closed/warning/selected)
                    if (el is SvgLine l)
                    {
                        sb.Append("<line x1=\"").Append(R(l.X1)).Append("\" y1=\"").Append(R(l.Y1))
                           .Append("\" x2=\"").Append(R(l.X2)).Append("\" y2=\"").Append(R(l.Y2))
                           .Append("\" stroke=\"").Append(l.Stroke ?? "#000").Append("\" stroke-width=\"").Append(R(l.StrokeWidth));
                        if (el is SvgLine && l.LineCap != null) sb.Append("\" stroke-linecap=\"").Append(l.LineCap);
                        sb.Append("\"/>").AppendLine();
                    }
                    else if (el is SvgPath p)
                    {
                        sb.Append("<path d=\"").Append(XmlEscape(p.Data ?? ""))
                           .Append("\" fill=\"").Append(p.Fill ?? "none")
                           .Append("\" stroke=\"").Append(p.Stroke ?? "none")
                           .Append("\" stroke-width=\"").Append(R(p.StrokeWidth));
                        if (p.LineCap != null) sb.Append("\" stroke-linecap=\"").Append(p.LineCap);
                        if (p.LineJoin != null) sb.Append("\" stroke-linejoin=\"").Append(p.LineJoin);
                        sb.Append("\"/>").AppendLine();
                    }
                    else if (el is SvgCircle c)
                    {
                        sb.Append("<circle cx=\"").Append(R(c.Cx)).Append("\" cy=\"").Append(R(c.Cy))
                           .Append("\" r=\"").Append(R(c.R)).Append("\" fill=\"").Append(c.Fill ?? "#000")
                           .Append("\"/>").AppendLine();
                    }
                    else if (el is SvgText tx && tx.Content != null)
                    {
                        var anchor = tx.Anchor ?? "start";
                        sb.Append("<text x=\"").Append(R(tx.X)).Append("\" y=\"").Append(R(tx.Y))
                           .Append("\" font-size=\"").Append(R(tx.FontSize > 0 ? tx.FontSize : 10))
                           .Append("\" text-anchor=\"").Append(anchor)
                           .Append("\" fill=\"").Append(tx.Color ?? "#000");
                        if (!string.IsNullOrEmpty(tx.Font)) sb.Append("\" font-weight=\"").Append(tx.Font);
                        sb.Append("\">").Append(XmlEscape(tx.Content)).Append("</text>").AppendLine();
                    }
                    else if (el is SvgImage img)
                    {
                        // placeholder: 6x6 gray tile + cross; real SVG resources come from unused endpoint (v2)
                        sb.Append("<g transform=\"translate(").Append(R(img.X)).Append(' ').Append(R(img.Y)).Append(')');
                        if (img.Transform != null) sb.Append(' ').Append(XmlEscape(img.Transform));
                        sb.Append("\">")
                          .Append("<rect x=\"-3\" y=\"-3\" width=\"6\" height=\"6\" fill=\"#cccccc\" stroke=\"#999999\"/>")
                          .AppendLine("</g>");
                    }
                }
            }

            sb.AppendLine("</svg>");
        }

        private static string R(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);

        private static string XmlEscape(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}


