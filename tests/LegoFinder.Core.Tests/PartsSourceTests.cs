using LegoFinder.Core.Parts;

namespace LegoFinder.Core.Tests;

public class PartsSourceTests
{
    [Fact]
    public void Parses_moc_url_with_designer_and_name_from_slug()
    {
        const string url = "https://rebrickable.com/mocs/MOC-37396/Schwimpy/31058-velociraptooth/";

        var s = PartsSource.Parse(url);

        Assert.Equal(PartsSourceKind.RebrickableMoc, s.Kind);
        Assert.Equal("MOC-37396", s.Id);
        Assert.Equal("Schwimpy", s.Designer);
        Assert.Equal("31058 Velociraptooth", s.Name);
        Assert.Equal(url, s.Url);
    }

    [Fact]
    public void Parses_short_moc_url_and_www_host()
    {
        var s = PartsSource.Parse("https://www.rebrickable.com/mocs/moc-123");

        Assert.Equal("MOC-123", s.Id);
        Assert.Null(s.Designer);
        Assert.Null(s.Name);
    }

    [Theory]
    [InlineData("MOC-37396")]
    [InlineData("moc-37396")]
    [InlineData("MOC37396")]
    [InlineData("  MOC-37396  ")]
    public void Parses_moc_ids(string input)
    {
        var s = PartsSource.Parse(input);
        Assert.Equal(PartsSourceKind.RebrickableMoc, s.Kind);
        Assert.Equal("MOC-37396", s.Id);
    }

    [Theory]
    [InlineData("31058", "31058-1")]
    [InlineData("31058-1", "31058-1")]
    [InlineData("10497-2", "10497-2")]
    [InlineData("https://rebrickable.com/sets/31058-1/mighty-dinosaurs/", "31058-1")]
    [InlineData("https://rebrickable.com/sets/31058/", "31058-1")]
    public void Parses_sets(string input, string expected)
    {
        var s = PartsSource.Parse(input);
        Assert.Equal(PartsSourceKind.RebrickableSet, s.Kind);
        Assert.Equal(expected, s.Id);
    }

    [Fact]
    public void Existing_csv_file_is_a_csv_source()
    {
        var s = PartsSource.Parse("exports/velociraptooth.csv", fileExists: _ => true);

        Assert.Equal(PartsSourceKind.CsvFile, s.Kind);
        Assert.Equal("velociraptooth", s.Id);
    }

    [Fact]
    public void Missing_csv_file_is_reported()
    {
        Assert.Throws<FileNotFoundException>(() => PartsSource.Parse("missing.csv", fileExists: _ => false));
    }

    [Theory]
    [InlineData("https://example.com/mocs/MOC-1/", "Only rebrickable.com")]
    [InlineData("https://evilrebrickable.com/mocs/MOC-1/", "Only rebrickable.com")]
    [InlineData("https://rebrickable.com/parts/3001/", "Unrecognised Rebrickable URL")]
    [InlineData("https://rebrickable.com/mocs/not-a-moc/", "Unrecognised Rebrickable URL")]
    [InlineData("velociraptooth", "Can't tell")]
    [InlineData("   ", "No parts source")]
    public void Rejects_unsupported_input(string input, string message)
    {
        var ex = Assert.Throws<ArgumentException>(() => PartsSource.Parse(input, _ => false));
        Assert.Contains(message, ex.Message);
    }

    [Theory]
    [InlineData("31058-velociraptooth", "31058 Velociraptooth")]
    [InlineData("big_red-truck", "Big Red Truck")]
    [InlineData("caf%C3%A9", "Café")]
    public void Title_from_slug(string slug, string expected) => Assert.Equal(expected, PartsSource.TitleFromSlug(slug));
}
