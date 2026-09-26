# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-09-25

### Added
- `LayeredText`: renders a TextMeshPro text (UI or world space) with a stack of layers — outlines, shadows/glows,
  2.5D extrude and face — in one mesh and one draw call.
- `LayeredTextStyle`: reusable layer stacks with gradient space/angle; per-font materials stored as sub-assets.
- Face effects: two-colour gradient at any angle, texture, gloss band, inner shadow / inner highlight, shine.
- Arc: bend text along a circle (multi-line aware).
- `TextAnimator`: per-character transitions (Pop, Fade, Slide, Rotate, Flash, Color Fade) and loops (Wave, Shake,
  Pulse, Rainbow, Shine), stagger orders, loop modes, `Progress` for external driving, Edit-mode preview.
- Inline tags (`<wave>`, `<shake>`, `<pulse>`, `<rainbow>`, `<pause=…>`), punctuation pauses and the
  `CharacterRevealed` event for typewriter dialogue.
- `TextCounter`: animated numbers, allocation-free for whole-number formats.
- Style transitions (`LayeredText.TransitionTo`) and `LayeredTextStates` for button states.
- Presets: Candy Title, Gold 3D, Neon, Sunset, Soft Shadow; a bundled wide-padding Liberation Sans font asset.
- Tools ▸ TMP Text Effects ▸ Create Wide-Padding Font Asset.
- Showcase sample and EditMode tests.
