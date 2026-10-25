# LEGO Finder MVP — Product & Technical Specification

**Version:** 1.0  
**Status:** Ready for implementation planning  
**Project type:** Personal prototype / experimental application  
**Primary user:** A five-year-old child building LEGO models from YouTube tutorials  
**Initial platform:** Android phone or tablet, accessed through the native Share menu  
**Initial inputs:** Shared YouTube URL + pre-existing tray photographs  
**Initial output:** An annotated tray photograph showing numbered locations of required LEGO pieces

---

## 1. Executive summary

Build a child-friendly application that helps a child find the LEGO pieces required for a model shown in a YouTube building tutorial.

The child watches a YouTube video, taps **Share**, selects LEGO Finder, and the application receives the video URL.

The application then:

1. Identifies the LEGO model being built in the video.
2. Resolves the model to an online configuration or parts inventory, preferably through Rebrickable or another suitable catalogue.
3. Retrieves the list of required LEGO parts, including part identifiers, colours and quantities where available.
4. Loads an existing photograph of the user's LEGO collection, arranged in a single non-overlapping layer.
5. Identifies which required pieces appear in that photograph.
6. Maps each required part to its physical location in the photograph.
7. Produces a new annotated image with numbered circles around the matching pieces.
8. Displays the annotated image and a legend so the child can locate the pieces.

The first version deliberately excludes live camera capture, custom model training and advanced autonomous video analysis.

The purpose is to establish a working end-to-end pipeline and create a framework for experimenting with different model-resolution and image-recognition approaches.

### Core product principle

**The child should not need to know LEGO part names, part numbers, model identifiers or how to search a catalogue.**

The system should handle the technical work and present a visual answer.

---

## 2. Goals and non-goals

### 2.1 Goals

- Accept a YouTube URL through Android's Share menu.
- Resolve a video to a specific LEGO build and its parts inventory.
- Prefer existing published inventories over analysing video frames.
- Maintain a structured inventory of required parts.
- Use pre-existing photographs of the physical LEGO collection.
- Identify and locate required parts in those photographs.
- Return an annotated image with clearly numbered circles.
- Make it possible to replace the recognition implementation without redesigning the application.
- Record intermediate results and failures to support experimentation.
- Keep the workflow usable by a five-year-old with minimal adult assistance after initial setup.

### 2.2 Non-goals for the first MVP

The following are explicitly out of scope:

- Taking photographs using a live camera.
- Automatically analysing every frame of a YouTube video.
- Training a custom computer-vision model.
- Recognising overlapping or heavily occluded LEGO pieces.
- Detecting whether pieces are missing from the user's entire collection.
- Tracking pieces after they have been moved.
- Identifying the precise building step the child is currently following.
- Automatically controlling or embedding the YouTube player.
- Supporting every LEGO creator, catalogue and website from day one.
- Guaranteeing identification of every required piece.
- Using an LLM as the sole mechanism for determining exact LEGO part identities.

---

## 3. Primary user journey

### Scenario

The child is watching a YouTube video that demonstrates an alternative build of an existing LEGO set.

The project has already been configured with a photograph of the child's LEGO collection.

### User flow

**Step 1 — Share the video**

The child taps Share in YouTube and selects LEGO Finder from the Android share sheet.

**Step 2 — Receive the URL**

LEGO Finder receives the shared URL and extracts the video identifier. For example:

`https://youtu.be/BEyoBYjPNpY`

The application should support common YouTube URL formats, including `youtu.be`, standard watch URLs and supported mobile share URLs.

**Step 3 — Resolve the model**

The backend obtains available video metadata, such as title, description and creator.

It searches for a matching model page or configuration inventory. Rebrickable is the initial preferred source, but the resolution layer must not be tightly coupled to it.

For the known test case, the expected target is the Velociraptooth alternate build by Schwimpy, MOC-37396, based on LEGO set 31058.

**Step 4 — Retrieve the parts list**

The application retrieves or imports the configuration's inventory.

The inventory should contain, where available:

- LEGO part identifier.
- Part name.
- Colour identifier and name.
- Required quantity.
- Reference image.
- Source model identifier.
- Source URL.
- Confidence or verification status.

**Step 5 — Load the tray photograph**

For the MVP, the photograph already exists on the backend or in configured local storage.

The system retrieves the latest available tray photograph. At this stage, freshness is managed manually; automated camera capture comes later.

**Step 6 — Identify required pieces**

The image-processing pipeline identifies visible pieces and their positions, then compares the identified candidates with the required inventory.

For the initial experiment, use an existing LEGO recognition API if it supports the necessary image input and identification workflow.

Do not assume that a part-recognition API supports whole-tray object detection. Verify this in a technical spike.

**Step 7 — Generate annotated image**

The system creates an annotated copy of the tray photograph with a numbered circle around each confidently matched physical piece.

The numbers correspond to entries in a result legend.

Example:

- 1 — Black plate, candidate part ID
- 2 — Grey connector, candidate part ID
- 3 — Red brick, candidate part ID

The application must not assign the same physical piece to multiple required targets unless the use case explicitly permits it.

**Step 8 — Display results**

The child sees the annotated photograph, with large, readable numbers and a simple status message.

For the initial prototype, the image can be displayed on the web page opened by the PWA.

---

## 4. Proposed system architecture

Use a modular architecture so the YouTube resolver, catalogue integration and image-recognition implementation can evolve independently.

### High-level components

```text
YouTube Share
     |
     v
Android Share Target
     |
     v
Angular PWA
     |
     v
ASP.NET Core API
     |
     +--> Video Metadata Resolver
     |
     +--> Build / Configuration Resolver
     |          |
     |          v
     |     Rebrickable or other catalogue
     |
     +--> Parts Inventory Normalizer
     |
     +--> Tray Image Repository
     |
     +--> LEGO Recognition Adapter
     |
     +--> Part Matching Engine
     |
     +--> Annotation Renderer
     |
     v
Annotated Image + Match Results
     |
     v
Angular PWA
```

### Responsibilities

**Angular PWA**

- Receive and display the shared URL.
- Show processing progress.
- Present the resolved model for confirmation if necessary.
- Display the final annotated image.
- Provide simple retry and error states.

**ASP.NET Core API**

- Orchestrate the workflow.
- Validate inputs.
- Manage model resolution and inventory retrieval.
- Call external APIs through adapters.
- Store intermediate results and diagnostics.
- Coordinate matching and image annotation.

**Video metadata resolver**

- Parse YouTube URLs.
- Extract the video identifier.
- Retrieve accessible metadata using a permitted method.
- Identify potential references to the build, creator, original model or instructions.

**Build resolver**

- Search supported sources for the corresponding model.
- Rank candidate configurations.
- Return a resolved model only when there is sufficient evidence.
- Request user confirmation when multiple candidates are plausible.

**Inventory normalizer**

- Convert source-specific inventory data into a common internal representation.
- Preserve original part and colour identifiers.
- Expand quantities into individual target instances for matching.

**Recognition adapter**

- Provide a stable internal interface for identifying LEGO pieces from images or image crops.
- Initially integrate an existing recognition API.
- Allow a later implementation using a vision model, object detector or embedding-based retrieval.

**Matching engine**

- Compare target inventory items with detected tray objects.
- Account for part identity and colour.
- Assign physical detections to required target instances.
- Prevent duplicate assignment of one physical object.

**Annotation renderer**

- Draw numbered circles around matched objects.
- Generate a legend linking numbers to required part descriptions.
- Store match confidence and image coordinates.
- Produce a new image without modifying the original.

---

## 5. Critical technical constraint: recognition versus localisation

This is the highest-risk technical assumption in the project.

Identifying a LEGO part from a photograph and locating that part within a photograph of hundreds of pieces are different capabilities.

A recognition API may accept one isolated LEGO piece and return its likely part identity. It may not accept an entire tray photograph and return bounding boxes for every visible object.

The MVP must therefore separate these operations.

### 5.1 Required capabilities

The system needs:

1. **Object localisation:** identify where individual LEGO pieces appear in the tray photograph.
2. **Part recognition:** infer the identity and colour of each detected piece.
3. **Inventory matching:** compare detected parts with the required configuration.
4. **Coordinate preservation:** map each result back to the original photograph.

### 5.2 API-first strategy

Conduct a technical spike to determine whether the selected recognition API can:

- Accept the whole tray image.
- Identify multiple pieces in one request.
- Return bounding boxes or coordinates for each identified object.
- Return part identifiers and colour information.
- Handle the number and size of pieces in the photograph.
- Provide sufficiently reliable results for similar-looking parts.

Brickognize is a candidate for testing individual-part recognition. Its capabilities must be verified against the current API documentation and actual images.

If it cannot localise objects in a whole-tray photograph, the API alone cannot deliver the required result.

The first implementation should then test one of these alternatives:

- A separate object detector to locate individual pieces, followed by recognition of each crop.
- A pretrained vision model that identifies objects and produces candidate regions.
- An image-embedding approach that ranks candidate crops against catalogue images.

The initial deliverable should include the results of this investigation rather than silently assuming the API can solve the entire task.

---

## 6. YouTube URL and model resolution

The application must distinguish a video from the model being built in that video.

A video can contain multiple builds, as demonstrated by the test video containing multiple alternative models of LEGO set 31058.

### Resolution strategy

Use the following ordered process:

1. Parse the YouTube URL.
2. Retrieve accessible video title, description and creator metadata.
3. Look for explicit links to instructions, model pages, part lists or downloadable files.
4. Search the supported model catalogue using the title, creator, set number and model name.
5. Rank candidate models using available evidence.
6. Retrieve the parts inventory for the selected model.
7. Ask for confirmation if the result is ambiguous.

Do not download and analyse the entire video when a reliable model link or parts inventory already exists.

### Resolution confidence

Store a resolution status:

- `ResolvedAutomatically`
- `NeedsConfirmation`
- `InventoryUnavailable`
- `NotFound`
- `SourceAccessDenied`

A successful HTTP request is not sufficient evidence that the correct build has been identified.

### Fallbacks

If a model cannot be resolved:

- Display the most likely candidates with their reference images.
- Allow the parent to select the correct model.
- Permit a model URL or inventory file to be supplied directly.

Automatic frame analysis is a future fallback, not a dependency of the first MVP.

---

## 7. Catalogue and inventory integration

### Preferred source

Start with Rebrickable, because the project already has a known target model there.

Reference model:

- Model: Velociraptooth.
- Designer: Schwimpy.
- MOC identifier: MOC-37396.
- Base LEGO set: 31058.

The source page is:

https://rebrickable.com/mocs/MOC-37396/Schwimpy/31058-velociraptooth/

### Integration requirements

Investigate the available access methods in this order:

1. Public model page and inventory.
2. Official API, where the required inventory is supported.
3. Downloadable inventory or instruction files.
4. A manually imported inventory for the first end-to-end experiment.

Do not assume the API exposes every custom model inventory. Confirm the access method and licensing conditions before implementing automated retrieval.

### Normalized inventory schema

Each required part should be represented internally as:

```json
{
  "modelId": "MOC-37396",
  "source": "Rebrickable",
  "parts": [
    {
      "partId": "SOURCE_PART_ID",
      "partName": "Source part name",
      "colourId": "SOURCE_COLOUR_ID",
      "colourName": "Source colour name",
      "quantity": 2,
      "referenceImageUrl": null
    }
  ]
}
```

This is an illustrative schema, not a claim about the exact inventory contents.

Use stable identifiers rather than part names alone. Preserve source identifiers because different catalogues may use different representations.

The normalizer should support an inventory file import so that recognition experiments can proceed independently of catalogue access.

---

## 8. Tray image management

Live camera capture is explicitly deferred.

The MVP should support preloaded tray photographs.

### Initial setup

An administrator or parent can:

- Upload a tray photograph.
- Assign it an identifier.
- Record its source and capture date.
- Replace it with a newer photograph.
- Choose the photograph used for a search.

For the first experiment, use one tray photograph containing a representative collection of non-overlapping pieces.

### Image assumptions

The first test dataset should have:

- A single visible layer of pieces.
- Minimal overlap.
- Reasonably consistent lighting.
- Sufficient image resolution.
- A plain or visually uncluttered background.
- All or most required pieces visible.

The system should retain the original image dimensions and any image transformations used during processing.

### Coordinate convention

All final match coordinates must refer to the original tray photograph, not a resized crop.

For the initial implementation, bounding boxes are preferable to relying solely on a single centre point. The annotation renderer can calculate the circle centre from each matched bounding box.

Store coordinates in a documented format, preferably normalised values between 0 and 1, so that the frontend can render results at different display sizes.

---

## 9. Matching algorithm

The matching engine must map each required target instance to one visible physical piece.

### Inputs

- Required inventory with part IDs, colours and quantities.
- Tray detections with coordinates, candidate identities, colours and confidence scores.

### Processing

1. Expand each inventory quantity into individual required instances.
2. Filter tray detections by candidate part identity.
3. Compare colour identifiers or normalised colour estimates.
4. Rank possible matches by identity confidence and colour compatibility.
5. Assign physical detections to target instances.
6. Prevent one physical detection from satisfying multiple instances.
7. Mark targets without a sufficiently reliable match as unresolved.

Where multiple physical pieces are visually identical, assign them distinct target numbers if the model requires multiple copies.

The system does not need to distinguish one identical copy from another; it needs to highlight the correct number of physical copies.

### Match states

Use explicit result states:

- `Matched`
- `MultipleCandidates`
- `NotVisible`
- `LowConfidence`
- `RecognitionFailed`

Do not force an incorrect match merely to maximise the number of completed results.

### Confidence

Keep recognition confidence, inventory-match confidence and model-resolution confidence separate.

A single combined confidence score can hide the source of errors and make experimentation difficult.

---

## 10. Annotation and output requirements

The primary output is an annotated copy of the original tray photograph.

### Visual requirements

- Numbered circles with high contrast.
- Large, readable numbers.
- Circles centred on detected objects.
- No modification of the original image.
- A legend mapping each number to the corresponding required part.
- A clear visual distinction between confirmed and uncertain matches.
- A summary of matched and unresolved targets.

### Example output

Suppose the selected model has 20 unique required target instances and all 20 can be found.

The application generates an image containing circles numbered 1–20, plus a legend such as:

| Number | Part | Status |
|---|---|---|
| 1 | Part A, colour X | Matched |
| 2 | Part B, colour Y | Matched |
| 3 | Part C, colour Z | Matched |
| … | … | … |
| 20 | Part T, colour X | Matched |

These are illustrative entries only.

If the inventory contains 156 pieces, the output may contain up to 156 individual target assignments, but the first test should use a smaller subset to validate localisation and matching.

If the same type of part is required more than once, use distinct numbered circles for distinct physical pieces.

The system should also generate a machine-readable result containing target identifiers, coordinates, match status and confidence.

---

## 11. Suggested technology stack

The implementation should favour familiar, replaceable components rather than premature infrastructure.

| Area | Initial choice |
|---|---|
| Frontend | Angular |
| Mobile integration | Installable PWA and Android Web Share Target |
| Backend | ASP.NET Core Web API |
| Data model | C# records / DTOs |
| Database | SQLite for the prototype |
| Image processing | OpenCV or equivalent image-processing library |
| LEGO recognition | Existing API, subject to capability validation |
| Image storage | Local storage for development |
| Configuration | Environment variables and development secrets |
| Testing | xUnit for backend logic; frontend unit tests |
| Deployment | Local development initially; Docker Compose when useful |

Use interfaces around external services:

- `IVideoMetadataResolver`
- `IBuildResolver`
- `IPartsInventoryProvider`
- `ITrayImageRepository`
- `ILegoRecognitionProvider`
- `IPartMatchingService`
- `IImageAnnotationService`

These interfaces are illustrative names. The implementation may use a different naming convention while preserving the separation of responsibilities.

The objective is to swap a recognition API for a vision-model implementation without rewriting the URL resolver or inventory logic.

---

## 12. Proposed API contracts

The backend should expose a small number of endpoints.

### `POST /api/builds/resolve`

Accept a YouTube URL and return the candidate build or configuration.

Example request:

```json
{
  "youtubeUrl": "https://youtu.be/BEyoBYjPNpY"
}
```

Example response:

```json
{
  "status": "ResolvedAutomatically",
  "videoId": "BEyoBYjPNpY",
  "modelId": "MOC-37396",
  "modelName": "Velociraptooth",
  "inventoryAvailable": true
}
```

These are example values based on the known test case, not a guarantee that the resolver will automatically discover the model.

### `GET /api/builds/{modelId}/inventory`

Return the normalized parts inventory.

### `GET /api/trays`

Return the available preloaded tray photographs and their metadata.

### `POST /api/searches`

Start a search using a resolved build and selected tray image.

Example request:

```json
{
  "modelId": "MOC-37396",
  "trayId": "TRAY-001"
}
```

Example response:

```json
{
  "searchId": "SEARCH-001",
  "status": "Processing"
}
```

The implementation can return the completed result synchronously for small images, but should support a processing status if external API calls or image processing take too long.

### `GET /api/searches/{searchId}`

Return search status and, when complete, the annotated image reference and structured match results.

The precise DTOs should be finalised during implementation, but all endpoints should use stable identifiers and explicit status values.

---

## 13. Functional requirements

### FR-01: Accept shared URLs

The application must accept YouTube links shared through Android's native share sheet.

### FR-02: Parse URL formats

The application must support standard YouTube watch URLs and shortened share URLs, and reject unsupported or malformed inputs clearly.

### FR-03: Resolve model

The backend must attempt to identify the specific model represented by the video, using available metadata and catalogue sources.

### FR-04: Retrieve inventory

The system must retrieve or import a normalized parts inventory for the selected model.

### FR-05: Select tray

The system must load a configured tray photograph without requiring live camera capture.

### FR-06: Recognize pieces

The system must attempt to identify visible tray objects and retain their coordinates and recognition confidence.

### FR-07: Match inventory

The system must map required inventory instances to unique physical detections wherever possible.

### FR-08: Annotate results

The system must generate an annotated image with numbered circles and a corresponding legend.

### FR-09: Handle uncertainty

The system must report unresolved or low-confidence matches rather than silently returning unsupported results.

### FR-10: Capture diagnostics

The system must record which stage failed: URL resolution, inventory retrieval, object localisation, part recognition, matching or annotation.

---

## 14. Non-functional requirements

### Performance

For the first prototype, target completion of a search within 60 seconds for a modest tray image and a small inventory, subject to external API latency.

Measure actual performance before setting a stricter service-level objective.

### Reliability

External API failures must not corrupt the stored inventory or tray image.

A failed search must be retryable.

### Security

- Keep API keys on the backend.
- Do not expose catalogue API credentials in Angular code.
- Validate incoming URLs.
- Avoid unrestricted server-side fetching of arbitrary URLs; restrict external requests to approved hosts to reduce SSRF risk.
- Limit image upload sizes and validate image formats.
- Do not require personal information about the child.

### Privacy

The application is intended for family use. Tray images should be stored only as long as needed for the experiment unless the administrator chooses to retain them.

No child account or behavioural analytics are required for the MVP.

### Maintainability

External APIs must be accessed through adapters. Recognition and catalogue-specific DTOs must not leak throughout the domain layer.

---

## 15. Testing and evaluation strategy

The project is experimental. Recognition quality must be measured on real LEGO photographs, not inferred from vendor claims.

### Test dataset

Create a small labelled dataset containing:

- One tray photograph.
- Between 20 and 50 visible LEGO pieces.
- A known list of piece identities and colours.
- Ground-truth coordinates for visible pieces.
- A subset of pieces required by a known model.
- Some similar-looking parts to expose false matches.

For each target, record whether the part is visible and its correct identity and location.

### Metrics

Measure:

**Detection recall:** What fraction of visible pieces were detected?

**Identification accuracy:** What fraction of detected pieces were assigned the correct identity?

**Matching precision:** What fraction of the highlighted pieces were actually correct?

**Target coverage:** What fraction of required target instances were successfully located?

**Annotation accuracy:** Are the circles positioned over the correct physical pieces?

**Latency:** How long does each pipeline stage take?

### Initial acceptance criteria

For a deliberately small test with 10–20 distinct, visible targets:

- The system accepts a shared YouTube URL.
- The selected model and its inventory can be inspected and verified.
- The tray photograph is processed without live camera access.
- At least 80% of the test targets are correctly located in the first experiment.
- Every displayed match includes coordinates and a status.
- Unresolved targets are reported rather than falsely marked as correct.
- The annotated image is generated and displayed.

The 80% target is an experimental benchmark, not a claim that the chosen API can already achieve it.

For the child-facing release, measure accuracy on a larger, representative test set before relying on automatic results.

---

## 16. Implementation phases

### Phase 0 — Technical spikes

Before building the complete application, answer two questions:

1. Can a YouTube URL be resolved to the correct model and inventory using the available metadata and catalogue sources?
2. Can the selected recognition API identify and localise LEGO pieces in the actual tray photograph?

Produce a short technical report and a small test dataset.

**Deliverable:** Evidence that the two main external dependencies are usable, or a documented fallback for each.

### Phase 1 — URL and inventory pipeline

Implement URL parsing, metadata resolution, model selection and inventory normalization.

Use the known Velociraptooth model as the baseline.

Allow the inventory to be supplied manually if automated resolution fails.

**Deliverable:** A YouTube URL or model URL produces a verified normalized inventory.

### Phase 2 — Recognition and matching

Load a pre-existing tray photograph and identify candidate objects.

Integrate the recognition API and implement inventory matching.

If the API cannot handle whole-tray images, add the smallest necessary localisation component.

**Deliverable:** Structured match results with coordinates and confidence.

### Phase 3 — Image annotation

Draw numbered circles and generate a legend.

Display the annotated image in the Angular application.

**Deliverable:** An end-to-end search that produces a useful annotated image.

### Phase 4 — Android sharing

Configure the PWA's Web Share Target integration and test it on the actual Android device.

Ensure the shared URL is correctly received when launching the app from YouTube.

**Deliverable:** YouTube Share → LEGO Finder → results.

### Phase 5 — Experimental improvements

Only after the baseline works:

- Compare API recognition against a pretrained vision model.
- Improve matching of visually similar parts.
- Add alternative candidate rankings.
- Test multiple tray photographs.
- Improve uncertainty handling.
- Add live camera capture and image freshness management.

---

## 17. Known risks and mitigations

| Risk | Mitigation |
|---|---|
| Video title does not identify the build | Use descriptions, creator links, catalogue search and manual confirmation. |
| Model inventory is not accessible through the API | Import the inventory from a public page or supported downloadable file. |
| Recognition API cannot localise objects in a tray | Add a separate detector or evaluate a vision model. |
| Similar LEGO parts are confused | Retain candidate alternatives and confidence scores. |
| A target piece is not visible | Return `NotVisible` or `LowConfidence`; do not invent a location. |
| The tray photograph is outdated | Clearly show its capture date and allow replacement. |
| Android does not show the PWA in the share sheet | Verify browser support and fall back to a small native Android share-target application if necessary. |
| One piece is assigned to multiple targets | Use unique detection IDs and enforce one-to-one assignment. |
| External API changes or rate limits | Use adapters, bounded retries and cached inventories where permitted. |

---

## 18. Definition of done

The MVP is complete when the following workflow succeeds:

1. A YouTube URL is shared to LEGO Finder from Android.
2. The application resolves or allows selection of the corresponding LEGO build.
3. A verified parts inventory is available.
4. A preloaded tray photograph is selected.
5. The recognition pipeline identifies candidate pieces and their image coordinates.
6. The matching engine finds required pieces without duplicate physical assignments.
7. The annotation renderer generates an image with numbered circles and a matching legend.
8. The application displays matched and unresolved targets clearly.
9. The process is repeatable with another model and tray photograph.
10. The code contains replaceable interfaces for catalogue resolution and visual recognition.

The MVP is not considered successful merely because it retrieves a parts list or produces an annotated image. The critical outcome is whether the highlighted locations correspond to the actual required pieces.

---

## 19. Instructions for the implementing AI model

Implement this project incrementally. Do not attempt to build all phases in one pass.

First inspect the existing repository and report its structure, current technology and development constraints. Then implement Phase 0 and document the findings.

Do not fabricate API capabilities, catalogue data, part identifiers or model inventories. Verify all external dependencies against their current documentation and actual responses.

Keep the application small and modular. Avoid introducing microservices, Kubernetes, a vector database or custom model training without evidence that they are necessary.

Prefer the simplest solution that proves the end-to-end workflow.

At each phase, provide:

- Files created or modified.
- Architecture decisions and rationale.
- Commands to run locally.
- Configuration and secrets required.
- Automated test results.
- Known limitations.
- A demonstration of the completed functionality.

If a technical dependency is blocked, stop and explain the issue before building a speculative workaround.

**First milestone:** demonstrate that a verified inventory of required LEGO pieces can be matched to physical pieces in a pre-existing tray photograph and returned as a correctly annotated image. Once this works, integrate the Android Share workflow around it.

