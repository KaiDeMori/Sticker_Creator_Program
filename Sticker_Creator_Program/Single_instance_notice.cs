using Photino.NET;

namespace Sticker_Creator_Program;

public static class Single_instance_notice {
  /// <summary>
  /// Shown when a second launch loses Main's mutex race, so the user gets a plain notice instead of silently doing nothing.
  /// ShowMessage needs a live native instance, which only exists once the native window has actually been constructed, hence the off-screen window and the WindowCreated handoff.
  /// </summary>
  public static void show() {
    var notice_window = new PhotinoWindow()
        .SetTitle("Sticker Creator Program")
        .SetUseOsDefaultSize(false)
        .SetSize(0, 0)
        .SetUseOsDefaultLocation(false)
        .SetLeft(-10000)
        .SetTop(-10000)
        .SetMinimized(true)
        .LoadRawString("<html></html>");

    notice_window.WindowCreated += (sender, event_args) => {
      notice_window.ShowMessage("Sticker Creator Program", "Only one instance can be open at a time.");
      notice_window.Close();
    };

    notice_window.WaitForClose();
  }
}
