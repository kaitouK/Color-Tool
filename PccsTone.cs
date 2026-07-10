namespace ColorTool;

/// <summary>
/// PCCS 色調定義。
/// 分類座標採用 HLS 三角形的內在座標，而非 HLS 的 L/S：
///   C  = 純色成分（重心座標的 wc，等於 RGB 的 max-min，即 chroma）
///   Rl = 白黑比例 ww/(ww+wbk)，代表去掉純色後偏白的程度（0=全黑, 1=全白）
/// 分類採用加權 nearest-centroid：找 (C, Rl) 距離最近的代表點，
/// 區塊邊界即為 Voronoi 圖；HLS 的 S 對淡色永遠是 1，不適合直接當分類軸。
/// </summary>
public sealed record PccsTone(int Index, string Code, string Name, string Group, bool IsAchromatic, double RepC, double RepRl)
{
    /// <summary>此色調代表點的重心座標權重 (wc, ww, wbk)。</summary>
    public (double Wc, double Ww, double Wbk) RepresentativeWeights()
    {
        double wc = RepC;
        double ww = (1.0 - wc) * RepRl;
        double wbk = (1.0 - wc) * (1.0 - RepRl);
        return (wc, ww, wbk);
    }

    /// <summary>此色調代表點換算成 HLS 的 (L, S)，方便套用到滑桿。</summary>
    public (double L, double S) RepresentativeHls()
    {
        (double wc, double ww, _) = RepresentativeWeights();
        double v = wc + ww;
        double sHsv = v == 0 ? 0 : wc / v;
        return ColorMath.HsvToHls(sHsv, v);
    }
}

public static class Pccs
{
    // Voronoi 距離的軸向權重（σ 越小該軸的差異越敏感），微調色調範圍時從這裡下手
    public static double SigmaC = 0.16;
    public static double SigmaRl = 0.12;

    public static readonly PccsTone[] Tones =
    {
        new(0,  "W",   "白色",     "無彩色", true,  0.00, 0.95),
        new(1,  "LG",  "淺灰",     "無彩色", true,  0.00, 0.72),
        new(2,  "MG",  "中灰",     "無彩色", true,  0.00, 0.47),
        new(3,  "DG",  "深灰",     "無彩色", true,  0.00, 0.23),
        new(4,  "BK",  "黑色",     "無彩色", true,  0.00, 0.04),
        new(5,  "V",   "鮮豔色調", "鮮豔",   false, 0.90, 0.50),
        new(6,  "B",   "明亮色調", "明亮",   false, 0.66, 0.78),
        new(7,  "S",   "強烈色調", "鮮豔",   false, 0.66, 0.46),
        new(8,  "Dp",  "深色調",   "昏暗",   false, 0.66, 0.15),
        new(9,  "P",   "淡色調",   "明亮",   false, 0.40, 0.85),
        new(10, "L",   "淺色調",   "暗淡",   false, 0.40, 0.60),
        new(11, "Dl",  "濁色調",   "暗淡",   false, 0.40, 0.36),
        new(12, "Dk",  "暗色調",   "昏暗",   false, 0.40, 0.12),
        new(13, "Vp",  "極淡色調", "明亮",   false, 0.16, 0.85),
        new(14, "Lgr", "淺灰色調", "暗淡",   false, 0.16, 0.60),
        new(15, "Gr",  "灰色調",   "暗淡",   false, 0.16, 0.36),
        new(16, "Dgr", "深灰色調", "昏暗",   false, 0.16, 0.12),
    };

    /// <summary>Hue &amp; Tone 網格中有彩色調的顯示順序（由鮮豔到灰濁）。</summary>
    public static readonly int[] ChromaticDisplayOrder = { 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

    /// <summary>
    /// 繪圖專用的 Tone Region（雙系統的「視覺側」）：
    /// 每個有彩色調在 (C, Rl) 空間中指定一塊矩形範圍，映射到三角形後
    /// 以圓角多邊形呈現，模仿 PCCS 教科書色調圖的外觀。
    /// 分類（ClassifyCRl）仍走 nearest-centroid，兩套系統各自獨立，
    /// 調整視覺外觀不影響分類結果。
    /// 邊界值取自代表點之間的中點，讓區塊外觀與分類大致吻合。
    /// </summary>
    public sealed record PccsToneRegion(int ToneIndex, double C0, double C1, double Rl0, double Rl1);

    public static readonly PccsToneRegion[] Regions =
    {
        new(13, 0.08, 0.28, 0.725, 1.00),  // Vp
        new(14, 0.08, 0.28, 0.48,  0.725), // Lgr
        new(15, 0.08, 0.28, 0.24,  0.48),  // Gr
        new(16, 0.08, 0.28, 0.00,  0.24),  // Dgr
        new(9,  0.28, 0.53, 0.725, 1.00),  // P
        new(10, 0.28, 0.53, 0.48,  0.725), // L
        new(11, 0.28, 0.53, 0.24,  0.48),  // Dl
        new(12, 0.28, 0.53, 0.00,  0.24),  // Dk
        new(6,  0.53, 0.78, 0.62,  1.00),  // B
        new(7,  0.53, 0.78, 0.305, 0.62),  // S
        new(8,  0.53, 0.78, 0.00,  0.305), // Dp
        new(5,  0.78, 1.00, 0.00,  1.00),  // V
    };

    /// <summary>以三角形內在座標 (C, Rl) 分類色調：加權距離最近的代表點獲勝（Voronoi）。</summary>
    public static PccsTone ClassifyCRl(double c, double rl)
    {
        double best = double.MaxValue;
        PccsTone bestTone = Tones[0];

        foreach (var tone in Tones)
        {
            double dc = (c - tone.RepC) / SigmaC;
            double dr = (rl - tone.RepRl) / SigmaRl;
            double d = dc * dc + dr * dr;

            if (d < best)
            {
                best = d;
                bestTone = tone;
            }
        }
        return bestTone;
    }

    /// <summary>以三角形重心座標權重分類（wc=純色、ww=白、wbk=黑）。</summary>
    public static PccsTone ClassifyWeights(double wc, double ww, double wbk)
    {
        double denom = ww + wbk;
        double rl = denom <= 0 ? 0.5 : ww / denom;
        return ClassifyCRl(wc, rl);
    }

    /// <summary>以 0~1 的 RGB 分類。max-min 即 C；min 為白成分、1-max 為黑成分。</summary>
    public static PccsTone ClassifyRgb(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double c = max - min;
        double denom = min + (1.0 - max);
        double rl = denom <= 0 ? 0.5 : min / denom;
        return ClassifyCRl(c, rl);
    }
}
