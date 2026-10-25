using LegoFinder.Cli;

namespace LegoFinder.Cli.Tests;

public class DotEnvTests
{
    [Fact]
    public void Parses_keys_comments_quotes_and_export()
    {
        var values = DotEnv.Parse(new StringReader("""
            # comment
            REBRICKABLE_API_KEY=abc123

            export LEGOFINDER_CATALOGUE="C:/data/all downloads.zip"
            SINGLE='x # not a comment'
            INLINE=value # trailing comment
            EMPTY=
            not a setting
            =nokey
            """));

        Assert.Equal("abc123", values["REBRICKABLE_API_KEY"]);
        Assert.Equal("C:/data/all downloads.zip", values["LEGOFINDER_CATALOGUE"]);
        Assert.Equal("x # not a comment", values["SINGLE"]);
        Assert.Equal("value", values["INLINE"]);
        Assert.Equal("", values["EMPTY"]);
        Assert.Equal(5, values.Count);
    }

    [Fact]
    public void Environment_wins_over_the_file_and_empty_means_unset()
    {
        var file = new Dictionary<string, string> { ["A"] = "file", ["B"] = "file", ["C"] = "" };
        var env = new Dictionary<string, string> { ["A"] = "env", ["B"] = "" };

        var get = DotEnv.Layered(name => env.GetValueOrDefault(name), file);

        Assert.Equal("env", get("A"));
        Assert.Equal("file", get("B"));
        Assert.Null(get("C"));
        Assert.Null(get("D"));
    }

    [Fact]
    public void Finds_the_file_in_a_parent_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "legofinder-dotenv", Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(nested);
        try
        {
            Assert.Null(DotEnv.Find(nested) is { } found && found.StartsWith(root, StringComparison.Ordinal) ? found : null);
            File.WriteAllText(Path.Combine(root, ".env"), "X=1");

            Assert.Equal(Path.Combine(root, ".env"), DotEnv.Find(nested));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Lookup_for_the_current_directory_falls_back_to_the_environment()
    {
        var get = DotEnv.ForCurrentDirectory();
        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), get("PATH"));
    }
}
