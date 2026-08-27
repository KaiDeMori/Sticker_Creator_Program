# signal-cli integration

signal-cli owns the entire Signal account lifecycle: linking, session state, and publishing. The application never holds Signal credentials and never implements Signal's pack encryption or manifest protobuf.

signal-cli is a third-party GPLv3 command-line Signal client built on the official `libsignal-client` library. It is not a Signal Foundation product.

## Requirements

- A Java Runtime Environment, version 25 or later, launchable as `java`.
- signal-cli 0.14.7 or later, extracted so `bin/`, `lib/`, and `man/` sit directly in the install directory.
- For linking and publishing only: a verified Signal account whose primary device can approve a link request.

## Install directory resolution

1. `signal-cli/` next to the executable, when that directory exists. This is the published layout.
2. Otherwise four levels up from the build output directory, which is `signal-cli/` in the workspace root. This is the source-tree run.

## Invocation

The batch launcher is never used. It inlines every dependency jar's absolute path into a single `set CLASSPATH` line, which exceeds the Windows command-line length limit once the install path is nested deep enough.

`java` is invoked directly, with this fixed prefix on every call:

```
java --enable-native-access=ALL-UNNAMED -cp "<install_directory>/lib/*" org.asamk.signal.Main -d "<data_directory>" <command> <arguments>
```

- `--enable-native-access=ALL-UNNAMED` is what the batch launcher passes by default.
- Arguments are added individually to `ProcessStartInfo.ArgumentList`, which escapes each element. The single-string `Arguments` property is never used.
- Both output streams are read asynchronously before waiting for exit, which avoids a pipe-buffer deadlock.
- Failure is exit code alone. A non-zero exit raises with the captured standard error.

## Data directory

`signal_cli_data/` next to the executable, passed explicitly with `-d` on every invocation. signal-cli's own default location is never used.

The directory does not exist until an account is linked. Once linked it holds:

- `data/accounts.json` — maps the linked account to a local numeric identifier.
- `data/<local_id>` and `data/<local_id>.d/account.db` — identity and session state, in unencrypted SQLite.
- `avatars/profile-<phone_number>` — cached profile avatar, with the phone number in the file name.

## Link detection

Whether a device is linked is read directly from `data/accounts.json`, not from a signal-cli subprocess call, which avoids a Java Virtual Machine start on every application launch. A non-empty `accounts` array means linked. The same file supplies the linked phone number.

## Linking

- Only `link` is ever called. `register` and `addDevice` are not. There is exactly one data directory, one account, and no account switcher.
- The phone number must match E.164 format: a leading `+`, no leading zero, 7 to 15 digits total.
- The device name is free text and must not be blank. It is what Signal shows under Linked Devices.
- `link -n "<device name>"` prints an `sgnl://linkdevice?...` URI to standard output and then blocks. The application renders that URI as a QR code and waits.
- Approval comes only from the account's primary device — the phone registered directly, not a linked one. Another linked client cannot approve.
- An account is limited to 5 linked devices.
- The chosen device name is persisted to application configuration only after the subprocess exits successfully.
- Cancelling kills the tracked subprocess and suppresses its result.

The `sgnl://` URI carries the account UUID and a public key valid for linking a new device during its validity window. It must never be sent to an online QR-code service.

## Unlinking

Unlinking the device from the Signal account happens on the phone. signal-cli's own device-management commands require primary-device approval, so the application cannot do it.

The application deletes its local data directory recursively. That is safe regardless of whether the phone-side unlink has happened.

## Publishing

### Manifest

Written to `pack_info/manifest.json`. The file name must be exactly `manifest.json`; `uploadStickerPack` looks for that name in the given path's directory.

```json
{
  "title": "Turtles",
  "author": "Kai",
  "cover":    { "file": "_WebP/turtle.webp", "contentType": "image/webp", "emoji": "🐢" },
  "stickers": [
    { "file": "_WebP/turtle.webp", "contentType": "image/webp", "emoji": "🐢" },
    { "file": "_WebP/wave.webp",   "contentType": "image/webp", "emoji": "🌊" }
  ]
}
```

- `contentType` is always `image/webp`. The application produces static WebP only.
- `file` is relative to the manifest's own directory, never absolute. signal-cli resolves it against the manifest's location.
- The cover sticker appears in both `cover` and `stickers`. It is one of the pack's stickers, not an extra image.
- `emoji` is a free string in the manifest and in the sticker protobuf. Neither signal-cli nor the Signal server restricts or truncates it.

### Preparing and uploading are separate steps

Publishing is irreversible, so the manifest is built by its own operation before any subprocess runs. That operation is also reachable on its own, which is how the manifest is inspected without publishing.

1. **Prepare.** The Editor flushes every pending edit to disk and waits for the acknowledgement. C# then reloads pack state from disk, recomputes validity, and builds the manifest. A non-empty error list aborts here, with no manifest written and no subprocess call. Otherwise the manifest is serialized once, written atomically, and returned to the Editor together with its SHA-256 fingerprint.
2. **Confirm.** The Editor displays those exact bytes. Nothing has been uploaded at this point.
3. **Upload.** `uploadStickerPack <manifest_path>` uploads the file as-is. It does not resize or convert.

The upload never rebuilds the manifest. It re-checks validity, then re-fingerprints the file on disk and refuses to run if it no longer matches what was confirmed — the guard against a change made outside the application between confirmation and upload. Edits made inside the application cannot occur there, because both dialogs are modal.

The resulting URL is the first `https://signal.art/...` match in standard output, and is appended to the pack's URL file.

The application never reads a manifest back as pack state. The fingerprint check hashes the file's text; it does not parse it.
- Publishing is irreversible. A published pack cannot be edited, replaced, or deleted.

## Note to Self

`send --note-to-self -m "<text>"` delivers to the linked account's own conversation. No account flag is passed, which is valid whenever exactly one local account is linked.

## QR codes

Rendered offline through `QRCoder` into a PNG data URL, used for the link URI and for a published pack's install URL.
