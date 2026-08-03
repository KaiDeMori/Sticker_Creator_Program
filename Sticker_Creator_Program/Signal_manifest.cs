using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sticker_Creator_Program;

public class Signal_manifest_sticker {
  [JsonPropertyName("file")]
  public string file { get; set; } = "";

  [JsonPropertyName("contentType")]
  public string content_type { get; set; } = Signal_manifest.content_type;

  [JsonPropertyName("emoji")]
  public string emoji { get; set; } = "";
}

public class Signal_manifest_document {
  public string title { get; set; } = "";
  public string author { get; set; } = "";
  public Signal_manifest_sticker cover { get; set; } = new();
  public List<Signal_manifest_sticker> stickers { get; set; } = new();
}

/// <summary>
/// Builds the manifest signal-cli's uploadStickerPack expects, from an already-validated pack.
/// The pack's validity guarantee (valid_pack_spec.md) is assumed here, not re-checked — the caller re-checks it before building.
/// </summary>
public static class Signal_manifest {
  public const string content_type = "image/webp";
  public const string manifest_file_name = "manifest.json";

  public static string manifest_file_path(string pack_directory) =>
     Path.Combine(Pack_store.pack_info_directory_path(pack_directory), manifest_file_name);

  /// <summary>
  /// signal-cli resolves a manifest sticker's "file" relative to manifest.json's own directory (pack_info/), never as an absolute path.
  /// </summary>
  private static string manifest_relative_WebP_path(string pack_directory, string original_file_name) =>
     $"{Pack_store.WebP_directory_name}/{Path.GetFileName(Pack_store.WebP_file_path(pack_directory, original_file_name))}";

  private static Signal_manifest_sticker to_manifest_sticker(string pack_directory, Sticker_entry sticker) =>
     new() {
       file = manifest_relative_WebP_path(pack_directory, sticker.file),
       emoji = sticker.emoji,
     };

  public static Signal_manifest_document build(string pack_directory, Pack_meta meta, List<Sticker_entry> stickers) {
    var cover_sticker = stickers.First(sticker => sticker.file == meta.cover);

    return new Signal_manifest_document {
      title = meta.title,
      author = meta.author,
      cover = to_manifest_sticker(pack_directory, cover_sticker),
      stickers = stickers.Select(sticker => to_manifest_sticker(pack_directory, sticker)).ToList(),
    };
  }

  /// <summary>
  /// Atomically overwrites pack_info/manifest.json. Regenerated on every publish attempt, never read back by this app.
  /// </summary>
  public static void write(string pack_directory, Signal_manifest_document document) {
    var pack_info_directory = Pack_store.pack_info_directory_path(pack_directory);
    Directory.CreateDirectory(pack_info_directory);
    var manifest_path = manifest_file_path(pack_directory);
    var temp_path = Path.Combine(pack_info_directory, $"{manifest_file_name}.{Guid.NewGuid():N}.tmp");

    try {
      File.WriteAllText(temp_path, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));

      if (File.Exists(manifest_path)) {
        File.Replace(temp_path, manifest_path, null);
      }
      else {
        File.Move(temp_path, manifest_path);
      }
    }
    catch {
      File.Delete(temp_path);
      throw;
    }
  }
}
