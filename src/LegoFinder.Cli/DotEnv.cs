namespace LegoFinder.Cli;

/// <summary>
/// Reads <c>KEY=VALUE</c> settings from a <c>.env</c> file so secrets like the Rebrickable API key can live in a
/// gitignored file instead of every shell's environment. Real environment variables always win.
/// </summary>
public static class DotEnv
{
    public const string FileName = ".env";

    /// <summary>Looks for <c>.env</c> in <paramref name="startDirectory"/> and then each parent folder.</summary>
    public static string? Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static Dictionary<string, string> Parse(TextReader reader)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }
            else if (value.IndexOf(" #", StringComparison.Ordinal) is var comment and >= 0)
            {
                value = value[..comment].TrimEnd();
            }

            values[key] = value;
        }

        return values;
    }

    /// <summary>An environment lookup that falls back to the <c>.env</c> file; empty values count as unset.</summary>
    public static Func<string, string?> Layered(Func<string, string?> environment, IReadOnlyDictionary<string, string> file) =>
        name => environment(name) is { Length: > 0 } fromEnv ? fromEnv
            : file.TryGetValue(name, out var fromFile) && fromFile.Length > 0 ? fromFile
            : null;

    public static Func<string, string?> ForCurrentDirectory()
    {
        var path = Find(Directory.GetCurrentDirectory());
        if (path is null)
        {
            return Environment.GetEnvironmentVariable;
        }

        using var reader = new StreamReader(path);
        return Layered(Environment.GetEnvironmentVariable, Parse(reader));
    }
}
