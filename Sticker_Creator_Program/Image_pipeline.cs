using ImageMagick;

namespace Sticker_Creator_Program;

public class Squareness_problem {
  public string file { get; set; } = "";
  public int? width { get; set; }
  public int? height { get; set; }
  public string? error { get; set; }
}

public class Squareness_check_error : Exception {
  public List<Squareness_problem> problem_files { get; }

  public Squareness_check_error(List<Squareness_problem> problem_files)
      : base($"{problem_files.Count} file(s) failed the squareness check") {
    this.problem_files = problem_files;
  }
}

/// <summary>
/// An internal invariant violation: conversion always resizes output to the required square size, so this should never actually be thrown.
/// </summary>
public class Output_dimension_error : Exception {
  public List<Squareness_problem> problem_files { get; }

  public Output_dimension_error(List<Squareness_problem> problem_files)
      : base($"{problem_files.Count} converted file(s) are not the required sticker dimensions") {
    this.problem_files = problem_files;
  }
}

public enum Conversion_fit {
  lossless,
  lossy,
  uncrushable,
}

public class Conversion_result {
  public string file { get; set; } = "";
  public Conversion_fit fit { get; set; }
  public int? quality { get; set; }
  public long byte_size { get; set; }

  /// <summary>
  /// Marks the near-impossible incompressible case.
  /// </summary>
  public bool is_trophy { get; set; }
}

public static class Image_pipeline {
  public const int sticker_side_pixels = 512;

  /// <summary>
  /// Matches Signal's own MAX_STICKER_BYTE_SIZE.
  /// </summary>
  public const int target_byte_size = 300 * 1024;

  /// <summary>
  /// Effectively unreachable for real art.
  /// </summary>
  public const int trophy_quality_threshold = 70;

  /// <summary>
  /// A source with this file-name stem earns the trophy outright, so the flow is testable without an impossible image; empty disables it.
  /// </summary>
  public const string trophy_trigger_stem = "Trophy_fractal";

  private const int fine_quality_ceiling = 100;
  private const int fine_quality_floor = 90;
  private const int fine_quality_step = 1;
  private const int coarse_quality_step = 5;
  private const int floor_quality = 1;

  public static void check_all_square(string art_directory, List<string> filenames) {
    var problems = new List<Squareness_problem>();
    foreach (var file_name in filenames) {
      var dimensions = probe_dimensions(Path.Combine(art_directory, file_name));
      if (dimensions is null) {
        problems.Add(new Squareness_problem { file = file_name, error = "could not read image dimensions" });
        continue;
      }

      var (width, height) = dimensions.Value;
      if (width != height) {
        problems.Add(new Squareness_problem { file = file_name, width = width, height = height });
      }
    }

    if (problems.Count > 0) {
      throw new Squareness_check_error(problems);
    }
  }

  public static List<Conversion_result> convert_all(string art_directory, string WebP_directory, List<string> filenames, bool enable_lossless, Action<Conversion_result>? on_file_converted = null) {
    check_all_square(art_directory, filenames);
    Directory.CreateDirectory(WebP_directory);

    var results = new List<Conversion_result>();
    foreach (var file_name in filenames) {
      var stem = Path.GetFileNameWithoutExtension(file_name);
      var destination_path = Path.Combine(WebP_directory, $"{stem}.webp");
      var result = convert_one_adaptive(Path.Combine(art_directory, file_name), destination_path, file_name, enable_lossless);
      results.Add(result);
      on_file_converted?.Invoke(result);
    }

    var written = results
        .Where(result => result.fit != Conversion_fit.uncrushable)
        .Select(result => result.file)
        .ToList();
    verify_output_dimensions(WebP_directory, written);

    return results;
  }

  /// <summary>
  /// On total failure, removes any stale WebP left by a previous successful conversion of this stem, so a failed re-conversion cannot leave an outdated output in place.
  /// </summary>
  private static Conversion_result convert_one_adaptive(string source_path, string destination_path, string file_name, bool enable_lossless) {
    using var image = new MagickImage(source_path);
    image.Resize(new MagickGeometry(sticker_side_pixels, sticker_side_pixels));
    image.Format = MagickFormat.WebP;

    byte[] encoded = [];
    if (enable_lossless) {
      apply_lossless_profile(image);
      encoded = image.ToByteArray();
      if (encoded.LongLength <= target_byte_size) {
        File.WriteAllBytes(destination_path, encoded);
        return new Conversion_result { file = file_name, fit = Conversion_fit.lossless, byte_size = encoded.LongLength, is_trophy = false };
      }
    }

    foreach (var quality in lossy_quality_ladder()) {
      apply_lossy_profile(image, quality);
      encoded = image.ToByteArray();
      if (encoded.LongLength <= target_byte_size) {
        File.WriteAllBytes(destination_path, encoded);
        return new Conversion_result {
          file = file_name,
          fit = Conversion_fit.lossy,
          quality = quality,
          byte_size = encoded.LongLength,
          is_trophy = quality < trophy_quality_threshold || matches_trophy_trigger(file_name),
        };
      }
    }

    if (File.Exists(destination_path)) {
      File.Delete(destination_path);
    }
    return new Conversion_result {
      file = file_name,
      fit = Conversion_fit.uncrushable,
      quality = floor_quality,
      byte_size = encoded.LongLength,
      is_trophy = true,
    };
  }

  private static IEnumerable<int> lossy_quality_ladder() {
    for (var quality = fine_quality_ceiling; quality >= fine_quality_floor; quality -= fine_quality_step) {
      yield return quality;
    }
    for (var quality = fine_quality_floor - coarse_quality_step; quality > floor_quality; quality -= coarse_quality_step) {
      yield return quality;
    }
    yield return floor_quality;
  }

  private static bool matches_trophy_trigger(string file_name) =>
      trophy_trigger_stem.Length > 0 &&
      string.Equals(Path.GetFileNameWithoutExtension(file_name), trophy_trigger_stem, StringComparison.OrdinalIgnoreCase);

  private static void apply_lossless_profile(MagickImage image) {
    image.Quality = 100;
    image.Settings.SetDefine(MagickFormat.WebP, "lossless", "true");
    image.Settings.SetDefine(MagickFormat.WebP, "method", "6");
    image.Settings.SetDefine(MagickFormat.WebP, "exact", "true");
  }

  private static void apply_lossy_profile(MagickImage image, int quality) {
    image.Quality = (uint) quality;
    image.Settings.SetDefine(MagickFormat.WebP, "lossless", "false");
    image.Settings.SetDefine(MagickFormat.WebP, "method", "6");
    image.Settings.SetDefine(MagickFormat.WebP, "alpha-quality", "100");
    image.Settings.SetDefine(MagickFormat.WebP, "exact", "false");
  }

  public static void verify_output_dimensions(string WebP_directory, List<string> filenames) {
    var problems = new List<Squareness_problem>();
    foreach (var file_name in filenames) {
      var output_name = $"{Path.GetFileNameWithoutExtension(file_name)}.webp";
      var dimensions = probe_dimensions(Path.Combine(WebP_directory, output_name));
      if (dimensions is null) {
        problems.Add(new Squareness_problem { file = output_name, error = "converted output could not be read" });
        continue;
      }

      var (width, height) = dimensions.Value;
      if (width != sticker_side_pixels || height != sticker_side_pixels) {
        problems.Add(new Squareness_problem {
          file = output_name,
          width = width,
          height = height,
          error = $"converted output is {width}×{height}, expected {sticker_side_pixels}×{sticker_side_pixels}",
        });
      }
    }

    if (problems.Count > 0) {
      throw new Output_dimension_error(problems);
    }
  }

  /// <summary>
  /// Reads header metadata only rather than decoding pixels.
  /// </summary>
  /// <returns>Null when the file can't be identified as an image at all.</returns>
  public static (int width, int height)? probe_dimensions(string image_path) {
    try {
      var info = new MagickImageInfo(image_path);
      return ((int) info.Width, (int) info.Height);
    }
    catch (MagickException) {
      return null;
    }
  }
}
