using System.Text.RegularExpressions;

namespace LegoFinder.Core.Parts;

public enum PartsSourceKind
{
    RebrickableMoc,
    RebrickableSet,
    CsvFile,
}

/// <summary>What the user asked <c>legofinder parts</c> for, parsed from a URL, an id or a file path.</summary>
public sealed partial record PartsSource(PartsSourceKind Kind, string Id, string? Url, string? Designer, string? Name)
{
    /// <summary>
    /// Accepts a Rebrickable MOC or set URL, a bare MOC id ("MOC-37396"), a set number ("31058" or "31058-1"),
    /// or a path to an existing .csv file.
    /// </summary>
    public static PartsSource Parse(string input, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var s = input.Trim();
        if (s.Length == 0)
        {
            throw new ArgumentException("No parts source given.");
        }

        if (s.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && fileExists(s))
        {
            return new PartsSource(PartsSourceKind.CsvFile, Path.GetFileNameWithoutExtension(s), null, null, null);
        }

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return ParseUrl(uri, s);
        }

        if (MocId().Match(s) is { Success: true } moc)
        {
            return new PartsSource(PartsSourceKind.RebrickableMoc, NormaliseMocId(moc.Groups[1].Value), null, null, null);
        }

        if (SetNum().Match(s) is { Success: true } set)
        {
            return new PartsSource(PartsSourceKind.RebrickableSet, NormaliseSetNum(set.Value), null, null, null);
        }

        if (s.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException($"CSV file not found: {s}");
        }

        throw new ArgumentException(
            $"Can't tell what '{s}' is. Pass a Rebrickable MOC/set URL, a MOC id like MOC-37396, a set number like 31058-1, or a .csv file.");
    }

    private static PartsSource ParseUrl(Uri uri, string original)
    {
        var host = uri.Host.ToLowerInvariant();
        if (host != "rebrickable.com" && !host.EndsWith(".rebrickable.com", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Only rebrickable.com URLs are supported (got {uri.Host}).");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // /mocs/MOC-37396/Schwimpy/31058-velociraptooth/
        if (segments.Length >= 2 && segments[0].Equals("mocs", StringComparison.OrdinalIgnoreCase)
            && MocId().Match(segments[1]) is { Success: true } moc)
        {
            var designer = segments.Length >= 3 ? Uri.UnescapeDataString(segments[2]) : null;
            var name = segments.Length >= 4 ? TitleFromSlug(segments[3]) : null;
            return new PartsSource(PartsSourceKind.RebrickableMoc, NormaliseMocId(moc.Groups[1].Value), original, designer, name);
        }

        // /sets/31058-1/mighty-dinosaurs/
        if (segments.Length >= 2 && segments[0].Equals("sets", StringComparison.OrdinalIgnoreCase)
            && SetNum().IsMatch(segments[1]))
        {
            return new PartsSource(PartsSourceKind.RebrickableSet, NormaliseSetNum(segments[1]), original, null, null);
        }

        throw new ArgumentException($"Unrecognised Rebrickable URL: {original}. Expected /mocs/MOC-…/ or /sets/…/.");
    }

    internal static string TitleFromSlug(string slug)
    {
        var words = Uri.UnescapeDataString(slug).Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    private static string NormaliseMocId(string digits) => $"MOC-{digits}";

    private static string NormaliseSetNum(string s) => s.Contains('-') ? s : s + "-1";

    [GeneratedRegex(@"^MOC-?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex MocId();

    [GeneratedRegex(@"^\d{3,7}(-\d+)?$")]
    private static partial Regex SetNum();
}
