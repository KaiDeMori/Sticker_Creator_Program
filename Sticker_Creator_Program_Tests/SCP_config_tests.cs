using System.Text.Json;
using Sticker_Creator_Program;

namespace Sticker_Creator_Program_Tests;

// SCP_config keeps its state in static fields and always resolves its file next to the
// test binary, so every test resets that shared state itself instead of relying on a
// fresh instance per test.
public class SCP_config_tests : IDisposable
{
    private static readonly string config_path = Path.Combine(AppContext.BaseDirectory, "SCP_config.json");

    public SCP_config_tests()
    {
        delete_config_file_if_present();
        SCP_config.Active = new SCP_config();
    }

    public void Dispose()
    {
        delete_config_file_if_present();
        SCP_config.Active = new SCP_config();
    }

    private static void delete_config_file_if_present()
    {
        if (File.Exists(config_path)) File.Delete(config_path);
    }

    [Fact]
    public void load_creates_default_config_file_when_none_exists()
    {
        SCP_config.load();

        Assert.True(File.Exists(config_path));
        Assert.Equal("", SCP_config.Active.packs_directory);
        Assert.False(SCP_config.Active.enable_lossless_compression);
    }

    [Fact]
    public void save_then_load_round_trips_property_values()
    {
        SCP_config.Active = new SCP_config
        {
            packs_directory = @"C:\some\packs",
            enable_lossless_compression = true,
            picker_zoom = 2.5,
        };
        SCP_config.save();

        SCP_config.Active = new SCP_config();
        SCP_config.load();

        Assert.Equal(@"C:\some\packs", SCP_config.Active.packs_directory);
        Assert.True(SCP_config.Active.enable_lossless_compression);
        Assert.Equal(2.5, SCP_config.Active.picker_zoom);
    }

    [Fact]
    public void load_falls_back_to_defaults_when_file_contains_invalid_JSON()
    {
        File.WriteAllText(config_path, "{ not valid JSON");

        SCP_config.load();

        Assert.Equal("", SCP_config.Active.packs_directory);
        Assert.False(SCP_config.Active.enable_lossless_compression);
    }

    [Fact]
    public void load_falls_back_to_defaults_when_file_contains_JSON_null()
    {
        File.WriteAllText(config_path, "null");

        SCP_config.load();

        Assert.NotNull(SCP_config.Active);
        Assert.Equal("", SCP_config.Active.packs_directory);
    }

    [Fact]
    public void load_defaults_missing_properties_when_JSON_predates_them()
    {
        File.WriteAllText(config_path, """{ "packs_directory": "Legacy" }""");

        SCP_config.load();

        Assert.Equal("Legacy", SCP_config.Active.packs_directory);
        Assert.False(SCP_config.Active.enable_lossless_compression);
        Assert.Equal(1.0, SCP_config.Active.picker_zoom);
    }

    [Fact]
    public void load_reads_from_disk_without_rewriting_an_existing_file()
    {
        File.WriteAllText(config_path, JsonSerializer.Serialize(new SCP_config { packs_directory = "OnDisk" }));
        var content_before_load = File.ReadAllText(config_path);

        SCP_config.load();

        Assert.Equal("OnDisk", SCP_config.Active.packs_directory);
        Assert.Equal(content_before_load, File.ReadAllText(config_path));
    }

    [Fact]
    public void save_writes_indented_JSON_reflecting_current_Active_state()
    {
        SCP_config.Active = new SCP_config { packs_directory = "Turtles", enable_lossless_compression = true };

        SCP_config.save();

        var written_json = File.ReadAllText(config_path);
        Assert.Contains("\n", written_json);
        using var document = JsonDocument.Parse(written_json);
        Assert.Equal("Turtles", document.RootElement.GetProperty("packs_directory").GetString());
        Assert.True(document.RootElement.GetProperty("enable_lossless_compression").GetBoolean());
    }
}
