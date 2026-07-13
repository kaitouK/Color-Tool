# ColorTool

**English** | [繁體中文](README.zh-TW.md)

A WPF color design tool built around the **PCCS (Practical Color Co-ordinate System) 17-tone model**, combining interactive color pickers with image color analysis.

## Features

### Color picker
- **Dual hue rings** displayed side by side, with synchronized selectors — one with an HSL triangle inside, one with an HSV square.
- **Live value readouts** in four color models: RGB / CMYK / HSV / HLS.
- **Slider mode toggle** between HSL and HSV; values convert automatically when switching.
- **Similar / gradient swatch tabs (OkLCH)** — a seamless 7×5 grid of square swatches (rounded only at the block's outer corners) with the current color at the center, for picking highlight/shadow variants:
  - *Similar*: hue ±8° per column, OkLCH lightness ±0.05 per row (lighter on top).
  - *Gradient*: hue ±4° per column, lightness ±0.03 per row.
  - Clicking a swatch applies its color and moves the white selection frame; the grid itself stays put. Click the color square at the right of the tab header (a live preview of the current color) to regenerate the grid around it.

### PCCS tone map
- A large HLS triangle divided into the **17 PCCS tones**:
  - Achromatic (uppercase codes): `W` white, `LG` light gray, `MG` medium gray, `DG` dark gray, `BK` black — shown as a separate gray axis bar.
  - Chromatic: `V` vivid, `B` bright, `S` strong, `Dp` deep, `P` pale, `L` light, `Dl` dull, `Dk` dark, `Vp` very pale, `Lgr` light grayish, `Gr` grayish, `Dgr` dark grayish.
- **Dual-system design**:
  - *Classification* uses weighted nearest-centroid over the triangle's intrinsic coordinates `(C, Rl)` — `C` = chroma (`max−min`), `Rl` = white ratio `white/(white+black)` — with per-axis weights `σC`, `σRl`.
  - *Rendering* uses a separate set of tone-region polygons mapped onto a **bullet-shaped silhouette** (flat gray-axis base, quarter-ellipse nose at vivid) and drawn as inset rounded (quadratic-Bezier) vector shapes for a textbook-style look. Adjusting the visuals never affects classification, and vice versa.
- Click a tone region to jump to that tone's representative value (hue is preserved); the region containing the current color is outlined in white.
- The map recolors in real time as you rotate the hue ring.
- After importing an image, its tone distribution is projected onto the map as dots (area ≈ share, color = that tone's average).
- Below the map sits a **PCCS scheme generator** (click to apply): tone-in-tone, tone-on-tone (lightness ladder), camaïeu, contrasting-tone, and pentad rows that update live with the current color.

### Image analysis
- Import an image (PNG / JPEG / BMP / GIF / WebP / TIFF); the preview scales with the window.
- **Hue & Tone grid** — **24 hues (15° each)** × 12 chromatic tones with square cells and hue values along the top, plus a vertical achromatic column on the right graded into **11 lightness levels (0–100 in steps of 10, labeled)**. Cells that appear in the image light up with that cell's average color; unused cells stay dark. Click a lit cell to apply its color to the pickers.
- **Background filtering** — a cluster owning more than half of the image border is treated as background (typical for product shots on white), excluded from the palette roles, and listed separately as「背景」. Toggle with the 忽略背景 checkbox. Full-bleed photos split the border among many clusters, so nothing gets filtered.
- **Four donut charts**, with slices colored by the image's actual average colors. With 忽略背景 checked, the hue/tone/lightness charts exclude background pixels (experimental). Click the hue chart to switch slices to each hue's bright-tone representative color; click a tone chart to render its groups in the blue hue:
  - *Hue balance* — the 10-hue color circle (R/YR/Y/GY/G/BG/B/PB/P/RP, 36° each) plus an achromatic `N` slice.
  - *Tone balance (chromatic)* — the four PCCS tone groups: vivid / bright / dark / grayish.
  - *Tone balance (with achromatic)* — the four groups plus achromatic.
  - *Lightness balance* — OkLab L split into four bands (0–0.25 / –0.5 / –0.75 / –1), slices drawn in each band's midpoint gray.
- **Grayscale & 4-level posterize previews** — two checkboxes re-render the imported image by OkLab lightness: full grayscale, or posterized down to four pure gray levels (a value-study / notan view).
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

### Palette Generation Engine
A second tab in the right column hosts a configuration-driven palette generator built on **OKLCH as the sole internal color model** (every color is generated in OKLCH first, then converted to sRGB/HEX). The engine (`PaletteEngine.cs`) is a pure function — `GeneratePalette(PaletteConfig) → Palette` — with no UI dependency, so it can be reused from CLI/Web/Desktop hosts.

- **Structure**: `Hue[] × Lightness[] × Chroma[]`, each color stored as OKLCH plus sRGB/HEX.
- **Hue**: arbitrary hue count (12/18/24/36/48/72 or any integer), offset (0°/7.5°/15°/22.5°/custom), clockwise or counter-clockwise; `Hue = Offset + n × (360 / HueCount)`.
- **Lightness**: three modes — manual stops, automatic linear (start/end/steps), or curve distribution (Linear / Gamma with configurable γ / Log / Bezier) to bias shadows or highlights.
- **Chroma**: three modes — absolute values, **relative to the max chroma reachable at that hue+lightness in the target gamut** (`C = Cmax × ratio`, the default, keeps perceived chroma consistent across hues), or adaptive (per-hue correction: yellow lowered, purple raised, blue slightly raised; the correction curve is isolated so it can be swapped out).
- **Neutral colors**: optionally add a `C = 0` entry per lightness step.
- **Target gamut**: sRGB / Display-P3 / Rec2020 / Unlimited, with out-of-gamut strategies **Clip / Scale / Compress / Perceptual** (extensible switch).
- **Sorting**: hue-first / lightness-first / chroma-first.
- **Naming**: `H015-L80-C060` style, hue names (`Red-03`), PCCS style (`V-R`), or a custom template formatter (`H{H}-L{L}-C{C}`, tokens `{H} {L} {C} {I} {HEX}`).
- **Metadata**: palette name, description, create time, color space, target gamut, version — all serialized in the JSON export.
- **Export** (`PaletteExporter.cs`, decoupled from the engine): **JSON** (primary), CSS variables, HTML preview page, SVG, ASE (Adobe Swatch Exchange), PNG.
- **Presets** (data only — no baked-in algorithms): Default, PCCS, Pastel, Anime, Watercolor, UI Design, Dark UI, Pixel Art.
- Generated swatches preview in-app; click any swatch to send it to the pickers.

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
| `PaletteEngine.cs` | Pure OKLCH palette generation engine (config in, palette out) + presets |
| `PaletteExporter.cs` | JSON / CSS / HTML / SVG / ASE / PNG exporters, decoupled from the engine |
