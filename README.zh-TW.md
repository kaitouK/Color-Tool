# 配色工具 ColorTool

[English](README.md) | **繁體中文**

以 **PCCS（實用配色體系）十七色調**為核心的 WPF 配色工具，結合互動式選色器與圖片色彩分析。

## 功能

### 選色器
- **雙色相環**並列顯示、選取點即時同步——一個內嵌 HSL 三角形，一個內嵌 HSV 方形。
- **四種色彩模型即時換算**：RGB／CMYK／HSV／HLS。
- **滑桿模式切換**：可在 HSL 與 HSV 之間切換，切換時數值自動換算。
- **近似色／漸層色色票分頁（OkLCH）**——7×5 色票、目前主色置中，輔助挑選高光與陰影：
  - *近似色*：X 軸色相每格 ±8°、Y 軸 OkLCH 明度每格 ±0.05（上亮下暗）。
  - *漸層色*：每格 ±2°、明度 ±0.01。
  - 點擊任一色票即成為新的目前顏色。

### PCCS 色調地圖
- 大型 HLS 三角形切割為 **PCCS 十七色調**：
  - 無彩色（全大寫代號）：`W` 白、`LG` 淺灰、`MG` 中灰、`DG` 深灰、`BK` 黑——以獨立的灰軸長條呈現。
  - 有彩色：`V` 鮮豔、`B` 明亮、`S` 強烈、`Dp` 深、`P` 淡、`L` 淺、`Dl` 濁、`Dk` 暗、`Vp` 極淡、`Lgr` 淺灰、`Gr` 灰、`Dgr` 深灰色調。
- **雙系統設計**：
  - **分類側**：在三角形內在座標 `(C, Rl)` 上做加權 nearest-centroid——`C` = 純色量（`max−min`）、`Rl` = 白黑比 `white/(white+black)`，軸向權重為 `σC`、`σRl`。
  - **視覺側**：另建一份 Tone Region 幾何，映射到**彈頭形輪廓**（灰軸側平底、V 端四分之一橢圓收尖），以內縮＋二次貝茲圓角的向量圖形呈現教科書色調圖外觀。調整視覺不影響分類，反之亦然。
- 點擊色調區塊可跳到該色調的代表值（色相不變）；目前顏色所在的區塊會描白邊。
- 轉動色相環時整張地圖即時換色。

### 圖片分析
- 匯入圖片（PNG／JPEG／BMP／GIF／WebP／TIFF），原圖預覽隨視窗縮放。
- **Hue & Tone 分佈網格**——12 色相（每 30°）× 12 有彩色調＋無彩列（**明度 0~100 分十階、共 11 級**）。圖片中有出現的格子以該格平均色亮起，沒出現的維持暗色；點擊亮格可將該色套用到選色器。
- **背景過濾**——某聚類佔影像最外圈過半即視為背景（商品圖白底的典型特徵），排除在配色角色外並另列「背景」色塊；可用「忽略背景」核取方塊開關。滿版照片的邊框會被多群瓜分，不會誤觸發。
- **色相平衡與色調平衡甜甜圈圖**，切片顏色取自圖片的實際平均色。
- **配色角色抽取**——不是單純列出前 N 大顏色，而是指派角色：
  - **主色／次要色／輔助色**：K-means（k = 8）分群後面積前三大的群。
  - **點綴色**：其餘群中「視覺重要性分數」最高者：

    ```
    Score = A^α × S^β × ΔE^γ × C^δ × H^ε × T
    ```

    | 項目 | 意義 | 預設指數 |
    |------|------|----------|
    | `A`  | 色塊面積佔比 | α = 0.2 |
    | `S`  | 平均飽和度 | β = 1.0 |
    | `ΔE` | 與主色的 CIELAB 色差（ΔE76 ÷ 100） | γ = 0.6 |
    | `C`  | 局部對比——沿交界以接觸長度加權的鄰近區域平均色差 | δ = 0.8 |
    | `H`  | 與主色的色相互補度——`sin(色相差/2)`，180° 時最高 | ε = 1.5 |
    | `T`  | PCCS 色調倍率——鮮豔/強烈 ×1.3、淡/灰濁 ×0.7、無彩 ×0.3 | — |

    α < 1 刻意壓縮面積項，避免小而搶眼的色塊因面積太小失去競爭力。
    `H` 與 `T` 只用於點綴色的排序——主色／次要色／輔助色仍純粹依面積排序。
- 點擊任一角色色塊即可套用到選色器。

## 快速開始

需求：Windows、[.NET 10 SDK](https://dotnet.microsoft.com/)（目標框架 `net10.0-windows`）。

```powershell
dotnet build ColorTool.csproj
dotnet run --project ColorTool.csproj
```

純 WPF，無外部套件相依。

## 參數微調

| 想調整什麼 | 位置 |
|------------|------|
| 色調分類的敏感度 | [PccsTone.cs](PccsTone.cs) 的 `Pccs.SigmaC`／`Pccs.SigmaRl` |
| 各色調代表點 | `Pccs.Tones` 表中的 `RepC`／`RepRl` |
| 色調地圖區塊形狀（僅視覺） | [PccsTone.cs](PccsTone.cs) 的 `Pccs.Regions` |
| 視覺重要性指數 | [ImageAnalyzer.cs](ImageAnalyzer.cs) 的 `AlphaArea`／`BetaSaturation`／`GammaDeltaE`／`DeltaLocalContrast`／`EpsilonHueComplement` |
| 點綴色的 PCCS 色調倍率 | [ImageAnalyzer.cs](ImageAnalyzer.cs) 的 `ToneAccentWeight` |
| K-means 分群數 | [ImageAnalyzer.cs](ImageAnalyzer.cs) 的 `KMeansCore(samples, 8)` 呼叫 |
| Hue & Tone 網格雜訊門檻 | `MainWindow.UpdateAnalysisUI` 的 `minShare` |

## 專案結構

| 檔案 | 職責 |
|------|------|
| `MainWindow.xaml(.cs)` | 版面、選色器、色調地圖繪製與互動、分析面板 UI |
| `ColorMath.cs` | HLS／HSV／RGB／Lab 轉換與 ΔE |
| `PccsTone.cs` | 十七色調定義、nearest-centroid 分類器、Tone Region 幾何 |
| `ImageAnalyzer.cs` | 像素統計、K-means、視覺重要性分數、配色角色 |
