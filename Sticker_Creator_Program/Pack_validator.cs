namespace Sticker_Creator_Program;

public class Validity_error {
  public string condition { get; set; } = "";
  public string? file { get; set; }
}

public static class Pack_validator {
  /// <summary>
  /// Matches Signal's own MAX_STICKERS.
  /// </summary>
  public const int max_sticker_count = 200;

  public const string condition_title_not_empty = "meta.title must not be empty.";
  public const string condition_author_not_empty = "meta.author must not be empty.";
  public const string condition_minimum_sticker_count = "The pack must hold at least 1 sticker.";
  public const string condition_maximum_sticker_count = "The pack must hold at most 200 stickers.";
  public const string condition_cover_not_empty = "meta.cover must not be empty.";
  public const string condition_cover_names_present_sticker = "meta.cover must name a sticker present in the pack.";
  public const string condition_sticker_file_exists = "The file named by file must exist in the pack folder.";
  public const string condition_sticker_dimensions_readable = "The source file's dimensions must be readable as an image.";
  public const string condition_sticker_is_square = "The source image's width must equal its height.";
  public const string condition_sticker_webp_exists = "A WebP output file must exist for the sticker in pack_info/_WebP/.";
  public const string condition_sticker_webp_is_512 = "The WebP output must be exactly 512 × 512 pixels.";
  public const string condition_sticker_webp_size_limit = "The WebP output must be at most 307200 bytes (300 KiB).";
  public const string condition_sticker_emoji_not_empty = "The sticker's emoji must not be empty.";

  public static List<Validity_error> compute_error_list(string pack_directory, Pack_state state) {
    var errors = new List<Validity_error>();

    if (string.IsNullOrEmpty(state.meta.title)) {
      errors.Add(new Validity_error { condition = condition_title_not_empty });
    }
    if (string.IsNullOrEmpty(state.meta.author)) {
      errors.Add(new Validity_error { condition = condition_author_not_empty });
    }
    if (state.stickers.Count < 1) {
      errors.Add(new Validity_error { condition = condition_minimum_sticker_count });
    }
    if (state.stickers.Count > max_sticker_count) {
      errors.Add(new Validity_error { condition = condition_maximum_sticker_count });
    }
    if (string.IsNullOrEmpty(state.meta.cover)) {
      errors.Add(new Validity_error { condition = condition_cover_not_empty });
    }
    if (!state.stickers.Any(sticker => sticker.file == state.meta.cover)) {
      errors.Add(new Validity_error { condition = condition_cover_names_present_sticker });
    }

    foreach (var sticker in state.stickers) {
      errors.AddRange(compute_sticker_errors(pack_directory, sticker, sticker.file == state.meta.cover));
    }

    return errors;
  }

  private static IEnumerable<Validity_error> compute_sticker_errors(string pack_directory, Sticker_entry sticker, bool is_cover) {
    var source_path = Path.Combine(pack_directory, sticker.file);
    var source_file_exists = File.Exists(source_path);
    if (!source_file_exists) {
      yield return new Validity_error { condition = condition_sticker_file_exists, file = sticker.file };
    }

    var source_dimensions = source_file_exists ? Image_pipeline.probe_dimensions(source_path) : null;
    if (source_dimensions is null) {
      yield return new Validity_error { condition = condition_sticker_dimensions_readable, file = sticker.file };
    }
    else if (source_dimensions.Value.width != source_dimensions.Value.height) {
      yield return new Validity_error { condition = condition_sticker_is_square, file = sticker.file };
    }

    if (!sticker.converted) {
      yield return new Validity_error { condition = condition_sticker_webp_exists, file = sticker.file };
    }
    else {
      var webp_path = Pack_store.WebP_file_path(pack_directory, sticker.file);
      var webp_dimensions = Image_pipeline.probe_dimensions(webp_path);
      if (webp_dimensions is null
          || webp_dimensions.Value.width != Image_pipeline.sticker_side_pixels
          || webp_dimensions.Value.height != Image_pipeline.sticker_side_pixels) {
        yield return new Validity_error { condition = condition_sticker_webp_is_512, file = sticker.file };
      }

      if (new FileInfo(webp_path).Length > Image_pipeline.target_byte_size) {
        yield return new Validity_error { condition = condition_sticker_webp_size_limit, file = sticker.file };
      }
    }

    // Signal's own manifest format does not require the cover image to carry an emoji.
    if (!is_cover && string.IsNullOrEmpty(sticker.emoji)) {
      yield return new Validity_error { condition = condition_sticker_emoji_not_empty, file = sticker.file };
    }
  }
}
