# 3-way merge of OCAD maps

> **Status: planned, nothing built.** Decisions below were settled with the product
> owner on 2026-11-16. Test fixtures exist in `tests/TestData/merge/`.
> Design mockups: <https://claude.ai/artifact/5Fxg9tMzTida3tS9kHcFet> (private canvas;
> boards 0, A, C, D are the chosen design, E/F compare overlay styles, B is dropped).

## The problem

Two mappers each take a copy of the same map and edit it. Today the only way to
combine their work is by hand in OCAD. The merge tool takes the common ancestor
(**base**) and the two edited copies (**branches**), works out what each mapper
changed, merges what doesn't clash, lets the user decide what does, and saves a new
`.ocd`. It is git's 3-way merge, made visual.

## Decisions

| Topic | Decision |
| --- | --- |
| Inputs | Ad hoc: the user picks base + two branch files. No folder layout required; base may optionally be picked from a project's releases. |
| Branches | Exactly two (classic 3-way). More mappers = merge pairwise. |
| Names | Editable labels, default the file name (e.g. "Kari", "Ola"). Kari = teal, Ola = orange, conflict = crimson. |
| Unit of decision | Regions first (rectangle / lasso), single objects when needed, bulk per branch or symbol. |
| Object identity | **CreationDate** (ms precision), whatever the symbol; geometry fallback when no date matches. Slot numbers are *not* identity. |
| "Changed" | The object record differs, ignoring ModificationDate, with a geometry tolerance of **0.1 m on the ground** (converted with the map scale). |
| Conflicts | (1) same object edited by both, incl. edit vs delete; (2) spatial overlap: a Kari change and an Ola change whose bounding boxes come within a buffer (default 10 m, adjustable); (3) the same symbol definition changed by both. |
| Clean changes | Pre-accepted, shown on the map, can be rejected. Only conflicts *must* be resolved. |
| Resolutions | Take Kari · Take Ola · Keep both · Keep base. "Keep both" gives the second copy a **new CreationDate**. |
| Non-object data | 3-way per item: colours and symbols per item; each settings block (setup/georef, background maps, layouts, parameter strings) from whichever branch changed it, base if neither, a Kari/Ola choice if both. |
| Output | Save as a new `.ocd`. **Byte-faithful**: start from base's bytes and splice records; untouched data is preserved exactly. Inputs are never written. |
| Formats | OCAD 11, 12, 2018 and 2019+ native. |
| Performance | Must stay usable on `large_map.ocd`-sized maps (~10 MB). |
| Resume | Decisions kept in IndexedDB, keyed by the three files' hashes. Lost if browser data is cleared; acceptable. |
| Layout | Board **A** is the main screen (one map + side panel), **C**'s region tools live inside it, **D** is the conflict walker opened from A. |
| Overlay | **Vector outlines on a greyed map** (board E/F style 1): the existing renderer draws tiles, greyed by CSS; the engine sends changed shapes as GeoJSON; Leaflet draws them. Solid = added, dashed = changed, ring × = deleted, hatched crimson = conflict. |
| Region tools | **Leaflet-Geoman** (MIT, free edition): rectangle + polygon/lasso, regions editable after drawing. Regions are UI state only, never written to the file. |
| Region borders | An object crossing a region's border is **not** decided by the region; it is listed as needing a decision. |
| Suggest regions | In the first region release: cluster changes by branch and location. |
| Entry point | New `/merge` page, linked from the Workspace and Viewer headers. Norwegian and English from the start (TranslationTests). |
| Delivery | Engine first, one PR to develop per milestone (below). |

## What OCAD actually does to a file

Observed while making the fixtures (OCAD 2020.8.6, saving as 2018):

- Editing an object rewrites it **in its own slot**, keeps **CreationDate**, updates ModificationDate.
- Deleting leaves the record in its slot with index **status 3**. Readers must skip status ≠ 1 (2 = hidden).
- New objects take the next free slot, so the **same slot holds different objects** in two branches.
- ModificationDate can change with no real edit.
- ServerObjectId is −1 without OCAD Server.
- **Object › Change Creation Date…** exists, so duplicate dates are possible and must be handled (fall back to geometry; never silently pair two objects).

Still to verify: whether cut/paste and duplicate keep CreationDate; the 2019+ native
record layout; text objects; symbol graphics changes; a symbol number added by both.

## Architecture

```
Graphics2D ← MapModel ← Map_SkiaStd ← OcadViewer.Core ← RenderCli
                                            ↑
                    OcadViewer.Versioning ← OcadViewer.Wasm
                    OcadViewer.Merge      ←┘
```

**`OcadViewer.Merge`** — new, pure, depends on nothing (like Versioning). Add it to
`ProjectDependencyTests.Allowed` with `[]`, and `OcadViewer.Wasm` gains it.

- `OcadFile` — reads the raw file: header, symbol/object/string index blocks, records as
  byte slices with their parsed headers (symbol, type, status, dates, coordinates,
  bounding box). Versions 11, 12, 2018, 2019+. No PurplePen dependency: we need the
  bytes, not a drawing model.
- `ObjectMatcher` — pairs base↔branch objects by CreationDate, falling back to
  geometry; reports unmatched and ambiguous pairs.
- `ThreeWayDiff` — per object: unchanged / added / changed / deleted per branch, using
  the 0.1 m tolerance and ignoring ModificationDate; same for symbols, colours and
  settings blocks.
- `ConflictDetector` — same-object, edit-vs-delete, spatial overlap (bbox + buffer),
  symbol-definition conflicts.
- `MergeDecisions` — default (clean changes accepted) plus user overrides per object,
  region and symbol; serialisable for IndexedDB.
- `OcadSplicer` — writes the result: copies base, appends/replaces/marks-deleted object
  records, rewrites index blocks, adds symbols, assigns new CreationDates for "keep both".
- `ChangeGeometry` — GeoJSON (paper mm, as Leaflet already uses `L.CRS.Simple`) of every
  change for the overlay and for hit-testing.

**`OcadViewer.Merge.Tests`** — NUnit, against `tests/TestData/merge/`. Golden checks:
the README's expected table, and **merge(base, base, base) == base byte for byte**.

**`OcadViewer.Wasm`** — `/merge` page, components for the setup screen (board 0), the
map + side panel (A), regions tab (C) and walker (D). The result map is rendered by
handing the spliced bytes to the existing `LoadMap`; greying is a CSS filter on the
tile layer. JS: a `mergeOverlay.js` module (GeoJSON layers, selection) and
Leaflet-Geoman for regions.

## Milestones

1. **Engine.** `OcadViewer.Merge` + tests: read, match, diff, detect, decide (defaults),
   splice. Done when every row of the fixtures' expected table passes, the identity
   merge is byte-identical, and a merged fixture opens in OCAD without complaint.
   Includes the open verifications above (2019+ format, cut/paste dates).
2. **Read-only change view.** `/merge`, setup screen with checks, board A map with
   outline overlay, side panel lists (Conflicts / Kari / Ola / Symbols), click to
   select. No saving.
3. **Decisions + save.** Take/keep buttons, reject clean changes, symbol and settings
   choices, undo, Save merged `.ocd` via the save picker.
4. **Regions.** Leaflet-Geoman rectangle/lasso, Regions tab, per-region
   Kari/Ola/Both/Base/Per object, border-crossing objects flagged, Suggest regions.
5. **Conflict walker.** Board D: Base | Kari | Ola zoomed panes, keys 1–4, filmstrip.
6. **Resume.** IndexedDB keyed by file hashes; "Unfinished merge found" on the setup screen.

## Risks

- **Writer correctness** is the whole product: a file OCAD rejects or silently
  misreads loses a mapper's work. Mitigate with the identity test, fixture merges
  opened in OCAD (it can be driven by computer use on this machine), and never
  touching the inputs.
- **2019+ native format** may differ from 2018; read it before promising support.
- **Performance** on 10 MB maps in single-threaded WebAssembly: matching is a hash
  join on CreationDate; overlap needs a spatial grid, not all-pairs.
- **Identity breaks** (cut/paste, Change Creation Date, copy between files) degrade to
  delete+add. That is safe but noisy; the geometry fallback limits it.
