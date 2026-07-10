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
    private readonly Border[,] hueToneCells = new Border[17, 12];
    private readonly Border[] achLevelCells = new Border[11];
    private static readonly Color CellOffColor = Color.FromRgb(0x1E, 0x1E, 0x1E);

    // 近似色／漸層色色票（7x5，主色置中）
    private readonly Border[,] swatchCells = new Border[5, 7];
    private readonly SolidColorBrush[,] swatchBrushes = new SolidColorBrush[5, 7];
    private bool similarTabActive = true;
    private (int R, int G, int B) currentRgb = (0, 0, 0);
    private string? lastImagePath;

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
        DrawHueRing(); // 啟動時繪製外環
        UpdateUI();
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
                Data = BuildRoundedPolygon(pts, 9),
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
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        for (int c = 0; c < 12; c++)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 第 0 列：12 個色相的顏色標記
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        for (int c = 0; c < 12; c++)
        {
            (int r, int gg, int b) = ColorMath.HlsToRgb(c * 30, 0.5, 1.0);
            var marker = new Border
            {
                Height = 5,
                Margin = new Thickness(1.5, 0, 1.5, 3),
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)gg, (byte)b)),
                Opacity = 0.9,
                ToolTip = $"{c * 30}°"
            };
            Grid.SetRow(marker, 0);
            Grid.SetColumn(marker, c + 1);
            g.Children.Add(marker);
        }

        // 12 個有彩色調列
        for (int ri = 0; ri < Pccs.ChromaticDisplayOrder.Length; ri++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
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

            for (int c = 0; c < 12; c++)
            {
                var cell = new Border
                {
                    Margin = new Thickness(1.5),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(CellOffColor)
                };
                cell.MouseLeftButtonDown += HueToneCell_Click;
                Grid.SetRow(cell, ri + 1);
                Grid.SetColumn(cell, c + 1);
                g.Children.Add(cell);
                hueToneCells[tone.Index, c] = cell;
            }
        }

        // 最後一列：5 個無彩色調
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        var achLabel = new TextBlock
        {
            Text = "無彩",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        Grid.SetRow(achLabel, 13);
        Grid.SetColumn(achLabel, 0);
        g.Children.Add(achLabel);

        // 無彩列：明度 0~100 分十階、含兩端共 11 級（左黑右白）
        var achRow = new UniformGrid { Rows = 1, Columns = 11 };
        Grid.SetRow(achRow, 13);
        Grid.SetColumn(achRow, 1);
        Grid.SetColumnSpan(achRow, 12);
        for (int i = 0; i < 11; i++)
        {
            var cell = new Border
            {
                Margin = new Thickness(1.5),
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(CellOffColor),
                ToolTip = $"無彩 L≈{i * 10}%"
            };
            cell.MouseLeftButtonDown += HueToneCell_Click;
            achRow.Children.Add(cell);
            achLevelCells[i] = cell;
        }
        g.Children.Add(achRow);
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
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 7; col++)
            {
                var brush = new SolidColorBrush(Colors.Black);
                bool isCenter = row == 2 && col == 3; // 中心格＝目前主色
                var cell = new Border
                {
                    Margin = new Thickness(1.5),
                    CornerRadius = new CornerRadius(3),
                    Background = brush,
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(isCenter ? 1.5 : 0),
                    BorderBrush = isCenter ? Brushes.White : null
                };
                cell.MouseLeftButtonDown += SwatchCell_Click;
                SwatchGrid.Children.Add(cell);
                swatchCells[row, col] = cell;
                swatchBrushes[row, col] = brush;
            }
        }
    }

    // 近似色：X 軸 OkLCH 色相 ±8°/格、Y 軸 L ±0.05/格（上亮下暗，輔助挑高光與陰影）
    // 漸層色：X 軸 ±2°/格、Y 軸 L ±0.01/格（細微漸層）
    private void UpdateSwatchGrid(int r, int g, int b)
    {
        (double okL, double okC, double okH) = ColorMath.RgbToOklch(r, g, b);
        double hueStep = similarTabActive ? 8.0 : 2.0;
        double lStep = similarTabActive ? 0.05 : 0.01;

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

    private void SwatchCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (((Border)sender).Background is SolidColorBrush b)
            ApplyRgbColor(b.Color);
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

        UpdateSwatchGrid(currentRgb.R, currentRgb.G, currentRgb.B);
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
        UpdateSwatchGrid(r, g, b);

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
            AnalyzedImagePreview.Source = preview;
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

    private void UpdateAnalysisUI(ImageAnalysisResult res)
    {
        if (res.Total == 0) return;

        // Hue & Tone 網格：有用到的格子上色（可點擊套用），沒用到的維持暗色
        const double minShare = 0.002; // 佔比低於 0.2% 視為雜訊
        foreach (int idx in Pccs.ChromaticDisplayOrder)
        {
            for (int c = 0; c < 12; c++)
            {
                var cell = hueToneCells[idx, c];
                double share = res.CellCount[idx, c] / (double)res.Total;
                if (share >= minShare)
                {
                    cell.Background = new SolidColorBrush(res.CellAvg[idx, c]);
                    cell.ToolTip = $"{Pccs.Tones[idx].Code} × {c * 30}°：{share:P1}，點擊套用";
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

        // 色相平衡圓餅圖（無彩色像素獨立成一塊）
        var hueItems = new List<(double Frac, Color Color, string Tip)>();
        for (int c = 0; c < 12; c++)
        {
            double frac = res.HueCount[c] / (double)res.Total;
            if (frac > 0) hueItems.Add((frac, res.HueAvg[c], $"{c * 30}° 附近：{frac:P1}"));
        }
        if (res.AchromaticCount > 0)
        {
            double frac = res.AchromaticCount / (double)res.Total;
            hueItems.Add((frac, res.AchromaticAvg, $"無彩色：{frac:P1}"));
        }
        DrawPie(HuePieCanvas, hueItems);

        // 色調平衡圓餅圖
        var toneItems = new List<(double Frac, Color Color, string Tip)>();
        foreach (var t in Pccs.Tones)
        {
            double frac = res.ToneCount[t.Index] / (double)res.Total;
            if (frac > 0) toneItems.Add((frac, res.ToneAvg[t.Index], $"{t.Code} {t.Name}：{frac:P1}"));
        }
        DrawPie(TonePieCanvas, toneItems);

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
