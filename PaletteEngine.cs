namespace ColorTool;

// ==========================================================
// Palette Generation Engine
// 以 OKLCH 為內部唯一色彩模型的調色盤生成引擎。
// 純函式設計：GeneratePalette(config) → Palette，不依賴任何 UI，
// 可供 CLI／Web／Desktop 共用；所有設定皆為資料驅動。
// ==========================================================

public enum LightnessMode { Manual, Linear, Curve }
public enum CurveType { Linear, Gamma, Log, Bezier }
public enum ChromaMode { Absolute, Relative, Adaptive }
public enum HueDirection { Clockwise, CounterClockwise }
public enum TargetGamut { Srgb, DisplayP3, Rec2020, Unlimited }
public enum GamutStrategy { Clip, Scale, Compress, Perceptual }
public enum PaletteSorting { HueFirst, LightnessFirst, ChromaFirst }
public enum NamingStyle { Hlc, HueName, Pccs, Custom }

public class PaletteConfig
{
    public string PaletteName { get; set; } = "Palette";
    public string Description { get; set; } = "";

    // Hue：Hue = Offset + n × (360 / HueCount)
    public int HueCount { get; set; } = 12;
    public double HueOffset { get; set; } = 0;
    public HueDirection Direction { get; set; } = HueDirection.Clockwise;

    // Lightness（百分比 0~100）
    public LightnessMode LightnessMode { get; set; } = LightnessMode.Linear;
    public double[] LightnessStops { get; set; } = { 95, 80, 65, 50, 35, 20 }; // Manual 模式
    public double LightnessStart { get; set; } = 95;
    public double LightnessEnd { get; set; } = 20;
    public int LightnessSteps { get; set; } = 6;
    public CurveType Curve { get; set; } = CurveType.Linear;
    public double Gamma { get; set; } = 2.0;
    public double BezierControl { get; set; } = 0.35;

    // Chroma：Absolute＝絕對 C 值；Relative/Adaptive＝Cmax 的比例（0~1）
    public ChromaMode ChromaMode { get; set; } = ChromaMode.Relative; // 預設模式
    public double[] ChromaStops { get; set; } = { 0.9, 0.6, 0.3 };
    public bool IncludeNeutral { get; set; } = true;

    public TargetGamut Gamut { get; set; } = TargetGamut.Srgb;
    public GamutStrategy OutOfGamut { get; set; } = GamutStrategy.Scale;
    public PaletteSorting Sorting { get; set; } = PaletteSorting.HueFirst;

    public NamingStyle Naming { get; set; } = NamingStyle.Hlc;
    public string NameTemplate { get; set; } = "H{H}-L{L}-C{C}";
}

public class PaletteEntry
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    // OKLCH（內部唯一色彩模型）
    public double L { get; set; }
    public double C { get; set; }
    public double H { get; set; }
    // 同步保存的 sRGB / HEX
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public string Hex { get; set; } = "";
    public bool Neutral { get; set; }
}

public class Palette
{
    public string PaletteName { get; set; } = "";
    public string Description { get; set; } = "";
    public string CreateTime { get; set; } = "";
    public string ColorSpace { get; set; } = "OKLCH";
    public string TargetGamut { get; set; } = "sRGB";
    public string Version { get; set; } = "1.0";
    public int SuggestedColumns { get; set; } = 12; // 匯出 PNG/SVG 的建議換行寬
    public List<PaletteEntry> Entries { get; set; } = new();
}

public static class PaletteEngine
{
    // ---------- 主要 API：純函式 ----------
    public static Palette GeneratePalette(PaletteConfig cfg)
    {
        double[] lStops = ResolveLightness(cfg);   // 0~1
        double[] cStops = cfg.ChromaStops;
        int hueCount = Math.Max(1, cfg.HueCount);
        double step = 360.0 / hueCount;

        var entries = new List<PaletteEntry>();

        for (int hi = 0; hi < hueCount; hi++)
        {
            double hue = cfg.Direction == HueDirection.Clockwise
                ? cfg.HueOffset + hi * step
                : cfg.HueOffset - hi * step;
            hue = ((hue % 360) + 360) % 360;

            foreach (double l in lStops)
            {
                foreach (double cs in cStops)
                {
                    double chroma = ResolveChroma(cfg, cs, l, hue);
                    (double l2, double c2) = ApplyGamutStrategy(cfg, l, chroma, hue);
                    entries.Add(MakeEntry(l2, c2, hue, false));
                }
            }
        }

        // Neutral：每個 Lightness 加入 C=0
        if (cfg.IncludeNeutral)
            foreach (double l in lStops)
                entries.Add(MakeEntry(l, 0, 0, true));

        SortEntries(entries, cfg.Sorting);
        AssignNames(entries, cfg);

        return new Palette
        {
            PaletteName = cfg.PaletteName,
            Description = cfg.Description,
            CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ColorSpace = "OKLCH",
            TargetGamut = cfg.Gamut.ToString(),
            Version = "1.0",
            SuggestedColumns = SuggestColumns(cfg, lStops.Length, cStops.Length),
            Entries = entries,
        };
    }

    // ---------- Lightness ----------
    private static double[] ResolveLightness(PaletteConfig cfg)
    {
        if (cfg.LightnessMode == LightnessMode.Manual)
            return cfg.LightnessStops.Select(v => Math.Clamp(v / 100.0, 0.0, 1.0)).ToArray();

        int n = Math.Max(2, cfg.LightnessSteps);
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)(n - 1);
            if (cfg.LightnessMode == LightnessMode.Curve)
                t = ShapeCurve(t, cfg);
            result[i] = Math.Clamp((cfg.LightnessStart + (cfg.LightnessEnd - cfg.LightnessStart) * t) / 100.0, 0.0, 1.0);
        }
        return result;
    }

    // 曲線分布：調整明度取樣的疏密（偏亮部或偏暗部）
    private static double ShapeCurve(double t, PaletteConfig cfg) => cfg.Curve switch
    {
        CurveType.Gamma => Math.Pow(t, cfg.Gamma),
        CurveType.Log => Math.Log(1 + 9 * t) / Math.Log(10),
        CurveType.Bezier => 2 * (1 - t) * t * cfg.BezierControl + t * t, // 二次貝茲（P0=0, P1=控制點, P2=1）
        _ => t,
    };

    // ---------- Chroma ----------
    private static double ResolveChroma(PaletteConfig cfg, double stop, double l, double hue)
    {
        switch (cfg.ChromaMode)
        {
            case ChromaMode.Absolute:
                return Math.Max(0, stop);
            case ChromaMode.Adaptive:
            {
                double cmax = MaxChroma(l, hue, cfg.Gamut);
                return Math.Min(cmax, cmax * stop * AdaptiveFactor(hue));
            }
            default: // Relative（預設）：C = Cmax × Ratio
                return MaxChroma(l, hue, cfg.Gamut) * stop;
        }
    }

    // Adaptive 的色相修正曲線：黃色降低、紫色提高、藍色略提升，讓視覺彩度更平均。
    // 以簡單的餘弦窗實作，之後可整段替換成更精確的模型（介面不變）。
    private static double AdaptiveFactor(double h)
    {
        return 1.0
            - 0.22 * Bump(h, 110, 55)   // 黃
            + 0.15 * Bump(h, 315, 55)   // 紫
            + 0.08 * Bump(h, 262, 40);  // 藍

        static double Bump(double hue, double center, double width)
        {
            double d = Math.Abs(((hue - center) % 360 + 540) % 360 - 180);
            if (d >= width) return 0;
            double c = Math.Cos(d / width * Math.PI / 2);
            return c * c;
        }
    }

    // ---------- 色域 ----------
    // XYZ(D65) → 目標色域線性 RGB（CSS Color 4 係數）
    private static readonly double[,] XyzToP3 =
    {
        {  2.4934969, -0.9313836, -0.4027108 },
        { -0.8294890,  1.7626641,  0.0236247 },
        {  0.0358458, -0.0761724,  0.9568845 },
    };
    private static readonly double[,] XyzToRec2020 =
    {
        {  1.7166512, -0.3556708, -0.2533663 },
        { -0.6666844,  1.6164812,  0.0157685 },
        {  0.0176399, -0.0427706,  0.9421031 },
    };

    private static bool InGamut(double l, double c, double h, TargetGamut gamut)
    {
        if (gamut == TargetGamut.Unlimited) return true;
        (double r, double g, double b) = ColorMath.OklchToLinearSrgb(l, c, h);
        if (gamut == TargetGamut.Srgb) return In01(r) && In01(g) && In01(b);

        // 線性 sRGB → XYZ(D65) → 目標色域
        double x = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b;
        double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        double z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;
        double[,] m = gamut == TargetGamut.DisplayP3 ? XyzToP3 : XyzToRec2020;
        double tr = m[0, 0] * x + m[0, 1] * y + m[0, 2] * z;
        double tg = m[1, 0] * x + m[1, 1] * y + m[1, 2] * z;
        double tb = m[2, 0] * x + m[2, 1] * y + m[2, 2] * z;
        return In01(tr) && In01(tg) && In01(tb);

        static bool In01(double v) => v >= -1e-6 && v <= 1.0 + 1e-6;
    }

    // 該 (L, H) 在目標色域內可達的最大 Chroma（二分搜尋）
    public static double MaxChroma(double l, double h, TargetGamut gamut)
    {
        if (gamut == TargetGamut.Unlimited) return 0.4; // OKLCH 實務上限
        if (l <= 0.0005 || l >= 0.9995) return 0;
        double lo = 0, hi = 0.5;
        for (int i = 0; i < 24; i++)
        {
            double mid = (lo + hi) / 2;
            if (InGamut(l, mid, h, gamut)) lo = mid; else hi = mid;
        }
        return lo;
    }

    // 超出色域的處理策略（可擴充：新增 case 即可）
    private static (double L, double C) ApplyGamutStrategy(PaletteConfig cfg, double l, double c, double h)
    {
        if (cfg.Gamut == TargetGamut.Unlimited) return (l, c);
        double cmax = MaxChroma(l, h, cfg.Gamut);
        switch (cfg.OutOfGamut)
        {
            case GamutStrategy.Clip:
                return (l, c); // 保留 OKLCH 值，RGB 轉換時逐通道裁切
            case GamutStrategy.Compress:
            {
                // 軟膝壓縮：0.8×Cmax 以下不動，以上以 1/4 斜率壓入
                double knee = 0.8 * cmax;
                double c2 = c <= knee ? c : knee + (c - knee) * 0.25;
                return (l, Math.Min(c2, cmax));
            }
            case GamutStrategy.Perceptual:
            {
                // 佔位實作：彩度縮回色域並將明度微調向中間，之後可替換成 cusp 投影
                if (c <= cmax) return (l, c);
                double ratio = cmax / Math.Max(c, 1e-9);
                double l2 = l + (0.5 - l) * (1 - ratio) * 0.3;
                return (l2, Math.Min(c, MaxChroma(l2, h, cfg.Gamut)));
            }
            default: // Scale
                return (l, Math.Min(c, cmax));
        }
    }

    // ---------- Entry／排序／命名 ----------
    private static PaletteEntry MakeEntry(double l, double c, double h, bool neutral)
    {
        (int r, int g, int b) = ColorMath.OklchToRgb(l, c, h);
        return new PaletteEntry
        {
            L = l, C = c, H = neutral ? 0 : h,
            R = (byte)r, G = (byte)g, B = (byte)b,
            Hex = $"#{r:X2}{g:X2}{b:X2}",
            Neutral = neutral,
        };
    }

    private static void SortEntries(List<PaletteEntry> entries, PaletteSorting sorting)
    {
        entries.Sort((a, b) => sorting switch
        {
            PaletteSorting.LightnessFirst =>
                Cmp(b.L, a.L, a.Neutral ? 1 : 0, b.Neutral ? 1 : 0, a.H, b.H, b.C, a.C),
            PaletteSorting.ChromaFirst =>
                Cmp(b.C, a.C, a.H, b.H, b.L, a.L, 0, 0),
            _ => // HueFirst：中性色排最後
                Cmp(a.Neutral ? 1 : 0, b.Neutral ? 1 : 0, a.H, b.H, b.L, a.L, b.C, a.C),
        });

        static int Cmp(double a1, double b1, double a2, double b2, double a3, double b3, double a4, double b4)
        {
            int c = a1.CompareTo(b1);
            if (c != 0) return c;
            c = a2.CompareTo(b2);
            if (c != 0) return c;
            c = a3.CompareTo(b3);
            return c != 0 ? c : a4.CompareTo(b4);
        }
    }

    private static readonly string[] HueEngNames =
        { "Red", "YellowRed", "Yellow", "GreenYellow", "Green", "BlueGreen", "Blue", "PurpleBlue", "Purple", "RedPurple" };

    private static void AssignNames(List<PaletteEntry> entries, PaletteConfig cfg)
    {
        var counters = new Dictionary<string, int>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            e.Index = i;
            switch (cfg.Naming)
            {
                case NamingStyle.HueName:
                {
                    string baseName = e.Neutral ? "Neutral" : HueEngNames[Hue10Of(e)];
                    counters.TryGetValue(baseName, out int n);
                    counters[baseName] = ++n;
                    e.Name = $"{baseName}-{n:D2}";
                    break;
                }
                case NamingStyle.Pccs:
                {
                    var tone = Pccs.ClassifyRgb(e.R / 255.0, e.G / 255.0, e.B / 255.0);
                    e.Name = tone.IsAchromatic || e.Neutral
                        ? tone.Code
                        : $"{tone.Code}-{new[] { "R", "YR", "Y", "GY", "G", "BG", "B", "PB", "P", "RP" }[Hue10Of(e)]}";
                    break;
                }
                case NamingStyle.Custom:
                    e.Name = ApplyTemplate(cfg.NameTemplate, e, i);
                    break;
                default: // Hlc
                    e.Name = ApplyTemplate("H{H}-L{L}-C{C}", e, i);
                    break;
            }
        }

        static int Hue10Of(PaletteEntry e)
        {
            (double h, _, _) = ColorMath.RgbToHls(e.R, e.G, e.B);
            return ((int)Math.Round(h / 36.0)) % 10;
        }
    }

    // Formatter 模板：{H} 色相、{L} 明度%、{C} 彩度×1000、{I} 索引、{HEX} 色碼
    private static string ApplyTemplate(string tpl, PaletteEntry e, int index) => tpl
        .Replace("{H}", ((int)Math.Round(e.H)).ToString("D3"))
        .Replace("{L}", ((int)Math.Round(e.L * 100)).ToString("D2"))
        .Replace("{C}", ((int)Math.Round(e.C * 1000)).ToString("D3"))
        .Replace("{I}", index.ToString("D3"))
        .Replace("{HEX}", e.Hex);

    private static int SuggestColumns(PaletteConfig cfg, int lCount, int cCount) => cfg.Sorting switch
    {
        PaletteSorting.LightnessFirst => Math.Max(1, cfg.HueCount * cCount + (cfg.IncludeNeutral ? 1 : 0)),
        PaletteSorting.ChromaFirst => Math.Max(1, cfg.HueCount * lCount),
        _ => Math.Max(1, lCount * cCount), // HueFirst：一列＝一個色相
    };

    // ---------- Preset：本質是一組設定，不寫死演算法 ----------
    public static readonly (string Name, Func<PaletteConfig> Create)[] Presets =
    {
        ("Default", () => new PaletteConfig()),
        ("PCCS", () => new PaletteConfig
        {
            PaletteName = "PCCS",
            HueCount = 24,
            LightnessMode = LightnessMode.Manual,
            LightnessStops = new double[] { 87, 78, 69, 60, 50, 40, 30 },
            ChromaStops = new[] { 0.95, 0.65, 0.35 },
        }),
        ("Pastel", () => new PaletteConfig
        {
            PaletteName = "Pastel",
            HueCount = 12,
            LightnessMode = LightnessMode.Manual,
            LightnessStops = new double[] { 92, 86, 80 },
            ChromaStops = new[] { 0.25, 0.4 },
        }),
        ("Anime", () => new PaletteConfig
        {
            PaletteName = "Anime",
            HueCount = 18,
            LightnessMode = LightnessMode.Manual,
            LightnessStops = new double[] { 90, 75, 60, 45, 30 },
            ChromaStops = new[] { 0.85, 0.55 },
        }),
        ("Watercolor", () => new PaletteConfig
        {
            PaletteName = "Watercolor",
            HueCount = 12,
            LightnessMode = LightnessMode.Manual,
            LightnessStops = new double[] { 95, 88, 80, 70 },
            ChromaStops = new[] { 0.2, 0.35, 0.5 },
            OutOfGamut = GamutStrategy.Compress,
        }),
        ("UI Design", () => new PaletteConfig
        {
            PaletteName = "UI Design",
            HueCount = 12,
            LightnessMode = LightnessMode.Manual,
            LightnessStops = new double[] { 98, 93, 85, 74, 62, 50, 40, 30 },
            ChromaStops = new[] { 0.7 },
        }),
        ("Dark UI", () => new PaletteConfig
        {
            PaletteName = "Dark UI",
            HueCount = 12,
            LightnessMode = LightnessMode.Manual,
            LightnessStops = new double[] { 12, 16, 22, 30, 45, 60 },
            ChromaStops = new[] { 0.5, 0.25 },
            Sorting = PaletteSorting.LightnessFirst,
        }),
        ("Pixel Art", () => new PaletteConfig
        {
            PaletteName = "Pixel Art",
            HueCount = 8,
            HueOffset = 22.5,
            LightnessMode = LightnessMode.Manual,
            LightnessStops = new double[] { 85, 65, 45, 25 },
            ChromaStops = new[] { 0.8, 0.5 },
            Sorting = PaletteSorting.LightnessFirst,
        }),
    };
}
