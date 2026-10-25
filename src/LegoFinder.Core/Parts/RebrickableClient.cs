using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LegoFinder.Core.Contracts;

namespace LegoFinder.Core.Parts;

/// <summary>Fetches MOC and set inventories from the Rebrickable API v3.</summary>
public sealed class RebrickableClient(HttpClient http, string apiKey)
{
    public static readonly Uri BaseUri = new("https://rebrickable.com/api/v3/");

    public async Task<PartsList> GetPartsAsync(PartsSource source, bool includeSpares, CancellationToken ct = default)
    {
        var id = Uri.EscapeDataString(source.Id);
        string? name = source.Name;
        string sourceUrl;
        string partsPath;

        switch (source.Kind)
        {
            case PartsSourceKind.RebrickableMoc:
                // There is no MOC details endpoint, so name/designer come from the URL slug.
                partsPath = $"lego/mocs/{id}/parts/";
                sourceUrl = source.Url ?? $"https://rebrickable.com/mocs/{id}/";
                break;
            case PartsSourceKind.RebrickableSet:
                partsPath = $"lego/sets/{id}/parts/";
                using (var set = await GetJsonAsync(new Uri(BaseUri, $"lego/sets/{id}/"), ct))
                {
                    name ??= GetString(set.RootElement, "name");
                    sourceUrl = source.Url ?? GetString(set.RootElement, "set_url") ?? $"https://rebrickable.com/sets/{id}/";
                }

                break;
            default:
                throw new ArgumentException($"{source.Kind} is not a Rebrickable API source.", nameof(source));
        }

        var lines = new List<PartLine>();
        Uri? next = new(BaseUri, $"{partsPath}?page_size=1000");
        while (next is not null)
        {
            using var page = await GetJsonAsync(next, ct);
            foreach (var item in page.RootElement.GetProperty("results").EnumerateArray())
            {
                if (!includeSpares && item.TryGetProperty("is_spare", out var spare) && spare.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                lines.Add(ParseLine(item, lines.Count + 1));
            }

            next = GetString(page.RootElement, "next") is { } n ? new Uri(n) : null;
        }

        return new PartsList
        {
            Source = "Rebrickable",
            Kind = source.Kind == PartsSourceKind.RebrickableMoc ? ModelKind.Moc : ModelKind.Set,
            Id = source.Id,
            Name = name,
            Designer = source.Designer,
            SourceUrl = sourceUrl,
            Lines = lines,
        };
    }

    internal static PartLine ParseLine(JsonElement item, int number)
    {
        var part = item.GetProperty("part");
        var externalIds = new Dictionary<string, IReadOnlyList<string>>();
        if (part.TryGetProperty("external_ids", out var ext) && ext.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in ext.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array))
            {
                externalIds[p.Name] = p.Value.EnumerateArray().Select(v => v.ToString()).ToList();
            }
        }

        PartColour? colour = null;
        if (item.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            var brickLinkIds = new List<int>();
            if (c.TryGetProperty("external_ids", out var cext) && cext.ValueKind == JsonValueKind.Object
                && cext.TryGetProperty("BrickLink", out var bl) && bl.TryGetProperty("ext_ids", out var ids))
            {
                brickLinkIds.AddRange(ids.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Number).Select(v => v.GetInt32()));
            }

            colour = new PartColour
            {
                Id = c.TryGetProperty("id", out var cid) && cid.ValueKind == JsonValueKind.Number ? cid.GetInt32() : null,
                Name = GetString(c, "name"),
                Rgb = GetString(c, "rgb"),
                IsTransparent = c.TryGetProperty("is_trans", out var t) && t.ValueKind == JsonValueKind.True,
                BrickLinkIds = brickLinkIds,
            };
        }

        var partId = GetString(part, "part_num") ?? throw new InvalidDataException("Inventory item has no part_num.");
        return new PartLine
        {
            Number = number,
            PartId = partId,
            PartName = GetString(part, "name"),
            Colour = colour,
            Quantity = item.GetProperty("quantity").GetInt32(),
            ImageUrl = GetString(part, "part_img_url"),
            CatalogueUrl = GetString(part, "part_url") ?? $"https://rebrickable.com/parts/{Uri.EscapeDataString(partId)}/",
            ExternalIds = externalIds,
        };
    }

    private async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken ct)
    {
        // Only ever talk to Rebrickable, even when following "next" links from a response.
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(BaseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Refusing to fetch non-Rebrickable URL {uri}.");
        }

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt), ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized =>
                        "Rebrickable rejected the API key. Set REBRICKABLE_API_KEY or pass --api-key (get one at https://rebrickable.com/api/).",
                    // Seen 2026-10-25: a valid key gets 403 for MOC parts lists, which the API doesn't serve to ordinary accounts.
                    HttpStatusCode.Forbidden =>
                        $"Rebrickable won't let this API key read {uri.AbsolutePath} (MOC parts lists aren't available through the API). "
                        + "Export the parts as 'Rebrickable CSV' from the MOC's page and pass the file instead.",
                    HttpStatusCode.NotFound =>
                        $"Rebrickable has no inventory at {uri.AbsolutePath} (it may not exist, or the MOC's parts list may not be public). Try exporting the parts as CSV instead.",
                    _ => $"Rebrickable returned {(int)response.StatusCode} {response.ReasonPhrase} for {uri.AbsolutePath}.",
                }, null, response.StatusCode);
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(body, cancellationToken: ct);
        }
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
