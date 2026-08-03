using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace Sticker_Creator_Program;

public class Sticker_entry {
  [YamlMember(Order = 0)]
  public string emoji { get; set; } = "";

  [YamlMember(Order = 1)]
  public string file { get; set; } = "";

  [YamlIgnore]
  public bool converted { get; set; }

  [YamlIgnore]
  public bool source_exists { get; set; }

  [YamlIgnore]
  public int? width { get; set; }

  [YamlIgnore]
  public int? height { get; set; }

  [YamlIgnore]
  public string url { get; set; } = "";

  /// <summary>
  /// The setting the current WebP was produced with — sourced from conversion_info.json, not stickers.yaml, since it is a derived fact about the output, not part of the user's mapping.
  /// Null for a sticker never converted, or converted before this was tracked.
  /// </summary>
  [YamlIgnore]
  public string? fit { get; set; }

  [YamlIgnore]
  public int? quality { get; set; }
}

/// <summary>
/// One conversion outcome, keyed by source stem in conversion_info.json, read back so the Editor can show it on a sticker's card after a reload.
/// </summary>
public class Conversion_info_entry {
  public string fit { get; set; } = "";
  public int? quality { get; set; }
}

public class Pack_meta {
  [YamlMember(Order = 0)]
  public string title { get; set; } = "";

  [YamlMember(Order = 1)]
  public string author { get; set; } = "";

  [YamlMember(Order = 2)]
  public string cover { get; set; } = "";
}

public class Pack_state {
  public Pack_meta meta { get; set; } = new();
  public List<Sticker_entry> stickers { get; set; } = new();
  public int original_count { get; set; }
  public int converted_count { get; set; }
  public int mapped_count { get; set; }
}

public class Pack_save_payload {
  public Pack_meta meta { get; set; } = new();
  public List<Sticker_entry> stickers { get; set; } = new();
}

file class Pack_file {
  public Pack_meta? meta { get; set; }
  public List<Sticker_entry>? stickers { get; set; }
}

public static class Pack_store {
  public const string stickers_yaml_file_name = "stickers.yaml";
  public const string conversion_info_file_name = "conversion_info.json";
  public const string signal_art_url_file_name = "signal_art_url.txt";
  public const string pack_info_directory_name = "pack_info";
  public const string WebP_directory_name = "_WebP";

  public static readonly HashSet<string> art_extensions = new(StringComparer.OrdinalIgnoreCase) {
    ".png", ".webp", ".apng", ".gif", ".jpg", ".jpeg",
  };

  private static readonly Regex escaped_supplementary_character = new(@"\\U([0-9A-Fa-f]{8})");

  public static string pack_info_directory_path(string pack_directory) =>
      Path.Combine(pack_directory, pack_info_directory_name);

  public static string WebP_directory_path(string pack_directory) =>
      Path.Combine(pack_info_directory_path(pack_directory), WebP_directory_name);

  public static string WebP_file_path(string pack_directory, string original_file_name) =>
      Path.Combine(WebP_directory_path(pack_directory), Path.GetFileNameWithoutExtension(original_file_name) + ".webp");

  /// <summary>
  /// A page loaded from file:// can reference any absolute file:// URL from an image's src attribute, not just paths under its own directory — no scheme handler, no host-name parsing, no allowlist to reimplement; the browser's own local-file resolution does all of it.
  /// </summary>
  public static string file_url(string absolute_path) =>
      new Uri(absolute_path).AbsoluteUri;

  public static string sticker_url(string pack_directory, Sticker_entry sticker) =>
      file_url(sticker.converted ? WebP_file_path(pack_directory, sticker.file) : Path.Combine(pack_directory, sticker.file));

  public static List<string> scan_packs(string packs_directory) {
    if (!Directory.Exists(packs_directory)) {
      return new List<string>();
    }

    return Directory.GetDirectories(packs_directory)
        .Select(Path.GetFileName)
        .Where(name => !string.IsNullOrEmpty(name))
        .Select(name => name!)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();
  }

  public static List<string> scan_art_files(string art_directory) {
    if (!Directory.Exists(art_directory)) {
      return new List<string>();
    }

    return Directory.GetFiles(art_directory)
        .Select(Path.GetFileName)
        .Where(name => !string.IsNullOrEmpty(name) && art_extensions.Contains(Path.GetExtension(name)))
        .Select(name => name!)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();
  }

  public static HashSet<string> scan_WebP_stems(string WebP_directory) {
    if (!Directory.Exists(WebP_directory)) {
      return new HashSet<string>();
    }

    return Directory.GetFiles(WebP_directory)
        .Where(path => string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase))
        .Select(path => Path.GetFileNameWithoutExtension(path))
        .ToHashSet();
  }

  /// <summary>
  /// Reordering is the user's explicit editing action in the Editor, so on-disk order must survive a reload rather than being rebuilt fresh from each directory scan.
  /// </summary>
  public static List<Sticker_entry> reconcile_stickers(List<Sticker_entry> existing_stickers, List<string> art_filenames) {
    var kept = new List<Sticker_entry>();
    var present = new HashSet<string>(StringComparer.Ordinal);

    foreach (var sticker in existing_stickers) {
      // An entry with no file name at all is malformed data, not a missing-source sticker — this is the only remaining reason to drop one.
      if (string.IsNullOrEmpty(sticker.file)) {
        continue;
      }
      kept.Add(new Sticker_entry { emoji = sticker.emoji, file = sticker.file });
      present.Add(sticker.file);
    }

    foreach (var file_name in art_filenames.OrderBy(name => name, StringComparer.Ordinal)) {
      if (!present.Contains(file_name)) {
        kept.Add(new Sticker_entry { emoji = "", file = file_name });
      }
    }

    return kept;
  }

  public static Pack_state load_pack_state(string pack_directory) {
    var yaml_path = Path.Combine(pack_info_directory_path(pack_directory), stickers_yaml_file_name);
    var webp_directory = WebP_directory_path(pack_directory);

    var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
    Pack_file? parsed = null;
    if (File.Exists(yaml_path)) {
      using var reader = new StreamReader(yaml_path);
      parsed = deserializer.Deserialize<Pack_file>(reader);
    }

    var meta = new Pack_meta {
      title = parsed?.meta?.title ?? "",
      author = parsed?.meta?.author ?? "",
      cover = parsed?.meta?.cover ?? "",
    };

    var stickers = reconcile_stickers(parsed?.stickers ?? new List<Sticker_entry>(), scan_art_files(pack_directory));
    var WebP_stems = scan_WebP_stems(webp_directory);
    var conversion_info = load_conversion_info(pack_directory);
    foreach (var sticker in stickers) {
      var stem = Path.GetFileNameWithoutExtension(sticker.file);
      sticker.converted = WebP_stems.Contains(stem);
      if (sticker.converted && conversion_info.TryGetValue(stem, out var info)) {
        sticker.fit = info.fit;
        sticker.quality = info.quality;
      }
    }

    return new Pack_state {
      meta = meta,
      stickers = stickers,
      original_count = stickers.Count,
      converted_count = stickers.Count(sticker => sticker.converted),
      mapped_count = stickers.Count(sticker => !string.IsNullOrEmpty(sticker.emoji)),
    };
  }

  /// <summary>
  /// The precondition unescape_supplementary_characters depends on: with no backslash anywhere in the stored values, every backslash in the emitted document was written by the emitter, so every escape sequence found there is encoding rather than data.
  /// A pack that does carry one keeps the escaped form instead, which loses nothing — it reads back identically.
  /// </summary>
  private static bool carries_a_backslash(Pack_save_payload payload) =>
      new string?[] { payload.meta.title, payload.meta.author, payload.meta.cover }
          .Concat(payload.stickers.SelectMany(sticker => new string?[] { sticker.emoji, sticker.file }))
          .Any(value => value != null && value.Contains('\\'));

  /// <summary>
  /// Restores every escaped supplementary-plane character in emitted YAML to its literal form.
  /// YamlDotNet's emitter classifies UTF-16 surrogate pairs as unprintable and escapes them, so an emoji outside the Basic Multilingual Plane would be stored as an escape sequence while a symbol inside it is stored literally.
  /// The supplementary planes are printable per the YAML specification, so writing them literally keeps stickers.yaml editable by hand; parsing is unaffected either way, since a reader accepts both forms.
  /// The plane check keeps a sequence that stands for a control character escaped, where a literal would corrupt the document's layout.
  /// </summary>
  private static string unescape_supplementary_characters(string yaml) {
    const int highest_code_point = 0x10FFFF;

    return escaped_supplementary_character.Replace(yaml, match =>
        int.TryParse(match.Groups[1].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code_point)
        && code_point > char.MaxValue
        && code_point <= highest_code_point
            ? char.ConvertFromUtf32(code_point)
            : match.Value);
  }

  /// <summary>
  /// Atomically overwrites stickers.yaml.
  /// The file — and its temp file while writing — lives in pack_info, never in the pack's root, so it can never be mistaken for art by scan_art_files nor edited by an unassuming user browsing the pack's images.
  /// Falls back to a plain move for a pack's first-ever save, since File.Replace throws if the destination doesn't exist yet.
  /// </summary>
  public static void write_pack_state(string pack_directory, Pack_save_payload payload) {
    var pack_info_directory = pack_info_directory_path(pack_directory);
    Directory.CreateDirectory(pack_info_directory);
    var yaml_path = Path.Combine(pack_info_directory, stickers_yaml_file_name);
    var temp_path = Path.Combine(pack_info_directory, $"{stickers_yaml_file_name}.{Guid.NewGuid():N}.tmp");

    var serializer = new SerializerBuilder().Build();
    try {
      var yaml = serializer.Serialize(new Pack_file { meta = payload.meta, stickers = payload.stickers });
      File.WriteAllText(temp_path, carries_a_backslash(payload) ? yaml : unescape_supplementary_characters(yaml));

      if (File.Exists(yaml_path)) {
        File.Replace(temp_path, yaml_path, null);
      }
      else {
        File.Move(temp_path, yaml_path);
      }
    }
    catch {
      File.Delete(temp_path);
      throw;
    }
  }

  /// <summary>
  /// Keyed by source stem so it survives the source file being renamed with the same stem, and so it lines up directly with scan_WebP_stems.
  /// Missing or unreadable reads back as empty rather than throwing, since it is a derived cache, not authoritative state.
  /// </summary>
  public static Dictionary<string, Conversion_info_entry> load_conversion_info(string pack_directory) {
    var path = Path.Combine(pack_info_directory_path(pack_directory), conversion_info_file_name);
    if (!File.Exists(path)) {
      return new Dictionary<string, Conversion_info_entry>();
    }

    try {
      using var stream = File.OpenRead(path);
      return JsonSerializer.Deserialize<Dictionary<string, Conversion_info_entry>>(stream)
          ?? new Dictionary<string, Conversion_info_entry>();
    }
    catch (JsonException) {
      return new Dictionary<string, Conversion_info_entry>();
    }
  }

  /// <summary>
  /// Atomically overwrites conversion_info.json.
  /// Every stem not in info simply has no entry afterward, which is correct: a full convert_all pass re-decides every sticker, so a stale stem never lingers.
  /// </summary>
  public static void write_conversion_info(string pack_directory, Dictionary<string, Conversion_info_entry> info) {
    var pack_info_directory = pack_info_directory_path(pack_directory);
    Directory.CreateDirectory(pack_info_directory);
    var path = Path.Combine(pack_info_directory, conversion_info_file_name);
    var temp_path = Path.Combine(pack_info_directory, $"{conversion_info_file_name}.{Guid.NewGuid():N}.tmp");

    try {
      File.WriteAllText(temp_path, JsonSerializer.Serialize(info));

      if (File.Exists(path)) {
        File.Replace(temp_path, path, null);
      }
      else {
        File.Move(temp_path, path);
      }
    }
    catch {
      File.Delete(temp_path);
      throw;
    }
  }

  /// <summary>
  /// Appends the URL signal-cli returned for a completed upload — Signal sticker packs cannot be deleted, edited, or replaced, so a prior publish's URL stays valid and must never be overwritten by a later one.
  /// One URL per line, oldest first.
  /// </summary>
  public static void append_signal_art_url(string pack_directory, string url) {
    var pack_info_directory = pack_info_directory_path(pack_directory);
    Directory.CreateDirectory(pack_info_directory);
    var path = Path.Combine(pack_info_directory, signal_art_url_file_name);
    File.AppendAllLines(path, new[] { url });
  }

  /// <summary>
  /// The most recent publish URL for this pack, or null if it has never been published.
  /// The file is append-only, oldest first, so the last non-blank line is the latest.
  /// </summary>
  public static string? latest_signal_art_url(string pack_directory) {
    var path = Path.Combine(pack_info_directory_path(pack_directory), signal_art_url_file_name);
    if (!File.Exists(path)) {
      return null;
    }

    return File.ReadAllLines(path).LastOrDefault(line => !string.IsNullOrWhiteSpace(line));
  }

  /// <summary>
  /// Materializes a real yaml with the folder name as a starting title, rather than the frontend silently standing in the folder name for an actually-empty title.
  /// A no-op once the file exists, so it never overwrites whatever the user has since set — including clearing the title back to empty.
  /// </summary>
  public static void ensure_pack_initialized(string pack_directory, string pack_name) {
    var yaml_path = Path.Combine(pack_info_directory_path(pack_directory), stickers_yaml_file_name);
    if (File.Exists(yaml_path)) {
      return;
    }

    var stickers = reconcile_stickers(new List<Sticker_entry>(), scan_art_files(pack_directory));
    write_pack_state(pack_directory, new Pack_save_payload {
      meta = new Pack_meta { title = pack_name },
      stickers = stickers,
    });
  }

  /// <summary>
  /// Deletes the derived WebP output and conversion_info.json entry for one removed sticker, if either is present, so removal leaves no orphaned artifacts on disk.
  /// Skipped entirely when a remaining sticker shares the removed file's stem (art_extensions allows e.g. "logo.png" and "logo.gif" to share one output path) — the artifacts still belong to that sticker.
  /// </summary>
  public static void remove_sticker_artifacts(string pack_directory, string removed_file, List<Sticker_entry> remaining_stickers) {
    var stem = Path.GetFileNameWithoutExtension(removed_file);
    if (remaining_stickers.Any(sticker => Path.GetFileNameWithoutExtension(sticker.file) == stem)) {
      return;
    }

    var webp_path = WebP_file_path(pack_directory, removed_file);
    if (File.Exists(webp_path)) {
      File.Delete(webp_path);
    }

    var conversion_info = load_conversion_info(pack_directory);
    if (conversion_info.Remove(stem)) {
      write_conversion_info(pack_directory, conversion_info);
    }
  }
}
