using System.Runtime.InteropServices;

namespace Sticker_Creator_Program;

/// <summary>
/// A WinExe app has no console by default, so an unhandled exception would otherwise vanish silently instead of being visible anywhere.
/// </summary>
public static class Native_console {
  [DllImport("kernel32.dll")]
  private static extern bool AllocConsole();

  public static void attach_error_console() {
    if (OperatingSystem.IsWindows()) {
      AllocConsole();
    }
  }
}
