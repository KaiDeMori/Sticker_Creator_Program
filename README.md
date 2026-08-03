# Sticker Creator Program (SCP)

A desktop application that turns a folder of square source images into a published Signal sticker pack.

Signal has no first-party tool for building a sticker pack. Assembling one by hand means resizing and re-encoding every image to Signal's limits, mapping an emoji to each sticker, and driving an upload client that expects a hand-written manifest. This application does all of it in one window, from the source folder to the finished `signal.art` link.

## What it does

- Lists the sticker packs in a chosen folder and opens one for editing.
- Sets the pack title and author, and maps one emoji per sticker from a bundled Apple emoji picker.
- Reorders stickers and marks the pack cover.
- Converts every source image to the format and size Signal requires, at the highest quality that fits.
- Reports exactly which conditions a pack still fails, and permits publishing only once none remain.
- Links a Signal device, publishes the pack, and hands back the install link as text, as a QR code, or as a message to your own Note to Self conversation.

## Documentation

See [documentation/INDEX.md](documentation/INDEX.md) for the architecture, the pack format, the conversion pipeline, the signal-cli integration, and how to build and run the project.

## Notes

- Signal account handling runs entirely through `signal-cli`. This application never holds Signal credentials.
- The bundled Apple emoji image set is used for personal, non-commercial purposes.
