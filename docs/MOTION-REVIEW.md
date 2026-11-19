# Motion review

Independent acceptance review of the motion spec (`docs/design/motion-spec.md`) against the implementation.

- **Reviewed commit:** `2a78749` (branch `dev`, "Screenshots with the blurred overlay backdrop; decisions and progress
  after Batch 3"), reviewed on 2026-11-18 in the worktree `final/review-motion`.
- **Gates at that commit:** `dotnet build -warnaserror` clean; `dotnet test --project tests/Doba.App.Tests` 579/579 passed.
- **How the frames were recorded.** A throwaway headless test (not committed) opened the real `MainWindow` with DemoData,
  OS animations on, at 1600×1000, visited every screen once first (so no frame waits on a first-time screen build), then
  triggered each transition with `Animator.HoldFinishTimers = true`. For every frame it took each running `Animator`
  handle, computed the state its own `From`/`To`/`Easing` give at a fixed time `t` since the handle started, applied it to
  the composition visual, forced a render tick and captured the window (`CaptureRenderedFrame`). An exit handle that
  reaches its end is completed, which starts the entrance exactly as in the app. So the frames show the real handles'
  parameters through the real composition renderer at exact times; XAML transitions (colour, hover, opacity fades in
  control templates) run on the wall clock and are only approximately in step with `t`. The probe also showed that the
  composition clock is real time: in normal real-time runs a frame is first drawn 30–400 ms after the trigger in this
  Debug headless build, and because `Ease.Out` is front-loaded (≈ 90 % of the travel by a third of the duration) such
  frames look nearly finished. That is why the deterministic sampling was used. Strips are in the git-ignored
  `artifacts/motion-review/` of the review worktree (`NN-name.png` + `NN-name.txt` with the frame times and the handles
  that completed).

## Acceptance checklist

| # | Checklist item (spec) | Covered by tests | Recorded observation | Verdict |
|---|---|---|---|---|
| 1 | All timings come from `Motion`, no number in XAML | `DesignSystem/NoLiteralsTests.App_sources_outside_Theme_and_Motion_have_no_colour_or_duration_literals` (+ `Scanner_flags_literals`, `Scanner_accepts_tokens`) | Grep of `src/Doba.App`: no `TimeSpan`/`Duration` literal for motion and no `SplineEasing`/`KeySpline` outside `Motion/`. The only `TimeSpan.From*` are non-animation clocks (DayClock, SelfCheck, PerfSession, chord timeout) or built from `Motion` constants (TimeWheel settle, EntranceExpiry). | OK. The scanner does not check curves (see finding "curves not scanned"). |
| 2 | Zoom in starts at the click point, zoom out from the centre | `Motion/TransitionTests.Zoom_in_starts_from_the_click_point_and_zoom_out_from_the_default_origin`, `Each_kind_swaps_first_then_enters_with_the_spec_values` | Strip 01: clicking Wed in Week zooms Day in around (62.5 %, 5.1 %) — the content grows from the upper right where the header was clicked. Strip 02: Day → Year via the switch grows down from 1.10 around 50 % 40 %. | OK ("centre" = the prototype's `50% 40%`, which breadcrumbs and the switch also use in the prototype). |
| 3 | Inbox → Today → Calendar comes from below, back from above; the sidebar highlight slides in sync | `Motion/TransitionTests.Sections_exit_first_swap_after_the_exit_and_enter_from_below_going_down` | Strips 05/06: old screen fades and moves 10 px opposite, then the new one rises 18 px (down) / drops (up). The sidebar highlight starts at the click, while the old screen is still exiting, and ends 130 ms before the content (strip 05 frames t = 40–120; strip 22). | Directions OK. Sync: deviation (finding "highlight leads the swap"); the test explicitly does not assert the timing. |
| 4 | Rapid repeated clicks do not break state or leave invisible content | `Motion/TransitionTests.Thirty_rapid_mixed_navigations_leave_the_content_fully_visible`, `A_click_during_a_section_exit_swaps_at_once_and_starts_the_new_step`, `A_second_toggle_mid_slide_continues_from_the_panel_and_ends_right`; `Motion/OverlayMotionTests.Rapid_open_close_open_calls_each_callback_once_and_ends_visible`, `Esc_during_the_entrance_exits_from_where_the_window_is_and_never_focuses_the_title`; `DesignSystem/AnimatorTests.Rapid_repeated_transitions_never_leave_content_invisible` | Strip 24: Next ×2 within 70 ms, and Today → Inbox during the exit, both end on a fully visible screen with the title in place. Strip 25: collapse then expand mid-slide ends at 232 px; Esc during the dialog entrance exits from where the panel was and leaves no overlay. No broken frame seen. | OK |
| 5 | The task dialog returns focus to the button it opened from | `Motion/OverlayMotionTests.Focus_returns_to_a_task_title_after_the_exit_animation`, `Focus_returns_to_New_task_after_the_exit_animation` | Not captured visually (focus is not visible in a frame unless keyboard-focused). | OK by test |
| 6 | With Windows animations off everything is instant | `Motion/AnimationsOffTests` (4 tests), `Motion/TransitionTests.With_animations_off_every_kind_swaps_at_once_and_ends_at_rest`, `Turning_os_animations_off_and_on_retimes_existing_xaml_transitions`, `Motion/OverlayMotionTests.With_animations_off_the_overlay_opens_focuses_and_closes_at_once`, `DesignSystem/AnimatorTests.Os_animations_off_makes_every_helper_instant` | Not recorded (nothing to see). | OK at start-up. Gap when the setting is turned off while running: the task-title strike still fades (finding "StrikeText not re-timed"). |

## Spec rows

Verdict "OK" means property, from/to, duration, curve and sequencing match the spec (checked in code and in the handle
values or declared transitions; the tests named assert them).

### Navigation transitions

| Spec | Implemented | Test | Recorded | Verdict |
|---|---|---|---|---|
| h1 on every transition: opacity 0→1, translateY 6→0, 360 ms E | `ContentTransitions.Enter` → `Animator.Animate(t.Title, Offset(0, TitleY, 0) → Rest, Title, EaseOut)` | `TransitionTests` `AssertTitleEntrance` in every kind | Title invisible at the start of each entrance, rises with the content (strips 01, 05, 07) | OK. Title + subtitle move together (DECISIONS "Content transitions"). |
| Zoom in: instant swap, opacity 0→1, scale 0.86→1, blur 6→0, 460 ms E, origin = click | `ContentTransitions.Zoom(…, ZoomInFrom, Origin(origin))`; origin from `Screens/Week/ZoomOrigin` | `Zoom_in_starts_from_the_click_point…`, `Each_kind_swaps_first…` | Strip 01 | OK; blur not drawn (optional in the spec; DECISIONS "Animator"). |
| Zoom out: scale 1.10→1, blur 6→0, 460 ms E | same with `ZoomOutFrom` | same | Strip 02 | OK; no blur (as above). |
| Next / prev (‹ ›, Today, Settings tabs): translateX ±32→0, opacity 0→1, 340 ms E | `Enter` Next/Prev; `Navigator.Shift/GoToday/SetTab` | `Each_kind_swaps_first…`, `Settings_zoom_around_50_30…` (tab slide) | Strips 03, 04 (Week 47/45 slide in from right/left), 08 (Settings tab) | OK |
| Sidebar section: exit opacity 1→0, translateY ∓10·dir, 130 ms EI; then enter 0→1, translateY 18·dir→0, 380 ms E; exit cancelled before entrance | `ContentTransitions.Exit/AfterExit` (`Animator.Reset` before `Enter`) | `Sections_exit_first_swap…`, `A_click_during_a_section_exit…` | Strips 05, 06 | OK |
| Settings in: content as zoom in around 50 % 30 %, new sidebar translateX 16→0 + fade, 460 / 340 ms E | `Enter` SettingsIn, `SlideSidebar(SettingsSidebar, +16)` | `Settings_zoom_around_50_30_and_slide_the_sidebar_that_appears` | Strip 07: both sidebar and content start invisible; sidebar slides from the right, content grows from upper centre | OK |
| Settings out: zoom out around 50 % 30 %, app sidebar −16→0 + fade | `Enter` SettingsOut | same | Strip 09 | OK |
| Sidebar collapse: width 232→0 (design: 56 px rail), opacity 1→0; 340 ms E, opacity 220 ms; labels fade 180 ms | `Motion/SidebarCollapse` (main panel slides by composition translation, widths change once), labels `MotionTimes.RailLabels` in `Theme/Controls/Sidebar.axaml` | `Sidebar_collapse_and_expand_slide_340ms_and_end_at_the_exact_widths`, `A_second_toggle_mid_slide…`, `A_sidebar_toggle_lays_the_week_out…` | Strips 11, 12, 27, 28: the panel edge travels 232↔56; on collapse the content is laid out at its final width at once, so its right edge is outside the window during the slide; labels have faded (180 ms ease) by the time the first frame of the slide is drawn in this Debug build | Faithful reading of the spec's own rule "layout is not animated" (DECISIONS "Sidebar collapse"). `Motion.SidebarFade` (220) is defined but unused — correct, because in the rail design the sidebar itself never fades. Needs a person (see below). |
| Launch: opacity 0→1, translateY 12→0, 520 ms E | `ContentTransitions.Launch` from `MainWindow.OnOpened` | `Launch_plays_once_without_holding_up_the_first_frame` | Strip 10: title bar, sidebar and header steady; content rises 12 px and fades in; title does not animate (as the prototype's `componentDidMount`) | OK |
| Zoom origin = element centre / content area; default 50 % 40 % | `ZoomOrigin.Of(view, source)`; `ContentTransitions.DefaultZoomOrigin` | `Zoom_in_starts_from_the_click_point…` | Strip 01 log: origin 62.5 %, 5.1 % | OK. Keyboard zooms use the default (DECISIONS). |
| Section direction: Inbox 0, Today 1, Calendar 2, Settings 3; `to ≥ from` → from below | `NavState.SectionIndex`, `Navigator.GoSection` | `Sections_exit_first_swap…` | Strips 05, 06 | OK |
| Sidebar highlight: one rectangle sliding `top`, 380 ms E, step 33 px | `Shell/SidebarHighlight` (render transform + composition FLIP) | `Sections_exit_first_swap…` (duration, curve, step) | Strips 05, 22 | Values OK; starts 130 ms before the content swap (finding). |
| View switch: translateX step 64 px, 420 ms E; text colour 250 ms; hidden on Inbox/Today/Settings (opacity 0, 250 ms) | `Controls/SegmentedSwitch`, `Theme/Controls/Switches.axaml` | `MicroTests.Header_view_switch_slides_its_indicator_by_64px_steps_in_420ms…` | Strip 20 (Week → Year slides 128 px left); strip 21: on returning to the calendar from Inbox the indicator appears under "Year" and slides to "Week" | Steps OK; extra slide on re-entry (finding). |

### Micro-interactions

| Spec | Implemented | Test | Recorded | Verdict |
|---|---|---|---|---|
| Checkbox: fill and border → accent 250 ms, check opacity 200 ms, Std | `Theme/Controls/TaskCheckBox.axaml` (`Check`, `CheckMark`, `EaseStd`) | `MicroTests.Checkbox_fills_with_the_accent_in_250ms_and_shows_its_mark_in_200ms` | Strip 16: could not be seen — the row chosen (an overdue multi-day task on Today) leaves the list at once when completed, as in the prototype. Covered by the test only. | OK by test |
| Title colour → muted, strike always present, its colour transparent → muted, 300 ms Std | `Controls/StrikeText` + `Button.Title` presenter transition (`Strike`) | `MicroTests.Done_title_fades…`, `Every_task_title_on_every_screen_is_a_StrikeText…` | Not captured on a staying row (see above) | OK (DECISIONS "Strike-through", second entry); re-timing gap (finding). |
| New task in a list: opacity 0→1, translateY −8→0, 360 ms E, only new / moved | `TaskListRow`, `TaskCard`, `DayEntrance`, `SpanBarButton` via `TaskActions.MarkCreated` | `MicroTests.New_or_moved_task_slides_in_from_minus_8_over_360ms…` | Strip 15: the new Inbox row drops in from 8 px above while fading in; neighbours do not move | OK |
| Tag filter: other tags opacity 1→0.3, 300 ms Std | `DoubleTransition Opacity Dim EaseStd` in every row/card/bar template | `MicroTests.Tag_filter_dims_other_tags_to_03_in_300ms_on_every_screen` | Strip 18: other-tag cards dim progressively, layout unchanged | OK |
| Progress (day, month, Today): width to new %, 500 ms E | `Controls/ProgressLine` (ScaleX FLIP from the left edge, as KICKOFF §7 requires) | `MicroTests.Progress_moves_by_a_left_anchored_scale_in_500ms_ease_out` | Strip 17: no change on that click (the completed task left Today's count); covered by the test | OK by test |
| Toggle: track #2A2A2F → accent 200 ms; knob travels 16 px 250 ms E | `Theme/Controls/Switches.axaml` (track `ToggleTrack`, Std — prototype `background-color 0.2s ease`), `Controls/ToggleSwitch` (knob FLIP, `Toggle`, EaseOut) | `MicroTests.Toggle_track_turns_accent_in_200ms_and_the_knob_travels_16px…` | Strip 19: knob slides 16 px left, track fades from accent to grey; no jump | OK |
| Segments: background and colour of the active one, 200 ms Std | `SegmentedControlItem` themes (`Segment`) | `MicroTests.Segment_background_and_text_change_in_200ms` (3 themes), `Settings_time_mode_and_duration_segments_use_the_segment_themes` | Strip 23 (mode segment) | OK; durations 200 instead of the prototype's 150 (DECISIONS "Small motion choices"). |
| Hover buttons / menu items: rgba(255,255,255,.04–.06), 150–180 ms Std | `Buttons.axaml` (`Hover` 180, `HoverFast` 150) | `MicroTests.Button_and_menu_item_hover_backgrounds_fade_in` | Not recorded (needs a pointer on a real display) | OK by test |
| Hover task card #17171A → #1C1C20, 180 ms Std | `Surfaces.axaml` `Hover` | `MicroTests.Task_card_hover_lifts_its_background_in_180ms` | — | OK by test |
| Hover bar / schedule block: brightness 1→1.2, 200 ms Std | `Button.Bar` `PART_Brighten` overlay, `ColorChange` | `MicroTests.Bar_and_schedule_block_hover_brightens_over_200ms` | — | Approximated by a 4 % white overlay (DECISIONS "Hover brightness") — acceptable, needs a person. |
| Hover day / month header opacity 1→0.75, 200 ms Std | `Button.Header` presenter `Opacity` `ColorChange` | `MicroTests.Day_and_month_headers_fade_to_075_on_hover_over_200ms` | — | OK by test |
| Accent / transparency change live, 300 ms Std | `Theme/ThemeService` tweens (`M.Ms(AccentChange)`, `EaseStd`) | `MicroTests.Accent_and_transparency_changes_cross_fade_in_300ms_ease`, `Settings_accent_change_reaches_the_window_through_the_cross_fade` | — | OK |

### Overlays

| Spec | Implemented | Test | Recorded | Verdict |
|---|---|---|---|---|
| Shade rgba(5,5,6,.6) + blur 3 px: in 0→1 220 ms ease-out; out 1→0 150 ms EI | `Shell/OverlayTransitions` (`Shade`, `EaseOut`; `DialogExit`, `EaseIn`); blur in `OverlayHost.FollowShade/ApplyBlur` | `OverlayMotionTests.Dialog_enters_shade_220…`, `Content_behind_an_open_overlay_is_blurred…`, `Blur_follows_the_shade_frame_by_frame…`, `OverlayBlurCostTests` | Strip 13: the page darkens and blurs together; on close both clear together; title bar included | OK. Shade uses the app's E rather than CSS `ease-out` (DECISIONS "Small motion choices"); blur follows the shade (DECISIONS "Backdrop blur") — faithful to the CSS `backdrop-filter` on a fading element. In the recording the blur level follows the wall clock, not the sampled `t`, so their exact frame-by-frame match was judged from the test, not the frames. |
| Task window in: opacity 0→1, translateY 10→0, scale 0.97→1, 320 ms E; then focus the title | `AnimatedOverlayTransitions.Open` (scaled around the laid-out centre); `OverlayHost.FocusShown` after `opened` | `Dialog_enters_shade_220_and_window_320_ease_out_then_focuses_the_title`, typed-ahead tests | Strip 13 frames t = 40–230: the window rises a little and grows into place | OK. Focus parking + typed-ahead buffering (DECISIONS "Overlay motion") is a faithful reading of "then focus the title" that does not lose keystrokes. |
| Task window out: opacity 1→0, translateY 0→6, scale 1→0.98, 150 ms EI; save and remove only after the exit | `AnimatedOverlayTransitions.Close` → `done` → `MainViewModel.CloseOverlay` → `OnClosed` | `Dialog_exits_in_150ms_ease_in_and_saves_only_when_the_exit_has_ended`, `Early_completion_during_the_exit…`, `Superseding_overlay_during_an_exit…` | Strip 13 frames t = 0–160 after close: the window sinks and fades | OK |
| Popover (Date, Time, Priority, Tag): opacity 0→1, translateY −4→0, scale 0.98→1, 180 ms E, origin top-left; closes at once | `Dialogs/Popovers/PopoverAnchor.Open/Close` | `OverlayMotionTests.Popover_enters_in_180ms_from_its_top_left_corner_and_closes_at_once` | Strip 14: Date popover drops 4 px from under the pill, growing from its top-left; Esc removes it in the next frame | OK |
| Calendar cell: hover ring 1 px #4A4A52 150 ms; selection → accent 150 ms; range = 15 % accent band | `Theme/Controls/Dialog.axaml` `Button.CalendarDay` (`HoverFast`) | `MicroTests.Calendar_cell_hover_ring_and_selection_fill_change_in_150ms` | Not recorded (hover) | OK by test |

### Time wheel

| Spec | Implemented | Test | Recorded | Verdict |
|---|---|---|---|---|
| Hours 00–23, minutes 00–55 step 5; row 34, 5 visible, 68 px padding | `Controls/TimeWheel`, `Theme/Controls/TimeWheel.axaml` | `Controls/TimeWheelTests` | Strip 23 | OK (layout read in code, not re-measured) |
| Snap to centre; value = round(offset / 34); commit after 120 ms still | `TimeWheel` settle (`WheelSettle`, not zeroed with animations off) | `TimeWheelTests` | Strip 23: value 10 → 12 commits after the scroll ends | OK (DECISIONS "Time wheel commit") |
| Click on a number: smooth scroll to it | `Animator.Animate(strip, …, Slide, EaseOut)` | `MicroTests.Time_popover_places_its_wheels_instantly_and_a_click_scrolls_smoothly` | Strip 23: strip scrolls two rows with ease-out, no jump | OK; 340 ms E chosen for the browser's unspecified smooth scroll (DECISIONS). |
| Selection band fixed, 7 % white, r 8; top/bottom mask 0 → 32–68 % → 0 | `TimeWheel.axaml` | `TimeWheelTests` | Strip 23: band fixed, rows fade at top and bottom | OK |
| Selected number 15 → 19 px, #8C8C92 → #EDEDEF, 200 ms | Scale FLIP + colour transition `WheelText` | `TimeWheelTests` | Strip 23: emphasis stays on 10 while the strip moves and moves to 12 on commit | OK (DECISIONS "Time wheel emphasis": follows the committed value; the spec says state does not change during the scroll). |
| On open both columns placed at once; scrolling turns on "Set time" | `TimeWheel` placement without animation | `MicroTests.Time_popover_places_its_wheels_instantly…` | Strip 23 first frame | OK |

### Implementation notes

| Spec | Verdict |
|---|---|
| One `Motion` service with the tokens; no timings in XAML | OK — `Motion.cs` (constants identical to the spec's snippet plus finer tokens), `MotionTimes` for XAML via `x:Static`. |
| Animate opacity and translate/scale on composition; zoom origin = centre point | OK — `Animator` (composition `Opacity`/`Translation`/`Scale`/`CenterPoint`); controls use FLIP over render transforms. |
| Sequence sections: exit, swap, stop the exit, then enter; a new click cancels the previous | OK — `ContentTransitions.Session.Interrupt/AfterExit`. |
| Animations off in Windows: all durations 0 | OK at start-up; run-time change handled by `MotionSync` (DECISIONS "OS animation setting at run time" — a defensible limit: it only errs toward no animation), except `StrikeText` (finding). |
| 60 fps on Week with ~40 cards; no visual tree rebuild during a transition | Tree reuse: `TransitionTests.Transitions_never_rebuild_the_screens_visual_trees`. 60 fps: not measurable headless (see below). |

## Only a person on a real display can judge

Build and run the AOT app with demo data (`dotnet publish src/Doba.App -c Release -r win-x64 -o artifacts\doba-win-x64`,
then `artifacts\doba-win-x64\Doba.exe --demo`) on the Windows 11 PC, with "Animation effects" on, at 100 % and at 150 %
scaling, ideally on a 120 Hz or faster monitor as well as a 60 Hz one.

1. **Smoothness.** In Week (Nov 9–15 in the demo) press ← and → ten times quickly, then 1/2/3/4 in turn. Watch for a
   stutter at the start of each slide or zoom (the first frame of a Release build should arrive well under the 30–400 ms
   seen headless in Debug). Run `Doba.exe --demo --perf --perf-scenario week` and note the frame statistics it prints.
2. **Zoom feel.** In Week click a day header near a corner (Mon 9, Sun 15): Day should grow out of that corner. Use the
   breadcrumbs and the switch to go back up: Year should settle from slightly larger around the upper middle. Judge
   whether the missing 6 px blur is noticed; if it is, it is the optional part of the spec.
3. **Sections.** Click Inbox → Today → Calendar → Today → Inbox in the sidebar at normal speed. The old page should sink
   or rise away quickly, the new one arrive from below going down and from above going up. Watch the sidebar highlight:
   since the fix of finding 1 it starts with the new page's entrance, after the 130 ms exit — check that it reads as
   "in sync".
4. **Sidebar collapse.** Click the panel-toggle button left of the title several times, slowly and then quickly. The
   panel edge should glide between 232 and 56 px without the content jumping at the end; on collapse the right edge of
   the content is briefly outside the window — check that this is not noticed. Check that the labels fade, not blink.
5. **Dialog.** Press N: the page darkens and blurs while the window rises into place; type immediately — the letters must
   land in the title. Press Esc: the window sinks and fades, the blur clears with the shade. Repeat N, Esc fast.
6. **Popovers and wheel.** In the dialog open Date, Time, Priority, Tag in turn: each drops from under its pill. In
   Time choose "Set time", click a number two rows away: the column scrolls smoothly and the number grows when it stops.
7. **Micro-interactions.** In Week tick and untick a card that stays in place (e.g. Laundry, Thu 12): fill, check mark,
   title colour and the strike line should fade, not blink. Click a tag in the sidebar: other cards dim to 0.3. In
   Settings › Tasks flip a toggle; in Appearance change accent and transparency (the colour should cross-fade 300 ms).
8. **Mica.** On Windows 11 with Mica active, repeat 3–5 and watch the title bar and sidebar areas: content transitions
   must not change the Mica tint; the dialog shade and blur cover the title bar too, as designed.
9. **Animations off.** Turn off Settings › Accessibility › Visual effects › Animation effects while Doba is running and
   repeat 2, 3, 5 and 7: everything must change at once. Also tick a task: since the fix of finding 3 the strike line
   is instant too. Turn the setting back on and check the transitions return.

## Findings

1. **Sidebar highlight leads the section swap by 130 ms** (owner: app). `SidebarViewModel.ApplyNav` runs on
   `Navigated`/`Nav`, which `MainViewModel.Apply` changes before `ContentSwapper` starts the 130 ms exit; the prototype
   (`navSection`, script 1178–1189) changes `view` — and with it the highlight — only in `go`, after the exit, so the
   highlight and the entrance start together. Checklist item 3 asks for the highlight to slide in sync. The test
   `Sections_exit_first_swap…` deliberately does not assert the timing ("requested from the lead"), and DECISIONS has no
   line for it. Minor.
2. **View-switch indicator slides from "Year" when the calendar is re-entered** (owner: ui). On Inbox / Today / Settings
   the switch is not shown (as the prototype's `sc-if isCal`) and `SegmentedSwitch.UpdateIndicator` parks the indicator
   under segment 0; when the calendar returns, the indicator animates from there to the current level (420 ms, 128 px for
   Week). The prototype mounts a fresh indicator at its place, so it does not slide. Recorded in strip 21. Minor.
3. **`StrikeText` is not re-timed when OS animations are turned off at run time** (owner: ui). The strike colour
   transition is created in code on the stroke brush (`StrikeText.OnLoaded`), and `MotionSync.Retime` only walks
   `Visual.Transitions`, so after the setting goes off a completed task's strike line still fades over 300 ms while
   everything else is instant (checklist item 6). Read in code, not reproduced by a test. Minor.
4. **Curves are not covered by the no-literals scanner** (owner: ui). `NoLiteralsTests` flags duration literals only; a
   `new SplineEasing(…)` or `Easing="0.1,0.9,0.2,1"` outside `Motion` would pass. None exists today. Minor.

## Status after the final review fixes

The table rows above describe `2a78749`. All four findings were fixed afterwards (integrated in `1b098ed` and
`187f206`):

1. Highlight timing: the highlight, current row and settings tab now change in the content swap
   (`MainViewModel.Present`), with the entrance. `TransitionTests.Sections_exit_first_swap_after_the_exit_and_enter_from_below_going_down`
   now asserts that the highlight does not move during the exit.
2. View-switch re-entry: coming back from no level places the indicator without sliding and only fades it in
   (`Controls/SegmentedSwitchTests`).
3. `StrikeText` reads its duration from `Motion` at every change and attaches the fade only while animations are on
   (`Controls/StrikeTextTests.Turning_OS_animations_off_while_running_makes_the_strike_instant`,
   `Shown_while_animations_are_off_it_fades_once_they_come_back`). Checklist item 6 has no remaining gap.
4. The no-literals scanner now flags easings outside `Motion` (`Easing="…"`, `new …Easing(`, `KeySpline`) with cases in
   `DesignSystem/NoLiteralsTests.Scanner_flags_literals`.

## Throwaway material (not committed)

`tests/Doba.App.Tests/Motion/ZzMotionReviewCapture.cs` and `artifacts/motion-review/*.png|txt` in the review worktree.
