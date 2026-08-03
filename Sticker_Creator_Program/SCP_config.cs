using System.Text.Json;

namespace Sticker_Creator_Program;

public class SCP_config {
  private const string config_file_name = "SCP_config.json";
  private static string config_file_path() =>
      Path.Combine(AppContext.BaseDirectory, config_file_name);

  public static SCP_config Active = new();

  #region config_properties

  public string packs_directory { get; set; } = "";
  public string device_name { get; set; } = "";
  public bool enable_lossless_compression { get; set; } = false;
  public bool lossless_warning_was_shown { get; set; } = false;
  public double picker_zoom { get; set; } = 1.0;

  #endregion

  public static void load() {
    var config_path = config_file_path();

    if (!File.Exists(config_path)) {
      Active = new SCP_config();
      save();
      return;
    }

    using var stream = File.OpenRead(config_path);
    SCP_config? config = null;
    try {
      config = JsonSerializer.Deserialize<SCP_config>(stream);
    }
    catch (Exception) {
    }

    Active = config ?? new();
  }

  public static void save() {
    File.WriteAllText(config_file_path(), JsonSerializer.Serialize(Active, new JsonSerializerOptions { WriteIndented = true }));
  }
}
