using System.Text.Json;
using Sticker_Creator_Program;

namespace Sticker_Creator_Program_Tests;

public class Signal_manifest_tests : IDisposable
{
    private readonly string pack_directory =
        Path.Combine(AppContext.BaseDirectory, "test_scratch", Guid.NewGuid().ToString("N"));

    public Signal_manifest_tests()
    {
        Directory.CreateDirectory(pack_directory);
    }

    public void Dispose()
    {
        Directory.Delete(pack_directory, recursive: true);
    }

    private Pack_meta build_meta() => new() { title = "Turtles", author = "A. Turtle", cover = "a.png" };

    private List<Sticker_entry> build_stickers() => new()
    {
        new() { file = "a.png", emoji = "🐢" },
        new() { file = "b.png", emoji = "🙂" },
    };

    [Fact]
    public void build_maps_title_and_author_from_meta()
    {
        var document = Signal_manifest.build(pack_directory, build_meta(), build_stickers());

        Assert.Equal("Turtles", document.title);
        Assert.Equal("A. Turtle", document.author);
    }

    [Fact]
    public void build_cover_matches_the_sticker_named_by_meta_cover()
    {
        var document = Signal_manifest.build(pack_directory, build_meta(), build_stickers());

        Assert.Equal("_WebP/a.webp", document.cover.file);
        Assert.Equal("🐢", document.cover.emoji);
    }

    [Fact]
    public void build_stickers_has_one_entry_per_sticker_including_the_cover()
    {
        var document = Signal_manifest.build(pack_directory, build_meta(), build_stickers());

        Assert.Equal(2, document.stickers.Count);
        Assert.Contains(document.stickers, sticker => sticker.file == "_WebP/a.webp" && sticker.emoji == "🐢");
        Assert.Contains(document.stickers, sticker => sticker.file == "_WebP/b.webp" && sticker.emoji == "🙂");
    }

    [Fact]
    public void build_sets_content_type_to_image_webp_on_cover_and_every_sticker()
    {
        var document = Signal_manifest.build(pack_directory, build_meta(), build_stickers());

        Assert.Equal("image/webp", document.cover.content_type);
        Assert.All(document.stickers, sticker => Assert.Equal("image/webp", sticker.content_type));
    }

    [Fact]
    public void manifest_file_path_points_at_pack_info_manifest_json()
    {
        Assert.Equal(
            Path.Combine(pack_directory, "pack_info", "manifest.json"),
            Signal_manifest.manifest_file_path(pack_directory));
    }

    [Fact]
    public void write_produces_JSON_using_signal_cli_s_exact_external_keys()
    {
        var document = Signal_manifest.build(pack_directory, build_meta(), build_stickers());

        Signal_manifest.write(pack_directory, document);

        using var written = JsonDocument.Parse(File.ReadAllText(Signal_manifest.manifest_file_path(pack_directory)));
        var root = written.RootElement;
        Assert.Equal("Turtles", root.GetProperty("title").GetString());
        Assert.Equal("A. Turtle", root.GetProperty("author").GetString());

        var cover = root.GetProperty("cover");
        Assert.Equal("_WebP/a.webp", cover.GetProperty("file").GetString());
        Assert.Equal("image/webp", cover.GetProperty("contentType").GetString());
        Assert.Equal("🐢", cover.GetProperty("emoji").GetString());

        var stickers = root.GetProperty("stickers");
        Assert.Equal(2, stickers.GetArrayLength());
        Assert.Equal("image/webp", stickers[0].GetProperty("contentType").GetString());
    }

    [Fact]
    public void write_overwrites_on_a_second_call()
    {
        var first_document = Signal_manifest.build(pack_directory, build_meta(), build_stickers());
        Signal_manifest.write(pack_directory, first_document);

        var second_meta = new Pack_meta { title = "Renamed", author = "A. Turtle", cover = "a.png" };
        var second_document = Signal_manifest.build(pack_directory, second_meta, build_stickers());
        Signal_manifest.write(pack_directory, second_document);

        using var written = JsonDocument.Parse(File.ReadAllText(Signal_manifest.manifest_file_path(pack_directory)));
        Assert.Equal("Renamed", written.RootElement.GetProperty("title").GetString());
    }
}
