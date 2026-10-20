# DMG Gaussian blur design

2026-10-19, Mac build 36. User requested the previous dark dot-grid design and
Gaussian blur in place of liquid glass.

- Restored the pre-build-35 dark gradient, violet glow, dots, long arrow and exact
  original title/subtitle/install instruction.
- `scripts/create-dmg-background.swift` blurs the actual background with
  `CIGaussianBlur`, radius 20 pixels at 2× resolution (10 logical points).
- The two 160×154-point rounded areas retain the original placement. A diffuse
  light tint keeps Finder's native black labels readable. There are no refractive
  transforms, specular rims, glass shadows or separate label pills.
- Final output: `DmgBackground.png`, 1320×800 pixels / 144 dpi = 660×400 points.
- Finder draws the real draggable icons at (170,200) and (490,200). They are not
  baked into the background. `DmgInstallationPreview.png` is a static composition
  for checking icon/label alignment.
- The discarded imagegen artwork and its prompt/script are archived locally in
  `.build/dmg-design-b35-generated/`; the current renderer has no AI artwork dependency.
