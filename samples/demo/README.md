# Demo data

This is a small offline example that exercises the whole pipeline without a network or API keys.

| File | What it is |
| --- | --- |
| `tray.png` | A synthetic top-down "photo" of 12 pieces on a plain background |
| `parts.json` | A hand-written parts list (`legofinder.parts/1`) for a made-up 7-line model |
| `scan.json` | A **hand-made scan** (`legofinder.scan/1`): real segmentation of `tray.png`, with each piece's part and colour filled in from the known layout instead of an online recogniser |
| `make-demo.cs` | Regenerates all three files: `dotnet run samples/demo/make-demo.cs` (run from the repository root) |

Run it:

```sh
dotnet run --project src/LegoFinder.Cli -- find --in samples/demo/parts.json --scan samples/demo/scan.json \
    --out-dir out/demo --recogniser none -o out/demo/find.json
dotnet run --project src/LegoFinder.Cli -- report --in out/demo/find.json -o out/demo/report.html
```

Expected results (`find` exits with 2, because not everything is found):

| Line | Part | Result | Why |
| --- | --- | --- | --- |
| 1 | Red 2×4 brick ×2 | Found | Three are in the photo; only two are circled. |
| 2 | Green 2×2 brick | Found | |
| 3 | Yellow 2×4 plate | Found | |
| 4 | Blue 1×2 brick ×2 | Found | One lies horizontal and one vertical. |
| 5 | Black 1×2 plate | Found | |
| 6 | Orange 2×2 plate | Not found | It isn't in the photo. |
| 7 | Lime round 1×1 | Uncertain (dashed) | The recogniser score is only 0.42. |

The red 2×2, the blue 2×4 and the grey 1×4 are decoys: right colour with the wrong part, right part with the wrong colour, or not in the model at all. None of them should be circled. `tests/LegoFinder.Core.Tests/FindRunnerTests.cs` checks all of this automatically.
