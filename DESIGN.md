# TimeGrapher-Net Design System

## 1. Atmosphere & Identity

TimeGrapher-Net is a dense watch-diagnostics workstation: compact, precise, and instrument-like. The signature is sapphire-crystal chrome around high-contrast measurement plots, with square edges and tabular, technical typography.

## 2. Color

### Palette

| Role | Token | Light | Dark | Usage |
|------|-------|-------|------|-------|
| Surface/primary | `SurfaceColor` | `#E2E3E4` | `#1D1D1B` | Main chrome background |
| Surface/panel | `PanelColor` | `#E2E3E4` | `#1D1D1B` | Panels and grouped controls |
| Surface/plot | `ScopeBgColor` | `#FFFFFF` | `#000000` | ScottPlot data area |
| Text/primary | `TextPrimaryColor` | `#1A1A1A` | `#C2C8CE` | Primary labels and values |
| Text/secondary | `TextSecondaryColor` | `#6E6E6E` | `#9A9DA1` | Secondary labels and hints |
| Border/chrome | `ChromeBorderColor` | `#CFCFCF` | `#333333` | Dividers and control borders |
| Accent/primary | `ChromeAccentColor` | `#C41230` | `#C24A62` | Active controls, warning chrome |
| Graph/grid | `ScopeGridColor` | `#EAEAEA` | `#1A1A1A` | Plot frame and grid |
| Trace/tic | `TraceTickColor` | `#2C9118` | `#5FDD45` | Tic data |
| Trace/toc | `TraceTockColor` | `#D22222` | `#FF5C5C` | Toc data |
| Status/good | `VarioGoodColor` | `#0072B2` | `#0072B2` | Healthy verdicts |
| Status/warn | `VarioWarnColor` | `#B06A00` | `#B06A00` | Caution verdicts |
| Status/bad | `VarioBadColor` | `#C03030` | `#FF6B6B` | Alerts and out-of-band states |

### Rules

- Use the `App.axaml` resource keys as the source of truth.
- Graph renderers must read the same palette through `PlotThemePalette`.
- Red is reserved for active chrome and true alert/out-of-band states.

## 3. Typography

### Scale

| Level | Size | Weight | Line Height | Tracking | Usage |
|-------|------|--------|-------------|----------|-------|
| Title | 16px | Bold | Default | 0 | App title and section headers |
| Body | 14px | Bold | Default | 0 | Default UI text and tab headers |
| Dense | 13px | Bold | Default | 0 | Compact table values only when 14px cannot fit |
| Control/sm | 12px | Bold | Default | 0 | Small toolbar controls |
| Caption | 11px | Bold | Default | 0 | Secondary hints and compact metadata |
| Plot label | 14px | Bold | Default | 0 | ScottPlot labels |

### Font Stack

- Primary: `D2Coding`
- Title/technical: `Hack`

### Rules

- Default UI and tab text target 14px.
- Use 13px or abbreviations only after screenshot verification shows clipping or overlap at 14px.
- Numeric and diagnostic values should remain bold and tabular-feeling for scan speed.

## 4. Spacing & Layout

### Base Unit

Spacing follows a 4px base where possible.

| Token | Value | Usage |
|-------|-------|-------|
| Tight | 4px | Icon/label gaps and micro margins |
| Compact | 8px | Panel padding, inline groups |
| Default | 12px | Graph overlays and grouped controls |
| Standard | 16px | Major inner spacing |
| Large | 24px | Status bars and panel separation |

### Grid

- Default application size: 1280 x 750.
- Minimum application size: 900 x 560.
- Left control panel width: 300px.
- Position strip width: 92px.
- Graph tabs use a two-row uniform tab header.

### Rules

- Dense measurement screens may prioritize stable data-area geometry over decorative spacing.
- Fixed-format controls must have stable dimensions so value updates do not shift plots.

## 5. Components

### Glass card

- **Structure**: `Border.GlassCard` with square corners, translucent panel fill, rim brush, and glass shadow.
- **Spacing**: compact 8px panel padding.
- **States**: passive chrome surface; no hover state.

### Position button

- **Structure**: `Button.PositionButton` with centered content.
- **Variants**: default and `.active`.
- **States**: active uses `ChromeAccentBrush` background and white text.

### Graph toolbar button

- **Structure**: compact `Button.PositionButton` above graph rows.
- **Size**: 30px min height, 12px text, 10px horizontal padding.
- **Purpose**: preserve graph data area while keeping controls readable.

## 6. Motion & Interaction

### Timing

| Type | Duration | Easing | Usage |
|------|----------|--------|-------|
| Alert pulse | 700ms | alternate | Warning overlays |
| Standard control | Fluent default | Fluent default | Buttons, toggles, combo boxes |

### Rules

- Keep interactions native Avalonia/Fluent unless a specific diagnostic state needs custom feedback.
- Do not animate graph layout dimensions.

## 7. Depth & Surface

### Strategy

Mixed glass chrome with square edges.

| Level | Token | Usage |
|-------|-------|-------|
| Backdrop | `AmbientBackdropBrush` | Main app field |
| Glass panel | `GlassPanelBrush` | Floating panels |
| Rim | `GlassRimBrush` | Crystal edge and dividers |
| Shadow | `GlassShadow` | Panel lift |

### Rules

- Corners stay square.
- Graph data areas stay visually flatter than surrounding chrome.
