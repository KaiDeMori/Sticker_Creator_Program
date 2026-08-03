using Sticker_Creator_Program;

namespace Sticker_Creator_Program_Tests;

// Each test gets its own fresh instance (xUnit's default), so the per-instance
// pack_directory is naturally isolated between tests without a shared fixture.
public class Pack_store_tests : IDisposable
{
    private readonly string pack_directory =
        Path.Combine(AppContext.BaseDirectory, "test_scratch", Guid.NewGuid().ToString("N"));

    public Pack_store_tests()
    {
        Directory.CreateDirectory(pack_directory);
    }

    public void Dispose()
    {
        Directory.Delete(pack_directory, recursive: true);
    }

    private void create_art_file(string file_name)
    {
        File.WriteAllText(Path.Combine(pack_directory, file_name), "");
    }

    private string pack_info_directory => Pack_store.pack_info_directory_path(pack_directory);

    [Fact]
    public void reconcile_stickers_preserves_order_keeps_missing_appends_new_sorted()
    {
        var existing = new List<Sticker_entry>
        {
            new() { emoji = "🙂", file = "b.png" },
            new() { emoji = "", file = "a.png" },
            new() { emoji = "🎉", file = "deleted.png" },
        };
        var art_filenames = new List<string> { "a.png", "b.png", "c.png" };

        var kept = Pack_store.reconcile_stickers(existing, art_filenames);

        Assert.Equal(new[] { "b.png", "a.png", "deleted.png", "c.png" }, kept.Select(sticker => sticker.file));
        Assert.Equal("🙂", kept[0].emoji);
        Assert.Equal("", kept[1].emoji);
        Assert.Equal("🎉", kept[2].emoji);
        Assert.Equal("", kept[3].emoji);
    }

    [Fact]
    public void reconcile_stickers_skips_entries_with_empty_file()
    {
        var existing = new List<Sticker_entry> { new() { emoji = "🙂", file = "" } };

        var kept = Pack_store.reconcile_stickers(existing, new List<string>());

        Assert.Empty(kept);
    }

    [Fact]
    public void load_pack_state_keeps_stickers_with_missing_source_files_and_counts_correctly()
    {
        create_art_file("a.png");
        create_art_file("b.png");
        Directory.CreateDirectory(Path.Combine(pack_info_directory, Pack_store.WebP_directory_name));
        File.WriteAllText(Path.Combine(pack_info_directory, Pack_store.WebP_directory_name, "a.webp"), "");
        File.WriteAllText(Path.Combine(pack_info_directory, Pack_store.stickers_yaml_file_name), """
            meta:
              title: Test pack
              author: Someone
              cover: ""
            stickers:
              - emoji: "🙂"
                file: a.png
              - emoji: "🎉"
                file: stale.png
            """);

        var state = Pack_store.load_pack_state(pack_directory);

        Assert.Equal(new[] { "a.png", "stale.png", "b.png" }, state.stickers.Select(sticker => sticker.file));
        Assert.Equal(3, state.original_count);
        Assert.Equal(1, state.converted_count);
        Assert.Equal(2, state.mapped_count);
    }

    [Fact]
    public void write_then_load_pack_state_preserves_a_missing_source_sticker()
    {
        create_art_file("a.png");
        Pack_store.write_pack_state(pack_directory, new Pack_save_payload
        {
            meta = new Pack_meta { title = "T", author = "A", cover = "" },
            stickers =
            {
                new() { emoji = "🙂", file = "a.png" },
                new() { emoji = "🎉", file = "gone.png" },
            },
        });

        var state = Pack_store.load_pack_state(pack_directory);

        Assert.Equal(new[] { "a.png", "gone.png" }, state.stickers.Select(sticker => sticker.file));
        Assert.Equal("🎉", state.stickers.Single(sticker => sticker.file == "gone.png").emoji);
    }

    [Fact]
    public void remove_sticker_artifacts_deletes_webp_and_conversion_info_entry()
    {
        Directory.CreateDirectory(Path.Combine(pack_info_directory, Pack_store.WebP_directory_name));
        var webp_path = Path.Combine(pack_info_directory, Pack_store.WebP_directory_name, "a.webp");
        File.WriteAllText(webp_path, "");
        Pack_store.write_conversion_info(pack_directory, new Dictionary<string, Conversion_info_entry>
        {
            ["a"] = new() { fit = "lossy", quality = 90 },
        });

        Pack_store.remove_sticker_artifacts(pack_directory, "a.png", new List<Sticker_entry>());

        Assert.False(File.Exists(webp_path));
        Assert.Empty(Pack_store.load_conversion_info(pack_directory));
    }

    [Fact]
    public void remove_sticker_artifacts_is_a_no_op_and_creates_nothing_when_neither_artifact_exists()
    {
        var conversion_info_path = Path.Combine(pack_info_directory, Pack_store.conversion_info_file_name);

        Pack_store.remove_sticker_artifacts(pack_directory, "a.png", new List<Sticker_entry>());

        Assert.False(File.Exists(conversion_info_path));
    }

    [Fact]
    public void remove_sticker_artifacts_leaves_other_entries_intact()
    {
        Pack_store.write_conversion_info(pack_directory, new Dictionary<string, Conversion_info_entry>
        {
            ["a"] = new() { fit = "lossy", quality = 90 },
            ["b"] = new() { fit = "lossless", quality = null },
        });

        Pack_store.remove_sticker_artifacts(pack_directory, "a.png", new List<Sticker_entry>());

        var remaining = Pack_store.load_conversion_info(pack_directory);
        Assert.False(remaining.ContainsKey("a"));
        Assert.True(remaining.ContainsKey("b"));
    }

    [Fact]
    public void remove_sticker_artifacts_skips_deletion_when_another_sticker_shares_the_stem()
    {
        Directory.CreateDirectory(Path.Combine(pack_info_directory, Pack_store.WebP_directory_name));
        var webp_path = Path.Combine(pack_info_directory, Pack_store.WebP_directory_name, "logo.webp");
        File.WriteAllText(webp_path, "");
        Pack_store.write_conversion_info(pack_directory, new Dictionary<string, Conversion_info_entry>
        {
            ["logo"] = new() { fit = "lossy", quality = 90 },
        });
        var remaining_stickers = new List<Sticker_entry> { new() { file = "logo.png", emoji = "🙂" } };

        Pack_store.remove_sticker_artifacts(pack_directory, "logo.gif", remaining_stickers);

        Assert.True(File.Exists(webp_path));
        Assert.True(Pack_store.load_conversion_info(pack_directory).ContainsKey("logo"));
    }

    [Fact]
    public void write_pack_state_round_trips_on_first_save_and_on_overwrite_without_persisting_converted()
    {
        create_art_file("a.png");
        var yaml_path = Path.Combine(pack_info_directory, Pack_store.stickers_yaml_file_name);

        Pack_store.write_pack_state(pack_directory, new Pack_save_payload
        {
            meta = new Pack_meta { title = "First", author = "A", cover = "" },
            stickers = { new() { emoji = "🙂", file = "a.png" } },
        });

        Assert.True(File.Exists(yaml_path));
        var first_state = Pack_store.load_pack_state(pack_directory);
        Assert.Equal("First", first_state.meta.title);
        Assert.DoesNotContain("converted", File.ReadAllText(yaml_path));

        // Second save exercises the File.Replace branch (destination already exists),
        // distinct from the File.Move branch the first save just exercised.
        Pack_store.write_pack_state(pack_directory, new Pack_save_payload
        {
            meta = new Pack_meta { title = "Second", author = "A", cover = "" },
            stickers = { new() { emoji = "🙂", file = "a.png" } },
        });

        var second_state = Pack_store.load_pack_state(pack_directory);
        Assert.Equal("Second", second_state.meta.title);
    }

    [Fact]
    public void write_pack_state_stores_emoji_as_literal_characters()
    {
        var yaml_path = Path.Combine(pack_info_directory, Pack_store.stickers_yaml_file_name);

        Pack_store.write_pack_state(pack_directory, new Pack_save_payload
        {
            meta = new Pack_meta { title = "T", author = "A", cover = "" },
            stickers =
            {
                new() { emoji = "🐢", file = "turtle.png" },
                new() { emoji = "⭐", file = "star.png" },
            },
        });

        var yaml = File.ReadAllText(yaml_path);
        Assert.Contains("🐢", yaml);
        Assert.Contains("⭐", yaml);
        Assert.DoesNotContain("\\U", yaml);
    }

    [Fact]
    public void load_pack_state_reads_literal_and_escaped_emoji_alike()
    {
        create_art_file("literal.png");
        create_art_file("escaped.png");
        Directory.CreateDirectory(pack_info_directory);
        File.WriteAllText(Path.Combine(pack_info_directory, Pack_store.stickers_yaml_file_name), """
            meta:
              title: T
              author: A
              cover: ""
            stickers:
              - emoji: 🐢
                file: literal.png
              - emoji: "\U0001F422"
                file: escaped.png
            """);

        var state = Pack_store.load_pack_state(pack_directory);

        Assert.All(state.stickers, sticker => Assert.Equal("🐢", sticker.emoji));
    }

    [Fact]
    public void write_pack_state_round_trips_a_value_that_carries_a_backslash()
    {
        var title_shaped_like_an_escape_sequence = @"C:\U0001F422";

        Pack_store.write_pack_state(pack_directory, new Pack_save_payload
        {
            meta = new Pack_meta { title = title_shaped_like_an_escape_sequence, author = "A", cover = "" },
            stickers = { new() { emoji = "🐢", file = "turtle.png" } },
        });

        var state = Pack_store.load_pack_state(pack_directory);

        Assert.Equal(title_shaped_like_an_escape_sequence, state.meta.title);
        Assert.Equal("🐢", state.stickers.Single().emoji);
    }

    [Fact]
    public void WebP_file_path_swaps_directory_and_extension()
    {
        var result = Pack_store.WebP_file_path(pack_directory, "a.png");

        Assert.Equal(Path.Combine(Pack_store.WebP_directory_path(pack_directory), "a.webp"), result);
    }

    [Fact]
    public void file_url_round_trips_a_path_containing_a_space()
    {
        var path_with_space = Path.Combine(pack_directory, "New folder", "a b.png");

        var url = Pack_store.file_url(path_with_space);

        Assert.StartsWith("file:///", url);
        Assert.Equal(path_with_space, new Uri(url).LocalPath);
    }

    [Fact]
    public void sticker_url_points_at_original_file_when_not_converted()
    {
        create_art_file("a.png");
        var sticker = new Sticker_entry { emoji = "", file = "a.png", converted = false };

        var url = Pack_store.sticker_url(pack_directory, sticker);

        Assert.Equal(Path.Combine(pack_directory, "a.png"), new Uri(url).LocalPath);
    }

    [Fact]
    public void sticker_url_points_at_WebP_file_when_converted()
    {
        create_art_file("a.png");
        var sticker = new Sticker_entry { emoji = "", file = "a.png", converted = true };

        var url = Pack_store.sticker_url(pack_directory, sticker);

        Assert.Equal(Pack_store.WebP_file_path(pack_directory, "a.png"), new Uri(url).LocalPath);
    }

    [Fact]
    public void ensure_pack_initialized_creates_yaml_with_pack_name_as_title_when_missing()
    {
        create_art_file("a.png");
        create_art_file("b.png");
        var yaml_path = Path.Combine(pack_info_directory, Pack_store.stickers_yaml_file_name);

        Pack_store.ensure_pack_initialized(pack_directory, "My Pack");

        Assert.True(File.Exists(yaml_path));
        var state = Pack_store.load_pack_state(pack_directory);
        Assert.Equal("My Pack", state.meta.title);
        Assert.Equal(new[] { "a.png", "b.png" }, state.stickers.Select(sticker => sticker.file));
    }

    [Fact]
    public void ensure_pack_initialized_does_not_overwrite_an_existing_yaml()
    {
        Pack_store.write_pack_state(pack_directory, new Pack_save_payload
        {
            meta = new Pack_meta { title = "Already Named" },
            stickers = new List<Sticker_entry>(),
        });

        Pack_store.ensure_pack_initialized(pack_directory, "Folder Name");

        var state = Pack_store.load_pack_state(pack_directory);
        Assert.Equal("Already Named", state.meta.title);
    }

    [Fact]
    public void load_pack_state_attaches_persisted_conversion_info_to_converted_stickers_only()
    {
        create_art_file("a.png");
        create_art_file("b.png");
        Directory.CreateDirectory(Path.Combine(pack_info_directory, Pack_store.WebP_directory_name));
        File.WriteAllText(Path.Combine(pack_info_directory, Pack_store.WebP_directory_name, "a.webp"), "");
        Pack_store.write_conversion_info(pack_directory, new Dictionary<string, Conversion_info_entry>
        {
            ["a"] = new() { fit = "lossy", quality = 82 },
            ["b"] = new() { fit = "lossless", quality = null },
        });

        var state = Pack_store.load_pack_state(pack_directory);

        var a = state.stickers.Single(sticker => sticker.file == "a.png");
        Assert.True(a.converted);
        Assert.Equal("lossy", a.fit);
        Assert.Equal(82, a.quality);

        // b has no matching .webp on disk, so it reads as not converted even though
        // conversion_info.json still has a stale "b" entry from a prior run.
        var b = state.stickers.Single(sticker => sticker.file == "b.png");
        Assert.False(b.converted);
        Assert.Null(b.fit);
        Assert.Null(b.quality);
    }

    [Fact]
    public void append_signal_art_url_keeps_every_prior_url_in_publish_order()
    {
        var path = Path.Combine(pack_info_directory, Pack_store.signal_art_url_file_name);

        Pack_store.append_signal_art_url(pack_directory, "https://signal.art/addstickers/#pack_id=first&pack_key=key1");
        Pack_store.append_signal_art_url(pack_directory, "https://signal.art/addstickers/#pack_id=second&pack_key=key2");

        Assert.Equal(
            new[]
            {
                "https://signal.art/addstickers/#pack_id=first&pack_key=key1",
                "https://signal.art/addstickers/#pack_id=second&pack_key=key2",
            },
            File.ReadAllLines(path));
    }

    [Fact]
    public void load_conversion_info_returns_empty_when_file_is_missing()
    {
        var info = Pack_store.load_conversion_info(pack_directory);

        Assert.Empty(info);
    }

    [Fact]
    public void write_conversion_info_round_trips_on_first_write_and_on_overwrite()
    {
        Pack_store.write_conversion_info(pack_directory, new Dictionary<string, Conversion_info_entry>
        {
            ["a"] = new() { fit = "lossy", quality = 90 },
        });
        var first = Pack_store.load_conversion_info(pack_directory);
        Assert.Equal("lossy", first["a"].fit);
        Assert.Equal(90, first["a"].quality);

        // Second write exercises the File.Replace branch (destination already exists) and
        // confirms a fresh convert_all pass fully replaces the prior file's contents.
        Pack_store.write_conversion_info(pack_directory, new Dictionary<string, Conversion_info_entry>
        {
            ["a"] = new() { fit = "lossless", quality = null },
        });
        var second = Pack_store.load_conversion_info(pack_directory);
        Assert.Equal("lossless", second["a"].fit);
        Assert.Null(second["a"].quality);
    }
}
