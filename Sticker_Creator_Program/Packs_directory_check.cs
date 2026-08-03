namespace Sticker_Creator_Program;

public class Packs_directory_check_result {
  public bool exists { get; set; }
  public bool has_images_directly { get; set; }
  public int subfolder_count { get; set; }
  public int subfolders_with_images_count { get; set; }

  /// <summary>
  /// Images sitting directly in the chosen folder mean the user pointed us at a single pack's art folder instead of the folder that holds packs — the mistake the first-run flow exists to catch.
  /// </summary>
  public bool is_blocked => exists && has_images_directly;
  public bool is_acceptable => exists && !has_images_directly;
}

/// <summary>
/// Inspects a candidate packs directory one level deep — pack folders directly inside it, each holding its own art files — so the first-run flow can react to what the user picked.
/// </summary>
public static class Packs_directory_check {
  public static Packs_directory_check_result check(string directory) {
    if (!Directory.Exists(directory)) {
      return new Packs_directory_check_result { exists = false };
    }

    var subfolders = Pack_store.scan_packs(directory);
    var subfolders_with_images_count = subfolders.Count(subfolder_name =>
        Pack_store.scan_art_files(Path.Combine(directory, subfolder_name)).Count > 0);

    return new Packs_directory_check_result {
      exists = true,
      has_images_directly = Pack_store.scan_art_files(directory).Count > 0,
      subfolder_count = subfolders.Count,
      subfolders_with_images_count = subfolders_with_images_count,
    };
  }
}
