using Sticker_Creator_Program;

namespace Sticker_Creator_Program_Tests;

/// <summary>
/// Exercises the real signal-cli subprocess rather than mocking it, since signal-cli's actual behavior is the contract this module tests against.
/// </summary>
public class Signal_cli_tests : IDisposable
{
    private readonly string data_directory =
        Path.Combine(AppContext.BaseDirectory, "test_scratch", Guid.NewGuid().ToString("N"));

    public Signal_cli_tests()
    {
        Directory.CreateDirectory(data_directory);
    }

    public void Dispose()
    {
        Directory.Delete(data_directory, recursive: true);
    }

    [Fact]
    public void install_directory_points_at_the_real_signal_cli_checkout()
    {
        Assert.True(Directory.Exists(Path.Combine(Signal_cli.install_directory(), "lib")));
    }

    [Fact]
    public void data_directory_is_inside_the_app_directory()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "signal_cli_data"), Signal_cli.data_directory());
    }

    [Fact]
    public void run_invokes_the_real_signal_cli_process()
    {
        var result = Signal_cli.run(data_directory, "--version");

        Assert.Equal(0, result.exit_code);
        Assert.Contains(result.standard_output_lines, line => line.Contains("signal-cli"));
    }

    [Fact]
    public void is_linked_is_false_when_no_account_file_exists()
    {
        Assert.False(Signal_cli.is_linked(data_directory));
    }

    [Fact]
    public void linked_phone_number_is_null_when_no_account_file_exists()
    {
        Assert.Null(Signal_cli.linked_phone_number(data_directory));
    }

    [Fact]
    public void is_linked_is_true_when_the_account_file_has_at_least_one_entry()
    {
        write_accounts_file("{\"accounts\": [{\"number\": \"+15551234567\"}], \"version\": 2}");

        Assert.True(Signal_cli.is_linked(data_directory));
    }

    [Fact]
    public void is_linked_is_false_when_the_account_file_is_empty()
    {
        write_accounts_file("{\"accounts\": [], \"version\": 2}");

        Assert.False(Signal_cli.is_linked(data_directory));
    }

    [Fact]
    public void linked_phone_number_reads_the_number_field_of_the_first_account()
    {
        write_accounts_file("{\"accounts\": [{\"number\": \"+15551234567\"}], \"version\": 2}");

        Assert.Equal("+15551234567", Signal_cli.linked_phone_number(data_directory));
    }

    [Fact]
    public void linked_phone_number_reads_the_real_signal_cli_account_file_shape()
    {
        write_accounts_file("""
            {
              "accounts" : [ {
                "path" : "614226",
                "environment" : "LIVE",
                "number" : "+4917662882151",
                "uuid" : "768dcc44-526f-4ede-af80-d5cf58c892da"
              } ],
              "version" : 2
            }
            """);

        Assert.True(Signal_cli.is_linked(data_directory));
        Assert.Equal("+4917662882151", Signal_cli.linked_phone_number(data_directory));
    }

    [Fact]
    public void linked_phone_number_is_null_when_the_first_account_has_no_number_field()
    {
        write_accounts_file("{\"accounts\": [{\"path\": \"614226\"}], \"version\": 2}");

        Assert.Null(Signal_cli.linked_phone_number(data_directory));
    }

    [Theory]
    [InlineData("+491701234567")]
    [InlineData("+15551234567")]
    public void is_valid_registration_phone_number_accepts_E164_numbers(string phone_number)
    {
        Assert.True(Signal_cli.is_valid_registration_phone_number(phone_number));
    }

    [Theory]
    [InlineData("491701234567")]
    [InlineData("+0701234567")]
    [InlineData("+1234")]
    [InlineData("+1234567890123456")]
    [InlineData("+4917012345ab")]
    [InlineData("")]
    public void is_valid_registration_phone_number_rejects_malformed_input(string phone_number)
    {
        Assert.False(Signal_cli.is_valid_registration_phone_number(phone_number));
    }

    [Theory]
    [InlineData("Sticker Creator Program")]
    [InlineData("Work laptop")]
    [InlineData("x")]
    public void is_valid_device_name_accepts_any_non_blank_name(string device_name)
    {
        Assert.True(Signal_cli.is_valid_device_name(device_name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(null)]
    public void is_valid_device_name_rejects_blank_input(string? device_name)
    {
        Assert.False(Signal_cli.is_valid_device_name(device_name));
    }

    [Fact]
    public void extract_upload_url_throws_when_the_exit_code_is_non_zero()
    {
        var result = new Signal_cli_result { exit_code = 1, standard_error_lines = new List<string> { "user not registered" } };

        var exception = Assert.Throws<InvalidOperationException>(() => Signal_cli.extract_upload_url(result));
        Assert.Contains("exited with code 1", exception.Message);
        Assert.Contains("user not registered", exception.Message);
    }

    [Fact]
    public void extract_upload_url_returns_the_matching_stdout_line()
    {
        var result = new Signal_cli_result
        {
            exit_code = 0,
            standard_output_lines = new List<string> { "Sticker pack URL: https://signal.art/addstickers/#pack_id=abc&pack_key=def" },
        };

        Assert.Equal("https://signal.art/addstickers/#pack_id=abc&pack_key=def", Signal_cli.extract_upload_url(result));
    }

    [Fact]
    public void extract_upload_url_throws_when_no_output_line_matches()
    {
        var result = new Signal_cli_result { exit_code = 0, standard_output_lines = new List<string> { "done" } };

        Assert.Throws<InvalidOperationException>(() => Signal_cli.extract_upload_url(result));
    }

    [Fact]
    public void ensure_signal_cli_succeeded_throws_when_the_exit_code_is_non_zero()
    {
        var result = new Signal_cli_result { exit_code = 1, standard_error_lines = new List<string> { "user not registered" } };

        var exception = Assert.Throws<InvalidOperationException>(() => Signal_cli.ensure_signal_cli_succeeded(result));
        Assert.Contains("exited with code 1", exception.Message);
        Assert.Contains("user not registered", exception.Message);
    }

    [Fact]
    public void ensure_signal_cli_succeeded_does_nothing_when_the_exit_code_is_zero()
    {
        var result = new Signal_cli_result { exit_code = 0 };

        Signal_cli.ensure_signal_cli_succeeded(result);
    }

    [Fact]
    public void qr_code_data_url_renders_a_PNG_data_URL()
    {
        var data_url = Signal_cli.qr_code_data_url("sgnl://linkdevice?uuid=test&pub_key=test");

        Assert.StartsWith("data:image/png;base64,", data_url);
        var png_bytes = Convert.FromBase64String(data_url["data:image/png;base64,".Length..]);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png_bytes[..4]);
    }

    private void write_accounts_file(string json)
    {
        var data_subdirectory = Path.Combine(data_directory, "data");
        Directory.CreateDirectory(data_subdirectory);
        File.WriteAllText(Path.Combine(data_subdirectory, "accounts.json"), json);
    }
}
