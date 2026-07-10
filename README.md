# ColorTool

**English** | [繁體中文](README.zh-TW.md)

A WPF color design tool built around the **PCCS (Practical Color Co-ordinate System) 17-tone model**, combining interactive color pickers with image color analysis.

## Features

### Color picker
- **Dual hue rings** displayed side by side, with synchronized selectors — one with an HSL triangle inside, one with an HSV square.
- **Live value readouts** in four color models: RGB / CMYK / HSV / HLS.
- **Slider mode toggle** between HSL and HSV; values convert automatically when switching.
- **Similar / gradient swatch tabs (OkLCH)** — a 7×5 swatch grid with the current color at the center, for picking highlight/shadow variants:
  - *Similar*: hue ±8° per column, OkLCH lightness ±0.05 per row (lighter on top).
  - *Gradient*: hue ±2° per column, lightness ±0.01 per row.
  - Click any swatch to make it the new current color.

### PCCS tone map
- A large HLS triangle divided into the **17 PCCS tones**:
  - Achromatic (uppercase codes): `W` white, `LG` light gray, `MG` medium gray, `DG` dark gray, `BK` black — shown as a separate gray axis bar.
  - Chromatic: `V` vivid, `B` bright, `S` strong, `Dp` deep, `P` pale, `L` light, `Dl` dull, `Dk` dark, `Vp` very pale, `Lgr` light grayish, `Gr` grayish, `Dgr` dark grayish.
- **Dual-system design**:
  - *Classification* uses weighted nearest-centroid over the triangle's intrinsic coordinates `(C, Rl)` — `C` = chroma (`max−min`), `Rl` = white ratio `white/(white+black)` — with per-axis weights `σC`, `σRl`.
  - *Rendering* uses a separate set of tone-region polygons mapped onto a **bullet-shaped silhouette** (flat gray-axis base, quarter-ellipse nose at vivid) and drawn as inset rounded (quadratic-Bezier) vector shapes for a textbook-style look. Adjusting the visuals never affects classification, and vice versa.
- Click a tone region to jump to that tone's representative value (hue is preserved); the region containing the current color is outlined in white.
- The map recolors in real time as you rotate the hue ring.

### Image analysis
- Import an image (PNG / JPEG / BMP / GIF / WebP / TIFF); the preview scales with the window.
- **Hue & Tone grid** — 12 hues (30° each) × 12 chromatic tones + an achromatic row graded into **11 lightness levels (0–100 in steps of 10)**. Cells that appear in the image light up with that cell's average color; unused cells stay dark. Click a lit cell to apply its color to the pickers.
- **Background filtering** — a cluster owning more than half of the image border is treated as background (typical for product shots on white), excluded from the palette roles, and listed separately as「背景」. Toggle with the 忽略背景 checkbox. Full-bleed photos split the border among many clusters, so nothing gets filtered.
- **Hue balance & tone balance donut charts**, with slices colored by the image's actual average colors.
- **Palette role extraction** — instead of a plain top-N list, colors are assigned roles:
  - **Main / Secondary / Auxiliary**: the three largest K-means clusters (k = 8) by area.
  - **Accent**: among the remaining clusters, the one with the highest *visual importance score*:

    ```
    Score = A^α × S^β × ΔE^γ × C^δ × H^ε × T
    ```

    | Term | Meaning | Default exponent |
    |------|---------|------------------|
    | `A`  | area share of the cluster | α = 0.2 |
    | `S`  | average saturation | β = 1.0 |
    | `ΔE` | CIELAB color difference vs. the main color (ΔE76 / 100) | γ = 0.6 |
    | `C`  | local contrast — boundary-weighted average ΔE against adjacent clusters | δ = 0.8 |
    | `H`  | hue complement vs. the main color — `sin(Δhue/2)`, peaks at 180° | ε = 1.5 |
    | `T`  | PCCS tone multiplier — vivid/strong ×1.3, pale/grayish ×0.7, achromatic ×0.3 | — |

    α < 1 deliberately compresses the area term so small-but-striking patches stay competitive.
    `H` and `T` apply to accent selection only — main/secondary/auxiliary are ranked purely by area.
- Click any role swatch to apply that color to the pickers.

## Getting started

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/) (project targets `net10.0-windows`).

```powershell
dotnet build ColorTool.csproj
dotnet run --project ColorTool.csproj
```

No external packages — pure WPF.

## Tuning

| What to adjust | Where |
|----------------|-------|
| Tone classification sensitivity | `Pccs.SigmaC` / `Pccs.SigmaRl` in [PccsTone.cs](PccsTone.cs) |
| Tone representative points | `RepC` / `RepRl` in the `Pccs.Tones` table |
| Tone map region shapes (visual only) | `Pccs.Regions` in [PccsTone.cs](PccsTone.cs) |
| Visual importance exponents | `AlphaArea` / `BetaSaturation` / `GammaDeltaE` / `DeltaLocalContrast` / `EpsilonHueComplement` in [ImageAnalyzer.cs](ImageAnalyzer.cs) |
| PCCS tone multipliers for accent | `ToneAccentWeight` in [ImageAnalyzer.cs](ImageAnalyzer.cs) |
| K-means cluster count | `KMeansCore(samples, 8)` call in [ImageAnalyzer.cs](ImageAnalyzer.cs) |
| Hue & Tone grid noise threshold | `minShare` in `MainWindow.UpdateAnalysisUI` |

## Project structure

| File | Responsibility |
|------|----------------|
| `MainWindow.xaml(.cs)` | Layout, pickers, tone map rendering/interaction, analysis panel UI |
| `ColorMath.cs` | HLS / HSV / RGB / Lab conversions, ΔE |
| `PccsTone.cs` | 17-tone definitions, nearest-centroid classifier, tone-region geometry |
| `ImageAnalyzer.cs` | Pixel statistics, K-means, visual importance scoring, palette roles |
