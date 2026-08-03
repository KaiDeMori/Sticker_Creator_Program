# Documentation index

Sticker Creator Program — a desktop application that turns a folder of square source images into a published Signal sticker pack.

## Documents

- [architecture.md](architecture.md) — Process model, startup gates, privilege split, configuration, security.
- [message_bridge.md](message_bridge.md) — The only channel between the web pages and the C# host, and its invariants.
- [pack_format.md](pack_format.md) — On-disk pack layout, file schemas, and the conditions a pack must satisfy to be uploadable.
- [image_conversion.md](image_conversion.md) — Source art to 512 × 512 WebP within Signal's byte limit.
- [signal_cli.md](signal_cli.md) — Device linking, manifest generation, and pack upload through signal-cli.
- [platform_notes.md](platform_notes.md) — Verified library and platform behavior the implementation depends on.
- [build_and_run.md](build_and_run.md) — Requirements, commands, packaging, and the test suite.
