# Doba design system

Everything visual comes from `src/Doba.App/Theme/` and every duration and easing from `src/Doba.App/Motion/`.
`tests/Doba.App.Tests/DesignSystem/NoLiteralsTests.cs` enforces it:

* Everywhere in `src/Doba.App` except the token definitions (`Theme/Tokens.axaml`, `Theme/AccentPalette.cs`,
  `Motion/Motion.cs`, `Motion/MotionTimes.cs`) and `Platform/`: no colour literal (`#RRGGBB`, `Colors.X`,
  `Brushes.X`, a named colour in a brush attribute or setter, `Color.FromRgb(<n>…)`, `new Color(<n>…)`,
  `new SolidColorBrush(0x…)`), no duration literal (`TimeSpan.From…(<n>)`, `"0:0:0.2"`, `Duration="…"` /
  `Delay="…"` without a binding), no easing outside Motion (`Easing="…"`, `new …Easing(`, `KeySpline`) and no
  `Task.Delay(<n>)`. That includes the rest of `Theme/` (Styles, control themes, ThemeService, the gallery) and
  `Motion/`.
* In `.axaml` under `Screens/` and `Controls/` also no numeric layout value: `Margin`, `Padding`, `Spacing`,
  `ColumnSpacing`, `RowSpacing`, `CornerRadius`, `FontSize`, `LineHeight`, `LetterSpacing`, `Width` / `Height` and
  their min / max, `BorderThickness`, `Size`, `StrokeWidth`, `ColumnDefinitions`, `RowDefinitions`, as attributes or
  setters. Allowed: 0, ±1 (hairline rules, one-pixel nudges), star weights and `Auto`. Control themes in
  `Theme/Controls/` may still carry fixed sizes that are part of a control's look.
* A two-entry allowlist covers `DayClock`'s minute-boundary timer arithmetic.

The living reference is `src/Doba.App/Theme/Gallery/ControlsGallery.axaml` (rendered to
`docs/screenshots/controls-gallery.png`): copy markup from there.

```xml
xmlns:c="using:Doba.App.Controls"     <!-- TaskCheckBox, PriorityIcon, Icon, Kbd, Pill, ToggleSwitch, SegmentedSwitch, ProgressLine, Card, StrikeText, TimeWheel -->
xmlns:sh="using:Doba.App.Shell"       <!-- SidebarItem -->
xmlns:m="using:Doba.App.Motion"       <!-- MotionTimes / Motion for transitions -->
xmlns:t="using:Doba.App.Theme"        <!-- ThemeConverters -->
```

## 1. Rules

* Colours: `{DynamicResource Brush.*}` (palette lives in a `ThemeVariant` dictionary — Dark only today, Light can be
  added with the same keys). `Color.*` exists for every brush when a `Color` is needed. The chrome base colour and the
  box-shadow lists are palette tokens too (§2.1).
* Accent: `Brush.Accent`, `Brush.Accent10` (0x1A), `Brush.Accent15` (0x26), `Brush.Accent25` (0x40), `Color.Accent`;
  the schedule's `Brush.Accent16` (0x29), `Brush.Accent35` (0x59), `Brush.Accent45` (0x73), `Brush.Accent70` (0xB3).
  Changed live by `ThemeService.ApplyAccent` (brushes are mutated in place; `Color.Accent` is rewritten → use DynamicResource).
* Density: `{DynamicResource Density.*}` only (rewritten by `ThemeService.ApplyDensity`).
* Fonts: the Window/PopupRoot themes set `Font.Sans` 13 px, `Text.Primary`, tabular numbers (`tnum`) app-wide. Use
  `FontWeight` Light (300) / Normal / Medium (500) / SemiBold (600) / Bold (700). Avalonia has no `text-transform`:
  upper-case labels in the projection or with `{x:Static t:ThemeConverters.Upper}`.
* Line height: Avalonia uses the font's natural line height; the CSS root is 1.4. Set `LineHeight` from the
  `LineHeight.*` tokens for multi-line text (card titles, notes, descriptions); single-line rows are sized by the
  container height, as in the prototype.
* Motion: never write a duration. In XAML transitions use `Duration="{x:Static m:MotionTimes.Hover}"
  Easing="{x:Static m:Motion.EaseStd}"` **inside a ControlTemplate** (evaluated per control, so the OS
  "animations off" setting is honoured); in code use `Motion.Ms(Motion.X)` or `Animator`. Animate only opacity and
  render transforms.
* Focus: every focusable control gets the keyboard focus ring automatically (see §4.12). Don't draw your own.

## 2. Tokens (`Theme/Tokens.axaml`)

### 2.1 Colours (`Color.<name>` / `Brush.<name>`)

| Token | Value | Where in the design |
|---|---|---|
| Bg.Window | `#0B0B0C` | window / sidebar backdrop |
| Bg.Main | `#0F0F10` | main panel (`<main>`), now-line label background, swatch ring gap |
| Bg.Card | `#17171A` | week/day cards, month items, key caps |
| Bg.CardHover | `#1C1C20` | card hover, density preview rows |
| Bg.SpanDone | `#161618` | done multi-day bar fill |
| Bg.BlockDone | `#19191C` | done schedule block fill |
| Bg.Dialog | `#141416` | task dialog, density option cards |
| Bg.DialogFooter | `#111113` | dialog footer; logo check stroke |
| Bg.Popover | `#1A1A1D` | Date/Time/Priority/Tag popovers |
| Bg.Selected | `#1E1E22` | header segment indicator, open pill, density "compact" button |
| Bg.Control | `#2A2A2F` | toggle track off, selected settings segment / duration / chip |
| Bg.Track | `#232326` | progress rail, done multi-day bar ring |
| Bg.Primary | `#E4E4E7` | primary button ("New task", "Create task") |
| Bg.Logo | `#EDEDEF` | title-bar logo tile |
| Bg.Shade | `rgba(5,5,6,0.6)` | dialog backdrop; the 3 px blur behind it is `Size.Overlay.BackdropBlur`, applied by `OverlayHost` |
| Glow.A / B / C | `#3A3A3E` / `#2A2A2E` / `#303034` | blurred background blobs behind the chrome |
| Overlay.Faint | white 1.5 % | month cells outside the month |
| Overlay.Stripe | white 2.2 % | next-week preview hatch (135°, 6 px stripes) |
| Overlay.Cell | white 2.5 % | month cell hover |
| Overlay.Field | white 3.5 % | search box fill, set pill fill |
| Overlay.Hover | white 4 % | `.ob` hover (outline/ghost buttons, pills, Add task), settings segment group |
| Overlay.Selected | white 5 % | selected popover row, new-tag input |
| Overlay.Press | white 6 % | caption button hover, menu row hover (`.mi`) |
| Overlay.Edge | white 7 % | main panel top/left border, time-wheel band |
| Overlay.Active | white 8 % | sidebar selection, search border |
| Overlay.KbdBorder | white 12 % | "Ctrl K" border |
| Overlay.OnPrimary | black 12 % | kbd inside the primary button |
| Text.Strong | `#F4F4F5` | sidebar item hover / current |
| Text.Primary | `#EDEDEF` | body text, titles |
| Text.Bright | `#D9D9DE` | outline buttons, key caps, shortcut rows |
| Text.Secondary | `#C9C9CE` | "Todo" label, caption glyphs, notes, times in meta |
| Text.Nav | `#B4B4BB` | sidebar items |
| Text.Tertiary | `#A1A1A6` | inactive segments, icon buttons, summaries, "Nov" |
| Text.Muted | `#8C8C92` | labels, meta, counts, placeholders, done titles |
| Text.Dim | `#77777E` | mini calendar days outside the month |
| Text.Faded | `#6E6E75` | next-week preview day number, weekend letters |
| Text.Faint | `#55555B` | "/" separators |
| Text.OnPrimary | `#0E0E0F` | text on light/accent fills, check marks, ASAP "!" |
| Text.White | `#FFFFFF` | task title hover (`.tt:hover`) |
| Border.Subtle | `#1D1D20` | row dividers, header bottom border, grid lines |
| Border.Default | `#26262A` | outline buttons, dialog ring, popover separators, header separators |
| Border.Popover | `#2A2A2F` | popover ring, chips, popover "Done" |
| Border.Strong | `#2E2E33` | dashed Add task / pills, kbd, scrollbar thumb |
| Border.Emphasis | `#3A3A40` | open pill, selected chip, field focus, new-tag ring |
| Border.Focus | `#4A4A52` | keyboard focus ring, calendar day hover ring |
| Border.Danger | `#3A2A28` | "Delete" border |
| Border.Check | `#66666E` | checkbox ring |
| Warning | `#D9A066` | ASAP badge, OVERDUE / ASAP group labels |
| Danger | `#E8907F` | "Delete" text |
| CloseHover | `#C42B1C` | close caption button hover |
| Priority.On / Off | `#D9D9DE` / `#3A3A40` | priority bars |
| Knob.Off / On | `#BDBDC3` / `#0E0E0F` | toggle knob |
| Slider.Border / Rail | `#858585` / `#3B3B3B` | transparency slider (Chromium's native range look) |
| Accent.Mist / Sage / Sand / Gray | `#9FB4C7` / `#A3B8A0` / `#C8B596` / `#C4C4CA` | Settings swatches |
| Accent (+10/15/25) | runtime | today, progress, check fill, now-line, selections; 10 % bar fill, 15 % range strip, 25 % bar ring/selection |
| Accent16 / 35 / 45 / 70 | runtime | schedule: dragged copy / resized block fill (16 %), dashed origin (35 %), copy / resized ring (45 %), grip bar (70 %) |
| Schedule.Hover (Lists region) | white 4.5 % (`#0BFFFFFF`) | schedule block hover layer (`.sbo`) |
| Schedule.Focus (Lists region) | `rgba(237,237,239,0.7)` | schedule block focus outline (1.5 px, offset 2; the checkbox's offset −3) |
| Schedule.List (Lists region) | `#18181B` | the schedule's "+N" list |
| Schedule.CopyBase / More / MoreHover / MoreOpen (brushes) | Bg.Dialog / Bg.Dialog / Bg.CardHover / Bg.Selected | opaque base under the dragged copy; "+N" background at rest / hover / open |
| Chrome.Base (colour, Dark palette) | `#161618` | base of the chrome tint; `ThemeService.ApplyChromeOpacity` reads it at startup and on a theme-variant change |
| Chrome (brush only, theme-independent) | `rgba(22,22,24, transparency %)` (default 72 %) | tint over the backdrop (sidebar + title bar) |
| Shell.GlowA / B / C (`Brush.Shell.*`, Dark) | radial gradients of Glow.A / B / C | the painted backdrop glows (`Shell.Backdrop` theme); hidden while Mica / acrylic is active |

Box shadows (`BoxShadows`, Dark palette; Avalonia paints the list last-on-top, so the 1 px ring comes last):

| Token | Value | Use |
|---|---|---|
| Shadow.Popover | `0 18 44 rgba(0,0,0,.55)`, ring `#2A2A2F` | Date / Time popovers (`Border.popover`) |
| Shadow.PopoverSmall | `0 16 40 rgba(0,0,0,.5)`, ring `#2A2A2F` | Priority / Tag popovers (`Border.popover-sm`) |
| Shadow.Dialog | `0 30 80 rgba(0,0,0,.6)`, ring `#26262A` | task dialog, search palette (`Border.dialog`) |
| Shadow.Ring.Emphasis | inset 1 px `#3A3A40` | gallery sample |
| Shadow.Schedule.Lift (Lists region) | `0 10 28 rgba(0,0,0,.5)`, `0 2 6 rgba(0,0,0,.35)` | lifted schedule copy, resized / landing block |
| Shadow.Schedule.List (Lists region) | `0 18 40 rgba(0,0,0,.55)`, ring `#2A2A2F` | the schedule's "+N" list |

### 2.2 Type

| Token | Value | Use |
|---|---|---|
| Font.Sans | Geist, Segoe UI Variable Text, Segoe UI | everything |
| Font.Mono | Geist Mono, Cascadia Mono, Consolas | labels, counts, times, tags, kbd |
| Font.Features | `tnum` | tabular numbers (set by the Window theme) |
| FontSize.10 | 10 | ASAP "!" |
| FontSize.10.5 | 10.5 | kbd, sidebar TAGS, card meta, "+N more", hints |
| FontSize.11 | 11 | mono labels, crumbs, meta, key caps, duration options |
| FontSize.11.5 | 11.5 | month items / bars, "1 day" |
| FontSize.12 | 12 | summaries, chips, popover "Today"/"Done" |
| FontSize.12.5 | 12.5 | buttons, segments, pills, desc, bar titles |
| FontSize.13 | 13 | body, sidebar, week card titles, menu rows |
| FontSize.13.5 | 13.5 | list-row titles, Add task, notes, day cards |
| FontSize.14 | 14 | settings row label, mini calendar month |
| FontSize.15 | 15 | title suffix, wheel numbers |
| FontSize.18 / 19 | 18 / 19 | wheel ":" / selected wheel number |
| FontSize.20 | 20 | task name input |
| FontSize.24 | 24 | page title, year month names |
| FontSize.40 | 40 | week day numbers |
| LetterSpacing.Label | 0.88 | 0.08em @ 11 px mono uppercase labels / crumbs |
| LetterSpacing.LabelSmall | 0.84 | 0.08em @ 10.5 px (TAGS, SETTINGS) |
| LetterSpacing.Stat | 0.66 | 0.06em @ 11 px year stats |
| LetterSpacing.Title | -0.48 | page title |
| LetterSpacing.MonthName | -0.72 | year month names |
| LetterSpacing.DayNumber | -1.6 | week day numbers |
| LetterSpacing.DialogTitle | -0.2 | task name |
| LetterSpacing.PopoverTitle | -0.14 | mini calendar month |
| LineHeight.Body / Card / DayTitle / Notes / Desc / Para / Title | 18.2 / 18 / 18.225 / 20.925 / 18.125 / 19.375 / 27.6 | see Tokens.axaml comments |

### 2.3 Shape, size, spacing, opacity

| Token | Value | Use |
|---|---|---|
| Radius.Bar | 2 | progress lines, preview bars |
| Radius.Badge | 3 | ASAP badge, 12/10 px checkboxes |
| Radius.Check | 4 | 16/14 px checkboxes, small kbd, priority button, scrollbar thumb |
| Radius.KeyCap | 5 | key caps, month items, logo tile |
| Radius.Item | 6 | sidebar/menu items, settings segments, week bars, small button |
| Radius.Control | 7 | 30 px buttons, header segments, chips, schedule blocks |
| Radius.Card | 8 | cards, pills, Add task, settings segment group, day spans |
| Radius.Panel | 10 | main panel corner, priority/tag popovers, density cards, toggle |
| Radius.Dialog | 12 | dialog, date/time popovers |
| Radius.MainPanel | 10,0,0,0 | main panel top-left |
| Size.TitleBar / Sidebar / Rail | 40 / 232 / 56 | shell |
| Size.Control / ControlSmall | 30 / 28 | buttons, pills / dialog close, chips, search |
| Size.Caption.Width | 46 | caption buttons |
| Size.NavItem / TagItem | 32 / 30 | sidebar rows |
| Size.Segment.Width | 62 | header segments (step 64 = `Motion.SegStep`) |
| Size.AddTask | 38 | Add task row, day spans |
| Size.Icon / Size.Check | 16 / 16 | |
| Size.Dialog.Width / Top | 640 / 150 | task dialog |
| Size.Popover.Date / Time / List | 316 / 248 / 200 | popovers |
| Size.Schedule.Width | 380 | Today/Day schedule column |
| Size.Settings/Today/Day.MaxWidth | 780 / 760 / 680 | content max widths |
| Size.Scrollbar | 8 | |
| Space.PageHeader / PageBody | 28,20,24,18 / 28,16,24,20 | page header / body padding |
| Space.Sidebar / NavItem | 10,4,10,10 / 10,0 | |
| Space.Button / ButtonWithKbd / Pill / AddTask | 12,0 / 12,0,6,0 / 11,0 / 12,0,8,0 | |
| Space.SettingsRow | 4,14 | settings rows (min height 66) |
| Space.Dialog.Header / Body / Pills / Footer | 20,0,10,0 / 20,0,20,4 / 20,10,20,18 / 20,12,14,12 | |
| Opacity.Disabled / Dimmed / HeaderHover | 0.45 / 0.3 / 0.75 | disabled controls / tag filter / `.hd:hover` |

### 2.3a Package regions

The tokens above are shared. Everything else is in the marked regions at the end of `Tokens.axaml` (once in the Dark
dictionary for colours and brushes, once in the theme-independent part for sizes); new tokens go into the region of
the area they belong to, and shared tokens are never moved or renamed. Each region comments every value with its
source in the prototype markup and, where a value was adjusted to land on the reference pixels, why.

| Region | Prefixes | Holds |
|---|---|---|
| Shell | `Size.Shell.*`, `Space.Shell.*`, `Thickness.Shell.*`, `Size.Overlay.BackdropBlur`, `Brush.Shell.Glow*` | title bar, page header, window default 1600×1000 and minimum 1100×700 (`Size.Shell.Window*`), backdrop blur, glows |
| Sidebar | `Sidebar.*` | nav gap (highlight step 33), tag header, add-tag row, settings label, footer |
| Month | `Space.Month.*`, `Radius.Month.*`, `Month.*`, `Size.Month.*` | grid paddings, bars, chips (row / lane / chip heights are in `Screens/Month/MonthLayout.cs`) |
| Day | `Size.Day.*`, `Space.Day.*`, `LineHeight.Day.Title` | spans, cards, meta row, schedule aside |
| Dialog | `Size.Dialog.*`, `Space.Dialog.*`, `Radius.Dialog.*`, `Thickness.Dialog.*` | task dialog and popovers; `Space.Dialog.Lift` / `Lift2` / `Raise` put single-line labels on the reference rows |
| TimeWheel | `Size.TimeWheel.*`, `Space.TimeWheel.Band`, `Brush.TimeWheel.Mask` | 34 px rows, 70×170 column, fade mask |
| Search | `Size.Search.*`, `Space.Search.*`, `Thickness.Search.Rule`, `Radius.Search.Footer` | palette query row, list, "More than 50" line |
| Lists | `Space.Inbox.*`, `Space.List.*`, `Space.Today.*`, `Space.Group.*`, `Space.Row.*`, `Space.AddTask.*`, `Space.Schedule.*`, `Size.Schedule.*`, `Thickness.Schedule.Focus`, `*.Schedule.*` colours / shadows, `Brush.Accent16/35/45/70`, `Space.Settings.*`, `Size.Settings.*`, `Radius.Settings.Swatch` | Inbox / Today / Settings layout, list rows, schedule timeline (see below) |
| Calendar | `Space.Week.*`, `Size.Week.*`, `Opacity.Week.*`, `Space.Year.*`, `Year.*`, `Size.Year.*` | Week header, cards, bars; Year cells (card-fitting sizes are in `Screens/Week/WeekLayout.cs`) |

Schedule (Today / Day aside; `Controls/ScheduleTimeline.axaml`):

| Token | Value | Use |
|---|---|---|
| Space.Schedule.Header | 0,4,0,6 | header margin (stays put; the grid scrolls under it) |
| Space.Schedule.Scroll / ScrollOut | 0,8,8,8 / 0,0,−8,0 | the grid scroller's padding 8 8 8 0 and margin-right −8 (its 8 px bar sits beside the 380 px column) |
| Size.Schedule.Check / CheckStroke / CheckHit | 12 / 4 / 24 | block checkbox (r3, ring Text.Muted), its check stroke (of 24), its hit area (left 3, top `chkTop`, r5 = Radius.KeyCap) |
| Space.Schedule.Grip, Size.Schedule.GripWidth / GripHeight | 1,0,1,−2 / 18 / 3 | grip zone (bottom −3 inside the border, height 11 or 8 from Core) and its bar (r2 = Radius.Bar) |
| Thickness.Schedule.Focus, Space.Schedule.FocusOffset / CheckFocusOffset | 1.5 / 2 / −3 | block focus outline (`FocusRing` with Schedule.Focus, unclipped) |
| Size.Schedule.ChipLabel / ChipRule | 46 / 8 | time chip in the hour column (Geist Mono 11/500 accent on Bg.Main) and its 8×1 rule |
| Space.Schedule.MoreTall | 0,7,0,0 | "+N" text 6 px under its 1 px dashed border when ≥ 44 px tall |
| Size.Schedule.List, Space.Schedule.List / ListHead | 232 / 6 / 8,4,8,6 | "+N" list width, padding (r10 = Radius.Panel), head padding |
| Size.Schedule.ListRow, Space.Schedule.ListGap / ListOpen / ListOpenGap | 32 / 2 / 8,0 / 8 | list rows: 28 px checkbox button (Size.ControlSmall), gap 2, open button padding and title ↔ time gap |

Block geometry (x, width, top, height, content tier, `chkTop`, grip height; 302 px column area at x 54, gap 4, up to three
columns) is computed in Core (`Doba.Core.Projections.ScheduleLayout` / `ScheduleColumns`); block content padding and
the time line's offset are `ScheduleBlockViewModel.ContentPaddingFor` / `MetaOffsetFor`.

### 2.4 Density (`ThemeService.DensityTable`)

| Key | Comfortable | Compact | Use |
|---|---|---|---|
| Density.RowPadding | 4,11 | 4,5 | Inbox/Today list rows |
| Density.TitleSize | 13.5 | 13 | list-row and day-card titles (`Button.Title` uses it) |
| Density.CardPadding | 11,9 | 11,6 | week cards (`Card` default) |
| Density.CardMinHeight | 36 | 30 | week cards |
| Density.CardGap | 9 | 3 | week card: checkbox ↔ title |
| Density.DayCardPadding | 12,11 | 12,6 | day cards |
| Density.DayCardGap | 6 | 3 | day card: title row ↔ meta row |

### 2.5 Icons (`Theme/Icons.axaml`, draw with `c:Icon`)

`Icon.Logo, Search, Caption.Minimize/Maximize/Close (ViewBox 10), Inbox, Today, Calendar, SidebarCollapse, Settings,
Back, Sidebar, ChevronLeft/Right, Plus, PlusSmall, Minus, Close, Check, ArrowRight, Date, Clock,
Tab.Appearance/Calendar/Tasks/Windows/Data/Shortcuts`. Each entry's comment gives the design's size and stroke width,
e.g. `<c:Icon Data="{StaticResource Icon.Search}" Size="14" StrokeWidth="1.8" />` (StrokeWidth is in viewBox units,
like SVG). The stroke uses the inherited foreground.

## 3. Runtime theme (`Theme/ThemeService.cs`, `Theme/AccentPalette.cs`)

```csharp
var theme = new ThemeService(Application.Current!);
theme.Apply(settings);                       // startup: accent, density, chrome tint, no animation
theme.ApplyAccent(Accent.Sage, animate: true);        // Settings: 300 ms `ease` cross-fade (instant if OS animations off)
theme.ApplyDensity(Density.Compact);
theme.ApplyChromeOpacity(72, animate: true);          // Brush.Chrome = rgba(22,22,24,0.72)
AccentPalette.ColorOf(Accent.Mist);                   // swatch colours
```

## 4. Controls and control themes

Pick named themes with `Theme="{StaticResource Button.Outline}"`. Templates live in `Theme/Controls/*.axaml`.

### 4.1 Buttons (`Buttons.axaml`)

| Theme | Look | Use |
|---|---|---|
| `Button.Ghost` (= default `Button`, `RepeatButton`) | transparent, Text.Muted, r6, hover Overlay.Hover 180 ms | tag "+" 24×24, dialog close 28×28, priority button 18×18 r4 |
| `Button.Outline` | 30 px, 1 px Border.Default, r7, 0 12, 12.5 Medium Text.Bright, hover Overlay.Hover | "Today", "Cancel", settings buttons |
| `Button.OutlineIcon` | 30×30, Text.Tertiary | sidebar toggle (Icon.Sidebar 16/1.7), ‹ › (15/1.8) |
| `Button.Danger` | outline with Border.Danger / Danger | "Delete" |
| `Button.Small` | 24 px, r6, 0 8, mono 10.5 | "→ Today" |
| `Button.PopoverDone` | 28 px, Border.Popover, 12 Medium | time popover "Done" |
| `Button.Primary` | 30 px, Bg.Primary, Text.OnPrimary, 12.5 SemiBold, 12 0 6; disabled fades to 0.45 (200 ms) | "New task" + `Kbd.onPrimary` (gap 8) |
| `Button.Menu` | 32 px, r6, 0 8, 13 Text.Primary, hover Overlay.Press 150 ms, `Classes="selected"` = Overlay.Selected | popover rows (icon 14 · label · tick 14, gap 10); set Foreground Text.Tertiary + Height 26/28 for popover header buttons |
| `Button.Caption` / `Button.CaptionClose` | 46×40, Text.Secondary, hover Overlay.Press / CloseHover (150 ms), not focusable | caption buttons (`Icon ViewBox=10 Size=10 StrokeWidth=1`) |
| `Button.Crumb` | mono 11, 0.88 spacing, 0 2, Muted → Primary (200 ms) | breadcrumbs (separator: `TextBlock.label.faint` "/") |
| `Button.Link` | mono 10.5, 6 2, Muted → Primary | "+N more", week numbers, inbox day links (override FontSize/LetterSpacing/Padding per use) |
| `Button.Dashed` | 38 px, dashed 3/2 Border.Strong, r8, 12 0 8, 13.5 Muted, hover Overlay.Hover | "Add task" (Icon.Plus 14/1.8 · label · `Kbd.muted`, gap 10) |
| `Button.Search` | 28 px, Overlay.Field, 1 px Overlay.Active, r7, 10 0 6, 12.5 Tertiary | title-bar search (Icon.Search 14 · label · Kbd "Ctrl K", gap 8) |
| `Button.Header` | transparent; hover fades content to 0.75 (200 ms) | week day header, year month header (`.hd`) |
| `Button.Title` | no chrome, left, `Density.TitleSize`, Primary → White on hover (300 ms) | task titles (`.tt`); content is a `c:StrikeText` (§4.16); done: `Foreground=Text.Muted` + `Struck` |
| `Button.Cell` | Border.Subtle top/left 1 px, hover Overlay.Cell (200 ms) | month day cells (Background Overlay.Faint outside the month) |
| `Button.Bar` | Accent10 fill + Accent25 1 px ring, r6, 10 0; hover brightens (overlay, 200 ms) | week multi-day bars (28 px, `TaskCheckBox BoxSize=14`), day spans (38 px, r8); done: Bg.SpanDone / Bg.Track. Schedule blocks are a layered panel of their own (`Controls/ScheduleTimeline.axaml`: frame, 4.5 % hover layer 180 ms, open button, checkbox button, grip) |
| `ToggleButton` (default) | ghost; checked Overlay.Selected | generic |
| `ToggleButton.Chip` | 28 px, r7, 0 10, Border.Popover, 12 Medium Tertiary; checked Bg.Control + Border.Emphasis + Primary (150 ms) | date quick options (+ `TextBlock.meta-sm` hint, gap 6) |

### 4.2 `c:TaskCheckBox` (`TaskCheckBox.axaml`)
`BoxSize` 16 (default; lists, cards, spans), 14 (week bars), 12 (year rows), 10 (month items, no mark). Ring
Border.Check → accent fill 250 ms, mark Text.OnPrimary 200 ms. Bind `IsChecked`. The CSS ring is 1.5 px; the control
snaps it down to whole device pixels, at least one (`TaskCheckBox.RingWidth(scaling)`: 1 px at 100 %, 1 px at 125 %,
2 px at 150 %), as Chromium paints it, and re-snaps when the window's scaling changes. In cards use
`VerticalAlignment="Top"` with the area's check-margin token (e.g. `Space.Day.Check`).

### 4.3 `c:PriorityIcon`
`Priority="{Binding Priority}"` (Doba.Core.Model.Priority): bars 13×13 lit by rank (Low 1, Medium 2, High 3) in
Priority.On/Off, or the ASAP badge (Warning, "!" Bold 10). Not focusable: wrap in a ghost `Button` 18×18 r4
(`CornerRadius="{DynamicResource Radius.Check}"`) for click-to-cycle.

### 4.4 `c:SegmentedSwitch` (`Switches.axaml`)
Header Year/Month/Week/Day: a ListBox — `SelectedIndex` two-way (−1 hides the indicator, 250 ms, and parks it under
the first item). Items 62×30, gap 2, 12.5 Medium, Tertiary → Primary (250 ms); the Bg.Selected indicator slides with a
composition FLIP (420 ms ease-out), continuing from its on-screen position on rapid clicks.

### 4.5 `SegmentedControl` (ListBox theme)
`<ListBox Theme="{StaticResource SegmentedControl}">` settings pills ("Dark | System"): Overlay.Hover group r8
padding 2 gap 2, items 26 px 0 11 r6 12.5 Medium; selected Bg.Control + Primary (200 ms).
`SegmentedControl.Fill` — equal widths, 28 px (time popover "Any time | Set time").
`SegmentedControl.Mono` — 24 px, 0 8, mono 11 (durations "15m 30m 1h 2h").

### 4.6 `c:ToggleSwitch`
36×20 track r10 padding 2: Bg.Control → accent (200 ms); 16 px knob Knob.Off → Knob.On, travels 16 px (composition,
250 ms ease-out). Bind `IsChecked`. (Ours, not Avalonia's ToggleSwitch, which moves the knob through layout.)

### 4.7 `c:Pill`
Dialog pills: content = icon 14 (Icon.Date / Icon.Clock, 1.7) or PriorityIcon or mono "#" + label, `Spacing="7"`.
`HasValue=False` dashed Border.Strong + Muted; `HasValue=True` solid Border.Strong + Overlay.Field + Primary;
`IsChecked=True` (popover open) Border.Emphasis + Bg.Selected. Hover Overlay.Hover; 180 ms.

### 4.8 `c:Kbd` (`Inputs.axaml`)
Default ("Ctrl K" in search): 10.5 mono Tertiary, Overlay.KbdBorder, r4, 18 px. Classes: `muted` (Add task "N",
Border.Strong, 20 px), `onPrimary` (inside primary buttons: Medium, OnPrimary on Overlay.OnPrimary, no border),
`keycap` (Settings shortcuts: 11 Text.Bright on Bg.Card, Border.Strong with 2 px bottom, r5, 1 7, 20 px; gap 4–8).

### 4.9 `TextBox`
Borderless, transparent, placeholder (`PlaceholderText`) Text.Muted, selection Accent25. Classes: `title` (task name
20 Medium −0.2, 40 px), `notes` (13.5 Text.Secondary, line height 1.55, wraps, AcceptsReturn). Field chrome (new-tag
box: 30 px, r6, Overlay.Selected, inset ring Border.Emphasis; search palette) is drawn by the parent Border.

### 4.10 `ScrollViewer` / `ScrollBar`
8 px bar beside the content (not overlay), transparent track, Border.Strong thumb r4, no arrows. Keep the views'
12 px right padding as in the prototype.

### 4.11 `c:ProgressLine`
`Value` 0..1 (`<c:ProgressLine Value="{Binding Done}" Margin="0,14,0,0" />`): 2 px Bg.Track rail r2, accent fill. Changes
animate the fill's ScaleX (origin left) on the compositor, 500 ms ease-out — never Width.

### 4.12 Focus ring (`c:FocusRing`, `Base.axaml`)
`AdornerLayer.DefaultFocusAdorner` = a 1 px Border.Focus ring inset 2 px (the design's `outline-offset:-2px`) that
follows the control's CornerRadius. Avalonia shows focus adorners only for Tab/arrow focus, never for clicks. Every
focusable control gets it; nothing to do in views. Caption buttons and Kbd are not focusable.
The one exception is the schedule block (`.sbm:focus-visible`): its open button sets its own `FocusAdorner` — a
`FocusRing` of 1.5 px `Brush.Schedule.Focus` 2 px *outside* the button, with `AdornerLayer.IsClipEnabled="False"` so the
layer does not clip it to the button (the checkbox button's ring is offset −3).

### 4.13 `c:Card` (`Surfaces.axaml`)
Bg.Card, r8, hover Bg.CardHover (180 ms), `Padding` = `Density.CardPadding` by default (day cards:
`Padding="{DynamicResource Density.DayCardPadding}"`), set `MinHeight="{DynamicResource Density.CardMinHeight}"` for week cards.

### 4.14 Other stock controls
`Window` (Bg.Window, font, tabular numbers, VisualLayerManager for adorners/overlay popups), `PopupRoot`,
`OverlayPopupHost`, `FlyoutPresenter` (popover surface), `ToolTip`, `ItemsControl`, `ListBox` / `ListBoxItem`
(plain, no selection chrome), `TransitioningContentControl` (no PageTransition — use Animator), `Thumb`, `Slider`
(Settings transparency: 8 px rail, accent fill, 16 px accent thumb; width 180).
Anything else (ComboBox, Calendar, Menu…) has **no template** — add a theme here first.

### 4.15 Style classes (`Styles.axaml`)
TextBlock: `mono`, `label` (mono 11 0.88 Muted), `section` (label + Medium; group headers), `label-sm` (10.5),
`meta` (mono 11 Muted), `meta-sm` (mono 10.5 Muted), `title` (24 Medium), `subtitle` (15 Muted), `day-number`
(40 Light), `month-name` (24 Light), `row-label` (14 Medium), `desc` (12.5 Muted, wraps), and colour classes
`primary bright secondary tertiary muted faint accent warning`.
Border: `popover` (Bg.Popover r12 + `Shadow.Popover`), `popover-sm` (r10 + `Shadow.PopoverSmall`), `dialog`
(Bg.Dialog r12 + `Shadow.Dialog`), `divider` (1 px Border.Subtle), `divider-strong` (1 px Border.Default), `vsep` (1×18
header separator). Overlay views set `ClipToBounds=False` so the ring and shadow outside the panel are drawn.

### 4.16 `c:StrikeText`
A `TextBlock` for task titles whose strike-through is always there and only changes colour (transparent ↔ `Struck`
colour, `Motion.Strike` 300 ms `EaseStd`), so completing a task never re-lays out the text. It draws its own line, not
Avalonia's `TextDecoration`: one device pixel thick on a whole pixel row, `⌈strikeout position + ½⌉` device pixels
above the baseline (5 px for the 11.5–13.5 px titles), as Chromium paints `line-through`. It reads the duration at
every change (instant when OS animations are off, also when switched while running), attaches the fade only while
animations are on, and snaps without fading on a data-context change or inside a `SnapScope`. Every task title on
every screen is a `StrikeText` (a test checks this).
`<c:StrikeText Text="{Binding Title}" Struck="{Binding Done}" StrikeColor="{DynamicResource Color.Text.Muted}" />`

### 4.17 Sidebar (`Theme/Controls/Sidebar.axaml`, `Shell/SidebarItem.cs`)
`sh:SidebarItem` (a `Button`) replaces the earlier `Button.Nav`. Default theme: a 32 px row, r6, padding 0 10, 13
Medium Text.Nav; icon 16 (stroke 1.6) · label · mono 11 Text.Muted count, gap 10. Pseudo-classes: `:current` (the open
section: text Text.Strong; its background is the separate sliding `SidebarHighlight`, 380 ms, step 33 px),
`:selected` (own Overlay.Active background: settings tab, active tag filter), `:rail` (inherited; in the 56 px rail
label and count fade over `Motion.RailLabels`, the Collapse icon turns 180°). `SidebarItem.Tag`: a 30 px tag row with a
16 px badge ("#" when expanded, the upper-case initial in the rail, accent when it is the filter); the label ends in an
ellipsis before the count. Hover / current colour changes take 200 ms.

### 4.18 Shell, dialog, search and wheel themes
* `Shell.Backdrop` (`Shell.axaml`): the painted backdrop — three radial-gradient boxes (`Brush.Shell.GlowA/B/C`) in a
  Canvas, nothing blurred at run time; hidden while Mica / acrylic is active.
* `Dialog.axaml`: `Button.DialogPrimary` (primary; `.inactive` fades to Opacity.Disabled but stays clickable),
  `Button.PopoverTool` (28×28 popover tool buttons, `.text` for the calendar "Today"), `Button.Stepper` ("Lasts" − / +),
  `Button.CalendarDay` (mini-calendar cell: 32×32 r8 square, hover ring Border.Focus 150 ms, selection accent, range
  band Accent15; the square uses `BackgroundSizing=OuterBorderEdge` so its fill reaches its edges).
* `Search.axaml`: `TextBox.Search` (15 px borderless query, no focus ring), `Button.SearchHit` (a `Button.Menu` row,
  not focusable — the keyboard stays in the query box), `ScrollViewer.Search` (the 8 px bar drawn over the list's
  right padding).
* `TimeWheel.axaml`: `c:TimeWheel` (70×170 column, 34 px rows, numbers 15 → 19 px by a render-transform scale,
  colour over `Motion.WheelText`, `Brush.TimeWheel.Mask` fade) and `TimeWheelBand` (the fixed 7 % white band, r8).
* `Switches.axaml` also has the item themes `SegmentedSwitchItem`, `SegmentedControlItem`, `.Fill`, `.Mono`.

## 4a. Rendering setup and pixel snapping

* **Text**: `App.Initialize` gives every top level grayscale antialiasing and no hinting
  (`TextOptions.SetTextRenderingMode(Antialias)`, `SetTextHintingMode(None)`), like the prototype's
  `-webkit-font-smoothing: antialiased` with unhinted outlines. Glyph boxes match the references; stems carry slightly
  more ink (Skia's text gamma, not configurable in Avalonia 12.1.6). Font weights are not altered to compensate.
* **Whole pixels**: Avalonia rounds layout to whole pixels where Chromium keeps fractions. Where that put an element
  1 px off the reference, a token moves it (documented next to the token: `Space.Shell.Title`, `Space.Day.Header`,
  `Space.Dialog.Lift` / `Raise`, …). Remaining 1–2 px differences are accepted (DECISIONS "Whole-pixel layout").
* `Controls/PixelCentring`: `Icon` and `PriorityIcon` shift their drawing so a fixed-size glyph centred in an
  odd-slack box rounds half up, as Chromium does (layout and hit testing unchanged).
* `Controls/SnapScope`: while open on a visual, its XAML transitions are off and every `StrikeText` under it snaps —
  used when a recycled list row is re-pointed at another task, so it shows that task's state without fading.
* Calendar tracks put column edges at `round(i·W/n)` and row edges at `floor(j·H/m)` (`Screens/Week/TrackGrid`).

## 5. Motion (`Motion/`)

* `Motion` — tokens (ms) and `EaseOut` (entrances, slides, indicators, toggles), `EaseIn` (exits), `EaseStd` (hover,
  colour). `Motion.Ms(x)` is zero when `Motion.SystemAnimationsEnabled` is false. Added: `ColorChange = 200`; for the
  schedule `Snap = 90` (a block / the time chip stepping between slots), `Lift = 180` (the dragged copy lifting),
  `Land = 220` (landing, the Esc flight back, neighbours re-laid out) and `ChipHold = 900` — how long the time chip
  stays after an arrow-key step; it is information, not animation, so `MotionTimes.ChipHold` is never zeroed.
* `MotionTimes` — the same tokens as `TimeSpan` for `x:Static` in XAML.
* `Animator` — render-thread (composition) helpers. They animate the element's composition visual (Opacity,
  Translation, Scale + CenterPoint) and never touch layout or the UI-side `Opacity`/`RenderTransform`:

```csharp
Animator.SlideTo(host, 0, -Motion.SectionExitY * dir, Motion.Exit,
    onFinished: h => { if (!h.WasCancelled) { SwapContent(); Animator.SlideFrom(host, 0, Motion.SectionEnterY * dir, Motion.Section); } });
Animator.ZoomFrom(host, Motion.ZoomInFrom, new RelativePoint(x, y, RelativeUnit.Relative));   // zoom in from the click
Animator.ZoomFrom(host, Motion.ZoomOutFrom, new RelativePoint(0.5, 0.4, RelativeUnit.Relative));
Animator.SlideFrom(host, forward ? Motion.SlideX : -Motion.SlideX, 0);                        // ‹ › Today, settings tabs
Animator.SlideFrom(row, 0, Motion.NewRowY, Motion.NewRow);                                     // new row
Animator.Animate(dialog, new MotionState(0, 0, Motion.DialogY, Motion.DialogScale, Motion.DialogScale),
    MotionState.Rest, Motion.DialogIn, Motion.EaseOut);                                       // dialog open
```

  Every call returns a `MotionHandle` (`IsAnimating`, `WasCancelled`, `Finished` task, `Current`, `Complete()`,
  `Cancel()`). Starting a new animation on the same element supersedes the old one. `Cancel()` and
  `Animator.Reset(el)` restore opacity 1 / identity, so rapid clicks never leave content invisible. After an exit
  completes, the element stays hidden on the render thread until content is swapped, an entrance runs, or `Reset`.
  Animate a stable host (the content presenter), not freshly created content, so the composition visual exists and
  has bounds. Blur (zoom 6→0 px) is not implemented (optional in the spec).
* `ContentTransitions` — the navigation transitions (zoom, period slide, section exit + enter, Settings in / out,
  title fade, launch); the shell's default `IContentTransitions`.
* `SidebarCollapse` — the 232 ↔ 56 px collapse as a 176 px composition slide of the main panel (340 ms); widths
  change once per toggle.
* `MotionSync` — when the OS "Animation effects" setting changes while running, sets the durations of the live
  windows' XAML transitions to zero or back. Transitions created while animations were off stay instant until
  re-templated.
* `Shell/OverlayTransitions` (`AnimatedOverlayTransitions`) — dialog / palette open (shade 220 ms, panel 320 ms
  opacity + translateY 10→0 + scale 0.97→1, ease-out), close (150 ms ease-in) and replace (shade kept). The backdrop
  blur behind an overlay (`Size.Overlay.BackdropBlur`, 3 px) follows the shade's opacity; nothing is redrawn while the
  shade is at rest.
* Popovers enter in 180 ms from their top-left corner (opacity, translateY −4→0, scale 0.98→1) and close at once
  (`Dialogs/Popovers/PopoverAnchor`).
