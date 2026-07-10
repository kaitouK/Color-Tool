using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace ColorTool;

public partial class MainWindow : Window
{
    private bool isSliderHsvMode = false; // 紀錄目前滑桿是 HSL 還是 HSV
    private bool forceRedraw = false; // 用來強制在切換模式時重繪圖形
    private bool isUpdatingFromWheel = false;
    private double lastDrawnHue = -1;

    // 定義內三角形的三個頂點 (相對於 120x120 的 Grid)
    private readonly Point pColor = new Point(120, 60); // 純色頂點 (指向右方)
    private readonly Point pWhite = new Point(30, 8);   // 純白頂點 (左上)
    private readonly Point pBlack = new Point(30, 112); // 純黑頂點 (左下)

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
            (double hsv_s, double hsv_v) = HlsToHsv(val1, val2);
            L_Slider.Value = hsv_v * 100;
            S_Slider.Value = hsv_s * 100;
        }
        else
        {
            ModeToggleButton.Content = "目前的滑桿屬性：HSL (點擊切換為 HSV)";
            ModeToggleButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4EC9B0"));
            L_Label.Text = "亮度 (L) - 明暗程度";
            S_Label.Text = "飽0和度 (S) [HLS] - 鮮豔程度";

            // 原本是 HSV，將 V 和 S 轉成 HLS 的 L 和 S 給滑桿
            (double hls_l, double hls_s) = HsvToHls(val2, val1);
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

        double rOut = 90, rIn = 70; // 圓環的外徑與內徑
        double cx = 90, cy = 90;
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

                    (int r, int g, int b) = HlsToRgb(angle, 0.5, 1.0);

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

                    (int r, int g, int b) = HsvToRgb(hue, s, v);

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

        (int pureR, int pureG, int pureB) = HlsToRgb(hue, 0.5, 1.0);
        double aaWidth = 1.0;//反鋸齒過渡寬度

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

                    // 🌟 計算點到三角形三條邊的最短距離，用來做邊緣平滑
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
    // 🌟 額外加入的數學輔助函式：計算點到線段的最短距離
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
    // 互動與更新邏輯
    // ==========================================
    private void HueRing_Mouse(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            // 因為左右兩個圓環綁定同一個事件，我們動態抓取是被誰點擊的
            Point p = e.GetPosition((IInputElement)sender);
            double dx = p.X - 90;
            double dy = p.Y - 90;
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
            (double hls_l, double hls_s) = HsvToHls(hsv_s, hsv_v);
            L_Slider.Value = hls_l * 100;
            S_Slider.Value = hls_s * 100;
        }
        isUpdatingFromWheel = false;
        UpdateUI();
    }

    private void HlsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // 如果是色盤自己引發的更新，就不重複跑，避免無窮迴圈
        if (isUpdatingFromWheel) return;

        UpdateUI();
    }
    private void UpdateUI()
    {
        // 確認 XAML 中所有新名字的物件都已建立
        if (ColorDisplay == null ||
             H_Slider == null || L_Slider == null || S_Slider == null ||
             H_ValueText == null || L_ValueText == null || S_ValueText == null ||
             RgbText == null || HlsText == null || CmykText == null || HsvText == null ||
             HueSelectorTransform_HSL == null || HueSelectorTransform_HSV == null ||
             SlSelectorTransform_HSL == null || SlSelectorTransform_HSV == null)
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
            (hls_l, hls_s) = HsvToHls(hsv_s, hsv_v);
        }
        else
        {
            hls_l = sliderVal1;
            hls_s = sliderVal2;
            (hsv_s, hsv_v) = HlsToHsv(hls_l, hls_s);
        }

        // 重新繪製中心形狀
        if (Math.Abs(lastDrawnHue - h) > 0.5)
        {
            DrawTriangle(h);
            DrawSquare(h);
            lastDrawnHue = h;
        }

        // 同步兩邊的「外環圈圈」
        double angleRad = h * Math.PI / 180.0;
        double ringX = 90 + 80 * Math.Cos(angleRad) - 7;
        double ringY = 90 + 80 * Math.Sin(angleRad) - 7;

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

        // 更新文字與右側顏色預覽
        H_ValueText.Text = $"{Math.Round(h)}°";
        L_ValueText.Text = $"{Math.Round(sliderVal1 * 100)}%";
        S_ValueText.Text = $"{Math.Round(sliderVal2 * 100)}%";

        (int r, int g, int b) = HlsToRgb(h, hls_l, hls_s);
        ColorDisplay.Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
        RgbText.Text = $"RGB: {r}, {g}, {b}";
        HlsText.Text = $"HLS: {Math.Round(h)}°, {Math.Round(hls_l * 100)}%, {Math.Round(hls_s * 100)}%";
        CmykText.Text = CalculateCmyk(r, g, b);
        HsvText.Text = CalculateHsv(r, g, b);
    }
    private (double s, double v) HlsToCoordinate(double hls_l, double hls_s)
    {
        double v = hls_l + hls_s * Math.Min(hls_l, 1.0 - hls_l);
        double s = (v == 0) ? 0 : 2.0 * (1.0 - hls_l / v);
        return (s, v);
    }

    // ==========================================
    // 數學輔助函式 (重心座標、HLS/HSV轉換)
    // ==========================================

    // 計算點 p 在三角形 a,b,c 中的重心座標 (wa, wb, wc)
    private void CalculateBarycentric(Point p, Point a, Point b, Point c, out double wa, out double wb, out double wc)
    {
        double detT = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        wa = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / detT;
        wb = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / detT;
        wc = 1.0 - wa - wb;
    }

    // HSV 轉 HLS (針對滑鼠點擊三角形的轉換)
    private (double l, double s) HsvToHls(double hsv_s, double hsv_v)
    {
        double l = hsv_v * (1.0 - hsv_s / 2.0);
        double s = 0;
        if (l > 0 && l < 1) s = (hsv_v - l) / Math.Min(l, 1.0 - l);
        return (l, s);
    }

    // HLS 轉 HSV (針對定位指示器)
    private (double s, double v) HlsToHsv(double hls_l, double hls_s)
    {
        double v = hls_l + hls_s * Math.Min(hls_l, 1.0 - hls_l);
        double s = (v == 0) ? 0 : 2.0 * (1.0 - hls_l / v);
        return (s, v);
    }
    // 專門為方形繪圖準備的 HSV 轉 RGB 數學公式
    private (int R, int G, int B) HsvToRgb(double h, double s, double v)
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
    // 🌟 HLS 轉 RGB 數學公式
    private (int R, int G, int B) HlsToRgb(double h, double l, double s)
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

    // HLS 轉 RGB 的輔助函式
    private double HueToRgb(double p, double q, double tc)
    {
        if (tc < 0) tc += 1.0;
        if (tc > 1.0) tc -= 1.0;

        if (tc < 1.0 / 6.0) return p + ((q - p) * 6.0 * tc);
        if (tc < 1.0 / 2.0) return q;
        if (tc < 2.0 / 3.0) return p + ((q - p) * (2.0 / 3.0 - tc) * 6.0);

        return p;
    }

    // 🌟 舊有的 RGB 轉 CMYK 公式 (保留使用)
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

    // 🌟 舊有的 RGB 轉 HSV 公式 (保留使用)
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