namespace ColorTool;

/// <summary>
/// 共用的色彩空間轉換數學（HLS/HSV/RGB），供 MainWindow 與圖片分析器使用。
/// </summary>
public static class ColorMath
{
    // HLS 轉 RGB
    public static (int R, int G, int B) HlsToRgb(double h, double l, double s)
    {
        if (s == 0)
        {
            // 飽和度為 0 就是灰色，RGB 三值相同，取決於亮度
            int gray = (int)Math.Round(l * 255.0);
            return (gray, gray, gray);
        }

        double q = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
        double p = 2.0 * l - q;

        double hk = h / 360.0; // 將角度縮放到 0~1

        double r = HueToRgb(p, q, hk + 1.0 / 3.0);
        double g = HueToRgb(p, q, hk);
        double b = HueToRgb(p, q, hk - 1.0 / 3.0);

        return ((int)Math.Round(r * 255.0), (int)Math.Round(g * 255.0), (int)Math.Round(b * 255.0));
    }

    private static double HueToRgb(double p, double q, double tc)
    {
        if (tc < 0) tc += 1.0;
        if (tc > 1.0) tc -= 1.0;

        if (tc < 1.0 / 6.0) return p + ((q - p) * 6.0 * tc);
        if (tc < 1.0 / 2.0) return q;
        if (tc < 2.0 / 3.0) return p + ((q - p) * (2.0 / 3.0 - tc) * 6.0);

        return p;
    }

    // HSV 轉 RGB
    public static (int R, int G, int B) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;

        double r = 0, g = 0, b = 0;
        if (h >= 0 && h < 60) { r = c; g = x; b = 0; }
        else if (h >= 60 && h < 120) { r = x; g = c; b = 0; }
        else if (h >= 120 && h < 180) { r = 0; g = c; b = x; }
        else if (h >= 180 && h < 240) { r = 0; g = x; b = c; }
        else if (h >= 240 && h < 300) { r = x; g = 0; b = c; }
        else if (h >= 300 && h < 360) { r = c; g = 0; b = x; }

        return ((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    // HSV 轉 HLS
    public static (double L, double S) HsvToHls(double hsvS, double hsvV)
    {
        double l = hsvV * (1.0 - hsvS / 2.0);
        double s = 0;
        if (l > 0 && l < 1) s = (hsvV - l) / Math.Min(l, 1.0 - l);
        return (l, s);
    }

    // HLS 轉 HSV
    public static (double S, double V) HlsToHsv(double hlsL, double hlsS)
    {
        double v = hlsL + hlsS * Math.Min(hlsL, 1.0 - hlsL);
        double s = (v == 0) ? 0 : 2.0 * (1.0 - hlsL / v);
        return (s, v);
    }

    // sRGB 轉 CIE Lab（D65 白點），供 ΔE 色差計算使用
    public static (double L, double A, double B) RgbToLab(int r, int g, int b)
    {
        double rl = SrgbToLinear(r / 255.0);
        double gl = SrgbToLinear(g / 255.0);
        double bl = SrgbToLinear(b / 255.0);

        double x = (rl * 0.4124564 + gl * 0.3575761 + bl * 0.1804375) / 0.95047;
        double y = rl * 0.2126729 + gl * 0.7151522 + bl * 0.0721750;
        double z = (rl * 0.0193339 + gl * 0.1191920 + bl * 0.9503041) / 1.08883;

        double fx = LabF(x), fy = LabF(y), fz = LabF(z);
        return (116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
    }

    private static double SrgbToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double LabF(double t) =>
        t > 216.0 / 24389.0 ? Math.Cbrt(t) : (24389.0 / 27.0 * t + 16.0) / 116.0;

    // ΔE76：Lab 空間的歐氏距離（黑↔白約為 100）
    public static double DeltaE((double L, double A, double B) p, (double L, double A, double B) q)
    {
        double dl = p.L - q.L, da = p.A - q.A, db = p.B - q.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    // sRGB 轉 OkLCH（L: 0~1、C: 彩度、H: 角度 0~360）——近似色／漸層色色票使用
    public static (double L, double C, double H) RgbToOklch(int r, int g, int b)
    {
        double rl = SrgbToLinear(r / 255.0);
        double gl = SrgbToLinear(g / 255.0);
        double bl = SrgbToLinear(b / 255.0);

        double l = 0.4122214708 * rl + 0.5363325363 * gl + 0.0514459929 * bl;
        double m = 0.2119034982 * rl + 0.6806995451 * gl + 0.1073969566 * bl;
        double s = 0.0883024619 * rl + 0.2817188376 * gl + 0.6299787005 * bl;

        double l_ = Math.Cbrt(l), m_ = Math.Cbrt(m), s_ = Math.Cbrt(s);

        double okL = 0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_;
        double okA = 1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_;
        double okB = 0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_;

        double c = Math.Sqrt(okA * okA + okB * okB);
        double h = Math.Atan2(okB, okA) * 180.0 / Math.PI;
        if (h < 0) h += 360;
        return (okL, c, h);
    }

    // OkLCH 轉 sRGB，超出色域時直接夾住線性 RGB
    public static (int R, int G, int B) OklchToRgb(double okL, double c, double hDeg)
    {
        double hr = hDeg * Math.PI / 180.0;
        double okA = c * Math.Cos(hr);
        double okB = c * Math.Sin(hr);

        double l_ = okL + 0.3963377774 * okA + 0.2158037573 * okB;
        double m_ = okL - 0.1055613458 * okA - 0.0638541728 * okB;
        double s_ = okL - 0.0894841775 * okA - 1.2914855480 * okB;

        double l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;

        double rl = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double gl = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

        return (
            (int)Math.Round(LinearToSrgb(Math.Clamp(rl, 0.0, 1.0)) * 255),
            (int)Math.Round(LinearToSrgb(Math.Clamp(gl, 0.0, 1.0)) * 255),
            (int)Math.Round(LinearToSrgb(Math.Clamp(bl, 0.0, 1.0)) * 255));
    }

    private static double LinearToSrgb(double c) =>
        c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;

    // RGB 轉 HLS（h: 0~360, l/s: 0~1）
    public static (double H, double L, double S) RgbToHls(int r, int g, int b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double delta = max - min;

        double l = (max + min) / 2.0;

        double s = 0;
        double denom = 1.0 - Math.Abs(2.0 * l - 1.0);
        if (delta > 0 && denom > 0) s = delta / denom;

        double h = 0;
        if (delta > 0)
        {
            if (max == rf) h = 60 * (((gf - bf) / delta) % 6);
            else if (max == gf) h = 60 * (((bf - rf) / delta) + 2);
            else h = 60 * (((rf - gf) / delta) + 4);
            if (h < 0) h += 360;
        }
        return (h, l, s);
    }
}
