using System.Text.Json;

namespace Sticker_Creator_Program;

public class Bridge_request {
  public string? type { get; set; }
  public JsonElement? payload { get; set; }
}

public class Bridge_response {
  public string type { get; set; } = "";
  public object? payload { get; set; }
}

public class Pack_state_reply {
  public string pack { get; set; } = "";
  public Pack_meta meta { get; set; } = new();
  public List<Sticker_entry> stickers { get; set; } = new();
  public int original_count { get; set; }
  public int converted_count { get; set; }
  public int mapped_count { get; set; }
  public List<Validity_error> error_list { get; set; } = new();
  public bool enable_lossless_compression { get; set; }
  public bool lossless_warning_was_shown { get; set; }
  public double picker_zoom { get; set; }
  public string art_url { get; set; } = "";
}

public class Link_request {
  public string phone_number { get; set; } = "";
  public string device_name { get; set; } = "";
}

public class Sticker_removal_request {
  public string file { get; set; } = "";
  public Pack_meta meta { get; set; } = new();
  public List<Sticker_entry> stickers { get; set; } = new();
}
