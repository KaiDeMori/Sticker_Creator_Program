# Image conversion

Conversion produces the upload-ready WebP files in `pack_info/_WebP/`. It runs in-process through the ImageMagick binding, never as a separate executable.

## Limits

- Output side length: 512 pixels.
- Output byte limit: 307200 bytes, which is 300 KiB.
- Output format: WebP, static only.

Both limits are Signal's own sticker limits.

## Scope of a pass

- One pass converts every source file in the pack root. It does not read the sticker list.
- The pass runs on a background thread and reports progress per file.
- Dimensions are read from image header metadata. Pixels are decoded only for conversion.

## Squareness check

Every source file is checked before any file is converted. The whole pass fails, writing nothing, when any file is non-square or unreadable. The failure reports one entry per offending file.

## Per-file conversion

Each source file is decoded once and resized once, then encoded repeatedly from that same in-memory image until a result fits the byte limit.

1. Resize into a 512 × 512 box, preserving aspect ratio. Square input yields exactly 512 × 512.
2. Attempt a lossless encode, if lossless compression is enabled.
3. Otherwise, or if the lossless result did not fit, walk the lossy quality ladder.
4. Write the first encode that fits.

### Lossless profile

Enabled by a persisted setting, disabled by default.

- Quality 100.
- `lossless=true`.
- `method=6`.
- `exact=true`, which preserves RGB values underneath fully transparent pixels.

### Lossy quality ladder

Descending, stopping at the first encode that fits:

1. 100 down to 90, in steps of 1.
2. 85 down to 5, in steps of 5.
3. 1.

Encode settings:

- Quality: the current ladder value.
- `lossless=false`, set explicitly. Quality 100 without it switches the WebP coder to lossless mode automatically.
- `method=6`.
- `alpha-quality=100`, which keeps the transparency channel at full quality while the color channels are lossy.
- `exact=false`.

## Outcomes

- `lossless` — the lossless encode fit. Output written.
- `lossy` — an encode at the recorded quality fit. Output written.
- `uncrushable` — no encode fit, down to quality 1. No output written.

Further:

- An `uncrushable` result deletes any output left by an earlier conversion of the same name stem.
- Every outcome is written to `pack_info/conversion_info.json`, keyed by source name stem.

## Output verification

After conversion, every written output is re-read and confirmed to be exactly 512 × 512. A mismatch fails the whole pass. This guards an internal invariant, not user input.

## Trophy

A file earns the trophy when the lossy ladder only fit below quality 70, or when the outcome is `uncrushable`. A pass producing a trophy reports it instead of a normal completion.

A source file whose name stem is `Trophy_fractal` earns the trophy outright, which keeps the flow testable.
