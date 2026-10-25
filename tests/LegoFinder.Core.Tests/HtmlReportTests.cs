using LegoFinder.Core.Contracts;
using LegoFinder.Core.Reporting;

namespace LegoFinder.Core.Tests;

public class HtmlReportTests
{
    private static FindResult Result(params LineResult[] lines) => new()
    {
        Parts = new ModelSummary("Rebrickable", ModelKind.Moc, "MOC-1", "Dino <b>", "Designer & Co", "https://rebrickable.com/mocs/MOC-1/"),
        Summary = FindSummary.From(lines),
        Images = [new AnnotatedImage("tray.jpg", "out/tray.annotated.jpg", 1)],
        Lines = lines,
    };

    private static LineResult Line(int n, LineStatus status, string? catalogue = "https://rebrickable.com/parts/3001/") => new()
    {
        Number = n,
        PartId = "3001",
        PartName = "Brick <2 x 4>",
        ColourName = "Red",
        ColourRgb = "C91A09",
        Quantity = 2,
        Found = status == LineStatus.Found ? 2 : 0,
        Status = status,
        CatalogueUrl = catalogue,
        ImageUrl = "https://cdn.rebrickable.com/x.jpg",
        Matches = [],
    };

    [Fact]
    public void Renders_summary_legend_and_embedded_images()
    {
        var html = HtmlReport.Render(Result(Line(1, LineStatus.Found), Line(2, LineStatus.NotFound)), _ => [1, 2, 3]);

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("Found <strong>1</strong> of 2 part types (2 of 4 pieces)", html);
        Assert.Contains("data:image/jpeg;base64,AQID", html);
        Assert.Contains("<a href=\"https://rebrickable.com/parts/3001/\">", html);
        Assert.Contains("background:#C91A09", html);
        Assert.Contains("class=\"NotFound\">Not found", html);
        Assert.Contains("border-color:#FF1744", html); // line 1 ring colour
        Assert.Contains("by Designer &amp; Co", html);
    }

    [Fact]
    public void Escapes_text_from_the_data()
    {
        var html = HtmlReport.Render(Result(Line(1, LineStatus.Found)), _ => null);

        Assert.Contains("Dino &lt;b&gt;", html);
        Assert.Contains("Brick &lt;2 x 4&gt;", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void Only_http_links_are_rendered()
    {
        var html = HtmlReport.Render(Result(Line(1, LineStatus.Found, catalogue: "javascript:alert(1)")), _ => null);

        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public void Missing_images_and_warnings_are_shown()
    {
        var result = Result(Line(1, LineStatus.Uncertain)) with { Warnings = ["photo changed"] };

        var html = HtmlReport.Render(result, _ => null);

        Assert.Contains("Image not available: out/tray.annotated.jpg", html);
        Assert.Contains("<li>photo changed</li>", html);
        Assert.Contains("Maybe (dashed)", html);
    }

    [Fact]
    public void Status_texts()
    {
        var html = HtmlReport.Render(Result(Line(1, LineStatus.PartlyFound)), _ => null);
        Assert.Contains("Some found", html);
    }
}
