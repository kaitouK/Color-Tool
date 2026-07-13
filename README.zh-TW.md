# 配色工具 ColorTool

[English](README.md) | **繁體中文**

以 **PCCS（實用配色體系）十七色調**為核心的 WPF 配色工具，結合互動式選色器與圖片色彩分析。

## 功能

### 選色器
- **雙色相環**並列顯示、選取點即時同步——一個內嵌 HSL 三角形，一個內嵌 HSV 方形。
- **四種色彩模型即時換算**：RGB／CMYK／HSV／HLS。
- **滑桿模式切換**：可在 HSL 與 HSV 之間切換，切換時數值自動換算。
- **近似色／漸層色色票分頁（OkLCH）**——7×5 無縫正方形色票（僅整塊外緣四角圓角）、目前主色置中，輔助挑選高光與陰影：
  - *近似色*：X 軸色相每格 ±8°、Y 軸 OkLCH 明度每格 ±0.05（上亮下暗）。
  - *漸層色*：每格 ±4°、明度 ±0.03。
  - 點色票會套用該色並移動白框標記，色票本身不重建；分頁標頭右側的顏色方塊（即時預覽目前色）按下才以目前顏色重新產生色票。

### PCCS 色調地圖
- 大型 HLS 三角形切割為 **PCCS 十七色調**：
  - 無彩色（全大寫代號）：`W` 白、`LG` 淺灰、`MG` 中灰、`DG` 深灰、`BK` 黑——以獨立的灰軸長條呈現。
  - 有彩色：`V` 鮮豔、`B` 明亮、`S` 強烈、`Dp` 深、`P` 淡、`L` 淺、`Dl` 濁、`Dk` 暗、`Vp` 極淡、`Lgr` 淺灰、`Gr` 灰、`Dgr` 深灰色調。
- **雙系統設計**：
  - **分類側**：在三角形內在座標 `(C, Rl)` 上做加權 nearest-centroid——`C` = 純色量（`max−min`）、`Rl` = 白黑比 `white/(white+black)`，軸向權重為 `σC`、`σRl`。
  - **視覺側**：另建一份 Tone Region 幾何，映射到**彈頭形輪廓**（灰軸側平底、V 端四分之一橢圓收尖），以內縮＋二次貝茲圓角的向量圖形呈現教科書色調圖外觀。調整視覺不影響分類，反之亦然。
- 點擊色調區塊可跳到該色調的代表值（色相不變）；目前顏色所在的區塊會描白邊。
- 轉動色相環時整張地圖即時換色。
- 匯入圖片後，圖中的色調分佈會以圓點投影在地圖上（點面積≈佔比、顏色＝該色調的平均色）。
- 地圖下方附 **PCCS 配色方案產生器**（點擊套用）：同色調、同色相（明暗階梯）、卡瑪伊尤、對決色調、五色相環，隨目前選色即時更新。

### 圖片分析
- 匯入圖片（PNG／JPEG／BMP／GIF／WebP／TIFF），原圖預覽隨視窗縮放。
- **Hue & Tone 分佈網格**——**24 色相（每 15°）**× 12 有彩色調，正方形方格、上方標示 hue 值；右側另有無彩直欄（**明度 0~100 分十階、共 11 級**，左標明度值）。圖片中有出現的格子以該格平均色亮起，沒出現的維持暗色；點擊亮格可將該色套用到選色器。
- **背景過濾**——某聚類佔影像最外圈過半即視為背景（商品圖白底的典型特徵），排除在配色角色外並另列「背景」色塊；可用「忽略背景」核取方塊開關。滿版照片的邊框會被多群瓜分，不會誤觸發。
- **四張甜甜圈圖**，切片顏色取自圖片的實際平均色；「忽略背景」開啟時，色相／色調／明度平衡會排除背景像素（實驗性）。點擊色相平衡可切換成各色相的明亮色調代表色、點擊色調平衡可切換成以藍色相呈現各色調群：
  - *色相平衡*——10 色色相環（R/YR/Y/GY/G/BG/B/PB/P/RP，每 36°）＋無彩 `N`。
  - *色調平衡（有彩）*——PCCS 四大色調分類：鮮豔／明亮／昏暗／暗淡。
  - *色調平衡（含無彩）*——四大分類＋無彩色。
  - *明度平衡*——OkLab L 分四階（0~0.25／~0.5／~0.75／~1），切片以各區間中點灰渲染。
- **灰階與四階化預覽**——兩個核取方塊以 OkLab 明度重新渲染匯入的圖片：完整灰階，或降為四個純灰階色塊的海報化（明度結構／notan 檢視）。
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

### 調色盤生成引擎（Palette Generation Engine）
右欄的第二個分頁是一套設定驅動的調色盤產生器，以 **OKLCH 為內部唯一色彩模型**（所有顏色一律先生成 OKLCH，再轉換為 sRGB/HEX）。引擎（`PaletteEngine.cs`）是純函式——`GeneratePalette(PaletteConfig) → Palette`——完全不依賴 UI，可供 CLI／Web／Desktop 共用。

- **結構**：`Hue[] × Lightness[] × Chroma[]`，每個色票同步保存 OKLCH 與 sRGB/HEX。
- **色相**：數量自由（12/18/24/36/48/72 或任意整數）、偏移（0°/7.5°/15°/22.5°/自訂）、順/逆時針；`Hue = Offset + n × (360 / HueCount)`。
- **明度**：三種模式——手動停駐點、自動線性等分（起/迄/階數）、曲線分布（Linear／Gamma 可調 γ／Log／Bezier），用來加重亮部或暗部。
- **彩度**：三種模式——絕對值、**相對於該色相＋明度在目標色域內可達的最大彩度**（`C = Cmax × 比例`，預設模式，維持不同色相間的視覺一致性）、自適應（依色相修正：黃降低、紫提高、藍略提升；修正曲線獨立成介面方便未來替換）。
- **中性色**：可選擇為每個明度自動加入 `C = 0`。
- **目標色域**：sRGB／Display-P3／Rec2020／不限制；超出色域策略：**Clip 裁切／Scale 縮放／Compress 壓縮／Perceptual 感知**（可擴充）。
- **排序**：色相優先／明度優先／彩度優先。
- **命名**：`H015-L80-C060` 編碼、色相名（`Red-03`）、PCCS Style（`V-R`）、或自訂模板 Formatter（`H{H}-L{L}-C{C}`，可用 `{H} {L} {C} {I} {HEX}`）。
- **Metadata**：調色盤名稱、描述、建立時間、色彩空間、目標色域、版本，一併寫入 JSON。
- **匯出**（`PaletteExporter.cs`，與引擎完全解耦）：**JSON**（主要格式）、CSS Variables、HTML 預覽頁、SVG、ASE（Adobe 色票）、PNG。
- **Preset**（只載入設定、不寫死演算法）：Default、PCCS、Pastel、Anime、Watercolor、UI Design、Dark UI、Pixel Art。
- 產生結果直接在面板內預覽，點任一色塊可套用到選色器。

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
| `PaletteEngine.cs` | 純函式 OKLCH 調色盤生成引擎（設定進、調色盤出）＋ Preset |
| `PaletteExporter.cs` | JSON／CSS／HTML／SVG／ASE／PNG 匯出器，與引擎解耦 |
