using Sticker_Creator_Program;

namespace Sticker_Creator_Program_Tests;

public class Packs_directory_check_tests : IDisposable
{
    private readonly string candidate_directory =
        Path.Combine(AppContext.BaseDirectory, "test_scratch", Guid.NewGuid().ToString("N"));

    public Packs_directory_check_tests()
    {
        Directory.CreateDirectory(candidate_directory);
    }

    public void Dispose()
    {
        Directory.Delete(candidate_directory, recursive: true);
    }

    private void create_image_file(string relative_path)
    {
        var full_path = Path.Combine(candidate_directory, relative_path);
        Directory.CreateDirectory(Path.GetDirectoryName(full_path)!);
        File.WriteAllText(full_path, "");
    }

    private void create_subfolder(string name)
    {
        Directory.CreateDirectory(Path.Combine(candidate_directory, name));
    }

    [Fact]
    public void empty_folder_is_acceptable()
    {
        var result = Packs_directory_check.check(candidate_directory);

        Assert.True(result.exists);
        Assert.True(result.is_acceptable);
        Assert.False(result.is_blocked);
        Assert.False(result.has_images_directly);
        Assert.Equal(0, result.subfolder_count);
        Assert.Equal(0, result.subfolders_with_images_count);
    }

    [Fact]
    public void folder_with_images_directly_is_blocked()
    {
        create_image_file("a.png");

        var result = Packs_directory_check.check(candidate_directory);

        Assert.True(result.has_images_directly);
        Assert.True(result.is_blocked);
        Assert.False(result.is_acceptable);
    }

    [Fact]
    public void folder_with_subfolder_containing_images_is_acceptable_and_counted()
    {
        create_image_file(Path.Combine("Turtles", "a.png"));

        var result = Packs_directory_check.check(candidate_directory);

        Assert.True(result.is_acceptable);
        Assert.False(result.has_images_directly);
        Assert.Equal(1, result.subfolder_count);
        Assert.Equal(1, result.subfolders_with_images_count);
    }

    [Fact]
    public void folder_with_empty_subfolder_is_acceptable_with_no_image_subfolders()
    {
        create_subfolder("Empty_pack");

        var result = Packs_directory_check.check(candidate_directory);

        Assert.True(result.is_acceptable);
        Assert.Equal(1, result.subfolder_count);
        Assert.Equal(0, result.subfolders_with_images_count);
    }

    [Fact]
    public void folder_with_images_and_subfolders_is_blocked()
    {
        create_image_file("a.png");
        create_image_file(Path.Combine("Turtles", "b.png"));

        var result = Packs_directory_check.check(candidate_directory);

        Assert.True(result.is_blocked);
        Assert.False(result.is_acceptable);
    }

    [Fact]
    public void missing_folder_reports_not_exists()
    {
        var missing_directory = Path.Combine(candidate_directory, "does_not_exist");

        var result = Packs_directory_check.check(missing_directory);

        Assert.False(result.exists);
        Assert.False(result.is_acceptable);
        Assert.False(result.is_blocked);
    }
}
