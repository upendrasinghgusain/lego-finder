using System.IO.Compression;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Parts;

namespace LegoFinder.Core.Tests;

public class CatalogueTests
{
    /// <summary>A tiny export with the same file layout and headers as Rebrickable's all_downloads.zip.</summary>
    internal static readonly Dictionary<string, string> Files = new()
    {
        ["sets.csv"] = "set_num,name,year,theme_id,num_parts,img_url\n31058-1,Mighty Dinosaurs,2017,672,174,x\n10000-1,\"Bricks, Bricks\",2020,1,1,x\n",
        ["inventories.csv"] = "id,version,set_num\n9246,1,31058-1\n161292,2,31058-1\n5,1,10000-1\n",
        ["inventory_parts.csv"] = """
            inventory_id,part_num,color_id,quantity,is_spare,img_url
            9246,3001,4,9,False,https://old/
            161292,3001,4,2,False,https://cdn.rebrickable.com/media/parts/elements/300121.jpg
            161292,6141,0,3,False,
            161292,6141,0,1,True,
            161292,3001,4,1,False,
            5,3003,1,1,False,
            """.Replace("            ", "") + "\n",
        ["parts.csv"] = "part_num,name,part_cat_id,part_material\n3001,Brick 2 x 4,11,Plastic\n6141,\"Plate Round 1 x 1, Solid Stud\",21,Plastic\n3003,Brick 2 x 2,11,Plastic\n",
        ["part_mappings.csv"] = "part_num,ext_system,ext_id,is_export_id,is_import_id\n6141,BL,4073,True,True\n6141,BO,123,True,True\n3001,BL,3001,True,True\n",
        ["color_mappings.csv"] = "id,ext_system,ext_id,ext_descr,is_export_id,is_import_id\n4,BL,5,\"[\"\"Red\"\"]\",True,True\n0,BL,11,\"[\"\"Black\"\"]\",True,True\n0,LE,26,x,True,True\n",
        ["elements.csv"] = "element_id,part_num,color_id,design_id\n614126,6141,0,6141\n300121,3001,4,3001\n",
    };

    internal static string WriteFolder(TempDir dir)
    {
        var folder = dir.File("db");
        Directory.CreateDirectory(folder);
        foreach (var (name, content) in Files)
        {
            File.WriteAllText(Path.Combine(folder, name), content);
        }

        return folder;
    }

    internal static string WriteZip(TempDir dir)
    {
        var zip = dir.File("all_downloads.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var (name, content) in Files)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }

        return zip;
    }

    [Fact]
    public void Reads_the_latest_set_inventory_merging_rows_and_skipping_spares()
    {
        using var dir = new TempDir();
        using var db = RebrickableCatalogue.Open(WriteFolder(dir));

        var parts = db.GetSet("31058-1", includeSpares: false);

        Assert.Equal(("RebrickableCatalogue", ModelKind.Set, "31058-1", "Mighty Dinosaurs"), (parts.Source, parts.Kind, parts.Id, parts.Name));
        Assert.Equal("https://rebrickable.com/sets/31058-1/", parts.SourceUrl);
        Assert.Collection(parts.Lines,
            l =>
            {
                Assert.Equal((1, "3001", 3, "Brick 2 x 4"), (l.Number, l.PartId, l.Quantity, l.PartName));
                Assert.Equal(("Red", "C91A09"), (l.Colour!.Name, l.Colour.Rgb));
                Assert.Equal([5], l.Colour.BrickLinkIds);
                Assert.Equal("https://cdn.rebrickable.com/media/parts/elements/300121.jpg", l.ImageUrl);
            },
            l =>
            {
                Assert.Equal(("6141", 3, "Plate Round 1 x 1, Solid Stud"), (l.PartId, l.Quantity, l.PartName));
                Assert.Equal(["4073"], l.ExternalIds["BrickLink"]);
                Assert.Equal([11], l.Colour!.BrickLinkIds);
                Assert.Equal("https://cdn.rebrickable.com/media/parts/elements/614126.jpg", l.ImageUrl); // from elements.csv
            });
    }

    [Fact]
    public void Includes_spares_when_asked()
    {
        using var dir = new TempDir();
        using var db = RebrickableCatalogue.Open(WriteFolder(dir));

        Assert.Equal(4, db.GetSet("31058-1", includeSpares: true).Lines[1].Quantity);
    }

    [Fact]
    public void Reads_the_zip_directly_and_handles_quoted_names()
    {
        using var dir = new TempDir();
        using var db = RebrickableCatalogue.Open(WriteZip(dir));

        var parts = db.GetSet("10000-1", false);

        Assert.Equal("Bricks, Bricks", parts.Name);
        Assert.Equal("3003", Assert.Single(parts.Lines).PartId);
    }

    [Fact]
    public void Enrich_fills_gaps_without_overwriting()
    {
        using var dir = new TempDir();
        using var db = RebrickableCatalogue.Open(WriteFolder(dir));
        var csv = RebrickableCsvImporter.Import(new StringReader("Part,Color,Quantity\n6141,0,2\n3001,4,1\n"), "m");
        var handWritten = csv with
        {
            Lines = [csv.Lines[0], csv.Lines[1] with { PartName = "My brick", ImageUrl = "https://mine/" }],
        };

        var enriched = db.Enrich(handWritten);

        var round = enriched.Lines[0];
        Assert.Equal("Plate Round 1 x 1, Solid Stud", round.PartName);
        Assert.Equal(["4073"], round.ExternalIds["BrickLink"]);
        Assert.Equal([11], round.Colour!.BrickLinkIds);
        Assert.Equal("https://cdn.rebrickable.com/media/parts/elements/614126.jpg", round.ImageUrl);
        Assert.Equal(("My brick", "https://mine/"), (enriched.Lines[1].PartName, enriched.Lines[1].ImageUrl));
    }

    [Fact]
    public void Enrich_keeps_existing_external_ids_and_colour_ids()
    {
        using var dir = new TempDir();
        using var db = RebrickableCatalogue.Open(WriteFolder(dir));
        var line = Make.Line(1, "6141", "05131D", brickLinkColours: [99], externalIds: new() { ["BrickLink"] = ["mine"] }) with
        {
            Colour = new PartColour { Id = 0, Rgb = "05131D", BrickLinkIds = [99] },
        };

        var enriched = db.Enrich(Make.Parts(line)).Lines[0];

        Assert.Equal(["mine"], enriched.ExternalIds["BrickLink"]);
        Assert.Equal([99], enriched.Colour!.BrickLinkIds);
        Assert.Equal("Black", enriched.Colour.Name);
    }

    [Fact]
    public void Unknown_parts_and_colourless_lines_pass_through()
    {
        using var dir = new TempDir();
        using var db = RebrickableCatalogue.Open(WriteFolder(dir));

        var line = db.Enrich(Make.Parts(Make.Line(1, "99999", rgb: null))).Lines[0];

        Assert.Equal("Part 99999", line.PartName);
        Assert.Null(line.Colour);
        Assert.False(line.ExternalIds.ContainsKey("BrickLink"));
    }

    [Theory]
    [InlineData("99999-1", "not in the Rebrickable catalogue")]
    public void Unknown_sets_are_reported(string set, string message)
    {
        using var dir = new TempDir();
        using var db = RebrickableCatalogue.Open(WriteFolder(dir));

        Assert.Contains(message, Assert.Throws<ArgumentException>(() => db.GetSet(set, false)).Message);
    }

    [Fact]
    public void Sets_without_inventories_are_reported()
    {
        using var dir = new TempDir();
        var folder = WriteFolder(dir);
        File.WriteAllText(Path.Combine(folder, "inventories.csv"), "id,version,set_num\n");
        using var db = RebrickableCatalogue.Open(folder);

        Assert.Contains("no inventory", Assert.Throws<ArgumentException>(() => db.GetSet("31058-1", false)).Message);
    }

    [Fact]
    public void Missing_files_and_columns_are_reported()
    {
        using var dir = new TempDir();
        var folder = WriteFolder(dir);
        File.Delete(Path.Combine(folder, "sets.csv"));
        File.WriteAllText(Path.Combine(folder, "parts.csv"), "nope\n");
        using var db = RebrickableCatalogue.Open(folder);

        Assert.Throws<FileNotFoundException>(() => db.GetSet("31058-1", false));
        Assert.Contains("missing column", Assert.Throws<InvalidDataException>(() => db.Enrich(Make.Parts(Make.Line(1, "3001", null) with { PartName = null }))).Message);
    }

    [Fact]
    public void Missing_zip_entries_are_reported()
    {
        using var dir = new TempDir();
        var zip = dir.File("empty.zip");
        ZipFile.Open(zip, ZipArchiveMode.Create).Dispose();
        using var db = RebrickableCatalogue.Open(zip);

        Assert.Throws<FileNotFoundException>(() => db.GetSet("31058-1", false));
    }

    [Fact]
    public void Open_rejects_missing_paths_and_non_zips()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("x.zip"), "not a zip");

        Assert.Throws<FileNotFoundException>(() => RebrickableCatalogue.Open(dir.File("missing.zip")));
        Assert.Throws<InvalidDataException>(() => RebrickableCatalogue.Open(dir.File("x.zip")));
    }

    [Fact]
    public void Default_location_prefers_the_environment_variable()
    {
        Assert.Equal("/x/all.zip", RebrickableCatalogue.FindDefault(name => name == "LEGOFINDER_CATALOGUE" ? "/x/all.zip" : null));
        // The test runner's working directory has no data/rebrickable folder.
        Assert.Null(RebrickableCatalogue.FindDefault(_ => null));
    }

    [Fact]
    public void Enriched_csv_lines_now_match_brickognize_bricklink_ids()
    {
        // The point of the catalogue: Rebrickable 6141 is BrickLink 4073, which is what the recogniser reports.
        using var dir = new TempDir();
        using var db = RebrickableCatalogue.Open(WriteFolder(dir));
        var parts = RebrickableCsvImporter.Import(new StringReader("Part,Color,Quantity\n6141,0,1\n"), "m");
        var photo = Make.Photo("p", Make.Detection("d1", "05131D", ("4073", 0.9)));

        Assert.Equal(LineStatus.NotFound, Matching.PartMatcher.Match(parts, [photo]).Single().Status);
        Assert.Equal(LineStatus.Found, Matching.PartMatcher.Match(db.Enrich(parts), [photo]).Single().Status);
    }
}
