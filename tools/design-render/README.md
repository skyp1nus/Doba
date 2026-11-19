# Design reference renderer

Renders `docs/design/design-reference.dc.html` in headless Chromium (Playwright) at 1600×1000 and writes the
reference PNGs in `docs/design/screens/`.

Setup (scratch folder): `site/support.js` = the dc runtime (`artifact-type/dc-runtime.js` from the published
design artifact), `site/Main.dc.html` = `docs/design/design-reference.dc.html`, `fonts/` = the Geist TTFs from
`src/Doba.App/Assets/Fonts`. Then `node render.js out [names...]`. `jobs.js` lists each screenshot's props and clicks.
