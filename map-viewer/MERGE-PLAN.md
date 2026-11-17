# 3-way merge of OCAD maps

> **Status: all six milestones implemented, in stacked pull requests (engine #147,
> view #148, decisions #149, regions #150, walker #151, resume #152).**
> Decisions below were settled with the product owner on 2026-11-16. Test fixtures
> and their expected results are in `tests/TestData/merge/`.
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
| Inputs | Ad hoc: the user picks base + two branch files. No folder layout required. |
| Branches | Exactly two (classic 3-way). More mappers = merge pairwise. |
| Names | Editable labels, default the file name (e.g. "Kari", "Ola"). Kari = teal, Ola = orange, conflict = crimson. |
| Unit of decision | Regions first (rectangle / lasso), single objects when needed, bulk per branch or symbol. |
| Object identity | **CreationDate** (ms precision), whatever the symbol; copies sharing a date resolved by nearest geometry; identical geometry when there is no date. Slot numbers are *not* identity. |
| "Changed" | The record differs, ignoring dates (and the colour field of objects that have a symbol), with a geometry tolerance of **0.1 m on the ground** (converted with the map scale). |
| Conflicts | (1) same object edited by both, incl. edit vs delete and both added; (2) spatial overlap: a Kari change and an Ola change whose extents come within a buffer (default 10 m, adjustable); (3) the same symbol or setting changed by both. |
| Clean changes | Pre-accepted, shown on the map, can be rejected. Only conflicts *must* be resolved. |
| Resolutions | Take Kari · Take Ola · Keep both · Keep base. "Keep both" (objects only) gives the second copy a **new CreationDate**; on a change only one branch made it means taking that branch. |
| Symbols in use | The merged map never has an object without its symbol. A symbol a branch **added** is kept automatically while objects in the merge use it (its reject is locked, with a note). A symbol a branch **deleted** that objects in the merge still use - the other branch's new or edited objects, or objects the deleting branch kept when OCAD asked - is a **conflict**; taking the deletion removes every object that uses it, and the panel lists them first. |
| Sprint maps | Sprint maps (ISSprOM 2019, 1:4000) merge exactly like forest maps; `SprintMapTests` runs one scenario on both. |
| Non-object data | 3-way per symbol (by number) and per parameter string (colours by number, others by type and first field). View-state strings (zoom, selection, recent files, display, view) are kept from base. Strings tied to an object slot are kept from base. |
| Output | Save as a new `.ocd`. **Byte-faithful**: base's bytes are copied, records appended and index entries repointed. Inputs are never written. |
| Formats | OCAD 11, 12 and 2018. OCAD 2019 and 2020 save in the 2018 format, so that covers them. **All three files must be the same version.** |
| Saving | Only when every conflict and overlap has a decision. |
| Performance | Must stay usable on `large_map.ocd`-sized maps (~10 MB). |
| Resume | Decisions kept in IndexedDB, keyed by the three files' hashes. Lost if browser data is cleared; acceptable. |
| Layout | Board **A** is the main screen (one map + side panel), **C**'s region tools live inside it, **D** is the conflict walker opened from A. |
| Overlay | **Vector outlines on a greyed map** (board E/F style 1): the existing renderer draws tiles, greyed by CSS; the engine sends changed shapes; Leaflet draws them. Solid = added, dashed = changed, ring × = deleted, hatched crimson = conflict. |
| Region tools | **Leaflet-Geoman** (MIT, free edition): rectangle + polygon/lasso, regions editable after drawing. Regions are UI state only, never written to the file. |
| Region borders | An object crossing a region's border is **not** decided by the region; it is listed as needing a decision. |
| Suggest regions | In the first region release: cluster changes by branch and location. |
| Entry point | New `/merge` page, linked from the Workspace and Viewer headers. Norwegian and English from the start (TranslationTests). |
| Delivery | Engine first, one stacked PR per milestone (below). |

Decisions taken during implementation, for the product owner to confirm (see the PRs):

- Files of different OCAD versions are refused rather than converted.
- Saving is blocked until every conflict and overlap is decided.
- Overlap groups join transitively (a chain of neighbours is one group).
- A colour or setting can only be taken from one file; "Keep both" applies to objects.
- Saving downloads `<base>-merged.ocd` rather than using a save picker: it works in every browser and can never overwrite an input.
- Suggested regions join changes within 50 m and reach 10 m beyond them.
- Regions are drawn as outlines only, so the changes inside stay clickable.
- The conflict walker exists only while open (its number keys must not act on the map screen) and parses the three files into its own panes each time it opens.
- Resuming needs the same three files picked again; only decisions and regions are kept in the browser, never the maps, and the record is removed once the merged map is saved.

## What OCAD actually does to a file

Observed while making the fixtures (OCAD 2020.8.6); details in `tests/TestData/merge/README.md`:

- Editing an object rewrites it **in its own slot**, keeps **CreationDate**, updates ModificationDate.
- Deleting leaves the record in its slot with index **status 3**.
- New objects take the next free slot, so the **same slot holds different objects** in two branches.
- **Cut/paste, copy/paste and Duplicate keep the creation date**: a copy shares its original's date.
- ModificationDate and the colour field of symbol objects change with no visible edit.
- Saving as OCAD 12 compacts slots; saving as OCAD 11 from OCAD 2020 zeroes creation dates.
- The display-mode string is rewritten on every save, which is why view-state strings are excluded.

Still not covered by a fixture: symbol graphics changes, background-map strings, line text (text along a curve).

## Architecture

```
Graphics2D ← MapModel ← Map_SkiaStd ← OcadViewer.Core ← RenderCli
                                            ↑
                    OcadViewer.Versioning ← OcadViewer.Wasm
OcadViewer.Ocd ←    OcadViewer.Merge      ←┘
```

**`OcadViewer.Ocd`** — reads and edits OCAD files at the byte level, depends on nothing
(enforced by `ProjectDependencyTests`).

- Public: `OcadFile` (objects, symbols, strings, version, scale), `OcadObject`,
  `OcadSymbol`, `OcadSetting` (with `SameContent`, told which fields to ignore by
  `ObjectFields` / `SymbolFields`), `OcadEditor` (replace / add / delete on a copy,
  byte-faithfully), and the small value types. `PublicSurfaceTests` pins that list.
- Internal: `FileLayout` (the one place that knows byte offsets per version),
  `IndexChain`, raw records, Delphi dates. Only `OcadViewer.Ocd.Tests` sees them.

**`OcadViewer.Merge`** — pure, depends only on `OcadViewer.Ocd`.

- `Matching/` — `ObjectMatcher` (identity) and `ContentComparer` (what "changed" means).
- `Analysis/` — `ThreeWay` (the 3-way rule, once for every kind of item),
  `MergeAnalyzer`, `OverlapDetector`, `SettingIdentity`.
- `Decisions/` — `MergeDecisions` (plain data by stable key) and `MergeResolution`
  (the single precedence rule: item decision, then overlap decision, then default).
- `Writing/` — `MergePlanner` (choices to operations) and `OcadSplicer` (operations
  to `OcadEditor` calls, fresh creation dates for "keep both"); `MergeWriter`
  composes them.

**`OcadViewer.Merge.Tests`** — NUnit against `tests/TestData/merge/`: the README's
table row by row, and merge(base, base, base) == base byte for byte.

**`OcadViewer.Wasm`** — `/merge` page and its components; the merged result is
rendered by handing the spliced bytes to the existing renderer.

### Testing on the development machine

Windows application control on the development machine refuses to load some freshly
built assemblies (0x800711C7), test assemblies included, and which ones changes from
build to build. Do not try to get around it. Run `dotnet test` normally; when it is
blocked, the PR build on GitHub is the authoritative run.

## Milestones

1. **Engine.** Read, match, diff, detect, decide, splice. *Done*: every row of the
   fixtures' table passes, the identity merge is byte-identical, and the merged
   fixtures open in OCAD 2020 showing exactly the chosen versions.
2. **Read-only change view.** *Done*: `/merge`, setup screen with checks, board A map with
   outline overlay, side panel lists (Conflicts / Kari / Ola / Symbols), click to select.
3. **Decisions + save.** *Done*: take/keep buttons, reject clean changes, symbol and settings
   choices, undo, Save merged `.ocd`.
4. **Regions.** *Done*: Leaflet-Geoman rectangle/lasso, Regions tab, per-region
   Kari/Ola/Both/Base/Per object, border-crossing objects flagged, Suggest regions.
5. **Conflict walker.** *Done*: board D: Base | Kari | Ola zoomed panes, keys 1–4, filmstrip.
6. **Resume.** *Done*: IndexedDB keyed by file hashes; "Unfinished merge found" on the setup screen.

## Risks

- **Writer correctness** is the whole product: a file OCAD rejects or silently
  misreads loses a mapper's work. Mitigated by the identity test, merged fixtures
  opened in OCAD, and never touching the inputs.
- **Performance** on 10 MB maps in single-threaded WebAssembly: matching is a hash
  join on CreationDate; overlap detection is all-pairs over *changes* (not objects),
  which is fine for hundreds of changes and should get a grid if merges get bigger.
- **Identity breaks** (Change Creation Date, OCAD 11 files without dates, copying
  between files) degrade to delete + add. Safe but noisy.
