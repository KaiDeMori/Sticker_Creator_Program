namespace Sticker_Creator_Program;

public class Pack_summary {
  public string name { get; set; } = "";
  public string title { get; set; } = "";
  public string author { get; set; } = "";
  public string cover { get; set; } = "";
  public string cover_url { get; set; } = "";
  public bool has_webp { get; set; }
  public int original_count { get; set; }
  public int converted_count { get; set; }
  public int mapped_count { get; set; }
  public string art_url { get; set; } = "";
}

public class Packs_response {
  public string packs_directory { get; set; } = "";
  public List<Pack_summary> packs { get; set; } = new();
}

/// <summary>
/// Field names and derivation rules here are dictated by what pack_selection.html's JS reads off each pack summary, not by this backend's own preferences — changing them requires updating that page too.
/// </summary>
public static class Packs_api {
  public static Packs_response build_packs_response(string packs_directory) {
    var packs = new List<Pack_summary>();
    foreach (var pack_name in Pack_store.scan_packs(packs_directory)) {
      var pack_directory = Path.Combine(packs_directory, pack_name);
      Pack_store.ensure_pack_initialized(pack_directory, pack_name);
      var state = Pack_store.load_pack_state(pack_directory);
      var WebP_stems = Pack_store.scan_WebP_stems(Pack_store.WebP_directory_path(pack_directory));

      var cover = !string.IsNullOrEmpty(state.meta.cover) && WebP_stems.Contains(Path.GetFileNameWithoutExtension(state.meta.cover))
          ? state.meta.cover
          : "";
      var default_cover = state.stickers.FirstOrDefault(sticker => sticker.converted)?.file ?? "";
      var effective_cover = !string.IsNullOrEmpty(cover) ? cover : default_cover;
      var cover_url = !string.IsNullOrEmpty(effective_cover)
          ? Pack_store.file_url(Pack_store.WebP_file_path(pack_directory, effective_cover))
          : "";

      packs.Add(new Pack_summary {
        name = pack_name,
        title = state.meta.title,
        author = state.meta.author,
        cover = cover,
        cover_url = cover_url,
        has_webp = WebP_stems.Count > 0,
        original_count = state.original_count,
        converted_count = state.converted_count,
        mapped_count = state.mapped_count,
        art_url = Pack_store.latest_signal_art_url(pack_directory) ?? "",
      });
    }

    return new Packs_response {
      packs_directory = packs_directory,
      packs = packs,
    };
  }
}
