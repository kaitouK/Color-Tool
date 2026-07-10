# CLAUDE.md

WPF 配色工具（PCCS 十七色調），純 WPF、無外部套件。UI 文字使用繁體中文，深色主題（背景 #121212、面板 #1A1A1A），無框視窗以 WindowChrome 實作。

## 建置與執行

```powershell
dotnet build ColorTool.csproj
dotnet run --project ColorTool.csproj
```

目標框架 `net10.0-windows`。改完程式要重跑時，先關掉執行中的程序再建置，否則 exe 被鎖定會建置失敗：
`Stop-Process -Name ColorTool -Force`

沒有測試專案；驗證方式是實際啟動程式操作。

## 架構

| 檔案 | 職責 |
|------|------|
| `MainWindow.xaml(.cs)` | 三欄響應式版面（選色器／圖片分析／可收合色調地圖側欄）、所有 UI 互動 |
| `ColorMath.cs` | HLS／HSV／RGB／CIELAB 轉換、ΔE76。所有色彩數學集中在這裡，不要在別處重複實作 |
| `PccsTone.cs` | 十七色調定義、分類器、Tone Region 繪圖幾何 |
| `ImageAnalyzer.cs` | 圖片統計（Hue&Tone、圓餅圖資料）、K-means、視覺重要性分數、配色角色 |

命名注意：程式碼中「HLS」即一般所稱 HSL（沿用既有命名，勿混用改名）。

## 關鍵設計決策（勿隨意推翻）

1. **色調分類座標用 (C, Rl)，不是 HLS 的 L/S**。
   C = 純色量（RGB 的 max−min），Rl = 白黑比 white/(white+black)。
   原因：HLS 的 S 對淡色恆為 1（例如粉紅 S=1），無法切出 PCCS 區塊。
   從 RGB 直接計算：`C = max−min`、`Rl = min/(min+(1−max))`；
   從三角形重心座標：`C = wc`、`Rl = ww/(ww+wbk)`。

2. **雙系統：分類與繪圖分離**（使用者明確指定）。
   - 分類側：`Pccs.ClassifyCRl` 加權 nearest-centroid，
     `d = ((C−RepC)/σC)² + ((Rl−RepRl)/σRl)²`，σC=0.16、σRl=0.12。
   - 視覺側：`Pccs.Regions` 定義 (C,Rl) 矩形區域，映射到「彈頭形」畫布——
     左緣平底（灰軸側）、V 端以四分之一橢圓輪廓收尖（`BulletHalfHeight`），
     上下曲線邊取樣 8 段後以內縮 2.5px＋二次貝茲圓角的向量 Path 呈現（教科書外觀）。
   - 調視覺改 `Regions`；調分類手感改 `RepC/RepRl` 或 `SigmaC/SigmaRl`。兩邊互不影響。
   - 不要改回矩形門檻切割或把繪圖換回逐像素 Voronoi 點陣（都被使用者否決過）。

3. **點綴色用視覺重要性分數**（使用者指定的公式）：
   基礎分（所有群、tooltip 顯示）：`Base = A^α × S^β × ΔE^γ × C^δ`；
   點綴色排序分：`Accent = Base × H^ε × T`。
   α=0.2、β=1.0、γ=0.6（建議 0.6~0.8）、δ=0.8、ε=1.5（建議 1.4~1.6）。
   A=面積佔比、S=平均飽和度（HSV S）、ΔE=與主色的 Lab 色差（÷100）、
   C=局部對比（沿聚類交界、以接觸長度加權的鄰近區域平均 ΔE，÷100）、
   H=與主色的色相互補度 `sin(色相差/2)`（0°→0、180°→1；任一方近無彩回傳 1）、
   T=PCCS 色調倍率（V/S=1.3、B=1.15、Dp=1.1、P/Vp/Dl/Lgr/Gr=0.7、Dgr=0.8、無彩=0.3、L/Dk=1）。
   α<1 是刻意的：壓縮面積項讓小色塊保有競爭力。
   **H 與 T 只影響點綴色排序，不影響主色／次要／輔助的面積排序**（使用者明確指定）。
   主色／次要色／輔助色＝K-means（k=8）面積前三大；點綴色＝其餘群中 Accent 最高（佔比 ≥0.2%）。

4. **響應式版面**：三欄星號寬度（3* / 4* / 3.5*），固定尺寸圖形（色相環、色調地圖）包在
   `Viewbox` 內等比縮放。滑鼠事件用 `e.GetPosition(該元素)`，WPF 會自動換算 Viewbox 縮放，
   互動程式碼不需處理縮放。新增固定尺寸視覺元件時比照辦理。
   左欄順序：色相環（貼合其下的）滑桿 → 近似色/漸層色色票分頁 → 數值 → 目前色調。

6. **背景偵測**（使用者採納的方案）：判斷特徵是「大面積貼著圖片四邊」而非顏色——
   聚類標記後統計影像最外圈像素歸屬，某群佔邊框 >50% 即視為背景，
   排除在配色角色外（另列「背景」色塊），Score 的面積分母改用非背景像素。
   滿版照片邊框被多群瓜分、無人過半 → 天然不觸發。`ImageAnalyzer.IgnoreBackground`
   對應分析面板的「忽略背景」核取方塊（預設開）。

7. **近似色／漸層色色票用 OkLCH**（使用者指定）：7×5 格、主色置中，
   近似色 X 軸色相 ±8°/格、Y 軸 L ±0.05/格（上亮下暗，供挑高光/陰影）；
   漸層色 ±2°/格、±0.01/格。OkLCH 轉換在 `ColorMath`，超色域直接夾 RGB。

8. **Hue&Tone 網格的無彩列是明度 0~100 十階共 11 級**（round(L×10)），
   與色調地圖旁的灰軸五階（W/LG/MG/DG/BK）是兩回事，勿混用。

5. **效能慣例**：色相改變時，色調地圖只更新 12 個 `SolidColorBrush`（`UpdatePccsMapColors`），
   不重建幾何；小三角形／方形仍為 WriteableBitmap 逐像素重繪，有 `lastDrawnHue` 去抖。
   `UpdateUI` 是所有狀態的匯流點，開頭的 null 防護是因為滑桿事件會在 InitializeComponent
   期間觸發——新增在建構式中以程式建立的 UI 欄位時，記得加進防護清單。

## 常見調整入口

| 需求 | 位置 |
|------|------|
| 色調範圍太大/太小 | `PccsTone.cs`：`RepC/RepRl`（移動代表點）或 `SigmaC/SigmaRl`（軸向敏感度） |
| 點綴色抓不準 | `ImageAnalyzer.cs`：五個指數欄位與 `ToneAccentWeight`；或 `KMeansCore(samples, 8)` 的 k |
| 背景誤判/漏判 | `ImageAnalyzer.BuildPalette` 的邊框過半門檻 0.5 |
| 色票的偏移步距 | `MainWindow.UpdateSwatchGrid` 的 hueStep/lStep |
| 彈頭外形胖瘦 | `MainWindow.BulletHalfHeight`（改輪廓函數）與 Bullet* 常數 |
| 網格亮起門檻 | `MainWindow.UpdateAnalysisUI` 的 `minShare`（預設 0.002） |
| 分析解析度 | `ImageAnalyzer.Analyze` 的 `DecodePixelWidth`（預設 160） |
| 24 色相細化（潛在需求） | Hue&Tone 網格與 `hueIdx = round(hue/30)%12` 的所有出現處 |

## 文件

README.md（英文）與 README.zh-TW.md（繁中）互為翻譯、以連結互跳；
修改功能或參數時兩份要同步更新。中文版標題為「配色工具 ColorTool」。
