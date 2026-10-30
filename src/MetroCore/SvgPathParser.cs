using System;
using System.Collections.Generic;
using System.Globalization;

namespace MetroCore
{
    /// <summary>
    /// Minimal SVG path "d" parser (M L H V C S Q T A Z, absolute and relative).
    /// Produces abstract figures convertible to XAML PathGeometry or SVG.
    /// </summary>
    public static class SvgPathParser
    {
        public sealed class Command
        {
            public char Kind;                     // M L C Q A Z
            public readonly double[] Args;
            public Command(char kind, double[] args) { Kind = kind; Args = args; }
        }

        public sealed class Figure
        {
            public bool IsClosed;
            public readonly List<Command> Commands = new List<Command>();
        }

        public static List<Figure> Parse(string d)
        {
            var figures = new List<Figure>();
            if (string.IsNullOrEmpty(d)) return figures;

            var toks = Tokenize(d);
            if (toks.Count == 0) return figures;

            double cx = 0, cy = 0, sx = 0, sy = 0;
            bool started = false;
            Figure fig = null;

            char cmd = ' ';
            bool rel = false;
            int argIdx = 0, argNeed = 0;
            var argBuf = new double[8];
            double ax1 = 0, ay1 = 0;

            void NextCmd(char c, bool isRel)
            {
                cmd = c; rel = isRel;
                argIdx = 0;
                argNeed = ArgsNeeded(c);
            }

            foreach (var tok in toks)
            {
                if (tok.IsCommand) { NextCmd(tok.C, char.IsLower(tok.C)); continue; }

                if (cmd == ' ') continue;
                if (argNeed == 0) continue;

                if (argIdx >= argNeed)
                {
                    // implicit repetition: M becomes L, others repeat themselves
                    if (cmd == 'M') { cmd = 'L'; argNeed = 2; argIdx = 0; }
                    else argIdx = 0;
                }
                if (argIdx < argBuf.Length) argBuf[argIdx++] = tok.N;

                if (argIdx < argNeed) continue;

                // full command available
                double a0 = argBuf.Length > 0 ? argBuf[0] : 0;
                double a1 = argBuf.Length > 1 ? argBuf[1] : 0;
                double a2 = argBuf.Length > 2 ? argBuf[2] : 0;
                double a3 = argBuf.Length > 3 ? argBuf[3] : 0;
                double a4 = argBuf.Length > 4 ? argBuf[4] : 0;
                double a5 = argBuf.Length > 5 ? argBuf[5] : 0;
                double a6 = argBuf.Length > 6 ? argBuf[6] : 0;

                switch (cmd)
                {
                    case 'M':
                    case 'm':
                        cx = rel ? cx + a0 : a0;
                        cy = rel ? cy + a1 : a1;
                        if (!started) { sx = cx; sy = cy; started = true; }
                        fig = new Figure(); // new figure per M
                        figures.Add(fig);
                        fig.Commands.Add(new Command('L', new[] { cx, cy }));
                        // implicit repetition: M becomes L
                        cmd = 'L'; argNeed = 2; break;

                    case 'L':
                    case 'l':
                        cx = rel ? cx + a0 : a0;
                        cy = rel ? cy + a1 : a1;
                        fig = fig ?? Ensure(figures);
                        fig.Commands.Add(new Command('L', new[] { cx, cy }));
                        break;

                    case 'H':
                    case 'h':
                        cx = rel ? cx + a0 : a0;
                        fig = fig ?? Ensure(figures);
                        fig.Commands.Add(new Command('L', new[] { cx, cy }));
                        break;

                    case 'V':
                    case 'v':
                        cy = rel ? cy + a1 : a1;
                        fig = fig ?? Ensure(figures);
                        fig.Commands.Add(new Command('L', new[] { cx, cy }));
                        break;

                    case 'C':
                    case 'c':
                        {
                            double x1 = rel ? cx + a0 : a0, y1 = rel ? cy + a1 : a1;
                            double x2 = rel ? cx + a2 : a2, y2 = rel ? cy + a3 : a3;
                            double x3 = rel ? cx + a4 : a4, y3 = rel ? cy + a5 : a5;
                            cx = x3; cy = y3;
                            ax1 = x2; ay1 = y2;
                            fig = fig ?? Ensure(figures);
                            fig.Commands.Add(new Command('C', new[] { x1, y1, x2, y2, x3, y3 }));
                            break;
                        }
                    case 'S':
                    case 's':
                        {
                            double x1 = cx, y1 = cy;
                            // reflect previous C2 → S first control point
                            var prev = fig != null && fig.Commands.Count > 0 ? fig.Commands[fig.Commands.Count - 1] : null;
                            if (prev != null && (prev.Kind == 'C') && prev.Args.Length >= 6) { x1 = 2 * cx - prev.Args[2]; y1 = 2 * cy - prev.Args[3]; }
                            double x2 = rel ? cx + a0 : a0, y2 = rel ? cy + a1 : a1;
                            double x3 = rel ? cx + a2 : a2, y3 = rel ? cy + a3 : a3;
                            cx = x3; cy = y3;
                            ax1 = x2; ay1 = y2;
                            fig = fig ?? Ensure(figures);
                            fig.Commands.Add(new Command('C', new[] { x1, y1, x2, y2, x3, y3 }));
                            break;
                        }
                    case 'Q':
                    case 'q':
                        {
                            double qx = rel ? cx + a0 : a0, qy = rel ? cy + a1 : a1;
                            double ex = rel ? cx + a2 : a2, ey = rel ? cy + a3 : a3;
                            cx = ex; cy = ey;
                            ax1 = qx; ay1 = qy;
                            fig = fig ?? Ensure(figures);
                            fig.Commands.Add(new Command('Q', new[] { qx, qy, ex, ey }));
                            break;
                        }
                    case 'T':
                    case 't':
                        {
                            double qx = cx, qy = cy;
                            var prev = fig != null && fig.Commands.Count > 0 ? fig.Commands[fig.Commands.Count - 1] : null;
                            if (prev != null && (prev.Kind == 'Q' || prev.Kind == 'T') && prev.Args.Length >= 2)
                            { qx = 2 * cx - prev.Args[0]; qy = 2 * cy - prev.Args[1]; }
                            double ex = rel ? cx + a0 : a0;
                            double ey = rel ? cy + a1 : a1;
                            cx = ex; cy = ey;
                            ax1 = qx; ay1 = qy;
                            fig = fig ?? Ensure(figures);
                            fig.Commands.Add(new Command('Q', new[] { qx, qy, ex, ey }));
                            break;
                        }
                    case 'A':
                    case 'a':
                        {
                            // full SVG arc: rx ry x-axis-rotation large-arc-flag sweep-flag x y
                            double rx = a0, ry = a1, rot = a2, largeArc = a3, sweep = a4;
                            double ex = rel ? cx + a5 : a5, ey = rel ? cy + a6 : a6;
                            fig = fig ?? Ensure(figures);
                            fig.Commands.Add(new Command('A', new[] { rx, ry, rot, largeArc, sweep, ex, ey }));
                            cx = ex; cy = ey;
                            break;
                        }
                    case 'Z':
                    case 'z':
                        if (fig != null) fig.IsClosed = true;
                        fig = null;
                        cx = sx; cy = sy;
                        break;
                }

                argIdx = 0;
            }

            return figures;
        }

        private static Figure Ensure(List<Figure> figures)
        {
            if (figures.Count == 0) { var f = new Figure(); figures.Add(f); return f; }
            return figures[figures.Count - 1];
        }

        private static int ArgsNeeded(char c)
        {
            switch (c)
            {
                case 'M': case 'm': case 'L': case 'l': case 'T': case 't': return 2;
                case 'H': case 'h': case 'V': case 'v': return 1;
                case 'C': case 'c': return 6;
                case 'S': case 's': case 'Q': case 'q': return 4;
                case 'A': case 'a': return 7; // rx ry rot largeArc sweep x y
                default: return 0;
            }
        }

        private sealed class Tok
        {
            public bool IsCommand;
            public char C;
            public bool IsNumber => !IsCommand;
            public double N;
        }

        private static List<Tok> Tokenize(string d)
        {
            var l = new List<Tok>();
            int i = 0;
            while (i < d.Length)
            {
                char c = d[i];
                if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
                if (char.IsLetter(c)) { l.Add(new Tok { IsCommand = true, C = c }); i++; continue; }
                if (c == '-' || c == '+' || c == '.' || char.IsDigit(c))
                {
                    int start = i;
                    if (c == '-' || c == '+') i++;
                    while (i < d.Length && char.IsDigit(d[i])) i++;
                    if (i < d.Length && d[i] == '.')
                    {
                        i++;
                        while (i < d.Length && char.IsDigit(d[i])) i++;
                    }
                    if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
                    {
                        int save = i; i++;
                        if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
                        if (i < d.Length && char.IsDigit(d[i])) { while (i < d.Length && char.IsDigit(d[i])) i++; }
                        else i = save;
                    }
                    string s = d.Substring(start, i - start);
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                        l.Add(new Tok { N = n });
                    continue;
                }
                i++;
            }
            return l;
        }
    }
}
