using ImageMagick;
using Sticker_Creator_Program;

namespace Sticker_Creator_Program_Tests;

// Exercises Magick.NET's real in-process API rather than mocking it — the whole point of
// this module is that ImageMagick's actual behavior is the contract, so a mock would test
// nothing meaningful.
public class Image_pipeline_tests : IDisposable
{
    private readonly string art_directory =
        Path.Combine(AppContext.BaseDirectory, "test_scratch", Guid.NewGuid().ToString("N"));

    public Image_pipeline_tests()
    {
        Directory.CreateDirectory(art_directory);
    }

    public void Dispose()
    {
        Directory.Delete(art_directory, recursive: true);
    }

    private void create_fixture_image(string file_name, int width, int height)
    {
        var settings = new MagickReadSettings { Width = (uint)width, Height = (uint)height };
        using var image = new MagickImage("xc:red", settings);
        image.Write(Path.Combine(art_directory, file_name));
    }

    private string WebP_directory_path() => Path.Combine(art_directory, "_WebP");

    [Fact]
    public void check_all_square_passes_when_every_file_is_square()
    {
        create_fixture_image("a.png", 4, 4);
        create_fixture_image("b.png", 8, 8);

        Image_pipeline.check_all_square(art_directory, new List<string> { "a.png", "b.png" });
    }

    [Fact]
    public void check_all_square_reports_non_square_and_unreadable_files()
    {
        create_fixture_image("square.png", 4, 4);
        create_fixture_image("wide.png", 8, 4);
        File.WriteAllText(Path.Combine(art_directory, "corrupt.png"), "not an image");

        var error = Assert.Throws<Squareness_check_error>(() =>
            Image_pipeline.check_all_square(art_directory, new List<string> { "square.png", "wide.png", "corrupt.png" }));

        var wide_problem = Assert.Single(error.problem_files, problem => problem.file == "wide.png");
        Assert.Equal(8, wide_problem.width);
        Assert.Equal(4, wide_problem.height);
        Assert.Null(wide_problem.error);

        var corrupt_problem = Assert.Single(error.problem_files, problem => problem.file == "corrupt.png");
        Assert.NotNull(corrupt_problem.error);
    }

    [Fact]
    public void convert_all_produces_matching_WebP_files_for_a_square_set()
    {
        create_fixture_image("a.png", 4, 4);
        create_fixture_image("b.jpg", 6, 6);

        Image_pipeline.convert_all(art_directory, WebP_directory_path(), new List<string> { "a.png", "b.jpg" }, enable_lossless: true);

        Assert.True(File.Exists(Path.Combine(WebP_directory_path(), "a.webp")));
        Assert.True(File.Exists(Path.Combine(WebP_directory_path(), "b.webp")));
        Assert.True(new FileInfo(Path.Combine(WebP_directory_path(), "a.webp")).Length > 0);
    }

    [Fact]
    public void convert_all_writes_outputs_at_the_required_sticker_size()
    {
        create_fixture_image("a.png", 4, 4);

        Image_pipeline.convert_all(art_directory, WebP_directory_path(), new List<string> { "a.png" }, enable_lossless: true);

        var dimensions = Image_pipeline.probe_dimensions(Path.Combine(WebP_directory_path(), "a.webp"));
        Assert.Equal((Image_pipeline.sticker_side_pixels, Image_pipeline.sticker_side_pixels), dimensions);
    }

    [Fact]
    public void probe_dimensions_reports_size_and_returns_null_for_unreadable_files()
    {
        create_fixture_image("wide.png", 8, 4);
        File.WriteAllText(Path.Combine(art_directory, "corrupt.png"), "not an image");

        Assert.Equal((8, 4), Image_pipeline.probe_dimensions(Path.Combine(art_directory, "wide.png")));
        Assert.Null(Image_pipeline.probe_dimensions(Path.Combine(art_directory, "corrupt.png")));
    }

    [Fact]
    public void probe_dimensions_returns_null_for_a_nonexistent_path()
    {
        var result = Image_pipeline.probe_dimensions(Path.Combine(art_directory, "does_not_exist.png"));

        Assert.Null(result);
    }

    [Fact]
    public void verify_output_dimensions_flags_an_output_that_is_not_the_required_size()
    {
        Directory.CreateDirectory(WebP_directory_path());
        var settings = new MagickReadSettings { Width = 4, Height = 4 };
        using (var image = new MagickImage("xc:red", settings))
        {
            image.Format = MagickFormat.WebP;
            image.Write(Path.Combine(WebP_directory_path(), "a.webp"));
        }

        var error = Assert.Throws<Output_dimension_error>(() =>
            Image_pipeline.verify_output_dimensions(WebP_directory_path(), new List<string> { "a.png" }));

        var problem = Assert.Single(error.problem_files);
        Assert.Equal("a.webp", problem.file);
        Assert.Equal(4, problem.width);
        Assert.Equal(4, problem.height);
    }

    [Fact]
    public void convert_all_converts_nothing_when_any_file_is_non_square()
    {
        create_fixture_image("square.png", 4, 4);
        create_fixture_image("wide.png", 8, 4);

        Assert.Throws<Squareness_check_error>(() =>
            Image_pipeline.convert_all(art_directory, WebP_directory_path(), new List<string> { "square.png", "wide.png" }, enable_lossless: true));

        Assert.False(Directory.Exists(WebP_directory_path()));
    }

    [Fact]
    public void convert_all_keeps_a_simple_image_lossless_when_lossless_is_enabled()
    {
        create_fixture_image("solid.png", 8, 8);

        var results = Image_pipeline.convert_all(art_directory, WebP_directory_path(), new List<string> { "solid.png" }, enable_lossless: true);

        var result = Assert.Single(results);
        Assert.Equal(Conversion_fit.lossless, result.fit);
        Assert.False(result.is_trophy);
        Assert.True(File.Exists(Path.Combine(WebP_directory_path(), "solid.webp")));
    }

    [Fact]
    public void convert_all_skips_lossless_when_disabled()
    {
        create_fixture_image("solid.png", 8, 8);

        var results = Image_pipeline.convert_all(art_directory, WebP_directory_path(), new List<string> { "solid.png" }, enable_lossless: false);

        var result = Assert.Single(results);
        Assert.Equal(Conversion_fit.lossy, result.fit);
        Assert.Equal(100, result.quality);
        Assert.False(result.is_trophy);
        Assert.True(File.Exists(Path.Combine(WebP_directory_path(), "solid.webp")));
    }

    [Fact]
    public void convert_all_never_awards_the_trophy_on_a_lossless_fit_even_for_the_trigger_stem()
    {
        var file_name = $"{Image_pipeline.trophy_trigger_stem}.png";
        create_fixture_image(file_name, 8, 8);

        var results = Image_pipeline.convert_all(art_directory, WebP_directory_path(), new List<string> { file_name }, enable_lossless: true);

        var result = Assert.Single(results);
        Assert.Equal(Conversion_fit.lossless, result.fit);
        Assert.Null(result.quality);
        Assert.False(result.is_trophy);
    }
}
