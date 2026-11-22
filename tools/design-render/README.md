# Design reference renderer

Renders `docs/design/design-reference.dc.html` in headless Chromium (Playwright) at 1600×1000 and writes the
reference PNGs in `docs/design/screens/`.

Setup (scratch folder): `site/support.js` = the dc runtime (`artifact-type/dc-runtime.js` from the published
design artifact), `site/Main.dc.html` = `docs/design/design-reference.dc.html`, `fonts/` = the Geist TTFs from
`src/Doba.App/Assets/Fonts`. Then `node render.js out [names...]`. `jobs.js` lists each screenshot's props, clicks and optional `clip` (the
`schedule-*` states are 420 px crops of the frame, like the canvas's "Schedule states" artboards).
Playwright must match an installed browser build: with chromium-1248 in `%LOCALAPPDATA%\ms-playwright`, install
`playwright@1.64.0` with `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`.
