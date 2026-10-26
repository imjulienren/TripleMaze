# TripleMaze WebGL

[Play TripleMaze](https://junlinr-cell.github.io/TripleMaze/)

A portrait arrow-and-triple-match puzzle with 50 image-based levels. Every fifth level is HARD, with an opening cue and a badge on the level and result screens. The player interface is in English and uses Nunito. A new session starts at Level 1.

The campaign has 40 normal levels with 66–99 arrows and 10 HARD levels with 114–147 arrows. Difficulty follows a varied five-level rhythm. See `50-level-design.csv` for the measured design values. Depth counts distinct direct and indirect blockers, following the ArrowSmash reference definition.

## Publishing

`TripleMaze_WebGL` contains the 2026-10-26 Unity export. The release build completed with zero errors and zero warnings. GitHub Actions runs `node scripts/prepare-pages.mjs` to decode Brotli files for GitHub Pages while preserving portrait browser layout.

Unity source is maintained separately in the private `junlinr-cell/TripleMaze_Unity` repository. The web update replaces the exported player and delivery notes; it does not update the historical Unity files in this web repository.

Development notes are in `开发总结.md` and `开发备忘录.md`. Font and image attribution files are in `licenses/`. The last user instruction stopped further runtime checks; full mobile playtesting and performance remain for manual review.
