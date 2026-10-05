#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false

// WallpaperChanger のアプリアイコン生成スクリプト。
// 図形（矩形・円・多角形・グラデーション）のみで描画しており、外部の画像・フォント・素材は使用していない。
//
// 使い方（.NET 10 SDK のファイルベース実行）:
//   dotnet run tools/IconGenerator/generate-icons.cs -- <出力フォルダ>
//
// 出力: A-aurora / B-monitors / C-grid / D-combined の .ico（16～256px）、256px の PNG、比較用の preview.png
// 採用: D-combined.ico を WallpaperChanger.UI/Resources/AppIcon.ico として使用している。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

string outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outDir);

var thread = new Thread(() => IconFactory.Run(outDir));
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

static class IconFactory
{
    static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    public static void Run(string outDir)
    {
        var variants = new (string Key, Action<DrawingContext, double> Draw)[]
        {
            ("A-aurora", DrawAurora),
            ("B-monitors", DrawMonitors),
            ("C-grid", DrawGrid),
            ("D-combined", DrawCombined),
        };

        foreach (var (key, draw) in variants)
        {
            var frames = IconSizes.Select(s => (Size: s, Bitmap: Render(draw, s))).ToList();
            WriteIco(Path.Combine(outDir, $"{key}.ico"), frames);
            SavePng(frames.Last().Bitmap, Path.Combine(outDir, $"{key}-256.png"));
        }

        SavePng(RenderPreview(variants), Path.Combine(outDir, "preview.png"));
        Console.WriteLine("done: " + outDir);
    }

    // ---------- common helpers ----------

    static Geometry Rounded(double x, double y, double w, double h, double r) =>
        new RectangleGeometry(new Rect(x, y, w, h), r, r);

    static LinearGradientBrush Linear(Color a, Color b, double angleDeg = 90)
    {
        double rad = angleDeg * Math.PI / 180;
        var brush = new LinearGradientBrush(a, b, new Point(0.5 - Math.Cos(rad) / 2, 0.5 - Math.Sin(rad) / 2),
            new Point(0.5 + Math.Cos(rad) / 2, 0.5 + Math.Sin(rad) / 2));
        brush.Freeze();
        return brush;
    }

    static LinearGradientBrush Stops(Point start, Point end, params (double Offset, Color Color)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        foreach (var (o, c) in stops) brush.GradientStops.Add(new GradientStop(c, o));
        brush.Freeze();
        return brush;
    }

    static SolidColorBrush Solid(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    static Geometry Poly(double s, params double[] xy)
    {
        var fig = new PathFigure { StartPoint = new Point(xy[0] * s, xy[1] * s), IsClosed = true, IsFilled = true };
        for (int i = 2; i < xy.Length; i += 2) fig.Segments.Add(new LineSegment(new Point(xy[i] * s, xy[i + 1] * s), true));
        var g = new PathGeometry([fig]); g.Freeze(); return g;
    }

    static Geometry Ribbon(double s, double y0, double thickness, double amp, double phase)
    {
        // smooth wave band across the icon
        var top = new PathFigure { StartPoint = new Point(-0.1 * s, (y0 + amp * Math.Sin(phase)) * s), IsClosed = true };
        int n = 24;
        var upper = new List<Point>(); var lower = new List<Point>();
        for (int i = 0; i <= n; i++)
        {
            double x = -0.1 + 1.2 * i / n;
            double y = y0 + amp * Math.Sin(phase + x * 5.2) + 0.05 * Math.Sin(x * 11 + phase);
            double t = thickness * (0.55 + 0.45 * Math.Sin(x * 3.1 + phase * 0.7));
            upper.Add(new Point(x * s, y * s));
            lower.Add(new Point(x * s, (y + t) * s));
        }
        top.StartPoint = upper[0];
        top.Segments.Add(new PolyBezierSegment(CatmullRom(upper), true));
        lower.Reverse();
        top.Segments.Add(new LineSegment(lower[0], true));
        top.Segments.Add(new PolyBezierSegment(CatmullRom(lower), true));
        var g = new PathGeometry([top]); g.Freeze(); return g;
    }

    /// <summary>Catmull-Rom spline through the points, as cubic bezier control triplets (excluding the start point).</summary>
    static IEnumerable<Point> CatmullRom(List<Point> pts)
    {
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Point p0 = pts[Math.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.Min(pts.Count - 1, i + 2)];
            yield return p1 + (p2 - p0) / 6;
            yield return p2 - (p3 - p1) / 6;
            yield return p2;
        }
    }

    // ---------- A: Aurora ----------

    static void DrawAurora(DrawingContext dc, double s)
    {
        bool tiny = s <= 24, small = s <= 40;
        double pad = s * 0.04, r = s * 0.22;
        Geometry card = Rounded(pad, pad, s - 2 * pad, s - 2 * pad, r);

        dc.PushClip(card);
        dc.DrawGeometry(Stops(new Point(0.5, 0), new Point(0.5, 1),
            (0, C(0xFF0A1433)), (0.55, C(0xFF15275A)), (1, C(0xFF1E3D7A))), null, card);

        if (!small)
        {
            var star = Solid(C(0xCCFFFFFF));
            foreach (var (x, y, rr) in new[] { (0.22, 0.17, 0.010), (0.70, 0.13, 0.008), (0.84, 0.30, 0.007), (0.40, 0.10, 0.006), (0.12, 0.36, 0.006) })
                dc.DrawEllipse(star, null, new Point(x * s, y * s), rr * s, rr * s);
        }

        // aurora ribbons
        dc.PushOpacity(0.9);
        dc.DrawGeometry(Stops(new Point(0, 0), new Point(1, 0.3),
            (0, C(0x0040F5C8)), (0.25, C(0xE040F5C8)), (0.65, C(0xD05AB8FF)), (1, C(0x30B57CFF))), null,
            Ribbon(s, tiny ? 0.30 : 0.26, tiny ? 0.16 : 0.13, 0.07, 0.4));
        if (!tiny)
        {
            dc.DrawGeometry(Stops(new Point(0, 0), new Point(1, 0.4),
                (0, C(0x00B57CFF)), (0.4, C(0x90B57CFF)), (0.8, C(0xA040F5C8)), (1, C(0x0040F5C8))), null,
                Ribbon(s, 0.40, 0.08, 0.05, 2.1));
        }
        dc.Pop();

        // mountains
        dc.DrawGeometry(Solid(C(0xFF2B4E8C)), null, Poly(s, -0.05, 0.80, 0.22, 0.52, 0.36, 0.64, 0.58, 0.44, 0.86, 0.70, 1.05, 0.62, 1.05, 1.05, -0.05, 1.05));
        if (!small)
        {
            var snow = Solid(C(0xFFE8F1FF));
            dc.DrawGeometry(snow, null, Poly(s, 0.58, 0.44, 0.64, 0.495, 0.60, 0.485, 0.565, 0.51, 0.535, 0.475));
            dc.DrawGeometry(snow, null, Poly(s, 0.22, 0.52, 0.265, 0.56, 0.235, 0.555, 0.205, 0.575, 0.18, 0.555));
        }
        dc.DrawGeometry(Solid(C(0xFF0B1730)), null, Poly(s, -0.05, 0.90, 0.30, 0.70, 0.52, 0.82, 0.74, 0.66, 1.05, 0.86, 1.05, 1.05, -0.05, 1.05));
        dc.Pop();

        // subtle rim so the dark icon stays visible on a dark taskbar
        dc.DrawGeometry(null, new Pen(Solid(C(tiny ? 0x99FFFFFF : 0x55FFFFFF)), Math.Max(1, s * 0.012)), card);
    }

    // ---------- B: Monitors ----------

    static void DrawMonitors(DrawingContext dc, double s)
    {
        bool tiny = s <= 24;
        double pad = s * 0.04, r = s * 0.22;
        Geometry card = Rounded(pad, pad, s - 2 * pad, s - 2 * pad, r);
        dc.DrawGeometry(Stops(new Point(0, 0), new Point(1, 1), (0, C(0xFF0078D4)), (1, C(0xFF3FB6F5))), null, card);

        if (!tiny)
        {
            DrawMonitor(dc, s, 0.40, 0.17, 0.48, 0.34, sunset: true, shadow: false);
        }
        DrawMonitor(dc, s, tiny ? 0.14 : 0.12, tiny ? 0.22 : 0.33, tiny ? 0.72 : 0.56, tiny ? 0.46 : 0.38, sunset: false, shadow: !tiny);
    }

    static void DrawMonitor(DrawingContext dc, double s, double x, double y, double w, double h, bool sunset, bool shadow)
    {
        double bezel = Math.Max(1.2, s * 0.03);
        if (shadow) dc.DrawGeometry(Solid(C(0x33002050)), null, Rounded((x + 0.015) * s, (y + 0.025) * s, w * s, h * s, s * 0.04));
        Geometry frame = Rounded(x * s, y * s, w * s, h * s, s * 0.04);
        dc.DrawGeometry(Solid(Colors.White), null, frame);
        Rect screen = new(x * s + bezel, y * s + bezel, w * s - 2 * bezel, h * s - 2 * bezel);
        Geometry screenGeo = new RectangleGeometry(screen, s * 0.02, s * 0.02);

        dc.PushClip(screenGeo);
        if (sunset)
        {
            dc.DrawRectangle(Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFFFF8A5B)), (0.6, C(0xFFFFC36B)), (1, C(0xFFFFE0A3))), null, screen);
            dc.DrawEllipse(Solid(C(0xFFFFF4D6)), null, new Point(screen.Left + screen.Width * 0.70, screen.Top + screen.Height * 0.55), screen.Width * 0.12, screen.Width * 0.12);
            dc.DrawGeometry(Solid(C(0xFFB0476A)), null, Poly(1,
                screen.Left, screen.Bottom - screen.Height * 0.25, screen.Left + screen.Width * 0.35, screen.Top + screen.Height * 0.45,
                screen.Left + screen.Width * 0.6, screen.Bottom - screen.Height * 0.2, screen.Right, screen.Top + screen.Height * 0.55,
                screen.Right, screen.Bottom, screen.Left, screen.Bottom));
        }
        else
        {
            dc.DrawRectangle(Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFF0E1C45)), (1, C(0xFF2C5BA8))), null, screen);
            dc.PushOpacity(0.95);
            dc.DrawGeometry(Stops(new Point(0, 0), new Point(1, 0), (0, C(0x0040F5C8)), (0.4, C(0xFF40F5C8)), (1, C(0x605AB8FF))), null, Poly(1,
                screen.Left, screen.Top + screen.Height * 0.38, screen.Left + screen.Width * 0.5, screen.Top + screen.Height * 0.12,
                screen.Right, screen.Top + screen.Height * 0.30, screen.Right, screen.Top + screen.Height * 0.48,
                screen.Left + screen.Width * 0.5, screen.Top + screen.Height * 0.30, screen.Left, screen.Top + screen.Height * 0.56));
            dc.Pop();
            dc.DrawGeometry(Solid(C(0xFF0A1530)), null, Poly(1,
                screen.Left, screen.Bottom - screen.Height * 0.22, screen.Left + screen.Width * 0.3, screen.Top + screen.Height * 0.55,
                screen.Left + screen.Width * 0.55, screen.Bottom - screen.Height * 0.3, screen.Left + screen.Width * 0.8, screen.Top + screen.Height * 0.5,
                screen.Right, screen.Bottom - screen.Height * 0.3, screen.Right, screen.Bottom, screen.Left, screen.Bottom));
        }
        dc.Pop();

        // stand
        double cx = (x + w / 2) * s;
        dc.DrawRectangle(Solid(Colors.White), null, new Rect(cx - s * 0.03, (y + h) * s - 0.5, s * 0.06, s * 0.07));
        dc.DrawGeometry(Solid(Colors.White), null, Rounded(cx - s * 0.12, (y + h + 0.065) * s, s * 0.24, Math.Max(1.5, s * 0.035), s * 0.017));
    }

    // ---------- D: Combined (B の構成 + 手前の画面に C の 4 マス、左上は A のオーロラ) ----------

    static void DrawCombined(DrawingContext dc, double s)
    {
        bool tiny = s <= 24, small = s <= 48;
        double pad = s * 0.04, r = s * 0.22;
        Geometry card = Rounded(pad, pad, s - 2 * pad, s - 2 * pad, r);
        dc.DrawGeometry(Stops(new Point(0, 0), new Point(1, 1), (0, C(0xFF0078D4)), (1, C(0xFF3FB6F5))), null, card);

        if (!tiny)
        {
            DrawMonitorWith(dc, s, 0.40, 0.17, 0.48, 0.34, shadow: false, SunsetScreen);
        }

        DrawMonitorWith(dc, s, tiny ? 0.12 : 0.10, tiny ? 0.20 : 0.30, tiny ? 0.76 : 0.60, tiny ? 0.50 : 0.42, shadow: !tiny,
            (dc2, screen) => GridScreen(dc2, screen, s, small, tiny));
    }

    static void GridScreen(DrawingContext dc, Rect screen, double s, bool small, bool tiny)
    {
        dc.DrawRectangle(Solid(C(0xFF1A1D2E)), null, screen);
        double gap = Math.Max(1, s * (tiny ? 0.025 : 0.018));
        double w = (screen.Width - gap) / 2, h = (screen.Height - gap) / 2, cr = s * (tiny ? 0.0 : 0.012);
        var cells = new[]
        {
            new Rect(screen.Left, screen.Top, w, h), new Rect(screen.Left + w + gap, screen.Top, w, h),
            new Rect(screen.Left, screen.Top + h + gap, w, h), new Rect(screen.Left + w + gap, screen.Top + h + gap, w, h),
        };

        // 左上: A のオーロラ
        Tile(dc, cells[0], cr, Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFF0A1433)), (1, C(0xFF22448A))), (d, rc) =>
        {
            d.PushOpacity(0.95);
            d.DrawGeometry(Stops(new Point(0, 0), new Point(1, 0), (0, C(0x2040F5C8)), (0.35, C(0xFF40F5C8)), (0.75, C(0xE05AB8FF)), (1, C(0x80B57CFF))), null, Poly(1,
                rc.Left, rc.Top + rc.Height * 0.42, rc.Left + rc.Width * 0.35, rc.Top + rc.Height * 0.16, rc.Left + rc.Width * 0.7, rc.Top + rc.Height * 0.28,
                rc.Right, rc.Top + rc.Height * 0.10, rc.Right, rc.Top + rc.Height * 0.26, rc.Left + rc.Width * 0.7, rc.Top + rc.Height * 0.44,
                rc.Left + rc.Width * 0.35, rc.Top + rc.Height * 0.32, rc.Left, rc.Top + rc.Height * 0.58));
            d.Pop();
            d.DrawGeometry(Solid(C(0xFF2B4E8C)), null, Poly(1,
                rc.Left, rc.Bottom - rc.Height * 0.22, rc.Left + rc.Width * 0.35, rc.Top + rc.Height * 0.52,
                rc.Left + rc.Width * 0.6, rc.Bottom - rc.Height * 0.3, rc.Left + rc.Width * 0.82, rc.Top + rc.Height * 0.58,
                rc.Right, rc.Bottom - rc.Height * 0.25, rc.Right, rc.Bottom, rc.Left, rc.Bottom));
            if (!small)
            {
                d.DrawGeometry(Solid(C(0xFFE8F1FF)), null, Poly(1,
                    rc.Left + rc.Width * 0.35, rc.Top + rc.Height * 0.52, rc.Left + rc.Width * 0.42, rc.Top + rc.Height * 0.61,
                    rc.Left + rc.Width * 0.35, rc.Top + rc.Height * 0.59, rc.Left + rc.Width * 0.29, rc.Top + rc.Height * 0.62));
            }
            d.DrawGeometry(Solid(C(0xFF0B1730)), null, Poly(1,
                rc.Left, rc.Bottom - rc.Height * 0.1, rc.Left + rc.Width * 0.45, rc.Bottom - rc.Height * 0.28,
                rc.Right, rc.Bottom - rc.Height * 0.08, rc.Right, rc.Bottom, rc.Left, rc.Bottom));
        }, tiny);

        // 右上: 海
        Tile(dc, cells[1], cr, Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFF6FD3FF)), (1, C(0xFF1C7FD6))), (d, rc) =>
        {
            d.DrawRectangle(Solid(C(0xFF0F5FB8)), null, new Rect(rc.Left, rc.Top + rc.Height * 0.58, rc.Width, rc.Height * 0.42));
            d.DrawRectangle(Solid(C(0xAAFFFFFF)), null, new Rect(rc.Left, rc.Top + rc.Height * 0.58, rc.Width, Math.Max(0.6, rc.Height * 0.05)));
        }, tiny);

        // 左下: 森
        Tile(dc, cells[2], cr, Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFFB9F27C)), (1, C(0xFF3FB36B))), (d, rc) =>
        {
            d.DrawGeometry(Solid(C(0xFF1E7A4A)), null, Poly(1, rc.Left, rc.Bottom, rc.Left + rc.Width * 0.3, rc.Top + rc.Height * 0.3,
                rc.Left + rc.Width * 0.55, rc.Top + rc.Height * 0.68, rc.Left + rc.Width * 0.78, rc.Top + rc.Height * 0.38, rc.Right, rc.Bottom));
        }, tiny);

        // 右下: 夜
        Tile(dc, cells[3], cr, Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFF7B5CFF)), (1, C(0xFF2B1E7A))), (d, rc) =>
        {
            double m = Math.Min(rc.Width, rc.Height);
            d.DrawEllipse(Solid(C(0xFFFFF6D8)), null, new Point(rc.Left + rc.Width * 0.62, rc.Top + rc.Height * 0.45), m * 0.24, m * 0.24);
            d.DrawEllipse(Solid(C(0xFF5A3FD8)), null, new Point(rc.Left + rc.Width * 0.62 + m * 0.12, rc.Top + rc.Height * 0.45 - m * 0.09), m * 0.21, m * 0.21);
        }, tiny);
    }

    static void SunsetScreen(DrawingContext dc, Rect screen)
    {
        dc.DrawRectangle(Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFFFF8A5B)), (0.6, C(0xFFFFC36B)), (1, C(0xFFFFE0A3))), null, screen);
        dc.DrawEllipse(Solid(C(0xFFFFF4D6)), null, new Point(screen.Left + screen.Width * 0.70, screen.Top + screen.Height * 0.55), screen.Width * 0.12, screen.Width * 0.12);
        dc.DrawGeometry(Solid(C(0xFFB0476A)), null, Poly(1,
            screen.Left, screen.Bottom - screen.Height * 0.25, screen.Left + screen.Width * 0.35, screen.Top + screen.Height * 0.45,
            screen.Left + screen.Width * 0.6, screen.Bottom - screen.Height * 0.2, screen.Right, screen.Top + screen.Height * 0.55,
            screen.Right, screen.Bottom, screen.Left, screen.Bottom));
    }

    static void DrawMonitorWith(DrawingContext dc, double s, double x, double y, double w, double h, bool shadow, Action<DrawingContext, Rect> paintScreen)
    {
        double bezel = Math.Max(1.2, s * 0.03);
        if (shadow) dc.DrawGeometry(Solid(C(0x33002050)), null, Rounded((x + 0.015) * s, (y + 0.025) * s, w * s, h * s, s * 0.04));
        dc.DrawGeometry(Solid(Colors.White), null, Rounded(x * s, y * s, w * s, h * s, s * 0.04));
        Rect screen = new(x * s + bezel, y * s + bezel, w * s - 2 * bezel, h * s - 2 * bezel);
        dc.PushClip(new RectangleGeometry(screen, s * 0.02, s * 0.02));
        paintScreen(dc, screen);
        dc.Pop();

        double cx = (x + w / 2) * s;
        dc.DrawRectangle(Solid(Colors.White), null, new Rect(cx - s * 0.03, (y + h) * s - 0.5, s * 0.06, s * 0.07));
        dc.DrawGeometry(Solid(Colors.White), null, Rounded(cx - s * 0.12, (y + h + 0.065) * s, s * 0.24, Math.Max(1.5, s * 0.035), s * 0.017));
    }

    // ---------- C: Grid ----------

    static void DrawGrid(DrawingContext dc, double s)
    {
        bool tiny = s <= 24, small = s <= 40;
        double pad = s * 0.04, r = s * 0.22;
        Geometry card = Rounded(pad, pad, s - 2 * pad, s - 2 * pad, r);
        dc.DrawGeometry(Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFF2A2F45)), (1, C(0xFF14172A))), null, card);

        double inner = s * (tiny ? 0.12 : 0.15), gap = Math.Max(1, s * (tiny ? 0.05 : 0.045));
        double cell = (s - 2 * inner - gap) / 2, cr = s * (tiny ? 0.05 : 0.07);
        var cells = new[]
        {
            new Rect(inner, inner, cell, cell), new Rect(inner + cell + gap, inner, cell, cell),
            new Rect(inner, inner + cell + gap, cell, cell), new Rect(inner + cell + gap, inner + cell + gap, cell, cell),
        };

        // sunset
        Tile(dc, cells[0], cr, Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFFFF7A59)), (1, C(0xFFFFC56E))), (dc2, rc) =>
        {
            dc2.DrawEllipse(Solid(C(0xFFFFF1CF)), null, new Point(rc.Left + rc.Width * 0.62, rc.Top + rc.Height * 0.58), rc.Width * 0.2, rc.Width * 0.2);
            dc2.DrawGeometry(Solid(C(0xFFC2456B)), null, Poly(1, rc.Left, rc.Bottom - rc.Height * 0.18, rc.Left + rc.Width * 0.4, rc.Top + rc.Height * 0.55, rc.Right, rc.Bottom - rc.Height * 0.12, rc.Right, rc.Bottom, rc.Left, rc.Bottom));
        }, small);
        // ocean
        Tile(dc, cells[1], cr, Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFF6FD3FF)), (1, C(0xFF1C7FD6))), (dc2, rc) =>
        {
            dc2.DrawRectangle(Solid(C(0xFF0F5FB8)), null, new Rect(rc.Left, rc.Top + rc.Height * 0.6, rc.Width, rc.Height * 0.4));
            dc2.DrawGeometry(Solid(C(0xAAFFFFFF)), null, Poly(1, rc.Left, rc.Top + rc.Height * 0.62, rc.Left + rc.Width * 0.25, rc.Top + rc.Height * 0.56, rc.Left + rc.Width * 0.5, rc.Top + rc.Height * 0.62, rc.Left + rc.Width * 0.75, rc.Top + rc.Height * 0.56, rc.Right, rc.Top + rc.Height * 0.62, rc.Right, rc.Top + rc.Height * 0.66, rc.Left, rc.Top + rc.Height * 0.66));
        }, small);
        // forest / mountains
        Tile(dc, cells[2], cr, Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFFB9F27C)), (1, C(0xFF3FB36B))), (dc2, rc) =>
        {
            dc2.DrawGeometry(Solid(C(0xFF1E7A4A)), null, Poly(1, rc.Left, rc.Bottom, rc.Left + rc.Width * 0.3, rc.Top + rc.Height * 0.35, rc.Left + rc.Width * 0.55, rc.Top + rc.Height * 0.7, rc.Left + rc.Width * 0.78, rc.Top + rc.Height * 0.42, rc.Right, rc.Bottom));
        }, small);
        // night
        Tile(dc, cells[3], cr, Stops(new Point(0.5, 0), new Point(0.5, 1), (0, C(0xFF7B5CFF)), (1, C(0xFF2B1E7A))), (dc2, rc) =>
        {
            dc2.DrawEllipse(Solid(C(0xFFFFF6D8)), null, new Point(rc.Left + rc.Width * 0.62, rc.Top + rc.Height * 0.38), rc.Width * 0.2, rc.Width * 0.2);
            dc2.DrawEllipse(Solid(C(0xFF5A3FD8)), null, new Point(rc.Left + rc.Width * 0.72, rc.Top + rc.Height * 0.32), rc.Width * 0.17, rc.Width * 0.17);
            dc2.DrawEllipse(Solid(C(0xDDFFFFFF)), null, new Point(rc.Left + rc.Width * 0.25, rc.Top + rc.Height * 0.25), rc.Width * 0.04, rc.Width * 0.04);
            dc2.DrawEllipse(Solid(C(0xBBFFFFFF)), null, new Point(rc.Left + rc.Width * 0.35, rc.Top + rc.Height * 0.62), rc.Width * 0.03, rc.Width * 0.03);
        }, small);
    }

    static void Tile(DrawingContext dc, Rect rc, double radius, Brush background, Action<DrawingContext, Rect> scene, bool simple)
    {
        Geometry g = new RectangleGeometry(rc, radius, radius);
        dc.PushClip(g);
        dc.DrawRectangle(background, null, rc);
        if (!simple) scene(dc, rc);
        dc.Pop();
    }

    // ---------- rendering / output ----------

    static BitmapSource Render(Action<DrawingContext, double> draw, int size)
    {
        // supersample for clean edges, then downscale
        int factor = size <= 64 ? 8 : 4;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen()) draw(dc, size * factor);
        var big = new RenderTargetBitmap(size * factor, size * factor, 96, 96, PixelFormats.Pbgra32);
        big.Render(visual);
        var scaled = new TransformedBitmap(big, new ScaleTransform(1.0 / factor, 1.0 / factor));
        var result = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        result.Freeze();
        return result;
    }

    static BitmapSource RenderPreview((string Key, Action<DrawingContext, double> Draw)[] variants)
    {
        int[] sizes = [256, 64, 48, 32, 24, 16];
        int rowH = 300, colW = 256 + 40 + sizes.Skip(1).Sum(x => x + 24) + 40;
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            for (int v = 0; v < variants.Length; v++)
            {
                for (int theme = 0; theme < 2; theme++)
                {
                    double ox = theme * colW, oy = v * rowH;
                    dc.DrawRectangle(Solid(theme == 0 ? C(0xFFF3F3F3) : C(0xFF202020)), null, new Rect(ox, oy, colW, rowH));
                    double x = ox + 20;
                    foreach (int sz in sizes)
                    {
                        var bmp = Render(variants[v].Draw, sz);
                        double y = oy + (rowH - sz) / 2.0;
                        dc.DrawImage(bmp, new Rect(Math.Round(x), Math.Round(y), sz, sz));
                        x += sz + 24;
                    }
                }
            }
        }
        var rtb = new RenderTargetBitmap(colW * 2, rowH * variants.Length, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    static void WriteIco(string path, List<(int Size, BitmapSource Bitmap)> frames)
    {
        // small sizes as 32bpp DIB (widest compatibility), 256 as PNG
        var images = frames.Select(f => f.Size >= 256 ? PngBytes(f.Bitmap) : DibBytes(f.Bitmap)).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0); w.Write((short)1); w.Write((short)frames.Count);
        int offset = 6 + 16 * frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            int sz = frames[i].Size; byte dim = (byte)(sz >= 256 ? 0 : sz);
            w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset);
            offset += images[i].Length;
        }
        foreach (byte[] img in images) w.Write(img);
        File.WriteAllBytes(path, ms.ToArray());
    }

    static byte[] PngBytes(BitmapSource b)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(b));
        using var ms = new MemoryStream(); encoder.Save(ms); return ms.ToArray();
    }

    static byte[] DibBytes(BitmapSource b)
    {
        int size = b.PixelWidth, stride = size * 4;
        byte[] px = new byte[stride * size];
        b.CopyPixels(px, stride, 0);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = size - 1; y >= 0; y--) w.Write(px, y * stride, stride);
        int maskStride = (size + 31) / 32 * 4;
        for (int y = size - 1; y >= 0; y--)
        {
            byte[] row = new byte[maskStride];
            for (int x = 0; x < size; x++) if (px[y * stride + x * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
            w.Write(row);
        }
        return ms.ToArray();
    }
}
