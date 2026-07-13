using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace ColorTool;

public partial class MainWindow : Window
{
    private bool isSliderHsvMode = false; // 紀錄目前滑桿是 HSL 還是 HSV
    private bool isUpdatingFromWheel = false;
    private double lastDrawnHue = -1;
    private int lastToneIndex = -1;

    // 定義內三角形的三個頂點 (相對於 120x120 的 Grid)
    private readonly Point pColor = new Point(120, 60); // 純色頂點 (指向右方)
    private readonly Point pWhite = new Point(30, 8);   // 純白頂點 (左上)
    private readonly Point pBlack = new Point(30, 112); // 純黑頂點 (左下)

    // 大 PCCS 色調地圖的「彈頭」外形參數 (相對於 370x360 的畫布)：
    // 左緣 x=90（灰軸側、垂直平底），尖端 x=360（V 色調），中線 y=180，左緣最大半高 156
    private const double BulletLeft = 90, BulletRight = 360, BulletMidY = 180, BulletHalfH = 156;

    // Tone Region 的向量圖形與其填色筆刷（索引＝色調 Index）
    private readonly System.Windows.Shapes.Path[] regionPaths = new System.Windows.Shapes.Path[17];
    private readonly SolidColorBrush[] regionBrushes = new SolidColorBrush[17];

    private readonly Border[] achCells = new Border[5];
    private readonly Border[,] hueToneCells = new Border[17, 24];
    private readonly Border[] achLevelCells = new Border[11];
    private Grid? achInnerGrid;
    private static readonly Color CellOffColor = Color.FromRgb(0x1E, 0x1E, 0x1E);

    // 10 色相環（每 36° 一格）與色調四大分類的顯示名稱
    private static readonly string[] Hue10Codes = { "R", "YR", "Y", "GY", "G", "BG", "B", "PB", "P", "RP" };
    private static readonly string[] Hue10Names = { "紅", "紅黃", "黃", "綠黃", "綠", "藍綠", "藍", "紫藍", "紫", "紅紫" };
    private static readonly string[] GroupNames = { "鮮豔", "明亮", "昏暗", "暗淡" };
    private static readonly string[] LightBandNames = { "0~0.25", "0.26~0.5", "0.51~0.75", "0.76~1" };

    // 甜甜圈的顯示模式切換（點擊圖面切換）與最後一次分析結果
    private ImageAnalysisResult? lastAnalysis;
    private bool huePieRepMode;    // 色相平衡：false=圖中平均色、true=各色相的明亮色調（B）代表色
    private bool tonePieBlueMode;  // 色調（有彩）：true=以 10 色相環的藍（216°）呈現各色調群
    private bool tonePieNBlueMode; // 色調（含無彩）：同上

    // PCCS 配色方案（5 列 × 5 色票）
    private readonly Border[,] schemeCells = new Border[5, 5];
    private readonly SolidColorBrush[,] schemeBrushes = new SolidColorBrush[5, 5];
    private static readonly string[] SchemeNames = { "同色調", "同色相", "卡瑪伊尤", "對決色調", "五色相" };

    // 近似色／漸層色色票（7x5，主色置中）
    // 色票不隨滑桿即時更新：按標頭右側的顏色方塊才以目前顏色重新產生（swatchBaseRgb）
    private readonly Border[,] swatchCells = new Border[5, 7];
    private readonly SolidColorBrush[,] swatchBrushes = new SolidColorBrush[5, 7];
    private bool similarTabActive = true;
    private (int R, int G, int B) currentRgb = (0, 0, 0);
    private (int R, int G, int B) swatchBaseRgb = (0, 0, 0);
    private int swatchMarkerRow = 2, swatchMarkerCol = 3;
    private readonly SolidColorBrush swatchApplyBrush = new(Colors.Black);
    private string? lastImagePath;

    // 圖片預覽的三種模式：原圖／灰階／四階化（灰階與海報化影像延遲產生並快取）
    private BitmapSource? previewOriginal, previewGray, previewPoster;

    public MainWindow()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "程式啟動發生錯誤");
        }
        CreateAchromaticBar();
        CreatePccsRegions();
        CreatePccsLabels();
        CreateHueToneGrid();
        CreateSwatchGrid();
        CreateSchemePanel();
        DrawHueRing(); // 啟動時繪製外環
        UpdateUI();
        SwatchApplyButton.Background = swatchApplyBrush;
        RefreshSwatchBase(); // 啟動時以初始顏色填滿色票

        // 點擊甜甜圈切換顯示模式
        HuePieCanvas.MouseLeftButtonDown += (_, _) => { huePieRepMode = !huePieRepMode; UpdatePies(); };
        ToneGroupPieCanvas.MouseLeftButtonDown += (_, _) => { tonePieBlueMode = !tonePieBlueMode; UpdatePies(); };
        ToneGroupNPieCanvas.MouseLeftButtonDown += (_, _) => { tonePieNBlueMode = !tonePieNBlueMode; UpdatePies(); };

        // 調色盤引擎的 Preset 清單
        foreach ((string name, _) in PaletteEngine.Presets)
            PresetCombo.Items.Add(new ComboBoxItem { Content = name, FontSize = 11 });
        PresetCombo.SelectedIndex = 0;
    }

    // 按鈕切換事件
    private void ModeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        isSliderHsvMode = !isSliderHsvMode;

        // 讀取切換前的滑桿值
        double val1 = L_Slider.Value / 100.0;
        double val2 = S_Slider.Value / 100.0;

        isUpdatingFromWheel = true; // 鎖定更新，避免滑桿亂跳觸發事件

        if (isSliderHsvMode)
        {
            ModeToggleButton.Content = "目前的滑桿屬性：HSV (點擊切換為 HSL)";
            ModeToggleButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CE9178"));
            L_Label.Text = "明度 (V) - 明暗程度";
            S_Label.Text = "飽和度 (S) [HSV] - 鮮豔程度";

            // 原本是 HLS，將 L 和 S 轉成 HSV 的 V 和 S 給滑桿
            (double hsv_s, double hsv_v) = ColorMath.HlsToHsv(val1, val2);
            L_Slider.Value = hsv_v * 100;
            S_Slider.Value = hsv_s * 100;
        }
        else
        {
            ModeToggleButton.Content = "目前的滑桿屬性：HSL (點擊切換為 HSV)";
            ModeToggleButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4EC9B0"));
            L_Label.Text = "亮度 (L) - 明暗程度";
            S_Label.Text = "飽和度 (S) [HLS] - 鮮豔程度";

            // 原本是 HSV，將 V 和 S 轉成 HLS 的 L 和 S 給滑桿
            (double hls_l, double hls_s) = ColorMath.HsvToHls(val2, val1);
            L_Slider.Value = hls_l * 100;
            S_Slider.Value = hls_s * 100;
        }

        isUpdatingFromWheel = false;
        UpdateUI();
    }

    // ==========================================
    // 繪圖邏輯
    // ==========================================
    private void DrawHueRing()
    {
        int size = 180;
        WriteableBitmap wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        int stride = size * 4;
        byte[] pixels = new byte[size * stride];

        // 外徑含反鋸齒必須留在 0~179 的像素範圍內，否則圓環下緣會被裁切
        double rOut = 88, rIn = 68; // 圓環的外徑與內徑
        double cx = 89.5, cy = 89.5;
        double aaWidth = 1.0; // 反鋸齒過渡區寬度（像素）

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                int index = y * stride + x * 4;
                if (dist >= (rIn - aaWidth) && dist <= (rOut + aaWidth))
                {
                    double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                    if (angle < 0) angle += 360;

                    (int r, int g, int b) = ColorMath.HlsToRgb(angle, 0.5, 1.0);

                    // 抗鋸齒用：計算外邊緣與內邊緣的透明度權重 (Alpha)
                    double alpha = 1.0;
                    if (dist > rOut)
                    {
                        alpha = 1.0 - (dist - rOut) / aaWidth; // 外邊緣淡出
                    }
                    else if (dist < rIn)
                    {
                        alpha = 1.0 - (rIn - dist) / aaWidth; // 內邊緣淡出
                    }
                    alpha = Math.Clamp(alpha, 0.0, 1.0);

                    pixels[index] = (byte)b;
                    pixels[index + 1] = (byte)g;
                    pixels[index + 2] = (byte)r;
                    pixels[index + 3] = (byte)(alpha * 255);
                }
            }
        }
        wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, stride, 0);
        HueRingImage_HSL.Source = wb;
        HueRingImage_HSV.Source = wb;
    }

    private void DrawSquare(double hue)
    {
        int size = 120;       // 畫布總大小維持 120
        int sqSize = 96;      // 方形的實際視覺大小 96
        int offset = 12;      // 上下左右各留白 12 像素 ((120-96)/2)

        WriteableBitmap wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        int stride = size * 4;
        byte[] pixels = new byte[size * stride];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = y * stride + x * 4;

                // 判斷像素是否落在中間的 96x96 方形範圍內
                if (x >= offset && x < offset + sqSize && y >= offset && y < offset + sqSize)
                {
                    // 換算 0.0 ~ 1.0 的比例 (根據 96x96 的區域)
                    double s = (x - offset) / (double)(sqSize - 1);
                    double v = 1.0 - ((y - offset) / (double)(sqSize - 1));

                    (int r, int g, int b) = ColorMath.HsvToRgb(hue, s, v);

                    pixels[index] = (byte)b;
                    pixels[index + 1] = (byte)g;
                    pixels[index + 2] = (byte)r;
                    pixels[index + 3] = 255;
                }
                else
                {
                    // 範圍外設定為完全透明
                    pixels[index + 3] = 0;
                }
            }
        }
        wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, stride, 0);
        InnerShapeImage_HSV.Source = wb;
    }

    private void DrawTriangle(double hue)
    {
        int size = 120;
        WriteableBitmap wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        int stride = size * 4;
        byte[] pixels = new byte[size * stride];

        (int pureR, int pureG, int pureB) = ColorMath.HlsToRgb(hue, 0.5, 1.0);
        double aaWidth = 1.0; // 反鋸齒過渡寬度

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Point p = new Point(x, y);
                CalculateBarycentric(new Point(x, y), pColor, pWhite, pBlack, out double wc, out double ww, out double wb_weight);

                int index = y * stride + x * 4;
                if (wc >= -0.01 && ww >= -0.01 && wb_weight >= -0.01) // 允許邊緣有極微小誤差
                {
                    double r = wc * pureR + ww * 255 + wb_weight * 0;
                    double g = wc * pureG + ww * 255 + wb_weight * 0;
                    double b = wc * pureB + ww * 255 + wb_weight * 0;

                    // 計算點到三角形三條邊的最短距離，用來做邊緣平滑
                    double d1 = DistanceToLineSegment(p, pColor, pWhite);
                    double d2 = DistanceToLineSegment(p, pWhite, pBlack);
                    double d3 = DistanceToLineSegment(p, pBlack, pColor);
                    double minDistToEdge = Math.Min(d1, Math.Min(d2, d3));

                    double alpha = 1.0;
                    // 如果點在三角形外面，或者在非常靠近邊緣的內部，就進行淡出
                    bool isInside = (wc >= 0 && ww >= 0 && wb_weight >= 0);

                    if (!isInside)
                    {
                        alpha = 1.0 - (minDistToEdge / aaWidth);
                    }
                    else if (minDistToEdge < 0.5) // 內部邊緣微調平滑
                    {
                        alpha = 0.5 + (minDistToEdge / 1.0);
                    }

                    alpha = Math.Clamp(alpha, 0.0, 1.0);

                    if (alpha > 0)
                    {
                        pixels[index] = (byte)Math.Clamp(b, 0, 255);
                        pixels[index + 1] = (byte)Math.Clamp(g, 0, 255);
                        pixels[index + 2] = (byte)Math.Clamp(r, 0, 255);
                        pixels[index + 3] = (byte)(alpha * 255);
                    }
                }
            }
        }
        wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, stride, 0);
        InnerShapeImage_HSL.Source = wb;
    }

    // ==========================================
    // PCCS 色調地圖（視覺側：Tone Region 向量圖形）
    // 分類仍走 Pccs.ClassifyCRl 的 nearest-centroid，這裡只負責畫
    // ==========================================

    // 彈頭外形：彩度 c 處的半高。左緣（c=0）全高、越靠 V 尖端越窄，
    // 用四分之一橢圓輪廓做出「平底＋圓錐收尖」的常見 PCCS 色調圖形狀
    private static double BulletHalfHeight(double c) =>
        BulletHalfH * Math.Sqrt(Math.Max(0.0, 1.0 - c * c));

    // (C, Rl) 座標映射到彈頭形畫布上的點
    private Point CRlToPoint(double c, double rl)
    {
        double x = BulletLeft + c * (BulletRight - BulletLeft);
        double hh = BulletHalfHeight(c);
        return new Point(x, BulletMidY - (rl - 0.5) * 2.0 * hh);
    }

    private void CreatePccsRegions()
    {
        const int edgeSamples = 8; // 上下兩條曲線邊的取樣段數
        foreach (var region in Pccs.Regions)
        {
            var t = Pccs.Tones[region.ToneIndex];

            // 上邊 C0→C1、下邊 C1→C0 沿彈頭輪廓取樣；左右兩側為垂直直線，由端點自然連接
            var pts = new List<Point>();
            for (int i = 0; i <= edgeSamples; i++)
                pts.Add(CRlToPoint(region.C0 + (region.C1 - region.C0) * i / (double)edgeSamples, region.Rl1));
            for (int i = 0; i <= edgeSamples; i++)
                pts.Add(CRlToPoint(region.C1 - (region.C1 - region.C0) * i / (double)edgeSamples, region.Rl0));

            // 去除重複點（V 區在尖端會塌縮成同一點）
            for (int i = pts.Count - 1; i >= 0; i--)
            {
                if ((pts[i] - pts[(i + 1) % pts.Count]).Length < 0.75)
                    pts.RemoveAt(i);
            }
            if (pts.Count < 3) continue;

            // 往質心內縮，做出區塊之間的間隙
            double cx = pts.Average(p => p.X);
            double cy = pts.Average(p => p.Y);
            for (int i = 0; i < pts.Count; i++)
            {
                Vector dir = new Point(cx, cy) - pts[i];
                if (dir.Length > 0) pts[i] += dir / dir.Length * 2.5;
            }

            var brush = new SolidColorBrush(Colors.Gray);
            var path = new System.Windows.Shapes.Path
            {
                Data = BuildRoundedPolygon(pts, 3),
                Fill = brush,
                StrokeThickness = 2.5,
                StrokeLineJoin = PenLineJoin.Round,
                Cursor = Cursors.Hand,
                Tag = region.ToneIndex,
                ToolTip = $"{t.Code} {t.Name}"
            };
            path.MouseLeftButtonDown += PccsRegion_Click;
            PccsMapCanvas.Children.Add(path);
            regionPaths[region.ToneIndex] = path;
            regionBrushes[region.ToneIndex] = brush;
        }
    }

    // 把多邊形的角換成二次貝茲曲線，做出教科書色調圖的圓角外觀
    private static Geometry BuildRoundedPolygon(IReadOnlyList<Point> pts, double radius)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Point cur = pts[i];
                Point prev = pts[(i - 1 + n) % n];
                Point next = pts[(i + 1) % n];
                Vector toPrev = prev - cur;
                Vector toNext = next - cur;
                double rPrev = Math.Min(radius, toPrev.Length / 2.5);
                double rNext = Math.Min(radius, toNext.Length / 2.5);
                Point entry = cur + toPrev / toPrev.Length * rPrev;
                Point exit = cur + toNext / toNext.Length * rNext;

                if (i == 0) ctx.BeginFigure(entry, true, true);
                else ctx.LineTo(entry, true, true);
                ctx.QuadraticBezierTo(cur, exit, true, true);
            }
        }
        geo.Freeze();
        return geo;
    }

    // 色相改變時只需更新各區塊的填色
    private void UpdatePccsMapColors(double hue)
    {
        foreach (int idx in Pccs.ChromaticDisplayOrder)
        {
            (double l, double s) = Pccs.Tones[idx].RepresentativeHls();
            (int r, int g, int b) = ColorMath.HlsToRgb(hue, l, s);
            regionBrushes[idx].Color = Color.FromRgb((byte)r, (byte)g, (byte)b);
        }
    }

    // 目前顏色所在的色調區塊描白邊
    private void UpdatePccsHighlight(int toneIndex)
    {
        foreach (int idx in Pccs.ChromaticDisplayOrder)
            regionPaths[idx].Stroke = idx == toneIndex ? Brushes.White : null;
    }

    // 額外加入的數學輔助函式：計算點到線段的最短距離
    private double DistanceToLineSegment(Point p, Point a, Point b)
    {
        double l2 = (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
        if (l2 == 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        double t = ((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / l2;
        t = Math.Clamp(t, 0.0, 1.0);

        double projectionX = a.X + t * (b.X - a.X);
        double projectionY = a.Y + t * (b.Y - a.Y);

        return Math.Sqrt((p.X - projectionX) * (p.X - projectionX) + (p.Y - projectionY) * (p.Y - projectionY));
    }

    // ==========================================
    // PCCS 介面建立（無彩色軸、區塊標籤、Hue&Tone 網格）
    // ==========================================
    private void CreateAchromaticBar()
    {
        for (int i = 0; i < 5; i++)
        {
            AchromaticBarGrid.RowDefinitions.Add(new RowDefinition());
            var t = Pccs.Tones[i];
            (double l, _) = t.RepresentativeHls();
            (int r, int g, int b) = ColorMath.HlsToRgb(0, l, 0);

            var cell = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b)),
                Margin = new Thickness(0, 1.5, 0, 1.5),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = i,
                ToolTip = $"{t.Code} {t.Name}"
            };
            cell.MouseLeftButtonDown += AchCell_Click;
            cell.Child = new TextBlock
            {
                Text = t.Code,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(l > 0.5 ? Color.FromRgb(0x22, 0x22, 0x22) : Color.FromRgb(0xDD, 0xDD, 0xDD))
            };
            Grid.SetRow(cell, i);
            AchromaticBarGrid.Children.Add(cell);
            achCells[i] = cell;
        }
    }

    private void CreatePccsLabels()
    {
        foreach (int idx in Pccs.ChromaticDisplayOrder)
        {
            var t = Pccs.Tones[idx];
            Point pos = CRlToPoint(t.RepC, t.RepRl);

            // 代表色偏亮就用深色字，偏暗就用淺色字
            double approxL = t.RepC * 0.5 + (1 - t.RepC) * t.RepRl;
            var label = new TextBlock
            {
                Text = t.Code,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Opacity = 0.9,
                Foreground = new SolidColorBrush(approxL > 0.55 ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xF0, 0xF0, 0xF0)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(label, pos.X - t.Code.Length * 3.5);
            Canvas.SetTop(label, pos.Y - 8);
            PccsLabelCanvas.Children.Add(label);
        }
    }

    private void CreateHueToneGrid()
    {
        var g = HueToneGrid;
        // 欄配置：色調標籤 26 ＋ 24 個色相欄（星號）＋ 間隔 12 ＋ 明度標籤 22 ＋ 無彩色階欄（與色相欄同寬）
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        for (int c = 0; c < 24; c++)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 第 0 列：雙層標頭——hue 數字（與色調標籤同色）在上、該色相的色線在下
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < 24; c++)
        {
            (int r, int gg, int b) = ColorMath.HlsToRgb(c * 15, 0.5, 1.0);
            var header = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = $"{c * 15}°"
            };
            header.Children.Add(new TextBlock
            {
                Text = (c * 15).ToString(),
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 1)
            });
            header.Children.Add(new Border
            {
                Height = 4,
                Margin = new Thickness(0.75, 0, 0.75, 2),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)gg, (byte)b)),
                Opacity = 0.9
            });
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, c + 1);
            g.Children.Add(header);
        }
        var achHeader = new TextBlock
        {
            Text = "無彩",
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 2)
        };
        Grid.SetRow(achHeader, 0);
        Grid.SetColumn(achHeader, 25);
        Grid.SetColumnSpan(achHeader, 3);
        g.Children.Add(achHeader);

        // 12 個有彩色調列（列高由 SizeChanged 依欄寬回設，維持正方形）
        for (int ri = 0; ri < Pccs.ChromaticDisplayOrder.Length; ri++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            var tone = Pccs.Tones[Pccs.ChromaticDisplayOrder[ri]];

            var rowLabel = new TextBlock
            {
                Text = tone.Code,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                ToolTip = $"{tone.Code} {tone.Name}"
            };
            Grid.SetRow(rowLabel, ri + 1);
            Grid.SetColumn(rowLabel, 0);
            g.Children.Add(rowLabel);

            for (int c = 0; c < 24; c++)
            {
                var cell = new Border
                {
                    Margin = new Thickness(0.75),
                    Background = new SolidColorBrush(CellOffColor)
                };
                cell.MouseLeftButtonDown += HueToneCell_Click;
                Grid.SetRow(cell, ri + 1);
                Grid.SetColumn(cell, c + 1);
                g.Children.Add(cell);
                hueToneCells[tone.Index, c] = cell;
            }
        }

        // 無彩 11 色階：直欄（上白下黑），左側標記明度 0~100
        achInnerGrid = new Grid { VerticalAlignment = VerticalAlignment.Top };
        achInnerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        achInnerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int row = 0; row < 11; row++)
        {
            achInnerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            int level = 10 - row; // 上白（100）下黑（0）

            var lvlLabel = new TextBlock
            {
                Text = (level * 10).ToString(),
                FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            Grid.SetRow(lvlLabel, row);
            Grid.SetColumn(lvlLabel, 0);
            achInnerGrid.Children.Add(lvlLabel);

            var cell = new Border
            {
                Margin = new Thickness(0.75),
                Background = new SolidColorBrush(CellOffColor),
                ToolTip = $"無彩 L≈{level * 10}%"
            };
            cell.MouseLeftButtonDown += HueToneCell_Click;
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, 1);
            achInnerGrid.Children.Add(cell);
            achLevelCells[level] = cell;
        }
        Grid.SetRow(achInnerGrid, 1);
        Grid.SetRowSpan(achInnerGrid, 12);
        Grid.SetColumn(achInnerGrid, 26);
        Grid.SetColumnSpan(achInnerGrid, 2);
        g.Children.Add(achInnerGrid);

        g.SizeChanged += HueToneGrid_SizeChanged;
    }

    // 依目前的星號欄寬把列高設成等值，讓方格維持正方形
    private void HueToneGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double star = (HueToneGrid.ActualWidth - 26 - 12 - 22) / 25.0;
        if (double.IsNaN(star) || star < 4) return;

        for (int r = 1; r <= 12 && r < HueToneGrid.RowDefinitions.Count; r++)
            HueToneGrid.RowDefinitions[r].Height = new GridLength(star);
        if (achInnerGrid != null)
            foreach (var rd in achInnerGrid.RowDefinitions)
                rd.Height = new GridLength(star);
    }

    // 點擊 Hue & Tone 網格中有顏色的格子 → 把該格的平均色反映到色相環與滑桿
    private void HueToneCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (((Border)sender).Tag is Color c) ApplyRgbColor(c);
    }

    // ==========================================
    // 近似色／漸層色色票（OkLCH，7x5，主色置中）
    // ==========================================
    private void CreateSwatchGrid()
    {
        // 色塊之間無間隙；外圓角由 SwatchGrid_SizeChanged 以 Clip 統一裁切
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 7; col++)
            {
                var brush = new SolidColorBrush(Colors.Black);
                var cell = new Border
                {
                    Background = brush,
                    Cursor = Cursors.Hand,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Tag = row * 7 + col
                };
                cell.MouseLeftButtonDown += SwatchCell_Click;
                SwatchGrid.Children.Add(cell);
                swatchCells[row, col] = cell;
                swatchBrushes[row, col] = brush;
            }
        }
    }

    // 白框標記移到指定格
    private void SetSwatchMarker(int row, int col)
    {
        swatchCells[swatchMarkerRow, swatchMarkerCol].BorderThickness = new Thickness(0);
        swatchMarkerRow = row;
        swatchMarkerCol = col;
        swatchCells[row, col].BorderThickness = new Thickness(1.5);
    }

    // 以目前顏色為新基準重建色票，標記置回中心
    private void RefreshSwatchBase()
    {
        swatchBaseRgb = currentRgb;
        UpdateSwatchGrid(swatchBaseRgb.R, swatchBaseRgb.G, swatchBaseRgb.B);
        SetSwatchMarker(2, 3);
    }

    private void SwatchApply_Click(object sender, MouseButtonEventArgs e) => RefreshSwatchBase();

    // ==========================================
    // 右欄分頁：色調地圖 ↔ 調色盤引擎
    // ==========================================
    private Palette? lastPalette;

    private void RightTab_Click(object sender, MouseButtonEventArgs e)
    {
        bool mapActive = ReferenceEquals(sender, MapTab);

        var panelBg = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        var active = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0));
        var inactive = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));

        MapTab.Background = mapActive ? panelBg : Brushes.Transparent;
        PaletteTab.Background = mapActive ? Brushes.Transparent : panelBg;
        MapTabText.Foreground = mapActive ? active : inactive;
        PaletteTabText.Foreground = mapActive ? inactive : active;
        MapTabContent.Visibility = mapActive ? Visibility.Visible : Visibility.Collapsed;
        PaletteTabContent.Visibility = mapActive ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PresetCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PalettePreviewPanel == null) return; // InitializeComponent 期間
        int i = PresetCombo.SelectedIndex;
        if (i < 0 || i >= PaletteEngine.Presets.Length) return;
        FillPaletteForm(PaletteEngine.Presets[i].Create());
    }

    // Preset（一組設定）載入表單
    private void FillPaletteForm(PaletteConfig c)
    {
        HueCountBox.Text = c.HueCount.ToString();
        HueOffsetBox.Text = c.HueOffset.ToString(CultureInfo.InvariantCulture);
        HueDirCombo.SelectedIndex = c.Direction == HueDirection.Clockwise ? 0 : 1;
        LightModeCombo.SelectedIndex = (int)c.LightnessMode;
        LightStopsBox.Text = string.Join(",", c.LightnessStops.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        LightStartBox.Text = c.LightnessStart.ToString(CultureInfo.InvariantCulture);
        LightEndBox.Text = c.LightnessEnd.ToString(CultureInfo.InvariantCulture);
        LightStepsBox.Text = c.LightnessSteps.ToString();
        CurveCombo.SelectedIndex = (int)c.Curve;
        GammaBox.Text = c.Gamma.ToString(CultureInfo.InvariantCulture);
        ChromaModeCombo.SelectedIndex = (int)c.ChromaMode;
        ChromaStopsBox.Text = string.Join(",", c.ChromaStops.Select(v =>
            c.ChromaMode == ChromaMode.Absolute
                ? v.ToString(CultureInfo.InvariantCulture)
                : Math.Round(v * 100).ToString(CultureInfo.InvariantCulture)));
        NeutralCheck.IsChecked = c.IncludeNeutral;
        GamutCombo.SelectedIndex = (int)c.Gamut;
        OogCombo.SelectedIndex = (int)c.OutOfGamut;
        SortCombo.SelectedIndex = (int)c.Sorting;
        NamingCombo.SelectedIndex = (int)c.Naming;
        NameTemplateBox.Text = c.NameTemplate;
    }

    // 表單組回 PaletteConfig（引擎只吃設定，不碰 UI）
    private PaletteConfig BuildPaletteConfig()
    {
        static double[] ParseList(string s) =>
            s.Split(new[] { ',', ' ', '、', ';' }, StringSplitOptions.RemoveEmptyEntries)
             .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();

        var c = new PaletteConfig
        {
            PaletteName = "ColorTool Palette",
            HueCount = Math.Clamp(int.Parse(HueCountBox.Text), 1, 360),
            HueOffset = double.Parse(HueOffsetBox.Text, CultureInfo.InvariantCulture),
            Direction = HueDirCombo.SelectedIndex == 1 ? HueDirection.CounterClockwise : HueDirection.Clockwise,
            LightnessMode = (LightnessMode)Math.Max(0, LightModeCombo.SelectedIndex),
            LightnessStart = double.Parse(LightStartBox.Text, CultureInfo.InvariantCulture),
            LightnessEnd = double.Parse(LightEndBox.Text, CultureInfo.InvariantCulture),
            LightnessSteps = Math.Clamp(int.Parse(LightStepsBox.Text), 2, 64),
            Curve = (CurveType)Math.Max(0, CurveCombo.SelectedIndex),
            Gamma = double.Parse(GammaBox.Text, CultureInfo.InvariantCulture),
            ChromaMode = (ChromaMode)Math.Max(0, ChromaModeCombo.SelectedIndex),
            IncludeNeutral = NeutralCheck.IsChecked == true,
            Gamut = (TargetGamut)Math.Max(0, GamutCombo.SelectedIndex),
            OutOfGamut = (GamutStrategy)Math.Max(0, OogCombo.SelectedIndex),
            Sorting = (PaletteSorting)Math.Max(0, SortCombo.SelectedIndex),
            Naming = (NamingStyle)Math.Max(0, NamingCombo.SelectedIndex),
            NameTemplate = string.IsNullOrWhiteSpace(NameTemplateBox.Text) ? "H{H}-L{L}-C{C}" : NameTemplateBox.Text,
        };

        var lStops = ParseList(LightStopsBox.Text);
        if (lStops.Length > 0) c.LightnessStops = lStops;

        var cStops = ParseList(ChromaStopsBox.Text);
        if (cStops.Length > 0)
            c.ChromaStops = c.ChromaMode == ChromaMode.Absolute
                ? cStops
                : cStops.Select(v => v > 1 ? v / 100.0 : v).ToArray(); // 比例可用 90 或 0.9 表示
        return c;
    }

    private void GeneratePalette_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            lastPalette = PaletteEngine.GeneratePalette(BuildPaletteConfig());
            RenderPalettePreview(lastPalette);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"設定解析失敗：\n{ex.Message}", "調色盤引擎");
        }
    }

    private void RenderPalettePreview(Palette pal)
    {
        const int maxPreview = 1500;
        PalettePreviewPanel.Children.Clear();
        int shown = 0;
        foreach (var en in pal.Entries)
        {
            if (shown++ >= maxPreview) break;
            var col = Color.FromRgb(en.R, en.G, en.B);
            var cell = new Border
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(0.5),
                Background = new SolidColorBrush(col),
                Cursor = Cursors.Hand,
                ToolTip = $"{en.Name}\n{en.Hex}\noklch({en.L:F2} {en.C:F3} {en.H:F0}°)"
            };
            cell.MouseLeftButtonDown += (_, _) => ApplyRgbColor(col);
            PalettePreviewPanel.Children.Add(cell);
        }
        PaletteInfoText.Text = pal.Entries.Count > maxPreview
            ? $"共 {pal.Entries.Count} 色（預覽顯示前 {maxPreview} 色，匯出為完整內容）"
            : $"共 {pal.Entries.Count} 色（點色塊可套用到選色器）";
    }

    private void ExportPalette_Click(object sender, RoutedEventArgs e)
    {
        if (lastPalette == null)
        {
            GeneratePalette_Click(sender, e);
            if (lastPalette == null) return;
        }

        string[] ext = { "json", "css", "html", "svg", "ase", "png" };
        string fmt = ext[Math.Clamp(ExportFormatCombo.SelectedIndex, 0, ext.Length - 1)];
        var dlg = new SaveFileDialog
        {
            Title = "匯出調色盤",
            FileName = $"palette.{fmt}",
            Filter = $"{fmt.ToUpperInvariant()} 檔|*.{fmt}|所有檔案|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            PaletteExporter.Save(lastPalette, dlg.FileName);
            PaletteInfoText.Text = $"已匯出：{dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"匯出失敗：\n{ex.Message}", "調色盤引擎");
        }
    }

    // ==========================================
    // PCCS 配色方案（色調地圖下方，5 種技法 × 5 色票）
    // ==========================================
    private void CreateSchemePanel()
    {
        string[] tips =
        {
            "同色調配色（tone in tone）：固定目前色調、鄰近色相 ±20°/±40°",
            "同色相配色（tone on tone）：固定目前色相、Vp/B/V/Dp/Dk 明暗階梯",
            "卡瑪伊尤（camaïeu）：以 OkLCH 做極近似的微差配色",
            "對決色調：明清色調（目前色相）對上暗清色調（補色相）",
            "五色相環（pentad）：固定目前色調、色相環五等分",
        };
        for (int row = 0; row < 5; row++)
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            rowPanel.Children.Add(new TextBlock
            {
                Text = SchemeNames[row],
                FontSize = 11,
                Width = 66,
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tips[row]
            });
            for (int i = 0; i < 5; i++)
            {
                var brush = new SolidColorBrush(Colors.Black);
                var cell = new Border
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = brush,
                    Cursor = Cursors.Hand
                };
                cell.MouseLeftButtonDown += SchemeCell_Click;
                rowPanel.Children.Add(cell);
                schemeCells[row, i] = cell;
                schemeBrushes[row, i] = brush;
            }
            SchemePanel.Children.Add(rowPanel);
        }
    }

    private void SchemeCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (((Border)sender).Background is SolidColorBrush b) ApplyRgbColor(b.Color);
    }

    private static double NormHue(double h) => ((h % 360) + 360) % 360;

    private void SetScheme(int row, int i, (int R, int G, int B) rgb)
    {
        schemeBrushes[row, i].Color = Color.FromRgb((byte)rgb.R, (byte)rgb.G, (byte)rgb.B);
        schemeCells[row, i].ToolTip = $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }

    // 依目前的色相與色調重算五種配色方案
    private void UpdateSchemes(double h, PccsTone tone)
    {
        // 無彩色沒有色相概念，方案改以 V 色調當基準
        (double tl, double ts) = tone.IsAchromatic ? Pccs.Tones[5].RepresentativeHls() : tone.RepresentativeHls();

        // 1 同色調：固定色調、色相 ±20°/±40°
        for (int i = 0; i < 5; i++)
            SetScheme(0, i, ColorMath.HlsToRgb(NormHue(h + (i - 2) * 20), tl, ts));

        // 2 同色相：Vp/B/V/Dp/Dk 明暗階梯
        int[] ladder = { 13, 6, 5, 8, 12 };
        for (int i = 0; i < 5; i++)
        {
            (double l2, double s2) = Pccs.Tones[ladder[i]].RepresentativeHls();
            SetScheme(1, i, ColorMath.HlsToRgb(h, l2, s2));
        }

        // 3 卡瑪伊尤：OkLCH 微偏移（左亮右暗的斜向微差）
        (double okL, double okC, double okH) = ColorMath.RgbToOklch(currentRgb.R, currentRgb.G, currentRgb.B);
        for (int i = 0; i < 5; i++)
        {
            int d = i - 2;
            SetScheme(2, i, ColorMath.OklchToRgb(Math.Clamp(okL - d * 0.045, 0.0, 1.0), okC, okH + d * 7));
        }

        // 4 對決色調：明清（P/B/V @ 目前色相）對 暗清（Dp/Dk @ 補色相）
        int[] duelTones = { 9, 6, 5, 8, 12 };
        for (int i = 0; i < 5; i++)
        {
            (double l2, double s2) = Pccs.Tones[duelTones[i]].RepresentativeHls();
            double hh = i < 3 ? h : NormHue(h + 180);
            SetScheme(3, i, ColorMath.HlsToRgb(hh, l2, s2));
        }

        // 5 五色相環：固定色調、72° 等分
        for (int i = 0; i < 5; i++)
            SetScheme(4, i, ColorMath.HlsToRgb(NormHue(h + i * 72), tl, ts));
    }

    // 色票維持正方形格（高＝寬×5/7），並以圓角矩形裁切整塊的四個角落
    private void SwatchGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = SwatchGrid.ActualWidth;
        if (double.IsNaN(w) || w <= 0) return;
        double hgt = w * 5.0 / 7.0;
        SwatchGrid.Height = hgt;
        SwatchGrid.Clip = new RectangleGeometry(new Rect(0, 0, w, hgt), 6, 6);
    }

    // 近似色：X 軸 OkLCH 色相 ±8°/格、Y 軸 L ±0.05/格（上亮下暗，輔助挑高光與陰影）
    // 漸層色：X 軸 ±4°/格、Y 軸 L ±0.03/格
    private void UpdateSwatchGrid(int r, int g, int b)
    {
        (double okL, double okC, double okH) = ColorMath.RgbToOklch(r, g, b);
        double hueStep = similarTabActive ? 8.0 : 4.0;
        double lStep = similarTabActive ? 0.05 : 0.03;

        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 7; col++)
            {
                double nl = Math.Clamp(okL + (2 - row) * lStep, 0.0, 1.0);
                double nh = okH + (col - 3) * hueStep;
                (int rr, int gg, int bb) = ColorMath.OklchToRgb(nl, okC, nh);
                swatchBrushes[row, col].Color = Color.FromRgb((byte)rr, (byte)gg, (byte)bb);
                swatchCells[row, col].ToolTip = $"#{rr:X2}{gg:X2}{bb:X2}";
            }
        }
    }

    // 點擊色票：白框移到該格並套用顏色（色票本身不重建，按顏色方塊按鈕才重建）
    private void SwatchCell_Click(object sender, MouseButtonEventArgs e)
    {
        var cell = (Border)sender;
        if (cell.Tag is int idx) SetSwatchMarker(idx / 7, idx % 7);
        if (cell.Background is SolidColorBrush b) ApplyRgbColor(b.Color);
    }

    private void SwatchTab_Click(object sender, MouseButtonEventArgs e)
    {
        similarTabActive = ReferenceEquals(sender, SimilarTab);

        var panelBg = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        var active = new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0xB0));
        var inactive = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));

        SimilarTab.Background = similarTabActive ? panelBg : Brushes.Transparent;
        GradientTab.Background = similarTabActive ? Brushes.Transparent : panelBg;
        SimilarTabText.Foreground = similarTabActive ? active : inactive;
        GradientTabText.Foreground = similarTabActive ? inactive : active;

        // 切換分頁時以「上次套用的基準色」重算，不追目前滑桿的顏色
        UpdateSwatchGrid(swatchBaseRgb.R, swatchBaseRgb.G, swatchBaseRgb.B);
        SetSwatchMarker(2, 3);
    }

    // ==========================================
    // 互動與更新邏輯
    // ==========================================
    private void HueRing_Mouse(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            // 因為左右兩個圓環綁定同一個事件，我們動態抓取是被誰點擊的
            Point p = e.GetPosition((IInputElement)sender);
            double dx = p.X - 89.5;
            double dy = p.Y - 89.5;
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (angle < 0) angle += 360;

            isUpdatingFromWheel = true;
            H_Slider.Value = angle;
            isUpdatingFromWheel = false;
            UpdateUI();
        }
    }

    // 點擊方形 (HSV 邏輯)
    private void Square_Mouse(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            Point p = e.GetPosition(InnerShapeImage_HSV);
            int sqSize = 96;
            int offset = 12;

            double px = Math.Clamp(p.X - offset, 0, sqSize - 1);
            double py = Math.Clamp(p.Y - offset, 0, sqSize - 1);

            double hsv_s = px / (double)(sqSize - 1);
            double hsv_v = 1.0 - (py / (double)(sqSize - 1));

            ApplyColorFromShape(hsv_s, hsv_v);
        }
    }

    // 點擊三角形 (重心座標邏輯，內建產出 HSV)
    private void Triangle_Mouse(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            Point p = e.GetPosition(InnerShapeImage_HSL);
            CalculateBarycentric(p, pColor, pWhite, pBlack, out double wc, out double ww, out double wb);
            if (wc < 0) wc = 0; if (ww < 0) ww = 0; if (wb < 0) wb = 0;
            double sum = wc + ww + wb;
            if (sum == 0) return;
            wc /= sum; ww /= sum; wb /= sum;

            double hsv_v = wc + ww;
            double hsv_s = (hsv_v == 0) ? 0 : wc / hsv_v;

            ApplyColorFromShape(hsv_s, hsv_v);
        }
    }

    // 點擊 Tone Region：直接跳到該區塊色調的代表值（色相不變）
    private void PccsRegion_Click(object sender, MouseButtonEventArgs e)
    {
        int idx = (int)((System.Windows.Shapes.Path)sender).Tag;
        (double l, double s) = Pccs.Tones[idx].RepresentativeHls();
        ApplyHls(l, s);
        e.Handled = true;
    }

    // 點到區塊間隙時，反推 (C, Rl) 後退回 nearest-centroid 分類（雙系統的分類側）
    private void PccsMapCanvas_Mouse(object sender, MouseButtonEventArgs e)
    {
        Point p = e.GetPosition(PccsMapCanvas);
        double c = (p.X - BulletLeft) / (BulletRight - BulletLeft);
        if (c < 0 || c > 1) return;

        double hh = Math.Max(BulletHalfHeight(c), 1e-6);
        double rl = 0.5 + (BulletMidY - p.Y) / (2.0 * hh);
        if (rl < -0.05 || rl > 1.05) return; // 點在彈頭輪廓外
        rl = Math.Clamp(rl, 0.0, 1.0);

        var tone = Pccs.ClassifyCRl(c, rl);
        (double l, double s) = tone.RepresentativeHls();
        ApplyHls(l, s);
    }

    // 點擊無彩色軸
    private void AchCell_Click(object sender, MouseButtonEventArgs e)
    {
        int i = (int)((Border)sender).Tag;
        (double l, _) = Pccs.Tones[i].RepresentativeHls();
        ApplyHls(l, 0);
    }

    // 整合：將點擊得到的 HSV 換算給滑桿
    private void ApplyColorFromShape(double hsv_s, double hsv_v)
    {
        isUpdatingFromWheel = true;
        if (isSliderHsvMode)
        {
            L_Slider.Value = hsv_v * 100;
            S_Slider.Value = hsv_s * 100;
        }
        else
        {
            (double hls_l, double hls_s) = ColorMath.HsvToHls(hsv_s, hsv_v);
            L_Slider.Value = hls_l * 100;
            S_Slider.Value = hls_s * 100;
        }
        isUpdatingFromWheel = false;
        UpdateUI();
    }

    // 直接以 HLS 的 L/S 設定滑桿（色相沿用目前值）
    private void ApplyHls(double hls_l, double hls_s)
    {
        isUpdatingFromWheel = true;
        if (isSliderHsvMode)
        {
            (double hsv_s, double hsv_v) = ColorMath.HlsToHsv(hls_l, hls_s);
            L_Slider.Value = hsv_v * 100;
            S_Slider.Value = hsv_s * 100;
        }
        else
        {
            L_Slider.Value = hls_l * 100;
            S_Slider.Value = hls_s * 100;
        }
        isUpdatingFromWheel = false;
        UpdateUI();
    }

    // 以 RGB 顏色設定目前選色（分析面板的主色點擊用）
    private void ApplyRgbColor(Color c)
    {
        (double h, double l, double s) = ColorMath.RgbToHls(c.R, c.G, c.B);
        isUpdatingFromWheel = true;
        H_Slider.Value = h;
        isUpdatingFromWheel = false;
        ApplyHls(l, s);
    }

    private void HlsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // 如果是色盤自己引發的更新，就不重複跑，避免無窮迴圈
        if (isUpdatingFromWheel) return;

        UpdateUI();
    }

    private void UpdateUI()
    {
        // 確認 XAML 中所有物件都已建立
        if (ColorDisplay == null ||
             H_Slider == null || L_Slider == null || S_Slider == null ||
             H_ValueText == null || L_ValueText == null || S_ValueText == null ||
             RgbText == null || HlsText == null || CmykText == null || HsvText == null ||
             HueSelectorTransform_HSL == null || HueSelectorTransform_HSV == null ||
             SlSelectorTransform_HSL == null || SlSelectorTransform_HSV == null ||
             ToneNameText == null || achCells[4] == null || regionPaths[5] == null ||
             swatchBrushes[4, 6] == null)
            return;

        double h = H_Slider.Value;
        double sliderVal1 = L_Slider.Value / 100.0;
        double sliderVal2 = S_Slider.Value / 100.0;

        double hls_l, hls_s, hsv_v, hsv_s;

        // 計算絕對的 HLS 與 HSV
        if (isSliderHsvMode)
        {
            hsv_v = sliderVal1;
            hsv_s = sliderVal2;
            (hls_l, hls_s) = ColorMath.HsvToHls(hsv_s, hsv_v);
        }
        else
        {
            hls_l = sliderVal1;
            hls_s = sliderVal2;
            (hsv_s, hsv_v) = ColorMath.HlsToHsv(hls_l, hls_s);
        }

        (int r, int g, int b) = ColorMath.HlsToRgb(h, hls_l, hls_s);
        var tone = Pccs.ClassifyRgb(r / 255.0, g / 255.0, b / 255.0);
        currentRgb = (r, g, b);
        swatchApplyBrush.Color = Color.FromRgb((byte)r, (byte)g, (byte)b); // 標頭的顏色方塊即時預覽
        if (schemeCells[4, 4] != null) UpdateSchemes(h, tone);

        // 重新繪製中心形狀與 PCCS 地圖填色
        bool hueChanged = Math.Abs(lastDrawnHue - h) > 0.5;
        if (hueChanged)
        {
            DrawTriangle(h);
            DrawSquare(h);
            UpdatePccsMapColors(h);
            lastDrawnHue = h;
        }
        if (tone.Index != lastToneIndex)
        {
            UpdatePccsHighlight(tone.Index);
            UpdateAchromaticHighlight(tone.Index);
            lastToneIndex = tone.Index;
        }

        // 同步兩邊的「外環圈圈」（半徑＝圓環內外徑的中線）
        double angleRad = h * Math.PI / 180.0;
        double ringX = 89.5 + 78 * Math.Cos(angleRad) - 7;
        double ringY = 89.5 + 78 * Math.Sin(angleRad) - 7;

        HueSelectorTransform_HSL.X = ringX; HueSelectorTransform_HSL.Y = ringY;
        HueSelectorTransform_HSV.X = ringX; HueSelectorTransform_HSV.Y = ringY;

        // 定位「三角形」裡的小黑圈
        double wc = hsv_s * hsv_v;
        double ww = hsv_v - wc;
        double wb_weight = 1.0 - hsv_v;
        SlSelectorTransform_HSL.X = (wc * pColor.X + ww * pWhite.X + wb_weight * pBlack.X) - 5;
        SlSelectorTransform_HSL.Y = (wc * pColor.Y + ww * pWhite.Y + wb_weight * pBlack.Y) - 5;

        // 定位「方形」裡的小黑圈
        int sqSize = 96; int offset = 12;
        SlSelectorTransform_HSV.X = offset + hsv_s * (sqSize - 1) - 5;
        SlSelectorTransform_HSV.Y = offset + (1.0 - hsv_v) * (sqSize - 1) - 5;

        // 更新文字與顏色預覽
        H_ValueText.Text = $"{Math.Round(h)}°";
        L_ValueText.Text = $"{Math.Round(sliderVal1 * 100)}%";
        S_ValueText.Text = $"{Math.Round(sliderVal2 * 100)}%";

        ColorDisplay.Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
        ToneNameText.Text = $"目前色調：{tone.Code} {tone.Name}";
        ToneGroupText.Text = tone.IsAchromatic ? "無彩色" : $"{tone.Group}色調群";
        RgbText.Text = $"RGB: {r}, {g}, {b}";
        HlsText.Text = $"HLS: {Math.Round(h)}°, {Math.Round(hls_l * 100)}%, {Math.Round(hls_s * 100)}%";
        CmykText.Text = CalculateCmyk(r, g, b);
        HsvText.Text = CalculateHsv(r, g, b);
    }

    private void UpdateAchromaticHighlight(int toneIndex)
    {
        for (int i = 0; i < 5; i++)
            achCells[i].BorderBrush = i == toneIndex ? Brushes.White : Brushes.Transparent;
    }

    // ==========================================
    // 圖片分析
    // ==========================================
    private void MapToggle_Click(object sender, RoutedEventArgs e)
    {
        bool show = MapPanel.Visibility != Visibility.Visible;
        MapPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MapColumn.Width = show ? new GridLength(3.5, GridUnitType.Star) : new GridLength(0);
        MapToggleText.Text = show ? "色調地圖 ▸" : "色調地圖 ◂";
    }

    private void ImportImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "選擇要分析的圖片",
            Filter = "圖片檔|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有檔案|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var preview = new BitmapImage();
            preview.BeginInit();
            preview.UriSource = new Uri(dlg.FileName);
            preview.DecodePixelWidth = 1200;
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.EndInit();
            preview.Freeze();
            previewOriginal = preview;
            previewGray = null;
            previewPoster = null;
            UpdatePreviewImage();
            PreviewPlaceholder.Visibility = Visibility.Collapsed;

            var result = ImageAnalyzer.Analyze(dlg.FileName);
            UpdateAnalysisUI(result);
            lastImagePath = dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"無法分析這張圖片：\n{ex.Message}", "圖片分析");
        }
    }

    // 切換「忽略背景」時以同一張圖重新分析
    private void IgnoreBg_Changed(object sender, RoutedEventArgs e)
    {
        ImageAnalyzer.IgnoreBackground = IgnoreBgCheck.IsChecked == true;
        if (lastImagePath == null) return;
        try { UpdateAnalysisUI(ImageAnalyzer.Analyze(lastImagePath)); }
        catch { /* 檔案已被移動或刪除時維持原顯示 */ }
    }

    // 切換灰階／四階化預覽（四階化優先，因為它本身就是灰階）
    private void PreviewMode_Changed(object sender, RoutedEventArgs e) => UpdatePreviewImage();

    private void UpdatePreviewImage()
    {
        if (previewOriginal == null) return;

        if (PosterizeCheck.IsChecked == true)
        {
            previewPoster ??= MakeLightnessPreview(previewOriginal, posterize: true);
            AnalyzedImagePreview.Source = previewPoster;
        }
        else if (GrayscaleCheck.IsChecked == true)
        {
            previewGray ??= MakeLightnessPreview(previewOriginal, posterize: false);
            AnalyzedImagePreview.Source = previewGray;
        }
        else
        {
            AnalyzedImagePreview.Source = previewOriginal;
        }
    }

    // 以 OkLab 明度產生灰階或四階化（海報化）影像：
    // 灰階＝逐像素取等亮度灰；四階化＝L 依 0~0.25/~0.5/~0.75/~1 分四級，各級以區間中點的純灰渲染
    private static BitmapSource MakeLightnessPreview(BitmapSource src, bool posterize)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        int stride = w * 4;
        byte[] px = new byte[h * stride];
        conv.CopyPixels(px, stride, 0);

        // sRGB → 線性查表，避免每像素三次 Math.Pow
        var lut = new double[256];
        for (int i = 0; i < 256; i++) lut[i] = ColorMath.SrgbToLinear(i / 255.0);

        byte[] posterGray =
        {
            ColorMath.OklabLToGray(0.125),
            ColorMath.OklabLToGray(0.375),
            ColorMath.OklabLToGray(0.625),
            ColorMath.OklabLToGray(0.875),
        };

        for (int i = 0; i < px.Length; i += 4)
        {
            double okL = ColorMath.OklabLFromLinear(lut[px[i + 2]], lut[px[i + 1]], lut[px[i]]);
            byte gray;
            if (posterize)
            {
                int band = okL <= 0.25 ? 0 : okL <= 0.5 ? 1 : okL <= 0.75 ? 2 : 3;
                gray = posterGray[band];
            }
            else
            {
                gray = ColorMath.OklabLToGray(okL);
            }
            px[i] = gray; px[i + 1] = gray; px[i + 2] = gray;
        }

        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), px, stride, 0);
        wb.Freeze();
        return wb;
    }

    private void UpdateAnalysisUI(ImageAnalysisResult res)
    {
        if (res.Total == 0) return;

        // Hue & Tone 網格：有用到的格子上色（可點擊套用），沒用到的維持暗色
        const double minShare = 0.002; // 佔比低於 0.2% 視為雜訊
        foreach (int idx in Pccs.ChromaticDisplayOrder)
        {
            for (int c = 0; c < 24; c++)
            {
                var cell = hueToneCells[idx, c];
                double share = res.CellCount[idx, c] / (double)res.Total;
                if (share >= minShare)
                {
                    cell.Background = new SolidColorBrush(res.CellAvg[idx, c]);
                    cell.ToolTip = $"{Pccs.Tones[idx].Code} × {c * 15}°：{share:P1}，點擊套用";
                    cell.Tag = res.CellAvg[idx, c];
                    cell.Cursor = Cursors.Hand;
                }
                else
                {
                    cell.Background = new SolidColorBrush(CellOffColor);
                    cell.ToolTip = null;
                    cell.Tag = null;
                    cell.Cursor = Cursors.Arrow;
                }
            }
        }
        for (int i = 0; i < 11; i++)
        {
            var cell = achLevelCells[i];
            double share = res.AchLevelCount[i] / (double)res.Total;
            if (share >= minShare)
            {
                cell.Background = new SolidColorBrush(res.AchLevelAvg[i]);
                cell.ToolTip = $"無彩 L≈{i * 10}%：{share:P1}，點擊套用";
                cell.Tag = res.AchLevelAvg[i];
                cell.Cursor = Cursors.Hand;
            }
            else
            {
                cell.Background = new SolidColorBrush(CellOffColor);
                cell.ToolTip = $"無彩 L≈{i * 10}%";
                cell.Tag = null;
                cell.Cursor = Cursors.Arrow;
            }
        }

        lastAnalysis = res;
        UpdatePies();

        // 色調分佈投影到色調地圖：每個色調在代表點畫一顆點，面積≈佔比、顏色＝該色調的平均色
        PccsDotCanvas.Children.Clear();
        foreach (var t in Pccs.Tones)
        {
            double share = res.ToneCount[t.Index] / (double)res.Total;
            if (share < 0.002) continue;
            Point pos = CRlToPoint(Math.Max(t.RepC, 0.035), t.RepRl);
            double rad = 3.5 + Math.Sqrt(share) * 24;
            var dot = new Ellipse
            {
                Width = rad * 2,
                Height = rad * 2,
                Fill = new SolidColorBrush(res.ToneAvg[t.Index]),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Opacity = 0.92
            };
            Canvas.SetLeft(dot, pos.X - rad);
            Canvas.SetTop(dot, pos.Y - rad);
            PccsDotCanvas.Children.Add(dot);
        }

        // 配色角色：主色／次要色／輔助色／點綴色
        DominantColorsPanel.Children.Clear();
        foreach ((string role, Color col, double share, double score) in res.Palette)
        {
            string tip = score > 0
                ? $"{role}：{share:P1}，重要性 {score:F3}，點擊套用"
                : $"{role}：{share:P1}，點擊套用";
            var swatch = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(col),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = tip
            };
            swatch.MouseLeftButtonDown += (_, _) => ApplyRgbColor(col);

            var stack = new StackPanel { Width = 66, Margin = new Thickness(0, 0, 6, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = role,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            stack.Children.Add(swatch);
            stack.Children.Add(new TextBlock
            {
                Text = $"#{col.R:X2}{col.G:X2}{col.B:X2}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{share:P0}",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            DominantColorsPanel.Children.Add(stack);
        }
    }

    // 四張甜甜圈；分母用 PieTotal（「忽略背景」開啟時已排除背景像素——實驗性）
    private void UpdatePies()
    {
        if (lastAnalysis == null || lastAnalysis.PieTotal == 0) return;
        var res = lastAnalysis;
        double total = res.PieTotal;

        // 色相平衡：10 色相環＋無彩 N。切換模式＝各色相的明亮色調（B）代表色
        (double bL, double bS) = Pccs.Tones[6].RepresentativeHls();
        var hueItems = new List<(double Frac, Color Color, string Tip)>();
        for (int c = 0; c < 10; c++)
        {
            double frac = res.Hue10Count[c] / total;
            if (frac <= 0) continue;
            Color col = res.Hue10Avg[c];
            if (huePieRepMode)
            {
                (int r, int g, int b) = ColorMath.HlsToRgb(c * 36, bL, bS);
                col = Color.FromRgb((byte)r, (byte)g, (byte)b);
            }
            hueItems.Add((frac, col, $"{Hue10Codes[c]} {Hue10Names[c]}：{frac:P1}"));
        }
        if (res.PieAchromaticCount > 0)
        {
            double frac = res.PieAchromaticCount / total;
            hueItems.Add((frac, huePieRepMode ? MidGray() : res.PieAchromaticAvg, $"N 無彩色：{frac:P1}"));
        }
        DrawPie(HuePieCanvas, hueItems);

        // 色調平衡切換模式＝以 10 色相環的藍（216°）呈現各色調群的代表色調
        int[] groupRepTone = { 5, 6, 12, 11 }; // 鮮豔→V、明亮→B、昏暗→Dk、暗淡→Dl
        Color GroupColor(int gI, bool blueMode)
        {
            if (!blueMode) return res.GroupAvg[gI];
            (double l, double s) = Pccs.Tones[groupRepTone[gI]].RepresentativeHls();
            (int r, int g, int b) = ColorMath.HlsToRgb(216, l, s);
            return Color.FromRgb((byte)r, (byte)g, (byte)b);
        }

        // 有彩四大分類（分母＝有彩像素）
        long chromTotal = res.GroupCount.Sum();
        var groupItems = new List<(double Frac, Color Color, string Tip)>();
        if (chromTotal > 0)
        {
            for (int gI = 0; gI < 4; gI++)
            {
                double frac = res.GroupCount[gI] / (double)chromTotal;
                if (frac > 0) groupItems.Add((frac, GroupColor(gI, tonePieBlueMode), $"{GroupNames[gI]}色調：{frac:P1}（占有彩）"));
            }
        }
        DrawPie(ToneGroupPieCanvas, groupItems);

        // 四大分類＋無彩色（分母＝全部）
        var groupNItems = new List<(double Frac, Color Color, string Tip)>();
        for (int gI = 0; gI < 4; gI++)
        {
            double frac = res.GroupCount[gI] / total;
            if (frac > 0) groupNItems.Add((frac, GroupColor(gI, tonePieNBlueMode), $"{GroupNames[gI]}色調：{frac:P1}"));
        }
        if (res.PieAchromaticCount > 0)
        {
            double frac = res.PieAchromaticCount / total;
            groupNItems.Add((frac, tonePieNBlueMode ? MidGray() : res.PieAchromaticAvg, $"無彩色：{frac:P1}"));
        }
        DrawPie(ToneGroupNPieCanvas, groupNItems);

        // 明度平衡（不切換）：OkLab L 四階，切片用各區間中點的等亮度灰
        var lightItems = new List<(double Frac, Color Color, string Tip)>();
        for (int lI = 0; lI < 4; lI++)
        {
            double frac = res.LightnessCount[lI] / total;
            if (frac <= 0) continue;
            byte gray = ColorMath.OklabLToGray(0.125 + lI * 0.25);
            lightItems.Add((frac, Color.FromRgb(gray, gray, gray), $"L {LightBandNames[lI]}：{frac:P1}"));
        }
        DrawPie(LightPieCanvas, lightItems);

        static Color MidGray()
        {
            (double l, _) = Pccs.Tones[2].RepresentativeHls(); // MG 中灰
            (int r, int g, int b) = ColorMath.HlsToRgb(0, l, 0);
            return Color.FromRgb((byte)r, (byte)g, (byte)b);
        }
    }

    private void DrawPie(Canvas cv, List<(double Frac, Color Color, string Tip)> items)
    {
        cv.Children.Clear();
        double cx = 55, cy = 55, radius = 52;
        double start = -90;

        foreach (var it in items)
        {
            if (it.Frac <= 0.0005) continue;
            double sweep = it.Frac * 360.0;
            Shape shape;

            if (sweep >= 359.5)
            {
                var full = new Ellipse { Width = radius * 2, Height = radius * 2 };
                Canvas.SetLeft(full, cx - radius);
                Canvas.SetTop(full, cy - radius);
                shape = full;
            }
            else
            {
                double a0 = start * Math.PI / 180.0;
                double a1 = (start + sweep) * Math.PI / 180.0;
                var p0 = new Point(cx + radius * Math.Cos(a0), cy + radius * Math.Sin(a0));
                var p1 = new Point(cx + radius * Math.Cos(a1), cy + radius * Math.Sin(a1));

                var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
                fig.Segments.Add(new LineSegment(p0, true));
                fig.Segments.Add(new ArcSegment(p1, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true));
                var geo = new PathGeometry();
                geo.Figures.Add(fig);
                shape = new System.Windows.Shapes.Path { Data = geo };
            }

            shape.Fill = new SolidColorBrush(it.Color);
            shape.Stroke = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12));
            shape.StrokeThickness = 1;
            shape.ToolTip = it.Tip;
            cv.Children.Add(shape);
            start += sweep;
        }

        // 中心挖洞做成甜甜圈
        var hole = new Ellipse
        {
            Width = 40,
            Height = 40,
            Fill = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(hole, cx - 20);
        Canvas.SetTop(hole, cy - 20);
        cv.Children.Add(hole);
    }

    // ==========================================
    // 數學輔助函式（重心座標）
    // ==========================================

    // 計算點 p 在三角形 a,b,c 中的重心座標 (wa, wb, wc)
    private void CalculateBarycentric(Point p, Point a, Point b, Point c, out double wa, out double wb, out double wc)
    {
        double detT = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        wa = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / detT;
        wb = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / detT;
        wc = 1.0 - wa - wb;
    }

    // RGB 轉 CMYK 顯示字串
    private string CalculateCmyk(int r, int g, int b)
    {
        double rDash = r / 255.0;
        double gDash = g / 255.0;
        double bDash = b / 255.0;
        double k = 1.0 - Math.Max(rDash, Math.Max(gDash, bDash));
        if (k == 1.0) return "CMYK: 0%, 0%, 0%, 100%";
        double c = (1.0 - rDash - k) / (1.0 - k);
        double m = (1.0 - gDash - k) / (1.0 - k);
        double y = (1.0 - bDash - k) / (1.0 - k);
        return $"CMYK: {Math.Round(c * 100)}%, {Math.Round(m * 100)}%, {Math.Round(y * 100)}%, {Math.Round(k * 100)}%";
    }

    // RGB 轉 HSV 顯示字串
    private string CalculateHsv(int r, int g, int b)
    {
        double rDash = r / 255.0;
        double gDash = g / 255.0;
        double bDash = b / 255.0;
        double max = Math.Max(rDash, Math.Max(gDash, bDash));
        double min = Math.Min(rDash, Math.Min(gDash, bDash));
        double delta = max - min;

        double h = 0;
        if (delta != 0)
        {
            if (max == rDash) h = 60 * (((gDash - bDash) / delta) % 6);
            else if (max == gDash) h = 60 * (((bDash - rDash) / delta) + 2);
            else if (max == bDash) h = 60 * (((rDash - gDash) / delta) + 4);
            if (h < 0) h += 360;
        }
        double s = max == 0 ? 0 : delta / max;
        return $"HSV: {Math.Round(h)}°, {Math.Round(s * 100)}%, {Math.Round(max * 100)}%";
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    // 最大化 / 還原按鈕
    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
        {
            this.WindowState = WindowState.Normal;
        }
        else
        {
            this.WindowState = WindowState.Maximized;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close(); // 關閉目前視窗
    }
}
