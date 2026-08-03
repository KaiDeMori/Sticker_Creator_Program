using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using Photino.NET;

namespace Sticker_Creator_Program;

/// <summary>
/// Top-level statements don't get [STAThread] applied automatically, and WebView2's COM-based initialization needs an STA thread — this class exists only to carry that attribute on a real Main.
/// </summary>
public static class SCP_Main {
  /// <summary>
  /// Set on the convert background thread and read by the trophy page's get_trophy after navigation.
  /// Volatile for the cross-thread handoff.
  /// </summary>
  private static volatile object? pending_trophy;

  /// <summary>
  /// The signal-cli "link" subprocess currently in flight, if any — set on the background link thread, read by cancel_link on the UI thread so it can find and kill it. Volatile for the cross-thread handoff.
  /// </summary>
  private static volatile Process? pending_link_process;

  /// <summary>
  /// Set by cancel_link before killing pending_link_process, so the background link thread knows to suppress link_result once the killed process exits — cancellation has no reply of its own.
  /// </summary>
  private static volatile bool link_was_cancelled;

  [STAThread]
  public static void Main(string[] args) {
    using var single_instance_mutex = new Mutex(initiallyOwned: true, "Global\\Sticker_Creator_Program_single_instance", out var created_new);
    if (!created_new) {
      Single_instance_notice.show();
      return;
    }

    if (!Signal_cli.java_is_available()) {
      show_java_missing_message();
      return;
    }

    try {
      SCP_config.load();

      string packs_directory = SCP_config.Active.packs_directory;
      var data_directory = Signal_cli.data_directory();

      bool has_packs_directory = !string.IsNullOrWhiteSpace(packs_directory);
      bool has_linked_device = has_packs_directory && Signal_cli.is_linked(data_directory);

      var startup_page =
          !has_packs_directory ? "web_pages/first_run.html" :
          !has_linked_device ? "web_pages/linked_device.html" :
                                  "web_pages/pack_selection.html";

      string? current_pack_name = null;

      string require_packs_directory() => packs_directory ?? throw new InvalidOperationException("No packs directory configured.");

      var window = new PhotinoWindow()
          .SetTitle("Sticker Creator Program")
          .SetIconFile("web_pages/assets/SCP_icon.ico")
          .SetSize(new Size(1100, 780))
          .Center()
          .RegisterWebMessageReceivedHandler((object? sender, string message) => {
            // This callback fires later, outside Main's try/catch's call stack, so an exception here would otherwise vanish silently instead of surfacing anywhere.
            var window = (PhotinoWindow) sender!;
            try {
              var request = JsonSerializer.Deserialize<Bridge_request>(message);
              switch (request?.type) {
                case "first_run_ready":
                  handle_first_run_ready(window, packs_directory);
                  break;
                case "choose_packs_directory":
                  handle_choose_packs_directory(window);
                  break;
                case "confirm_packs_directory": {
                    var chosen_directory = request.payload?.GetString()
                              ?? throw new InvalidOperationException("confirm_packs_directory received with no path.");
                    if (!Packs_directory_check.check(chosen_directory).is_acceptable) {
                      throw new InvalidOperationException($"Refusing to save an unusable packs directory: {chosen_directory}");
                    }
                    SCP_config.Active.packs_directory = chosen_directory;
                    SCP_config.save();
                    packs_directory = chosen_directory;
                    current_pack_name = null;
                    window.Load(Signal_cli.is_linked(data_directory) ? "web_pages/pack_selection.html" : "web_pages/linked_device.html");
                    break;
                  }
                case "open_first_run":
                  window.Load("web_pages/first_run.html");
                  break;
                case "open_linked_device_page":
                  window.Load("web_pages/linked_device.html");
                  break;
                case "linked_device_ready":
                  handle_linked_device_ready(window, data_directory);
                  break;
                case "start_link":
                  handle_start_link(window, data_directory, request.payload);
                  break;
                case "cancel_link":
                  handle_cancel_link();
                  break;
                case "unlink_local_data":
                  handle_unlink_local_data(window, data_directory);
                  break;
                case "get_packs":
                  handle_get_packs(window, require_packs_directory());
                  break;
                case "open_pack":
                  current_pack_name = request.payload?.GetString();
                  window.Load("web_pages/editor.html");
                  break;
                case "get_pack":
                  handle_get_pack(window, require_packs_directory(), current_pack_name);
                  break;
                case "save_pack":
                  handle_save_pack(window, require_packs_directory(), current_pack_name, request.payload);
                  break;
                case "remove_sticker":
                  handle_remove_sticker(window, require_packs_directory(), current_pack_name, request.payload);
                  break;
                case "convert_pack":
                  handle_convert_pack(window, require_packs_directory(), current_pack_name);
                  break;
                case "publish_pack":
                  handle_publish_pack(window, require_packs_directory(), current_pack_name, data_directory);
                  break;
                case "get_install_qr":
                  handle_get_install_qr(window, require_packs_directory(), current_pack_name);
                  break;
                case "send_note_to_self":
                  handle_send_note_to_self(window, require_packs_directory(), current_pack_name, data_directory);
                  break;
                case "set_lossless_enabled":
                  SCP_config.Active.enable_lossless_compression = request.payload?.GetBoolean()
                            ?? throw new InvalidOperationException("set_lossless_enabled received no payload.");
                  SCP_config.save();
                  break;
                case "set_lossless_warning_shown":
                  SCP_config.Active.lossless_warning_was_shown = true;
                  SCP_config.save();
                  break;
                case "set_picker_zoom":
                  SCP_config.Active.picker_zoom = request.payload?.GetDouble()
                            ?? throw new InvalidOperationException("set_picker_zoom received no payload.");
                  SCP_config.save();
                  break;
                case "open_trophy":
                  window.Load("web_pages/trophy.html");
                  break;
                case "get_trophy":
                  window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "trophy", payload = pending_trophy }));
                  break;
                case "close_trophy":
                  window.Load("web_pages/editor.html");
                  break;
                case "open_external_url":
                  handle_open_external_url(request.payload?.GetString());
                  break;
                case "open_pack_folder":
                  handle_open_pack_folder(window, require_packs_directory(), request.payload?.GetString());
                  break;
                case "open_pack_selection":
                  current_pack_name = null;
                  window.Load("web_pages/pack_selection.html");
                  break;
              }
            }
            catch (Exception exception) {
              window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "error", payload = exception.ToString() }));
            }
          })
          .Load(startup_page);

      window.WaitForClose();
    }
    catch (Exception exception) {
      Native_console.attach_error_console();
      Console.Error.WriteLine("Sticker Creator Program crashed:");
      Console.Error.WriteLine(exception);
      Console.Error.WriteLine();
      Console.Error.WriteLine("Press Enter to close…");
      Console.ReadLine();
    }
  }

  /// <summary>
  /// Shown before the real window exists when no Java executable can be found — signal-cli is a JVM application and cannot run without one.
  /// Uses the same off-screen-window ShowMessage pattern as Single_instance_notice.show, for the same reason: ShowMessage needs a live native instance.
  /// </summary>
  private static void show_java_missing_message() {
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
      notice_window.ShowMessage("Sticker Creator Program", $"Java was not found. Sticker Creator Program requires a Java Runtime Environment (JRE), version {Signal_cli.minimum_java_version} or later.");
      notice_window.Close();
    };

    notice_window.WaitForClose();
  }

  private static void handle_get_packs(PhotinoWindow window, string packs_directory) {
    var response = Packs_api.build_packs_response(packs_directory);
    window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "packs", payload = response }));
  }

  /// <summary>
  /// Doubles as a context request so the page knows whether a packs directory is already configured, to offer a "keep current" escape hatch — present only when reached via the Change button, never on a genuine first run.
  /// </summary>
  private static void handle_first_run_ready(PhotinoWindow window, string? packs_directory) {
    var reply = new {
      has_existing_config = packs_directory is not null,
      current_packs_directory = packs_directory,
    };
    window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "first_run_context", payload = reply }));
  }

  /// <summary>
  /// Carries both the name of the currently linked device, for the manage state's unlink instructions, and the default name, for the register state's pre-filled input.
  /// The two states are mutually exclusive, but the page decides which it is from the same reply.
  /// </summary>
  private static void handle_linked_device_ready(PhotinoWindow window, string data_directory) {
    var linked = Signal_cli.is_linked(data_directory);
    var reply = new {
      linked,
      phone_number = linked ? Signal_cli.linked_phone_number(data_directory) : null,
      device_name = linked ? SCP_config.Active.device_name : null,
      default_device_name = Signal_cli.default_device_name,
    };
    window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "linked_device_state", payload = reply }));
  }

  /// <summary>
  /// Runs signal-cli's "link" on a background thread — it blocks until the phone approves or rejects the request, which can take a while, so it can't run on the UI thread.
  /// Invalid input, or a failure to start the subprocess, is reported through link_result rather than thrown — both are expected outcomes here, not exceptional ones.
  /// The device name is persisted only once the link actually succeeds, so a rejected or cancelled attempt leaves no name behind for the manage state to claim.
  /// </summary>
  private static void handle_start_link(PhotinoWindow window, string data_directory, JsonElement? payload) {
    var request = payload!.Value.Deserialize<Link_request>()!;
    var phone_number = request.phone_number.Trim();
    var device_name = request.device_name.Trim();

    if (!Signal_cli.is_valid_registration_phone_number(phone_number)) {
      send_message(window, "link_result", new { ok = false, error = "Enter a phone number in international format, e.g. +491701234567." });
      return;
    }

    if (!Signal_cli.is_valid_device_name(device_name)) {
      send_message(window, "link_result", new { ok = false, error = "Enter a device name — it is what Signal shows for this device under Linked Devices." });
      return;
    }

    link_was_cancelled = false;

    Task.Run(() => {
      try {
        var process = Signal_cli.start(data_directory, new[] { "link", "-n", device_name }, on_output_line: line => {
          if (line.StartsWith("sgnl://")) {
            send_message(window, "link_qr", new { qr_data_url = Signal_cli.qr_code_data_url(line) });
          }
        });
        pending_link_process = process;
        process.WaitForExit();
        pending_link_process = null;

        if (link_was_cancelled) {
          return;
        }

        if (process.ExitCode == 0) {
          SCP_config.Active.device_name = device_name;
          SCP_config.save();
          send_message(window, "link_result", new { ok = true });
        }
        else {
          send_message(window, "link_result", new { ok = false, error = $"signal-cli exited with code {process.ExitCode}." });
        }
      }
      catch (Exception exception) {
        pending_link_process = null;
        send_message(window, "link_result", new { ok = false, error = exception.Message });
      }
    });
  }

  private static void handle_cancel_link() {
    link_was_cancelled = true;
    var process = pending_link_process;
    if (process is not null && !process.HasExited) {
      process.Kill(entireProcessTree: true);
    }
  }

  /// <summary>
  /// Actually unlinking the device from the Signal account happens on the phone, independent of this — deleting stale local state is always safe regardless of whether that phone-side step has already happened.
  /// </summary>
  private static void handle_unlink_local_data(PhotinoWindow window, string data_directory) {
    Directory.Delete(data_directory, recursive: true);
    window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "local_data_deleted", payload = new { ok = true } }));
  }

  /// <summary>
  /// <para>
  /// Opens the native folder picker and reports back a verdict on what the user chose, without persisting anything — saving happens only once the user confirms an acceptable folder (see the confirm_packs_directory branch).
  /// </para>
  /// <para>
  /// The picker runs off the UI thread on purpose.
  /// The native folder dialog spins its own nested modal message loop; opening it directly would do so while still inside the WebView2 web-message callback that dispatched us — re-entering that callback's stack, a documented WebView reentrancy hazard.
  /// Running it via the async overload lets the callback unwind first, so the dialog opens from the idle message loop instead.
  /// The reply is sent from the continuation; SendWebMessage marshals itself back to the UI thread.
  /// </para>
  /// </summary>
  private static void handle_choose_packs_directory(PhotinoWindow window) {
    window.ShowOpenFolderAsync("Select your packs folder").ContinueWith(picking => {
      try {
        var picked = picking.GetAwaiter().GetResult();
        object reply;
        if (picked.Length == 0) {
          reply = new { status = "cancelled" };
        }
        else {
          var chosen_directory = picked[0];
          var result = Packs_directory_check.check(chosen_directory);
          reply = result.is_blocked
                ? new {
                  status = "blocked",
                  reason = "This folder contains image files directly — that looks like a single pack's art folder, not a packs folder. Please choose or create a different, empty folder.",
                }
                : new {
                  status = "accept",
                  path = chosen_directory,
                  note = describe_acceptable_packs_directory(result),
                };
        }
        window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "packs_directory_verdict", payload = reply }));
      }
      catch (Exception exception) {
        window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "error", payload = exception.ToString() }));
      }
    });
  }

  private static string describe_acceptable_packs_directory(Packs_directory_check_result result) {
    if (result.subfolders_with_images_count > 0) {
      return $"This folder already holds {result.subfolders_with_images_count} pack folder(s) — they'll appear in your pack list.";
    }
    if (result.subfolder_count > 0) {
      return $"This folder contains {result.subfolder_count} subfolder(s), none with images yet.";
    }
    return "This folder is empty — a perfect fresh packs folder.";
  }

  /// <summary>
  /// Doubles as the Editor's readiness signal and its data request, since C# already holds current_pack_name, set on open_pack.
  /// </summary>
  private static void handle_get_pack(PhotinoWindow window, string packs_directory, string? current_pack_name) {
    var pack_name = current_pack_name ?? throw new InvalidOperationException("get_pack received with no pack open.");
    var pack_directory = Path.Combine(packs_directory, pack_name);
    var state = Pack_store.load_pack_state(pack_directory);
    foreach (var sticker in state.stickers) {
      var source_path = Path.Combine(pack_directory, sticker.file);
      sticker.source_exists = File.Exists(source_path);
      var dimensions = sticker.source_exists ? Image_pipeline.probe_dimensions(source_path) : null;
      sticker.width = dimensions?.width;
      sticker.height = dimensions?.height;
      sticker.url = Pack_store.sticker_url(pack_directory, sticker);
    }
    var error_list = Pack_validator.compute_error_list(pack_directory, state);
    var reply = new Pack_state_reply {
      pack = pack_name,
      meta = state.meta,
      stickers = state.stickers,
      original_count = state.original_count,
      converted_count = state.converted_count,
      mapped_count = state.mapped_count,
      error_list = error_list,
      enable_lossless_compression = SCP_config.Active.enable_lossless_compression,
      lossless_warning_was_shown = SCP_config.Active.lossless_warning_was_shown,
      picker_zoom = SCP_config.Active.picker_zoom,
      art_url = Pack_store.latest_signal_art_url(pack_directory) ?? "",
    };
    window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "pack_state", payload = reply }));
  }

  /// <summary>
  /// Renders the current pack's latest persisted signal.art URL as a QR code, on demand rather than alongside every get_pack reply — a pack is opened far more often than its install dialog, and QR rendering is otherwise wasted work.
  /// </summary>
  private static void handle_get_install_qr(PhotinoWindow window, string packs_directory, string? current_pack_name) {
    var pack_name = current_pack_name ?? throw new InvalidOperationException("get_install_qr received with no pack open.");
    var pack_directory = Path.Combine(packs_directory, pack_name);
    var url = Pack_store.latest_signal_art_url(pack_directory);

    if (url is null) {
      send_message(window, "install_qr", new { ok = false });
      return;
    }

    send_message(window, "install_qr", new { ok = true, qr_data_url = Signal_cli.qr_code_data_url(url) });
  }

  private static void handle_save_pack(PhotinoWindow window, string packs_directory, string? current_pack_name, JsonElement? payload) {
    var pack_name = current_pack_name ?? throw new InvalidOperationException("save_pack received with no pack open.");
    var pack_directory = Path.Combine(packs_directory, pack_name);
    var save_payload = payload!.Value.Deserialize<Pack_save_payload>()!;

    Pack_store.write_pack_state(pack_directory, save_payload);

    var error_list = Pack_validator.compute_error_list(pack_directory, new Pack_state { meta = save_payload.meta, stickers = save_payload.stickers });
    var reply = new { meta = save_payload.meta, stickers = save_payload.stickers, error_list };
    window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "pack_saved", payload = reply }));
  }

  /// <summary>
  /// The frontend has already removed the sticker from its local state and cleared meta.cover if it matched, before calling this — the same "local state mutates first, backend persists" shape save_pack and setCover already use.
  /// Persists first, then cleans up derived artifacts, then replies — in that order, so an artifact-deletion failure can't produce a second, unpaired reply to one request.
  /// </summary>
  private static void handle_remove_sticker(PhotinoWindow window, string packs_directory, string? current_pack_name, JsonElement? payload) {
    var pack_name = current_pack_name ?? throw new InvalidOperationException("remove_sticker received with no pack open.");
    var pack_directory = Path.Combine(packs_directory, pack_name);
    var request_payload = payload!.Value.Deserialize<Sticker_removal_request>()!;

    Pack_store.write_pack_state(pack_directory, new Pack_save_payload { meta = request_payload.meta, stickers = request_payload.stickers });
    Pack_store.remove_sticker_artifacts(pack_directory, request_payload.file, request_payload.stickers);

    var error_list = Pack_validator.compute_error_list(pack_directory, new Pack_state { meta = request_payload.meta, stickers = request_payload.stickers });
    var reply = new { meta = request_payload.meta, stickers = request_payload.stickers, error_list };
    window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "sticker_removed", payload = reply }));
  }

  /// <summary>
  /// Conversion is CPU-bound and can run for a long time (self-adjusting re-encodes), so it runs off the UI thread to keep the window responsive.
  /// Progress and the final outcome are marshalled back via SendWebMessage, which hops to the UI thread on its own.
  /// </summary>
  private static void handle_convert_pack(PhotinoWindow window, string packs_directory, string? current_pack_name) {
    var pack_name = current_pack_name ?? throw new InvalidOperationException("convert_pack received with no pack open.");
    var pack_directory = Path.Combine(packs_directory, pack_name);
    var filenames = Pack_store.scan_art_files(pack_directory);
    var WebP_directory = Pack_store.WebP_directory_path(pack_directory);
    var total = filenames.Count;
    var enable_lossless = SCP_config.Active.enable_lossless_compression;

    Task.Run(() => {
      try {
        var completed = 0;
        var results = Image_pipeline.convert_all(pack_directory, WebP_directory, filenames, enable_lossless, result => {
          completed++;
          send_message(window, "convert_progress", new {
            done = completed,
            total,
            file = result.file,
            fit = result.fit.ToString(),
            quality = result.quality,
          });
        });

        Pack_store.write_conversion_info(pack_directory, results.ToDictionary(
           result => Path.GetFileNameWithoutExtension(result.file),
           result => new Conversion_info_entry { fit = result.fit.ToString(), quality = result.quality }
        ));

        var trophy_result = results.FirstOrDefault(result => result.is_trophy);
        if (trophy_result is not null) {
          pending_trophy = new {
            file = trophy_result.file,
            fit = trophy_result.fit.ToString(),
            quality = trophy_result.quality,
            byte_size = trophy_result.byte_size,
          };
          send_message(window, "convert_result", new { ok = true, trophy = true });
          return;
        }

        send_message(window, "convert_result", new { ok = true });
      }
      catch (Squareness_check_error exception) {
        send_message(window, "convert_result", new { ok = false, problems = exception.problem_files });
      }
      catch (Output_dimension_error exception) {
        send_message(window, "convert_result", new { ok = false, problems = exception.problem_files });
      }
      catch (Exception exception) {
        send_message(window, "error", exception.ToString());
      }
    });
  }

  /// <summary>
  /// Re-checks validity server-side rather than trusting the Editor's last-loaded error_list, since the pack could have changed since — same reasoning handle_convert_pack applies to its own inputs.
  /// The upload itself blocks on the signal-cli subprocess, so this runs off the UI thread like handle_convert_pack does.
  /// </summary>
  private static void handle_publish_pack(PhotinoWindow window, string packs_directory, string? current_pack_name, string data_directory) {
    var pack_name = current_pack_name ?? throw new InvalidOperationException("publish_pack received with no pack open.");
    var pack_directory = Path.Combine(packs_directory, pack_name);

    Task.Run(() => {
      try {
        var state = Pack_store.load_pack_state(pack_directory);
        var error_list = Pack_validator.compute_error_list(pack_directory, state);
        if (error_list.Count > 0) {
          send_message(window, "publish_result", new { ok = false, error = "pack is no longer valid" });
          return;
        }

        var manifest = Signal_manifest.build(pack_directory, state.meta, state.stickers);
        Signal_manifest.write(pack_directory, manifest);

        var url = Signal_cli.upload_sticker_pack(Signal_manifest.manifest_file_path(pack_directory), data_directory);
        Pack_store.append_signal_art_url(pack_directory, url);
        send_message(window, "publish_result", new { ok = true, url });
      }
      catch (Exception exception) {
        send_message(window, "publish_result", new { ok = false, error = exception.Message });
      }
    });
  }

  /// <summary>
  /// Delivers the pack's latest signal.art URL to the linked account's own "Note to Self" conversation, so it reaches the phone that will install the pack without retyping or rescanning anything.
  /// Runs off the UI thread like handle_publish_pack, since it blocks on the same signal-cli JVM subprocess.
  /// </summary>
  private static void handle_send_note_to_self(PhotinoWindow window, string packs_directory, string? current_pack_name, string data_directory) {
    var pack_name = current_pack_name ?? throw new InvalidOperationException("send_note_to_self received with no pack open.");
    var pack_directory = Path.Combine(packs_directory, pack_name);
    var url = Pack_store.latest_signal_art_url(pack_directory)
        ?? throw new InvalidOperationException("send_note_to_self received with no published URL.");

    Task.Run(() => {
      try {
        Signal_cli.send_note_to_self(url, data_directory);
        send_message(window, "note_to_self_result", new { ok = true });
      }
      catch (Exception exception) {
        send_message(window, "note_to_self_result", new { ok = false, error = exception.Message });
      }
    });
  }

  private static void send_message(PhotinoWindow window, string type, object? payload) =>
      window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = type, payload = payload }));

  /// <summary>
  /// Opens one pack's root folder — where the user's source art lives, not the app-owned pack_info subfolder — in the native file explorer.
  /// Unlike the Editor's messages, this carries the pack name as payload rather than acting on current_pack_name: pack-selection holds no pack open, exactly as open_pack does.
  /// </summary>
  private static void handle_open_pack_folder(PhotinoWindow window, string packs_directory, string? pack_name) {
    if (pack_name is null) {
      throw new InvalidOperationException("open_pack_folder received with no pack name.");
    }
    var pack_directory = Path.Combine(packs_directory, pack_name);
    if (!Directory.Exists(pack_directory)) {
      throw new DirectoryNotFoundException($"Pack folder not found: {pack_name}");
    }
    Process.Start(new ProcessStartInfo(pack_directory) { UseShellExecute = true });
    window.SendWebMessage(JsonSerializer.Serialize(new Bridge_response { type = "folder_opened", payload = new { ok = true } }));
  }

  /// <summary>
  /// JS intercepts the click and hands the URL here instead of letting Photino navigate its own window to it.
  /// UseShellExecute is required — without it, launching a URL throws instead of resolving to the OS's default browser.
  /// </summary>
  private static void handle_open_external_url(string? url) {
    if (string.IsNullOrWhiteSpace(url)) {
      throw new InvalidOperationException("open_external_url received no URL.");
    }
    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
  }
}
