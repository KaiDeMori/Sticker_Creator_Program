# Architecture

## Process model

- One C# process owns one Photino window. That window's HTML, CSS, and JavaScript are the user interface.
- The window backend is WebView2 on Windows, supplied by the operating system.
- No HTTP server, no listening port, no second process, no second window.
- The entry point is a classic `Main` carrying `[STAThread]`. WebView2 initializes through COM and requires a single-threaded apartment.
- Navigation re-loads the same window with a different page path.
- The window's close event ends the process.

## Privilege split

- **Web pages** render state, collect input, and send messages. No file system access, no process start, no network access.
- **C# host** owns configuration, pack read and write, image conversion, validation, manifest generation, signal-cli invocation, native dialogs, and shell open.

## Startup

Three checks run before the main window opens.

1. **Single instance.** A named global mutex. A second launch shows a notice and exits.
2. **Java.** `java -version` must launch. signal-cli is a Java application.
3. **Configuration load.**

The startup page follows two gates, checked in order.

1. No packs directory configured — the first-run page.
2. Packs directory configured, no device linked — the linked-device page.
3. Both present — the pack list.

Both gates are applied again after a packs directory is confirmed.

The linked-device gate is skippable — the page offers to continue unlinked. Publishing requires a linked device. Editing and conversion do not.

## Pages

- `first_run.html` — choose the packs directory.
- `linked_device.html` — link or unlink a signal-cli device.
- `pack_selection.html` — list packs, open one, refresh from disk.
- `editor.html` — edit, convert, and publish one pack.
- `trophy.html` — report a trophy conversion outcome.

## Threading

- The message callback runs on the user-interface thread.
- Conversion, device linking, publishing, and sending a note to self run on background tasks and stream results back.
- A modal native dialog must not open from inside the message callback. The folder dialog is opened asynchronously so the callback unwinds first.
- The in-flight link subprocess is tracked in a field so cancellation can find and kill it.

## Files next to the executable

- `SCP_config.json` — persisted settings.
- `signal_cli_data/` — signal-cli's data directory, passed explicitly on every invocation. The operating system default location is never used.

### SCP_config.json

- `packs_directory` — absolute path to the folder holding pack folders.
- `device_name` — the name Signal shows for the linked device.
- `enable_lossless_compression` — whether conversion attempts a lossless encode first.
- `lossless_warning_was_shown` — whether the one-time lossless warning has been displayed.
- `picker_zoom` — the emoji picker's zoom factor.

Behavior:

- A missing file is created with defaults. An unparsable file falls back to defaults instead of failing startup.
- `device_name` is application state, written only after a link succeeds. signal-cli is never queried for it.

## Packs directory

- A configured folder that still exists is used as-is, with no re-scan.
- Absent configuration, or a configured folder that is gone, routes to the first-run page.
- A candidate folder is checked one level deep. Image files directly inside it are **blocked**. An empty folder, or one whose subfolders hold images, is **accepted**.
- Nothing is persisted until the user confirms. The confirmation re-checks acceptability in C# before saving.

## Security

- No listening port and no second process, therefore no token, no cross-origin policy, and no authentication layer.
- The application never holds Signal credentials. signal-cli owns the account lifecycle. The application never implements Signal's pack encryption or manifest protobuf.
- signal-cli stores account state in unencrypted SQLite, protected only by file permissions. Unlinking is available from the phone independently of that directory's contents.
- Every `file://` URL is built from a directory the application resolved and a file name it enumerated. No incoming URL is parsed back into a path.
- Upload is the only irreversible action. Building the manifest is a separate step, so its exact bytes are shown before the confirmation and can be generated without uploading at all. The upload verifies that the manifest still matches what was confirmed.
