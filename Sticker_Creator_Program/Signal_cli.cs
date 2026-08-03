using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using QRCoder;

namespace Sticker_Creator_Program;

public class Signal_cli_result {
  public int exit_code { get; set; }
  public List<string> standard_output_lines { get; set; } = new();
  public List<string> standard_error_lines { get; set; } = new();
}

/// <summary>
/// Shells out to signal-cli's own Java entry point directly, never the batch launcher.
/// The launcher inlines every dependency jar's absolute path into one command line, which exceeds cmd.exe's line-length limit once the install path is nested deep enough.
/// </summary>
public static class Signal_cli {
  public const string default_device_name = "Sticker Creator Program";
  public const int minimum_java_version = 25;

  /// <summary>
  /// True when a Java executable can be launched, checked once at startup so a missing JRE surfaces immediately instead of failing deep inside a link or publish attempt.
  /// </summary>
  public static bool java_is_available() {
    try {
      var process_start_info = new ProcessStartInfo("java", "-version") {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
      };
      using var process = Process.Start(process_start_info);
      process?.WaitForExit();
      return process is not null;
    }
    catch (Win32Exception) {
      return false;
    }
  }

  public static string install_directory() {
    var published_sibling = Path.Combine(AppContext.BaseDirectory, "signal-cli");
    if (Directory.Exists(published_sibling)) {
      return published_sibling;
    }
    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "signal-cli"));
  }

  public static string data_directory() =>
      Path.Combine(AppContext.BaseDirectory, "signal_cli_data");

  private static readonly Regex registration_phone_number_pattern = new(@"^\+[1-9]\d{6,14}$");
  private static readonly Regex upload_url_pattern = new(@"https://signal\.art/\S+");

  /// <summary>
  /// True when the value matches the E.164 format signal-cli's link command requires: a leading '+', no leading zero after it, 7 to 15 digits total.
  /// </summary>
  public static bool is_valid_registration_phone_number(string value) =>
     registration_phone_number_pattern.IsMatch(value);

  /// <summary>
  /// True when the value can serve as the name Signal shows for this device — any text that is not blank.
  /// </summary>
  public static bool is_valid_device_name(string? value) =>
     !string.IsNullOrWhiteSpace(value);

  /// <summary>
  /// True once a device is linked, checked locally from the account file rather than a signal-cli subprocess call, to avoid a JVM spin-up on every app startup.
  /// </summary>
  public static bool is_linked(string data_directory) =>
     read_accounts_array(data_directory) is { } accounts && accounts.GetArrayLength() > 0;

  /// <summary>
  /// Best-effort read of the linked phone number from the same account file. Returns null if it cannot be found.
  /// </summary>
  public static string? linked_phone_number(string data_directory) {
    if (read_accounts_array(data_directory) is not { } accounts || accounts.GetArrayLength() == 0) {
      return null;
    }
    var first_account = accounts[0];
    return first_account.TryGetProperty("number", out var number) && number.ValueKind == JsonValueKind.String
        ? number.GetString()
        : null;
  }

  /// <summary>
  /// Reads the "accounts" array out of signal-cli's own account file, detached from its parsed document so it stays valid after that document is disposed.
  /// </summary>
  private static JsonElement? read_accounts_array(string data_directory) {
    var accounts_file = Path.Combine(data_directory, "data", "accounts.json");
    if (!File.Exists(accounts_file)) {
      return null;
    }
    try {
      using var document = JsonDocument.Parse(File.ReadAllText(accounts_file));
      return document.RootElement.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array
          ? accounts.Clone()
          : null;
    }
    catch (JsonException) {
      return null;
    }
  }

  /// <summary>
  /// Returns the live process for callers that need to react to output before exit or track it for cancellation.
  /// Both output streams are read asynchronously before the caller waits for exit, avoiding the classic pipe-buffer deadlock.
  /// </summary>
  public static Process start(string data_directory, IEnumerable<string> command_arguments, Action<string>? on_output_line = null, Action<string>? on_error_line = null) {
    var process_start_info = new ProcessStartInfo("java") {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
    };

    process_start_info.ArgumentList.Add("--enable-native-access=ALL-UNNAMED");
    process_start_info.ArgumentList.Add("-cp");
    process_start_info.ArgumentList.Add(Path.Combine(install_directory(), "lib", "*"));
    process_start_info.ArgumentList.Add("org.asamk.signal.Main");
    process_start_info.ArgumentList.Add("-d");
    process_start_info.ArgumentList.Add(data_directory);
    foreach (var argument in command_arguments) {
      process_start_info.ArgumentList.Add(argument);
    }

    var process = Process.Start(process_start_info)
        ?? throw new InvalidOperationException("Failed to start signal-cli process.");

    process.OutputDataReceived += (_, event_args) => {
      if (event_args.Data is not null) {
        on_output_line?.Invoke(event_args.Data);
      }
    };
    process.ErrorDataReceived += (_, event_args) => {
      if (event_args.Data is not null) {
        on_error_line?.Invoke(event_args.Data);
      }
    };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    return process;
  }

  /// <summary>
  /// Runs signal-cli to completion and captures every output line, for callers that only need the final result.
  /// </summary>
  public static Signal_cli_result run(string data_directory, params string[] command_arguments) {
    var standard_output_lines = new List<string>();
    var standard_error_lines = new List<string>();

    using var process = start(data_directory, command_arguments, standard_output_lines.Add, standard_error_lines.Add);
    process.WaitForExit();

    return new Signal_cli_result {
      exit_code = process.ExitCode,
      standard_output_lines = standard_output_lines,
      standard_error_lines = standard_error_lines,
    };
  }

  /// <summary>
  /// Runs uploadStickerPack to completion and returns the resulting signal.art URL.
  /// </summary>
  public static string upload_sticker_pack(string manifest_path, string data_directory) =>
     extract_upload_url(run(data_directory, "uploadStickerPack", manifest_path));

  /// <summary>
  /// Pure over an already-produced result, so every branch is testable without ever invoking the real uploadStickerPack subprocess.
  /// </summary>
  public static string extract_upload_url(Signal_cli_result result) {
    ensure_signal_cli_succeeded(result);

    var url = result.standard_output_lines
        .Select(line => upload_url_pattern.Match(line))
        .FirstOrDefault(match => match.Success)?.Value;

    return url ?? throw new InvalidOperationException("signal-cli did not print a signal.art URL.");
  }

  /// <summary>
  /// Runs send --note-to-self to completion, delivering the message to the linked account's own "Note to Self" conversation.
  /// No account flag is passed: the account flag is optional whenever exactly one local account is linked, which this application always keeps true.
  /// </summary>
  public static void send_note_to_self(string message, string data_directory) =>
     ensure_signal_cli_succeeded(run(data_directory, "send", "--note-to-self", "-m", message));

  /// <summary>
  /// Pure over an already-produced result, so failure handling is testable without invoking a real subprocess. Shared by every command whose success is exit code alone.
  /// </summary>
  public static void ensure_signal_cli_succeeded(Signal_cli_result result) {
    if (result.exit_code != 0) {
      var detail = string.Join(Environment.NewLine, result.standard_error_lines);
      throw new InvalidOperationException(detail.Length > 0
          ? $"signal-cli exited with code {result.exit_code}: {detail}"
          : $"signal-cli exited with code {result.exit_code}.");
    }
  }

  /// <summary>
  /// Renders a QR code for the given text into a PNG data URL, ready for a plain &lt;img src&gt;.
  /// </summary>
  public static string qr_code_data_url(string content) {
    using var qr_code_data = QRCodeGenerator.GenerateQrCode(content, QRCodeGenerator.ECCLevel.Q);
    var png_bytes = new PngByteQRCode(qr_code_data).GetGraphic(20);
    return "data:image/png;base64," + Convert.ToBase64String(png_bytes);
  }
}
