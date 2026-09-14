# Installer artwork

Welcome and finish share `installer/Assets/wizard.bmp`, replacing the NSIS stock computer illustration. The artwork has no language-specific text. NSIS uses `AspectFitHeight` to preserve its proportions at different display scales and with localized dialog dimensions.

The high-resolution original is `installer/Assets/wizard-master.png`. The built-in image generation tool produced it using `apps/windows/Assets/App-master.png` as the icon reference. The source is converted without resizing to an opaque 24-bit RGB BMP. This avoids the indexed colors and low resolution of the stock artwork. The installer retains MUI2, DPI awareness, version information, publisher metadata, and LZMA compression.

Regenerate the NSIS bitmap with:

```powershell
./scripts/build-installer-art.ps1
```

Packaging runs this conversion automatically. It requires no image-generation service at build or installation time.

## Generation prompt

Create a polished production installer sidebar illustration for Voltura Earner, a Windows time and earnings tracker. Use case: logo-brand / product illustration. The attached image is the existing app icon reference; retain its recognizable cobalt blue rounded square and bold white dollar symbol. Create ONE tall portrait image, aspect ratio 164:314, ideally 1024x1960 or similar high resolution. Full-bleed deep midnight navy backdrop with very subtle cobalt lighting. Main hero is that blue dollar app tile, straight-on with slight premium dimensional depth, centered horizontally around the upper third of the panel, occupying about 60 percent of its width. Below and slightly behind it, an elegant thin silver-blue clock ring with just simple hands and tick marks (no numbers), and restrained flowing blue arcs towards the lower edge suggest time tracked and earnings. Keep the design clean, quiet and sophisticated, generous negative space, smooth surfaces, crisp edges, limited visual elements, no glitter, no dot patterns, no busy charts. This will be rendered as a narrow panel in a Windows installer beside plain white content, so the silhouette and composition must read clearly when reduced to 164x314. No words, letters, version numbers, captions, watermarks, computer hardware, install arrows, coins or banknotes. Only the white dollar symbol on the blue tile. All important artwork should have at least 12 percent left/right safe margin. Finished image only, no mockup of a window.
