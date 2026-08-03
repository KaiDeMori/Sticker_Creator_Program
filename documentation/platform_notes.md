# Platform notes

Library and platform behavior the implementation depends on. Every entry was verified against a primary source, named in brackets. Treat each as a claim to re-check when a package version moves.

## Window runtime

- **`[STAThread]` is required on a classic `Main`.** WebView2 initializes through COM and needs a single-threaded apartment. C# top-level statements do not apply the attribute to the generated entry point. Without it the window opens and responds to move and close, but never renders — no content, no developer tools, no context menu. [Photino's own sample; reproduced directly]
- **`Load` works after the window is already open** and re-navigates it in place. Both overloads branch on whether the native instance exists. This contradicts their own documentation comments, which state that `Load` must be called before initialization. [`PhotinoWindow.NET.cs`]
- **`SetIconFile` accepts only a file path.** It checks the given path, then the same path under the application base directory. There is no stream, byte array, or embedded-resource overload, so the window icon cannot be compiled in the way the executable icon is. [`PhotinoWindow.NET.cs`]
- **Developer tools and the context menu are enabled by default.** No opt-in call is needed. [`PhotinoWindow.NET.cs`]
- **No `Microsoft.Web.WebView2` package reference is needed.** The native wrapper hosts WebView2 itself. The WebView2 Runtime must still be present on the target machine — in-box on Windows 11, not guaranteed on Windows 10. [package graph]
- **Pin the Photino version exactly.** Photino has had breaking API changes across versions. [upstream issue tracker]
- **Never derive Photino API behavior from strings in a compiled assembly.** A method name found that way says nothing about its contract or its preconditions. Verify against upstream documentation or source.

## YAML serialization

- **The emitter escapes every supplementary-plane character.** Its printable-character analysis classifies UTF-16 surrogate pairs as unprintable, so a symbol in the Basic Multilingual Plane is written literally while an emoji above U+FFFF is written as a `\U` escape sequence. [verified against real output]
- **`EmitterSettings` offers no switch for it.** `UseUtf16SurrogatePairs` only chooses between one 8-digit escape and a pair of 4-digit escapes. Both are escapes. [`YamlDotNet.xml` API documentation]
- **Key order on write is not guaranteed by default.** Properties serialize in declaration order in practice, but the maintainer states no guarantee. Explicit `[YamlMember(Order = n)]` per property is the only guaranteed order. [upstream maintainer]
- **Prefer `List<T>` over `Dictionary<TKey, TValue>` for anything ordered.** Dictionary enumeration order is an implementation detail.

## File input and output

- **`File.Replace` throws `FileNotFoundException` when the destination does not exist.** Branch on `File.Exists` first and use `File.Move` otherwise. [Microsoft Learn]
- **Source and destination must be on the same volume** for `File.Replace`. [Microsoft Learn]
- **`destinationBackupFileName` accepts `null`** to skip creating a backup file. [Microsoft Learn]

## Subprocess invocation

- **`ProcessStartInfo.ArgumentList` escapes each element independently.** Elements need no manual quoting, which makes it safe for paths containing spaces. It is mutually exclusive with the single-string `Arguments` property. [Microsoft Learn]
- **Reading `StandardOutput` and `StandardError` synchronously can deadlock.** A child filling one pipe's buffer while the parent blocks on the other stalls both. Start asynchronous reads on both streams before waiting for exit. [Microsoft Learn]
- **Opening a URL or a folder through `Process.Start` requires `UseShellExecute = true`.** Modern .NET does not shell-execute by default, and omitting the flag throws `Win32Exception`. [Microsoft Learn]

## Image encoding

- **Package: `Magick.NET-Q8-AnyCPU`.** Q8 is the maintainer's recommended default; 8 bits per channel is sufficient for stickers bounded by 512 pixels and 300 KiB. `AnyCPU` bundles the native binaries for x86, x64, and arm64 in one package, so no per-platform executable is needed. [package documentation]
- **`MagickImageInfo` reads header metadata only**, giving dimensions without decoding pixels. `Width` and `Height` are `uint` in the version 14 line. [package documentation]
- **An unreadable or non-image file raises `MagickException`**, the base type of the coder and corruption exceptions. [package documentation]
- **No explicit initialization or resource-limit setup is needed.** The binding self-initializes on first use. [package documentation]
- **`Write(Stream)` honors the `Format` property; `Write(string path)` infers the format from the path extension when `Format` was never set.** A file's extension therefore does not prove its encoding — format is read from content. [verified by test]
- **Quality 100 without an explicit `webp:lossless=false` define switches the WebP coder to lossless mode automatically.** Set the define explicitly when lossy output is intended. [ImageMagick WebP encoding documentation, `cwebp` manual]

## Packaging

- **`PublishSingleFile` bundles a file referenced by `<ApplicationIcon>` into the executable by default**, even when the same file is a content item with `CopyToPublishDirectory`. The loose copy needed at runtime silently disappears from the publish output. An explicit `<ExcludeFromSingleFile>true</ExcludeFromSingleFile>` content update restores it. The same exclusion is applied to the emoji sprite sheet. [.NET SDK issues 854 and 3469]
- **Two independent mechanisms share the icon file.** `<ApplicationIcon>` embeds it into the executable as a Win32 resource at build time; the window icon is read from disk at runtime.
- **DPI awareness is declared in `app.manifest`** as `PerMonitorV2`, wired in through `<ApplicationManifest>`. This is Microsoft's documented recommendation over a runtime P/Invoke call, because it applies before any window exists. [Microsoft Learn]

## Target framework and project shape

- **The application targets `net9.0`** with runtime identifiers `win-x64` and `linux-x64`. The test project targets `net9.0-windows`.
- **Photino.NET declares `net8.0` and `net9.0`.** Newer target frameworks compile but are not runtime-verified against this setup.
- **The SDK is plain `Microsoft.NET.Sdk`**, not `Microsoft.NET.Sdk.WindowsDesktop`, and neither `UseWindowsForms` nor `UseWPF` is set. Microsoft deprecated the desktop SDK as a default even for WinForms and WPF. [Photino's own project file; Microsoft Learn]
- **One application project plus one test project.** The test project holds a project reference back to the application.
