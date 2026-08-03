# Build and run

All commands run from the workspace root. The shell scripts require Git Bash.

## Requirements

### Running

- Windows x64. Development and testing target Windows; `publish.sh` also produces a `linux-x64` package.
- Microsoft Edge WebView2 Runtime, the window backend on Windows.
- A Java Runtime Environment, version 25 or later, on `PATH`. Checked at startup; the application exits with a message when none is found.
- signal-cli 0.14.7 or later, in `signal-cli/` next to the executable or in the workspace root. That directory is untracked — every machine fetches its own copy.
- A verified Signal account, for linking and publishing only.

### Building

- .NET SDK 9.0 or later.
- NuGet dependencies restore automatically: `Magick.NET-Q8-AnyCPU`, `Photino.NET`, `QRCoder`, `YamlDotNet`.

## Commands

- Run the application:
  ```
  dotnet run --project Sticker_Creator_Program/Sticker_Creator_Program.csproj
  ```
- Run the test suite:
  ```
  dotnet test Sticker_Creator_Program_Tests/Sticker_Creator_Program_Tests.csproj
  ```
- Build the shippable packages into `_publish/`:
  ```
  ./publish.sh
  ```
- Fetch signal-cli into `signal-cli/`:
  ```
  ./download_signal_cli.sh
  ```

`publish.sh` produces self-contained, single-file `win-x64` and `linux-x64` packages with signal-cli copied alongside each. It needs `dotnet`, `tar`, and `zip` on `PATH`, and an existing signal-cli install to copy.

`download_signal_cli.sh` also fetches the man pages' AsciiDoc sources into `signal-cli/man/adoc/`. Those are the reference for signal-cli's command-line options, D-Bus interface, and JSON-RPC behavior; the extracted `man/` holds only pre-built, gzipped pages.

## Test suite

xUnit, one test file per unit under test. The image tests exercise the real ImageMagick binding rather than a mock. The signal-cli tests operate on already-produced results, so no subprocess is ever started.
