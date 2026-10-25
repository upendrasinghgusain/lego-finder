using System.Net;
using System.Text;
using LegoFinder.Core.Contracts;
using LegoFinder.Core.Rendering;

namespace LegoFinder.Core.Reporting;

/// <summary>Renders a find result as one self-contained HTML page (annotated images embedded).</summary>
public static class HtmlReport
{
    /// <param name="readImage">Returns the bytes of an annotated image path, or null if it can't be read.</param>
    public static string Render(FindResult result, Func<string, byte[]?> readImage)
    {
        var s = result.Summary;
        var title = result.Parts.Name ?? result.Parts.Id;
        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{E(title)}} – LEGO Finder</title>
            <style>
              :root { color-scheme: light dark; --bg: #fafafa; --fg: #1a1a1a; --muted: #666; --card: #fff; --line: #ddd; }
              @media (prefers-color-scheme: dark) { :root { --bg: #121212; --fg: #eee; --muted: #aaa; --card: #1e1e1e; --line: #333; } }
              body { margin: 0 auto; max-width: 1200px; padding: 16px; font: 16px/1.5 system-ui, sans-serif; background: var(--bg); color: var(--fg); }
              h1 { margin: 0 0 4px; } .muted { color: var(--muted); }
              .summary { font-size: 1.25rem; margin: 12px 0 20px; }
              figure { margin: 0 0 24px; } figure img { width: 100%; height: auto; border-radius: 8px; }
              figcaption { color: var(--muted); font-size: .9rem; }
              .table-wrap { overflow-x: auto; }
              table { border-collapse: collapse; width: 100%; background: var(--card); }
              th, td { border-bottom: 1px solid var(--line); padding: 6px 8px; text-align: left; vertical-align: middle; }
              .num { display: inline-block; min-width: 2.2em; padding: 2px 6px; border-radius: 999px; background: #111; color: #fff; font-weight: 700; text-align: center; border: 3px solid; }
              .swatch { display: inline-block; width: 1em; height: 1em; border-radius: 3px; border: 1px solid var(--line); vertical-align: -2px; margin-right: 4px; }
              td img { width: 48px; height: 48px; object-fit: contain; }
              .Found { color: #2e7d32; } .PartlyFound { color: #ef6c00; } .Uncertain { color: #8e24aa; } .NotFound { color: #c62828; }
              .warnings { background: #fff3e0; color: #5d4037; padding: 8px 12px; border-radius: 6px; }
            </style>
            </head>
            <body>
            <h1>{{E(title)}}</h1>
            <div class="muted">{{E(result.Parts.Source)}} {{E(result.Parts.Id)}}{{(result.Parts.Designer is { } d ? " by " + E(d) : "")}}{{Link(result.Parts.SourceUrl, " · source")}}</div>
            <div class="summary">Found <strong>{{s.Found + s.PartlyFound}}</strong> of {{s.Lines}} part types ({{s.PiecesFound}} of {{s.PiecesNeeded}} pieces).</div>

            """);

        if (result.Warnings.Count > 0)
        {
            html.Append("<div class=\"warnings\"><strong>Warnings</strong><ul>");
            foreach (var w in result.Warnings)
            {
                html.Append($"<li>{E(w)}</li>");
            }

            html.Append("</ul></div>\n");
        }

        foreach (var image in result.Images)
        {
            var bytes = readImage(image.Annotated);
            html.Append("<figure>");
            html.Append(bytes is null
                ? $"<p class=\"muted\">Image not available: {E(image.Annotated)}</p>"
                : $"<img src=\"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}\" alt=\"Annotated photo {E(image.Photo)}\">");
            html.Append($"<figcaption>{E(image.Photo)} – {image.Circles} circles</figcaption></figure>\n");
        }

        html.Append("""
            <div class="table-wrap"><table>
            <thead><tr><th>#</th><th></th><th>Part</th><th>Colour</th><th>Found</th><th>Status</th></tr></thead>
            <tbody>

            """);
        foreach (var line in result.Lines)
        {
            var ring = AnnotationRenderer.RingHex(line.Number);
            var swatch = line.ColourRgb is { Length: 6 } rgb && rgb.All(Uri.IsHexDigit)
                ? $"<span class=\"swatch\" style=\"background:#{rgb}\"></span>"
                : "";
            var thumb = IsHttp(line.ImageUrl) ? $"<img src=\"{E(line.ImageUrl!)}\" alt=\"\" loading=\"lazy\">" : "";
            var name = E(line.PartName ?? line.PartId) + " <span class=\"muted\">" + E(line.PartId) + "</span>";
            html.Append($"""
                <tr><td><span class="num" style="border-color:#{ring}">{line.Number}</span></td><td>{thumb}</td><td>{(IsHttp(line.CatalogueUrl) ? $"<a href=\"{E(line.CatalogueUrl!)}\">{name}</a>" : name)}</td><td>{swatch}{E(line.ColourName ?? "")}</td><td>{line.Found} / {line.Quantity}</td><td class="{line.Status}">{StatusText(line.Status)}</td></tr>

                """);
        }

        html.Append("</tbody></table></div>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static string StatusText(LineStatus status) => status switch
    {
        LineStatus.Found => "Found",
        LineStatus.PartlyFound => "Some found",
        LineStatus.Uncertain => "Maybe (dashed)",
        _ => "Not found",
    };

    private static string Link(string? url, string text) => IsHttp(url) ? $"<a href=\"{E(url!)}\">{E(text)}</a>" : "";

    private static bool IsHttp(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
