using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ColorTool;

public class ImageAnalysisResult
{
    public long Total;
    public long AchromaticCount;
    // 色相 24 格（每 15°，hueIdx = round(hue/15) % 24）
    public long[] HueCount = new long[24];
    public Color[] HueAvg = new Color[24];
    public Color AchromaticAvg;
    public long[] ToneCount = new long[17];
    public Color[] ToneAvg = new Color[17];
    // 有彩色調存 [toneIndex, hueIndex]；無彩色調一律存在 hueIndex 0
    public long[,] CellCount = new long[17, 24];
    public Color[,] CellAvg = new Color[17, 24];
    // 無彩像素依明度 0~100 分十階（含兩端共 11 級）
    public long[] AchLevelCount = new long[11];
    public Color[] AchLevelAvg = new Color[11];
    // ── 以下為甜甜圈圖專用統計（第二輪收集；「忽略背景」開啟時排除背景像素）──
    public long PieTotal;
    public long PieAchromaticCount;
    public Color PieAchromaticAvg;
    // 10 色相環（R/YR/Y/GY/G/BG/B/PB/P/RP，每 36°）——色相平衡甜甜圈用
    public long[] Hue10Count = new long[10];
    public Color[] Hue10Avg = new Color[10];
    // 色調四大分類（鮮豔/明亮/昏暗/暗淡）——色調平衡甜甜圈用
    public long[] GroupCount = new long[4];
    public Color[] GroupAvg = new Color[4];
    // OkLab L 四階明度佔比（0~0.25 / ~0.5 / ~0.75 / ~1）
    public long[] LightnessCount = new long[4];
    public List<(string Role, Color Color, double Share, double Score)> Palette = new();
}

public static class ImageAnalyzer
{
    // 視覺重要性分數的權重
    //   基礎分（所有群、tooltip 顯示）：Base = A^α × S^β × ΔE^γ × C^δ
    //   點綴色排序分：Accent = Base × H^ε × T
    //   A：面積佔比、S：平均飽和度、ΔE：與主色的 Lab 色差、C：局部對比、
    //   H：與主色的色相互補度、T：PCCS 色調倍率（H、T 僅點綴色使用，不影響主色排序）
    // α<1：面積做次線性壓縮，避免小色塊因面積太小失去競爭力
    public static double AlphaArea = 0.2;
    public static double BetaSaturation = 1.0;
    public static double GammaDeltaE = 0.6;          // 建議範圍 0.6~0.8
    public static double DeltaLocalContrast = 0.8;
    public static double EpsilonHueComplement = 1.5; // 建議範圍 1.4~1.6

    // 開啟時：影像最外圈過半屬於同一聚類 → 該聚類視為背景，排除在配色角色之外
    public static bool IgnoreBackground = true;

    public static ImageAnalysisResult Analyze(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.DecodePixelWidth = 160; // 縮小取樣，統計上已足夠
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();

        var conv = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        int stride = w * 4;
        byte[] data = new byte[h * stride];
        conv.CopyPixels(data, stride, 0);

        var res = new ImageAnalysisResult();
        long[,] hueSum = new long[24, 3];
        long[] achSum = new long[3];
        long[,] achLvlSum = new long[11, 3];
        long[,] toneSum = new long[17, 3];
        long[,,] cellSum = new long[17, 24, 3];
        var samples = new List<int>(w * h);

        for (int i = 0; i < data.Length; i += 4)
        {
            byte b = data[i], g = data[i + 1], r = data[i + 2], a = data[i + 3];
            if (a < 128) continue; // 忽略透明像素

            res.Total++;
            samples.Add((r << 16) | (g << 8) | b);

            var tone = Pccs.ClassifyRgb(r / 255.0, g / 255.0, b / 255.0);
            int hueIdx = 0;
            if (!tone.IsAchromatic)
            {
                (double hue, _, _) = ColorMath.RgbToHls(r, g, b);
                hueIdx = ((int)Math.Round(hue / 15.0)) % 24;
                res.HueCount[hueIdx]++;
                hueSum[hueIdx, 0] += r; hueSum[hueIdx, 1] += g; hueSum[hueIdx, 2] += b;
            }
            else
            {
                res.AchromaticCount++;
                achSum[0] += r; achSum[1] += g; achSum[2] += b;

                // 無彩明度階級：HLS L 四捨五入到 0~10
                double lit = (Math.Max(r, Math.Max(g, b)) + Math.Min(r, Math.Min(g, b))) / 2.0 / 255.0;
                int level = (int)Math.Round(lit * 10.0);
                res.AchLevelCount[level]++;
                achLvlSum[level, 0] += r; achLvlSum[level, 1] += g; achLvlSum[level, 2] += b;
            }

            res.ToneCount[tone.Index]++;
            toneSum[tone.Index, 0] += r; toneSum[tone.Index, 1] += g; toneSum[tone.Index, 2] += b;

            int col = tone.IsAchromatic ? 0 : hueIdx;
            res.CellCount[tone.Index, col]++;
            cellSum[tone.Index, col, 0] += r; cellSum[tone.Index, col, 1] += g; cellSum[tone.Index, col, 2] += b;
        }

        for (int c = 0; c < 24; c++)
            if (res.HueCount[c] > 0)
                res.HueAvg[c] = Avg(hueSum[c, 0], hueSum[c, 1], hueSum[c, 2], res.HueCount[c]);

        if (res.AchromaticCount > 0)
            res.AchromaticAvg = Avg(achSum[0], achSum[1], achSum[2], res.AchromaticCount);

        for (int i = 0; i < 11; i++)
            if (res.AchLevelCount[i] > 0)
                res.AchLevelAvg[i] = Avg(achLvlSum[i, 0], achLvlSum[i, 1], achLvlSum[i, 2], res.AchLevelCount[i]);

        for (int t = 0; t < 17; t++)
        {
            if (res.ToneCount[t] > 0)
                res.ToneAvg[t] = Avg(toneSum[t, 0], toneSum[t, 1], toneSum[t, 2], res.ToneCount[t]);
            for (int c = 0; c < 24; c++)
                if (res.CellCount[t, c] > 0)
                    res.CellAvg[t, c] = Avg(cellSum[t, c, 0], cellSum[t, c, 1], cellSum[t, c, 2], res.CellCount[t, c]);
        }

        BuildPalette(res, data, w, h, samples, out byte[]? labels, out int bgId);
        CollectPieStats(res, data, w, h, labels, bgId);
        return res;
    }

    // 甜甜圈圖的統計獨立成第二輪：偵測到背景時（僅在 IgnoreBackground 開啟）排除背景像素（實驗性）
    private static void CollectPieStats(ImageAnalysisResult res, byte[] data, int w, int h, byte[]? labels, int bgId)
    {
        long[,] hue10Sum = new long[10, 3];
        long[,] groupSum = new long[4, 3];
        long[] achSum = new long[3];

        int pxCount = w * h;
        for (int p = 0; p < pxCount; p++)
        {
            int i = p * 4;
            if (data[i + 3] < 128) continue;
            if (bgId >= 0 && labels != null && labels[p] == bgId) continue;

            byte b = data[i], g = data[i + 1], r = data[i + 2];
            res.PieTotal++;

            double okL = ColorMath.RgbToOklabL(r, g, b);
            int lIdx = okL <= 0.25 ? 0 : okL <= 0.5 ? 1 : okL <= 0.75 ? 2 : 3;
            res.LightnessCount[lIdx]++;

            var tone = Pccs.ClassifyRgb(r / 255.0, g / 255.0, b / 255.0);
            if (tone.IsAchromatic)
            {
                res.PieAchromaticCount++;
                achSum[0] += r; achSum[1] += g; achSum[2] += b;
                continue;
            }

            (double hue, _, _) = ColorMath.RgbToHls(r, g, b);
            int h10 = ((int)Math.Round(hue / 36.0)) % 10;
            res.Hue10Count[h10]++;
            hue10Sum[h10, 0] += r; hue10Sum[h10, 1] += g; hue10Sum[h10, 2] += b;

            int gIdx = tone.Group switch { "鮮豔" => 0, "明亮" => 1, "昏暗" => 2, _ => 3 };
            res.GroupCount[gIdx]++;
            groupSum[gIdx, 0] += r; groupSum[gIdx, 1] += g; groupSum[gIdx, 2] += b;
        }

        for (int c = 0; c < 10; c++)
            if (res.Hue10Count[c] > 0)
                res.Hue10Avg[c] = Avg(hue10Sum[c, 0], hue10Sum[c, 1], hue10Sum[c, 2], res.Hue10Count[c]);

        for (int gI = 0; gI < 4; gI++)
            if (res.GroupCount[gI] > 0)
                res.GroupAvg[gI] = Avg(groupSum[gI, 0], groupSum[gI, 1], groupSum[gI, 2], res.GroupCount[gI]);

        if (res.PieAchromaticCount > 0)
            res.PieAchromaticAvg = Avg(achSum[0], achSum[1], achSum[2], res.PieAchromaticCount);
    }

    private static Color Avg(long r, long g, long b, long n) =>
        Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));

    // ==========================================
    // 配色角色與視覺重要性分數
    // ==========================================
    private static void BuildPalette(ImageAnalysisResult res, byte[] data, int w, int h, List<int> samples,
        out byte[]? labels, out int bgId)
    {
        labels = null;
        bgId = -1;

        var (centroids, counts) = KMeansCore(samples, 8);
        var ids = new List<int>();
        for (int j = 0; j < centroids.Length; j++)
            if (counts[j] > 0) ids.Add(j);
        if (ids.Count == 0) return;
        int k = centroids.Length;

        // 全圖逐像素標記到最近的聚類，同時累計每群的平均飽和度
        int pxCount = w * h;
        labels = new byte[pxCount];
        Array.Fill(labels, byte.MaxValue);
        var satSum = new double[k];
        var pixCnt = new long[k];

        for (int p = 0; p < pxCount; p++)
        {
            int i = p * 4;
            if (data[i + 3] < 128) continue;
            double b = data[i], g = data[i + 1], r = data[i + 2];

            int bestId = ids[0];
            double bestD = double.MaxValue;
            foreach (int j in ids)
            {
                double dr = r - centroids[j][0], dg = g - centroids[j][1], db = b - centroids[j][2];
                double d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; bestId = j; }
            }
            labels[p] = (byte)bestId;
            pixCnt[bestId]++;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            satSum[bestId] += max <= 0 ? 0 : (max - min) / max;
        }

        long validTotal = pixCnt.Sum();
        if (validTotal == 0) return;

        // 各群代表色的 Lab 值與兩兩 ΔE 表
        var colors = new Color[k];
        var labs = new (double L, double A, double B)[k];
        foreach (int j in ids)
        {
            colors[j] = Color.FromRgb(
                (byte)Math.Clamp(Math.Round(centroids[j][0]), 0, 255),
                (byte)Math.Clamp(Math.Round(centroids[j][1]), 0, 255),
                (byte)Math.Clamp(Math.Round(centroids[j][2]), 0, 255));
            labs[j] = ColorMath.RgbToLab(colors[j].R, colors[j].G, colors[j].B);
        }
        var deTable = new double[k, k];
        foreach (int a in ids)
            foreach (int b in ids)
                deTable[a, b] = ColorMath.DeltaE(labs[a], labs[b]);

        // 局部對比：沿聚類交界（右鄰、下鄰）累計相鄰區域的 ΔE，以接觸長度加權平均
        var contrastSum = new double[k];
        var contactCnt = new long[k];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x;
                byte la = labels[p];
                if (la == byte.MaxValue) continue;
                if (x + 1 < w) AccumulateContact(la, labels[p + 1], deTable, contrastSum, contactCnt);
                if (y + 1 < h) AccumulateContact(la, labels[p + w], deTable, contrastSum, contactCnt);
            }
        }

        // 背景偵測：背景的特徵不是「白」而是「大面積貼著圖片四邊」——
        // 統計影像最外圈像素的聚類歸屬，某群佔邊框過半即視為背景。
        // 滿版照片的邊框會被多群瓜分、無人過半，天然不會誤殺。
        if (IgnoreBackground && w > 2 && h > 2)
        {
            var borderCnt = new long[k];
            long borderTotal = 0;
            void CountBorder(byte lab)
            {
                if (lab == byte.MaxValue) return;
                borderCnt[lab]++;
                borderTotal++;
            }
            for (int x = 0; x < w; x++) { CountBorder(labels[x]); CountBorder(labels[(h - 1) * w + x]); }
            for (int y = 1; y < h - 1; y++) { CountBorder(labels[y * w]); CountBorder(labels[y * w + w - 1]); }

            if (borderTotal > 0)
            {
                int top = ids.OrderByDescending(j => borderCnt[j]).First();
                if (borderCnt[top] / (double)borderTotal > 0.5 && pixCnt[top] < validTotal)
                    bgId = top;
            }
        }
        long roleTotal = bgId >= 0 ? validTotal - pixCnt[bgId] : validTotal;

        // 依面積排序（背景群排除在角色之外）；主色＝最大群，Score 以主色為比較基準
        // （lambda 不能直接捕捉 out 參數，先複製成區域變數）
        int excludeId = bgId;
        var ordered = ids.Where(j => j != excludeId).OrderByDescending(j => pixCnt[j]).ToList();
        if (ordered.Count == 0) return;
        int mainId = ordered[0];

        double BaseScoreOf(int j)
        {
            double a = pixCnt[j] / (double)roleTotal; // 面積佔比以「非背景像素」為分母
            double s = satSum[j] / pixCnt[j];
            double dE = Math.Clamp(deTable[j, mainId] / 100.0, 0.0, 1.0);
            double c = contactCnt[j] > 0
                ? Math.Clamp(contrastSum[j] / contactCnt[j] / 100.0, 0.0, 1.0)
                : 0.0;
            return Math.Pow(a, AlphaArea)
                 * Math.Pow(s, BetaSaturation)
                 * Math.Pow(dE, GammaDeltaE)
                 * Math.Pow(c, DeltaLocalContrast);
        }

        // 色相互補度與 PCCS 色調倍率只在點綴色排序使用，不影響主色／次要色／輔助色
        double AccentScoreOf(int j) =>
            BaseScoreOf(j)
            * Math.Pow(HueComplement(colors[mainId], colors[j]), EpsilonHueComplement)
            * ToneAccentWeight(Pccs.ClassifyRgb(
                colors[j].R / 255.0, colors[j].G / 255.0, colors[j].B / 255.0));

        string[] mainRoles = { "主色", "次要色", "輔助色" };
        for (int i = 0; i < mainRoles.Length && i < ordered.Count; i++)
        {
            int j = ordered[i];
            res.Palette.Add((mainRoles[i], colors[j], pixCnt[j] / (double)validTotal, BaseScoreOf(j)));
        }

        // 點綴色：前三大以外、點綴加權分數最高的群（佔比 <0.2% 視為雜訊）
        var candidates = ordered.Skip(3).Where(j => pixCnt[j] / (double)validTotal >= 0.002).ToList();
        if (candidates.Count > 0)
        {
            int accent = candidates.OrderByDescending(AccentScoreOf).First();
            if (AccentScoreOf(accent) <= 0) // 全灰圖等退化情況：退回與主色 ΔE 最大者
                accent = candidates.OrderByDescending(j => deTable[j, mainId]).First();
            res.Palette.Add(("點綴色", colors[accent], pixCnt[accent] / (double)validTotal, AccentScoreOf(accent)));
        }

        // 被排除的背景照樣列出來，讓使用者看得到濾掉了什麼
        if (bgId >= 0)
            res.Palette.Add(("背景", colors[bgId], pixCnt[bgId] / (double)validTotal, 0));
    }

    private static void AccumulateContact(byte la, byte lb, double[,] deTable, double[] contrastSum, long[] contactCnt)
    {
        if (lb == byte.MaxValue || lb == la) return;
        double d = deTable[la, lb];
        contrastSum[la] += d; contactCnt[la]++;
        contrastSum[lb] += d; contactCnt[lb]++;
    }

    // 色相互補度 H：sin(色相差/2)，0° → 0、90° → 0.71、180°（互補色）→ 1 最高
    // 任一方接近無彩色時色相無意義，回傳 1.0 中性值交給其他因子決定
    private static double HueComplement(Color main, Color cand)
    {
        if (Chroma01(main) < 0.08 || Chroma01(cand) < 0.08) return 1.0;

        (double h1, _, _) = ColorMath.RgbToHls(main.R, main.G, main.B);
        (double h2, _, _) = ColorMath.RgbToHls(cand.R, cand.G, cand.B);

        double diff = Math.Abs(h1 - h2);
        if (diff > 180) diff = 360 - diff;

        return Math.Sin(diff * Math.PI / 360.0);
    }

    private static double Chroma01(Color c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        return (max - min) / 255.0;
    }

    // PCCS 色調倍率 T：高彩度色調加分、高明度低彩度／灰濁減分、無彩色重扣（範圍 0.3~1.3）
    private static double ToneAccentWeight(PccsTone tone) => tone.Index switch
    {
        5 or 7 => 1.3,         // V 鮮豔、S 強烈
        6 => 1.15,             // B 明亮
        8 => 1.1,              // Dp 深
        9 or 13 => 0.7,        // P 淡、Vp 極淡（高明度低彩度通常不是點綴色）
        11 or 14 or 15 => 0.7, // Dl 濁、Lgr 淺灰調、Gr 灰調
        16 => 0.8,             // Dgr 深灰調
        <= 4 => 0.3,           // 無彩色 W/LG/MG/DG/BK
        _ => 1.0,              // L 淺、Dk 暗
    };

    // K-means 分群本體（回傳原始群心與計數，未排序）
    private static (double[][] Centroids, long[] Counts) KMeansCore(List<int> pixels, int k)
    {
        if (pixels.Count == 0) return (Array.Empty<double[]>(), Array.Empty<long>());

        int step = Math.Max(1, pixels.Count / 8000);
        var pts = new List<int>();
        for (int i = 0; i < pixels.Count; i += step) pts.Add(pixels[i]);
        int n = pts.Count;
        k = Math.Min(k, n);

        var rnd = new Random(20260710); // 固定種子，同一張圖結果穩定
        var centroids = new double[k][];
        centroids[0] = ToVec(pts[rnd.Next(n)]);
        for (int c = 1; c < k; c++)
        {
            // 挑離現有群心最遠的點當新群心（k-means++ 簡化版）
            int best = 0;
            double bestD = -1;
            for (int i = 0; i < n; i += 5)
            {
                var v = ToVec(pts[i]);
                double dMin = double.MaxValue;
                for (int j = 0; j < c; j++) dMin = Math.Min(dMin, Dist2(v, centroids[j]));
                if (dMin > bestD) { bestD = dMin; best = i; }
            }
            centroids[c] = ToVec(pts[best]);
        }

        var counts = new long[k];
        for (int iter = 0; iter < 12; iter++)
        {
            var sums = new double[k][];
            for (int j = 0; j < k; j++) sums[j] = new double[3];
            Array.Clear(counts, 0, k);

            foreach (int p in pts)
            {
                var v = ToVec(p);
                int nearest = 0;
                double dMin = double.MaxValue;
                for (int j = 0; j < k; j++)
                {
                    double d = Dist2(v, centroids[j]);
                    if (d < dMin) { dMin = d; nearest = j; }
                }
                sums[nearest][0] += v[0]; sums[nearest][1] += v[1]; sums[nearest][2] += v[2];
                counts[nearest]++;
            }
            for (int j = 0; j < k; j++)
                if (counts[j] > 0)
                    centroids[j] = new[] { sums[j][0] / counts[j], sums[j][1] / counts[j], sums[j][2] / counts[j] };
        }

        return (centroids, counts);
    }

    private static double[] ToVec(int packed) =>
        new double[] { (packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF };

    private static double Dist2(double[] a, double[] b)
    {
        double dr = a[0] - b[0], dg = a[1] - b[1], db = a[2] - b[2];
        return dr * dr + dg * dg + db * db;
    }
}
