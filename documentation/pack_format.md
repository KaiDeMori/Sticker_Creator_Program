# Pack format

## Packs directory

- Every immediate subfolder of the packs directory is a pack. There is no marker file.
- Packs are listed in ordinal name order.
- Listing a pack creates its `stickers.yaml` if absent, with the folder name as the title.

## Pack folder layout

```
<packs_directory>/
└── <pack_name>/
    ├── turtle.png                    source art, square
    ├── wave.png
    └── pack_info/
        ├── stickers.yaml             title, author, cover, emoji mapping, sticker order
        ├── conversion_info.json      per-file conversion outcome
        ├── signal_art_url.txt        one signal.art URL per publish, oldest first
        ├── manifest.json             upload manifest, regenerated whenever a manifest is prepared
        └── _WebP/                    upload-ready 512 × 512 WebP output
            ├── turtle.webp
            └── wave.webp
```

Source art belongs to the user and sits in the pack root. Everything in `pack_info/` is written by the application.

## Source art

- Accepted extensions, matched case-insensitively: `.png`, `.webp`, `.apng`, `.gif`, `.jpg`, `.jpeg`.
- Any other file in the pack root is ignored.
- Source images must be square. There is no cropping and no letterboxing.

## stickers.yaml

```yaml
meta:
  title: Turtles
  author: Kai
  cover: turtle.png
stickers:
- emoji: "🐢"
  file: turtle.png
- emoji: "🌊"
  file: wave.png
```

- `meta.cover` holds a source file name, or an empty string.
- `stickers` is ordered. That order is the pack order and the upload order.
- `file` is a source file name with extension, relative to the pack root.
- Key order within a sticker entry is fixed by explicit serialization order.

### Emoji storage

Emoji are stored as literal characters, so the file stays hand-editable. The YAML emitter escapes characters above the Basic Multilingual Plane, and those escape sequences are restored to literal characters after emitting. Quotes added by the emitter remain.

Exception: when any stored value contains a literal backslash, the document is left as emitted. Both forms parse back to the same string.

### One emoji per sticker

- `emoji` holds exactly one emoji, of exactly one codepoint.
- Stickers may share an emoji. There is no uniqueness requirement.
- Signal clients display the first codepoint only. A zero-width-joiner (ZWJ) sequence shows its base character alone.

### Reconciliation

The sticker list is reconciled against the pack root on every load.

1. Entries with a non-empty `file` are kept in stored order, whether or not the file exists on disk.
2. Source files no entry names are appended in ordinal name order, with an empty `emoji`.
3. An entry with an empty `file` is dropped. This is the only case that drops an entry.

## conversion_info.json

```json
{
  "turtle": { "fit": "lossless", "quality": null },
  "wave":   { "fit": "lossy",    "quality": 96 }
}
```

- Keyed by source file name stem, without extension.
- `fit` is `lossless`, `lossy`, or `uncrushable`. `quality` is null for a lossless result.
- A missing or unparsable file reads back as empty.
- Every conversion pass rewrites the whole file.

## signal_art_url.txt

- One `signal.art` URL per line, oldest first, append-only.
- The latest publish is the last non-blank line.
- Absent until the pack's first successful publish.

A published Signal sticker pack cannot be deleted, edited, or replaced, so earlier URLs stay valid and must never be overwritten.

## _WebP output

- One file per converted sticker, named `<source_stem>.webp`.
- Exactly 512 × 512 pixels, at most 307200 bytes.
- Source files sharing a name stem, for example `logo.png` and `logo.gif`, share one output path.

## Atomic writes

`stickers.yaml`, `conversion_info.json`, and `manifest.json` are written to a uniquely named temporary file inside `pack_info/`, then moved into place. A failure deletes the temporary file and leaves the previous file intact.

Temporary files live in `pack_info/`, never in the pack root, so they cannot be mistaken for source art.

## Validity

A pack is valid exactly when its error list is empty. Valid means uploadable. The error list is recomputed from disk state.

### Pack conditions

- `meta.title` is not empty.
- `meta.author` is not empty.
- The pack holds at least 1 and at most 200 stickers.
- `meta.cover` is not empty.
- `meta.cover` names a sticker present in the pack.

### Sticker conditions

- The file named by `file` exists in the pack root.
- The source file's dimensions are readable as an image.
- The source image's width equals its height.
- A WebP output exists in `pack_info/_WebP/`.
- The WebP output is exactly 512 × 512 pixels.
- The WebP output is at most 307200 bytes.
- The sticker's `emoji` is not empty.

The sticker named by `meta.cover` is exempt from the emoji condition. Signal's manifest format does not require an emoji on the cover. Every other condition applies to it.

### Cover

- An empty `meta.cover` fails validation.
- With `meta.cover` empty, the pack list displays the first converted sticker as an implied cover. That is display only.
- Removing the sticker named by `meta.cover` clears `meta.cover`.

## Missing and unreadable source files

- A sticker whose source file is missing or unreadable stays in the pack, keeps its emoji mapping and position, and reports through the error list.
- A missing source file does not block conversion. Conversion scans the pack root, so an absent file is not among the files to convert.
- An unreadable source file blocks conversion for the whole pack, the same as a non-square source.
- Only a missing source file offers removal. An unreadable file reappears on the next reconciliation, because reconciliation matches by file name.
- Removing a sticker deletes its entry, its `_WebP/` output, and its `conversion_info.json` entry.
