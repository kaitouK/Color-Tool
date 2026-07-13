using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ColorTool;

/// <summary>
/// 調色盤匯出器。JSON 為主要格式，其餘（CSS/HTML/SVG/ASE/PNG）皆由 Palette 資料轉出。
/// 與 PaletteEngine 完全解耦：只讀 Palette，不觸碰產生邏輯；新增格式＝新增一個方法＋Save 的分支。
/// </summary>
public static class PaletteExporter
{
    public static void Save(Palette palette, string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        switch (ext)
        {
            case "json": File.WriteAllText(path, ToJson(palette), Encoding.UTF8); break;
            case "css": File.WriteAllText(path, ToCss(palette), Encoding.UTF8); break;
            case "html": File.WriteAllText(path, ToHtml(palette), Encoding.UTF8); break;
            case "svg": File.WriteAllText(path, ToSvg(palette), Encoding.UTF8); break;
            case "ase": File.WriteAllBytes(path, ToAse(palette)); break;
            case "png": SavePng(palette, path); break;
            default: throw new NotSupportedException($"不支援的匯出格式：{ext}");
        }
    }

    // ---------- JSON（主要格式） ----------
    public static string ToJson(Palette p)
    {
        var obj = new
        {
            palette = new
            {
                name = p.PaletteName,
                description = p.Description,
                createTime = p.CreateTime,
                colorSpace = p.ColorSpace,
                targetGamut = p.TargetGamut,
                version = p.Version,
            },
            colors = p.Entries.Select(e => new
            {
                index = e.Index,
                name = e.Name,
                hex = e.Hex,
                oklch = new { l = Math.Round(e.L, 4), c = Math.Round(e.C, 4), h = Math.Round(e.H, 1) },
                rgb = new { r = e.R, g = e.G, b = e.B },
                neutral = e.Neutral,
            }),
        };
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    // ---------- CSS Variables ----------
    public static string ToCss(Palette p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"/* {p.PaletteName} — {p.CreateTime}（{p.ColorSpace} / {p.TargetGamut}） */");
        sb.AppendLine(":root {");
        foreach (var e in p.Entries)
            sb.AppendLine($"  --{CssName(e.Name)}: {e.Hex};");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string CssName(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        return sb.ToString().Trim('-');
    }

    // ---------- HTML ----------
    public static string ToHtml(Palette p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{p.PaletteName}</title>");
        sb.AppendLine("<style>body{background:#141414;color:#ccc;font-family:sans-serif;padding:24px}" +
                      ".g{display:flex;flex-wrap:wrap;gap:6px}" +
                      ".s{width:84px}.c{height:56px;border-radius:6px}" +
                      ".n{font-size:11px;margin-top:3px;word-break:break-all}.x{font-size:10px;color:#888}</style>");
        sb.AppendLine($"</head><body><h2>{p.PaletteName}</h2>" +
                      $"<p>{p.CreateTime}｜{p.ColorSpace}｜{p.TargetGamut}｜{p.Entries.Count} colors</p><div class=\"g\">");
        foreach (var e in p.Entries)
            sb.AppendLine($"<div class=\"s\"><div class=\"c\" style=\"background:{e.Hex}\"></div>" +
                          $"<div class=\"n\">{e.Name}</div><div class=\"x\">{e.Hex}</div></div>");
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    // ---------- SVG ----------
    public static string ToSvg(Palette p)
    {
        const int cell = 44, gap = 3;
        int cols = Math.Clamp(p.SuggestedColumns, 1, 64);
        int rows = (p.Entries.Count + cols - 1) / cols;
        int wPx = cols * (cell + gap) + gap;
        int hPx = rows * (cell + gap) + gap;

        var sb = new StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{wPx}\" height=\"{hPx}\" viewBox=\"0 0 {wPx} {hPx}\">");
        sb.AppendLine($"<!-- {p.PaletteName} — {p.CreateTime} -->");
        for (int i = 0; i < p.Entries.Count; i++)
        {
            var e = p.Entries[i];
            int x = gap + (i % cols) * (cell + gap);
            int y = gap + (i / cols) * (cell + gap);
            sb.AppendLine($"<rect x=\"{x}\" y=\"{y}\" width=\"{cell}\" height=\"{cell}\" rx=\"4\" fill=\"{e.Hex}\"><title>{e.Name} {e.Hex}</title></rect>");
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    // ---------- ASE（Adobe Swatch Exchange） ----------
    public static byte[] ToAse(Palette p)
    {
        using var ms = new MemoryStream();
        void U16(ushort v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        void U32(uint v) { U16((ushort)(v >> 16)); U16((ushort)v); }
        void F32(float v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            ms.Write(bytes, 0, 4);
        }

        ms.Write(Encoding.ASCII.GetBytes("ASEF"), 0, 4); // 簽名
        U16(1); U16(0);                                  // 版本 1.0
        U32((uint)p.Entries.Count);                      // 區塊數

        foreach (var e in p.Entries)
        {
            string name = e.Name;
            int nameChars = name.Length + 1;                       // 含結尾 null
            uint blockLen = (uint)(2 + nameChars * 2 + 4 + 12 + 2); // 名稱長度欄+名稱+模型+RGB 浮點+色彩型別

            U16(0x0001);       // color entry
            U32(blockLen);
            U16((ushort)nameChars);
            foreach (char ch in name) U16(ch); // UTF-16BE
            U16(0);
            ms.Write(Encoding.ASCII.GetBytes("RGB "), 0, 4);
            F32(e.R / 255f); F32(e.G / 255f); F32(e.B / 255f);
            U16(2);            // normal color
        }
        return ms.ToArray();
    }

    // ---------- PNG ----------
    public static void SavePng(Palette p, string path)
    {
        const int cell = 32;
        int cols = Math.Clamp(p.SuggestedColumns, 1, 64);
        int rows = (p.Entries.Count + cols - 1) / cols;
        int w = cols * cell, h = rows * cell;

        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        int stride = w * 4;
        byte[] px = new byte[h * stride];

        for (int i = 0; i < p.Entries.Count; i++)
        {
            var e = p.Entries[i];
            int cx = (i % cols) * cell, cy = (i / cols) * cell;
            for (int y = cy; y < cy + cell; y++)
            {
                int off = y * stride + cx * 4;
                for (int x = 0; x < cell; x++)
                {
                    px[off] = e.B; px[off + 1] = e.G; px[off + 2] = e.R; px[off + 3] = 255;
                    off += 4;
                }
            }
        }
        wb.WritePixels(new Int32Rect(0, 0, w, h), px, stride, 0);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(wb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
