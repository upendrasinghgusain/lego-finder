using LegoFinder.Core.Contracts;
using LegoFinder.Core.Matching;

namespace LegoFinder.Core.Tests;

public class PartMatcherTests
{
    private const string Red = "C91A09";
    private const string PhotoRed = "C3241A";
    private const string Blue = "0055BF";

    private static readonly MatchOptions ColourOn = new() { MatchColour = true };

    private static IReadOnlyList<LineResult> Match(PartsList parts, params Detection[] detections) =>
        PartMatcher.Match(parts, [Make.Photo("tray.jpg", detections)]);

    private static IReadOnlyList<LineResult> MatchWithColour(PartsList parts, params Detection[] detections) =>
        PartMatcher.Match(parts, [Make.Photo("tray.jpg", detections)], ColourOn);

    [Fact]
    public void Colour_matching_is_off_by_default() => Assert.False(new MatchOptions().MatchColour);

    [Fact]
    public void By_default_any_colour_of_the_right_part_counts()
    {
        var line = Assert.Single(Match(Make.Parts(Make.Line(1, "3001", Red)), Make.Detection("d1", Blue, ("3001", 0.9))));

        Assert.Equal(LineStatus.Found, line.Status);
        var m = Assert.Single(line.Matches);
        Assert.Equal(ColourEvidence.Ignored, m.ColourEvidence);
        Assert.True(m.Confident);
        Assert.True(m.ColourDistance > 25); // still reported for information
    }

    [Fact]
    public void By_default_the_right_colour_is_preferred_when_there_is_a_choice()
    {
        var line = Assert.Single(Match(
            Make.Parts(Make.Line(1, "3001", Red)),
            Make.Detection("blue", Blue, ("3001", 0.95)),
            Make.Detection("red", PhotoRed, ("3001", 0.7))));

        Assert.Equal("red", Assert.Single(line.Matches).DetectionId);
    }

    [Fact]
    public void By_default_lines_without_colour_can_be_confident()
    {
        var line = Assert.Single(Match(Make.Parts(Make.Line(1, "3001", rgb: null)), Make.Detection("d1", Blue, ("3001", 0.9))));
        Assert.Equal(LineStatus.Found, line.Status);
    }

    [Fact]
    public void Matches_part_and_measured_colour()
    {
        var result = MatchWithColour(Make.Parts(Make.Line(1, "3001", Red)), Make.Detection("d1", PhotoRed, ("3001", 0.9)));

        var line = Assert.Single(result);
        Assert.Equal(LineStatus.Found, line.Status);
        Assert.Equal(1, line.Found);
        var m = Assert.Single(line.Matches);
        Assert.Equal(("tray.jpg", "d1", ColourEvidence.Measured, true), (m.Photo, m.DetectionId, m.ColourEvidence, m.Confident));
        Assert.True(m.ColourDistance < 10);
    }

    [Fact]
    public void Wrong_colour_is_not_a_match()
    {
        var line = Assert.Single(MatchWithColour(Make.Parts(Make.Line(1, "3001", Red)), Make.Detection("d1", Blue, ("3001", 0.9))));

        Assert.Equal(LineStatus.NotFound, line.Status);
        Assert.Empty(line.Matches);
    }

    [Fact]
    public void Wrong_part_is_not_a_match()
    {
        var line = Assert.Single(Match(Make.Parts(Make.Line(1, "3001", Red)), Make.Detection("d1", PhotoRed, ("3003", 0.9))));
        Assert.Equal(LineStatus.NotFound, line.Status);
    }

    [Fact]
    public void Predicted_colour_counts_even_when_the_measured_colour_is_off()
    {
        var detection = Make.Detection("d1", "804040", ("3001", 0.9)) with
        {
            Colour = new DetectionColour { Measured = "804040", Predicted = [new ColourCandidate("5", "Red", "BrickLink", 0.7)] },
        };

        var line = Assert.Single(MatchWithColour(Make.Parts(Make.Line(1, "3001", Red, brickLinkColours: [5])), detection));

        Assert.Equal(ColourEvidence.Predicted, Assert.Single(line.Matches).ColourEvidence);
    }

    [Fact]
    public void Low_scoring_or_non_bricklink_colour_predictions_are_ignored()
    {
        var detection = Make.Detection("d1", Blue, ("3001", 0.9)) with
        {
            Colour = new DetectionColour
            {
                Measured = Blue,
                Predicted = [new ColourCandidate("5", "Red", "BrickLink", 0.1), new ColourCandidate("5", "Red", "Other", 0.9)],
            },
        };

        var line = Assert.Single(MatchWithColour(Make.Parts(Make.Line(1, "3001", Red, brickLinkColours: [5])), detection));

        Assert.Equal(LineStatus.NotFound, line.Status);
    }

    [Fact]
    public void Caps_matches_at_quantity_and_prefers_higher_scores()
    {
        var result = Match(
            Make.Parts(Make.Line(1, "3001", Red, quantity: 2)),
            Make.Detection("low", PhotoRed, ("3001", 0.6)),
            Make.Detection("high", PhotoRed, ("3001", 0.95)),
            Make.Detection("mid", PhotoRed, ("3001", 0.8)));

        var line = Assert.Single(result);
        Assert.Equal(["high", "mid"], line.Matches.Select(m => m.DetectionId));
        Assert.Equal(LineStatus.Found, line.Status);
    }

    [Fact]
    public void A_detection_is_only_ever_assigned_once()
    {
        // The same red piece could be either line; it goes to the line where its candidate scores higher.
        var parts = Make.Parts(Make.Line(1, "3001", Red), Make.Line(2, "3002", Red));
        var detection = Make.Detection("d1", PhotoRed, ("3002", 0.9), ("3001", 0.7));

        var result = Match(parts, detection);

        var assigned = result.SelectMany(l => l.Matches).ToList();
        Assert.Single(assigned);
        Assert.Equal(2, result.Single(l => l.Matches.Count == 1).Number);
    }

    [Fact]
    public void Same_detection_id_in_different_photos_are_different_pieces()
    {
        var parts = Make.Parts(Make.Line(1, "3001", Red, quantity: 2));
        var photos = new[]
        {
            Make.Photo("a.jpg", Make.Detection("x#1", PhotoRed, ("3001", 0.9))),
            Make.Photo("b.jpg", Make.Detection("x#1", PhotoRed, ("3001", 0.9))),
        };

        var line = Assert.Single(PartMatcher.Match(parts, photos));

        Assert.Equal(["a.jpg", "b.jpg"], line.Matches.Select(m => m.Photo).Order());
    }

    [Fact]
    public void Partly_found_and_uncertain_statuses()
    {
        var parts = Make.Parts(Make.Line(1, "3001", Red, quantity: 3), Make.Line(2, "3003", Red));
        var result = Match(parts,
            Make.Detection("d1", PhotoRed, ("3001", 0.9)),
            Make.Detection("d2", PhotoRed, ("3001", 0.3)),
            Make.Detection("d3", PhotoRed, ("3003", 0.4)));

        Assert.Equal(LineStatus.PartlyFound, result[0].Status);
        Assert.Equal(1, result[0].Found);
        Assert.Equal(2, result[0].Matches.Count);
        Assert.Equal(LineStatus.Uncertain, result[1].Status);
        Assert.False(Assert.Single(result[1].Matches).Confident);
    }

    [Fact]
    public void Unknown_colour_matches_but_is_never_confident()
    {
        var line = Assert.Single(MatchWithColour(Make.Parts(Make.Line(1, "3001", rgb: null)), Make.Detection("d1", Blue, ("3001", 0.99))));

        var m = Assert.Single(line.Matches);
        Assert.Equal(ColourEvidence.Unknown, m.ColourEvidence);
        Assert.Null(m.ColourDistance);
        Assert.Equal(LineStatus.Uncertain, line.Status);
    }

    [Fact]
    public void Only_top_candidates_are_considered()
    {
        var detection = Make.Detection("d1", PhotoRed, ("a", 0.9), ("b", 0.8), ("c", 0.7), ("3001", 0.6));

        Assert.Equal(LineStatus.NotFound, Assert.Single(Match(Make.Parts(Make.Line(1, "3001", Red)), detection)).Status);
    }

    [Fact]
    public void Candidates_below_min_score_are_ignored()
    {
        var parts = Make.Parts(Make.Line(1, "3001", Red));
        var photo = Make.Photo("p", Make.Detection("d1", PhotoRed, ("3001", 0.2)));

        var line = Assert.Single(PartMatcher.Match(parts, [photo], new MatchOptions { MinPartScore = 0.3 }));

        Assert.Equal(LineStatus.NotFound, line.Status);
    }

    [Fact]
    public void Detections_without_candidates_are_ignored()
    {
        Assert.Equal(LineStatus.NotFound, Assert.Single(Match(Make.Parts(Make.Line(1, "3001", Red)), Make.Detection("d1", PhotoRed))).Status);
    }

    [Theory]
    [InlineData("3069b", "3069")]
    [InlineData("3069", "3069b")]
    [InlineData("3069B", "3069b")]
    public void Mould_variants_match(string linePart, string candidate)
    {
        var line = Assert.Single(Match(Make.Parts(Make.Line(1, linePart, Red)), Make.Detection("d1", PhotoRed, (candidate, 0.9))));
        Assert.Equal(LineStatus.Found, line.Status);
    }

    [Fact]
    public void External_ids_match()
    {
        var parts = Make.Parts(Make.Line(1, "4073", Red, externalIds: new() { ["BrickLink"] = ["6141"] }));

        var line = Assert.Single(Match(parts, Make.Detection("d1", PhotoRed, ("6141", 0.9))));

        Assert.Equal(LineStatus.Found, line.Status);
    }

    [Fact]
    public void Printed_parts_do_not_match_the_plain_part()
    {
        var line = Assert.Single(Match(Make.Parts(Make.Line(1, "3001", Red)), Make.Detection("d1", PhotoRed, ("3001pr0001", 0.9))));
        Assert.Equal(LineStatus.NotFound, line.Status);
    }

    [Fact]
    public void Results_keep_line_metadata()
    {
        var parts = Make.Parts(Make.Line(1, "3001", Red) with { CatalogueUrl = "https://x/", ImageUrl = "https://i/" });

        var line = Assert.Single(PartMatcher.Match(parts, []));

        Assert.Equal(("3001", "Part 3001", "Colour C91A09", Red, "https://x/", "https://i/"),
            (line.PartId, line.PartName, line.ColourName, line.ColourRgb, line.CatalogueUrl, line.ImageUrl));
    }

    [Fact]
    public void Colour_fit_uses_line_without_rgb_but_with_bricklink_ids()
    {
        var want = new PartColour { BrickLinkIds = [5] };

        Assert.Null(PartMatcher.ColourFit(want, new DetectionColour { Measured = PhotoRed }, new MatchOptions()));
        Assert.Equal(ColourEvidence.Predicted, PartMatcher.ColourFit(want,
            new DetectionColour { Predicted = [new ColourCandidate("5", null, "BrickLink", 0.5)] }, new MatchOptions())!.Value.Evidence);
    }
}
