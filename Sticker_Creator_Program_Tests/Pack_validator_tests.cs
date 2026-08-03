using ImageMagick;
using Sticker_Creator_Program;

namespace Sticker_Creator_Program_Tests;

public class Pack_validator_tests : IDisposable
{
    private readonly string pack_directory =
        Path.Combine(AppContext.BaseDirectory, "test_scratch", Guid.NewGuid().ToString("N"));

    public Pack_validator_tests()
    {
        Directory.CreateDirectory(pack_directory);
    }

    public void Dispose()
    {
        Directory.Delete(pack_directory, recursive: true);
    }

    private string webp_directory => Pack_store.WebP_directory_path(pack_directory);

    private void create_fixture_image(string file_name, int width, int height)
    {
        var settings = new MagickReadSettings { Width = (uint)width, Height = (uint)height };
        using var image = new MagickImage("xc:red", settings);
        image.Write(Path.Combine(pack_directory, file_name));
    }

    // Writing through a MemoryStream, rather than a path, guarantees the Format property
    // decides the encoded bytes — letting a stem's ".webp" file hold non-WebP content
    // (oversized_webp_reports_byte_size_condition_only relies on this to force a large file
    // without depending on WebP's own compression ratio).
    private void create_webp_fixture(string stem, int width, int height, MagickFormat format = MagickFormat.WebP)
    {
        Directory.CreateDirectory(webp_directory);
        var settings = new MagickReadSettings { Width = (uint)width, Height = (uint)height };
        using var image = new MagickImage("xc:red", settings);
        image.Format = format;
        using var stream = new MemoryStream();
        image.Write(stream);
        File.WriteAllBytes(Path.Combine(webp_directory, $"{stem}.webp"), stream.ToArray());
    }

    private static bool has_error(List<Validity_error> errors, string condition, string? file = null) =>
        errors.Any(error => error.condition == condition && error.file == file);

    private Pack_state build_valid_state()
    {
        create_fixture_image("a.png", 4, 4);
        create_webp_fixture("a", Image_pipeline.sticker_side_pixels, Image_pipeline.sticker_side_pixels);

        return new Pack_state
        {
            meta = new Pack_meta { title = "Title", author = "Author", cover = "a.png" },
            stickers = new List<Sticker_entry>
            {
                new() { file = "a.png", emoji = "🙂", converted = true },
            },
        };
    }

    [Fact]
    public void fully_valid_pack_produces_empty_error_list()
    {
        var state = build_valid_state();

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.Empty(errors);
    }

    [Fact]
    public void empty_title_reports_condition()
    {
        var state = build_valid_state();
        state.meta.title = "";

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_title_not_empty));
    }

    [Fact]
    public void empty_author_reports_condition()
    {
        var state = build_valid_state();
        state.meta.author = "";

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_author_not_empty));
    }

    [Fact]
    public void zero_stickers_reports_minimum_count_condition()
    {
        var state = build_valid_state();
        state.stickers.Clear();

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_minimum_sticker_count));
    }

    [Fact]
    public void more_than_max_stickers_reports_maximum_count_condition()
    {
        var state = new Pack_state
        {
            meta = new Pack_meta { title = "Title", author = "Author", cover = "" },
            stickers = Enumerable.Range(0, Pack_validator.max_sticker_count + 1)
                .Select(index => new Sticker_entry { file = $"s{index}.png", emoji = "🙂" })
                .ToList(),
        };

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_maximum_sticker_count));
    }

    [Fact]
    public void empty_cover_reports_both_cover_conditions()
    {
        var state = build_valid_state();
        state.meta.cover = "";

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_cover_not_empty));
        Assert.True(has_error(errors, Pack_validator.condition_cover_names_present_sticker));
    }

    [Fact]
    public void cover_naming_absent_sticker_reports_condition_when_not_empty()
    {
        var state = build_valid_state();
        state.meta.cover = "missing.png";

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.False(has_error(errors, Pack_validator.condition_cover_not_empty));
        Assert.True(has_error(errors, Pack_validator.condition_cover_names_present_sticker));
    }

    [Fact]
    public void missing_source_file_reports_file_exists_and_dimensions_but_not_square()
    {
        var state = build_valid_state();
        state.stickers.Add(new Sticker_entry { file = "missing.png", emoji = "", converted = false });

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_sticker_file_exists, "missing.png"));
        Assert.True(has_error(errors, Pack_validator.condition_sticker_dimensions_readable, "missing.png"));
        Assert.False(has_error(errors, Pack_validator.condition_sticker_is_square, "missing.png"));
        Assert.True(has_error(errors, Pack_validator.condition_sticker_emoji_not_empty, "missing.png"));
    }

    [Fact]
    public void present_but_unreadable_source_file_reports_dimensions_condition_only()
    {
        var state = build_valid_state();
        File.WriteAllText(Path.Combine(pack_directory, "corrupt.png"), "not an image");
        state.stickers[0] = new Sticker_entry { file = "corrupt.png", emoji = "🙂", converted = false };
        state.meta.cover = "corrupt.png";

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.False(has_error(errors, Pack_validator.condition_sticker_file_exists, "corrupt.png"));
        Assert.True(has_error(errors, Pack_validator.condition_sticker_dimensions_readable, "corrupt.png"));
        Assert.False(has_error(errors, Pack_validator.condition_sticker_is_square, "corrupt.png"));
    }

    [Fact]
    public void non_square_source_file_reports_square_condition_only()
    {
        var state = build_valid_state();
        create_fixture_image("wide.png", 8, 4);
        state.stickers[0] = new Sticker_entry { file = "wide.png", emoji = "🙂", converted = false };
        state.meta.cover = "wide.png";

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.False(has_error(errors, Pack_validator.condition_sticker_file_exists, "wide.png"));
        Assert.False(has_error(errors, Pack_validator.condition_sticker_dimensions_readable, "wide.png"));
        Assert.True(has_error(errors, Pack_validator.condition_sticker_is_square, "wide.png"));
    }

    [Fact]
    public void unconverted_sticker_reports_webp_exists_condition_only()
    {
        var state = build_valid_state();
        state.stickers[0].converted = false;

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_sticker_webp_exists, "a.png"));
        Assert.False(has_error(errors, Pack_validator.condition_sticker_webp_is_512, "a.png"));
        Assert.False(has_error(errors, Pack_validator.condition_sticker_webp_size_limit, "a.png"));
    }

    [Fact]
    public void wrong_dimension_webp_reports_size_condition_only()
    {
        create_fixture_image("b.png", 4, 4);
        create_webp_fixture("b", 4, 4);
        var state = new Pack_state
        {
            meta = new Pack_meta { title = "Title", author = "Author", cover = "b.png" },
            stickers = new List<Sticker_entry> { new() { file = "b.png", emoji = "🙂", converted = true } },
        };

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_sticker_webp_is_512, "b.png"));
        Assert.False(has_error(errors, Pack_validator.condition_sticker_webp_size_limit, "b.png"));
    }

    [Fact]
    public void oversized_webp_reports_byte_size_condition_only()
    {
        create_fixture_image("c.png", 4, 4);
        create_webp_fixture("c", Image_pipeline.sticker_side_pixels, Image_pipeline.sticker_side_pixels, MagickFormat.Bmp);
        var state = new Pack_state
        {
            meta = new Pack_meta { title = "Title", author = "Author", cover = "c.png" },
            stickers = new List<Sticker_entry> { new() { file = "c.png", emoji = "🙂", converted = true } },
        };
        Assert.True(new FileInfo(Path.Combine(webp_directory, "c.webp")).Length > Image_pipeline.target_byte_size);

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.False(has_error(errors, Pack_validator.condition_sticker_webp_is_512, "c.png"));
        Assert.True(has_error(errors, Pack_validator.condition_sticker_webp_size_limit, "c.png"));
    }

    [Fact]
    public void load_pack_state_then_compute_error_list_reports_missing_source_conditions()
    {
        Pack_store.write_pack_state(pack_directory, new Pack_save_payload
        {
            meta = new Pack_meta { title = "Title", author = "Author", cover = "" },
            stickers = { new() { emoji = "🙂", file = "gone.png" } },
        });

        var state = Pack_store.load_pack_state(pack_directory);
        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_sticker_file_exists, "gone.png"));
        Assert.True(has_error(errors, Pack_validator.condition_sticker_dimensions_readable, "gone.png"));
    }

    [Fact]
    public void empty_emoji_reports_condition()
    {
        var state = build_valid_state();
        create_fixture_image("b.png", 4, 4);
        create_webp_fixture("b", Image_pipeline.sticker_side_pixels, Image_pipeline.sticker_side_pixels);
        state.stickers.Add(new Sticker_entry { file = "b.png", emoji = "", converted = true });

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.True(has_error(errors, Pack_validator.condition_sticker_emoji_not_empty, "b.png"));
    }

    [Fact]
    public void cover_sticker_with_empty_emoji_is_exempt()
    {
        var state = build_valid_state();
        state.stickers[0].emoji = "";
        create_fixture_image("b.png", 4, 4);
        create_webp_fixture("b", Image_pipeline.sticker_side_pixels, Image_pipeline.sticker_side_pixels);
        state.stickers.Add(new Sticker_entry { file = "b.png", emoji = "", converted = true });

        var errors = Pack_validator.compute_error_list(pack_directory, state);

        Assert.False(has_error(errors, Pack_validator.condition_sticker_emoji_not_empty, "a.png"));
        Assert.True(has_error(errors, Pack_validator.condition_sticker_emoji_not_empty, "b.png"));
    }
}
