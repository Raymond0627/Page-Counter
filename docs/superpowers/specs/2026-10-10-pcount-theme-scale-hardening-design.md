# Pcount — Theme Support & Large-Scale Scan Hardening — Design

Date: 2026-10-10
Status: Approved for planning
App: Lumeed Pcount (WinUI 3 / .NET 8, unpackaged)
Projects: `PdfPageCounter.Core`, `PdfPageCounter.App`, `PdfPageCounter.Tests`

## 1. Goal

Make Lumeed Pcount:

1. Reliably scan **300,000+ PDF files** on local and network/complex storage
   without crashing.
2. Show **correct per-folder counts** (files, pages, unique pages, duplicates)
   at **any nesting depth**, including very deep `parent/child/child/child/...`
   trees.
3. Support **System / Light / Dark** themes, with the choice persisted across
   launches.

Decisions confirmed with the user:

- Theme: System + Light + Dark, **persisted**.
- Duplicate definition: **unchanged** — same filename (case-insensitive) AND
  same page count.
- Target: **64-bit only** (x64 + ARM64). Drop x86.
- Tree UI: **keep WinUI `TreeView`**, fixed binding + lazy expansion.

## 2. Problem Analysis

### 2.1 Crash at ~300k files

- Every scanned file becomes a `PdfFileRecord` retaining three path strings
  (`Name`, `FullPath`, `ParentFolderPath`). At 300k files this is a large but
  survivable footprint on 64-bit — the real risks are the two below.
- The app currently builds as **x86** too. A 32-bit process is capped near 2 GB,
  so 300k files can trigger `OutOfMemoryException`.
- `MainPage.xaml` sets `IsExpanded="True"` on **every** tree node. WinUI
  materializes the entire visual tree at once, which for tens of thousands of
  folders can exhaust memory / hang the UI thread.

### 2.2 Incorrect counts on deep folders

In `FolderScanner.DiscoverAsync` (`FolderScanner.cs:146-184`) the directory and
file listings are created with the **lazy** `Directory.Enumerate*` APIs inside a
`try`, but are **iterated outside that try** (`foreach (var sub in subdirs)` at
line 159 and `foreach (var file in files)` at line 174). Exceptions raised
during lazy enumeration (a transient I/O fault, a permission error deep in a
subtree, a file removed mid-enumeration) therefore propagate out of
`DiscoverAsync`. They are swallowed by the catch around `await producer`
(`FolderScanner.cs:99-103`), which **silently ends the entire discovery pass** —
dropping every not-yet-visited branch. This is the observed "wrong number on
sub-sub-sub folders".

There is also **no reparse-point / cycle guard**. On "complex storage"
(junctions or directory symlinks, common on network shares), traversal can
revisit the same directory forever, growing the work stack until memory is
exhausted — a second crash vector.

### 2.3 Deep-folder display defects

- `MainPage.xaml` nests a `TreeViewItem` inside the `TreeView.ItemTemplate` while
  the `TreeView` itself has `ItemsSource`. This container-in-container pattern
  mis-renders and recycles deep nodes, so labels can appear under the wrong
  parent.
- `{x:Bind DisplayLabel}` / `{x:Bind BadgeText}` default to **OneTime**, so live
  label updates made by `FolderNodeViewModel.RefreshLabel()` are not observed.
- `MainViewModel.SyncTreeFromModel` runs every 500 ms and performs a fully
  recursive `Reconcile` whose insert path is O(n²). On a large tree this
  dominates CPU and contends with scanning.

## 3. Design

### 3.1 Theme subsystem (new, App layer)

**Components**

- `SettingsStore` — reads/writes a small JSON settings file at
  `%LOCALAPPDATA%\LumeedPcount\settings.json` using `System.Text.Json`
  (no new package). `ApplicationData.Current` is not usable because the app is
  unpackaged. Errors are swallowed; defaults are used on any failure.
- `ThemeService` — owns the persisted `AppTheme` value
  (`System | Light | Dark`), maps it to `ElementTheme`
  (`Default | Light | Dark`), and applies it. Exposes `Current`, `Apply(...)`,
  and a `Changed` event.

**Apply mechanism**

- Set `RequestedTheme` on the window's root content:
  `((FrameworkElement)App.MainAppWindow.Content).RequestedTheme = theme`.
  This cascades to the page, cards, `ListView`, title bar, and flyouts. The
  existing `MicaBackdrop` is retained.
- Called on startup (after the window is created and content is loaded) and on
  every user change. The selected value is saved immediately.

**UI**

- A `DropDownButton` with a sun/moon glyph in the top bar. Its flyout contains
  three radio options — *System*, *Light*, *Dark* — bound to the
  `ThemeService`. Default when no setting exists: **System**.

**Persistence format**

```json
{ "theme": "System" }
```

Unknown/missing values fall back to `System`.

### 3.2 Scanner robustness — crash + deep-count correctness

All changes are within `FolderScanner.DiscoverAsync` plus small model tweaks; the
public surface (`ScanAsync`, events) is unchanged.

- **Materialized, guarded enumeration.** Replace `Directory.EnumerateDirectories`
  / `Directory.EnumerateFiles` with `Directory.GetDirectories` / `GetFiles`
  (arrays) invoked **inside** the existing `try`, and wrap per-directory
  processing so a single folder that cannot be listed is reported through
  `FolderSkipped` and **skipped**, never aborting the whole scan. This preserves
  every branch the OS lets us read.
- **Reparse-point / cycle protection.** Skip directories whose attributes
  include `FileAttributes.ReparsePoint`, and maintain a visited set of
  normalized full paths so a directory is never traversed twice. This prevents
  infinite traversal on junctions/symlinks and prevents double-counting.
- **Counts as `long`.** `FolderNode` file/page/duplicate/error counts and the
  related `SummaryViewModel` metrics already surface as `long` in the UI; the
  model's `int` counters are widened to `long` to remove any overflow ceiling
  for extreme scans.
- **Memory reduction.** Drop the redundant `PdfFileRecord.ParentFolderPath`
  field (derive the parent from `FullPath` when attaching to a folder), reducing
  per-file string storage for 300k+ scans. `Name` and `FullPath` are retained;
  `Path.GetDirectoryName` is cheap and only used during attach.
- **Unchanged semantics.** Duplicate detection, unique-page total, and the
  post-order rollup stay exactly as specified and covered by existing tests.

### 3.3 UI tree — correct display + scalability

**Binding fix (`MainPage.xaml`)**

- Use the supported hierarchical pattern: the `TreeView.ItemTemplate` supplies
  the node **content**; child enumeration is provided through the tree item's
  `ItemsSource` bound to `Children`. The template must not create a second
  `TreeViewItem` container inside a tree that already has `ItemsSource`.
  (Exact WinUI 3 pattern verified against current docs during implementation.)
- `IsExpanded` is bound (TwoWay) to a VM property, **default collapsed**, so
  branches materialize only when the user expands them.
- `DisplayLabel` and `BadgeText` bind `Mode=OneWay` so `RefreshLabel()` updates
  are observed.

**Live-update strategy (`MainViewModel`)**

- During a scan, update only the **summary counters and progress text** (already
  driven by the 100 ms timer reading interlocked snapshot fields). Remove the
  per-tick tree mutation.
- Build the tree **once** when the scan completes or is cancelled (existing
  `BuildTreeFromModel`), then re-apply the current scope and refresh summary.
- Remove `_treeSyncTimer` and `SyncTreeFromModel`/`Reconcile`/`GetInsertIndex`.
- The file-detail `ListView` is populated only when a folder is selected
  (already the case) and remains virtualized.

### 3.4 Build configuration

- `PdfPageCounter.App.csproj`: set `<Platforms>x64;ARM64</Platforms>` (remove
  x86). `RuntimeIdentifier` continues to resolve to the host architecture.
- No change to Core or Tests target frameworks (`net8.0`).

## 4. Files Touched

New (App):
- `PdfPageCounter.App/Services/SettingsStore.cs`
- `PdfPageCounter.App/Services/ThemeService.cs`

Core:
- `PdfPageCounter.Core/Services/FolderScanner.cs`
- `PdfPageCounter.Core/Models/FolderNode.cs`
- `PdfPageCounter.Core/Models/PdfFileRecord.cs`

App:
- `PdfPageCounter.App/MainPage.xaml`
- `PdfPageCounter.App/MainPage.xaml.cs`
- `PdfPageCounter.App/ViewModels/MainViewModel.cs`
- `PdfPageCounter.App/ViewModels/FolderNodeViewModel.cs`
- `PdfPageCounter.App/App.xaml.cs`
- `PdfPageCounter.App/PdfPageCounter.App.csproj`

Tests:
- `PdfPageCounter.Tests/FolderScannerTests.cs` (additions)

## 5. Testing & Verification

Core unit tests (`dotnet test`), added to `FolderScannerTests`:

- **Abort-resistance:** a directory that throws during listing does not prevent
  sibling/other branches from being counted (simulated unreachable/deleted
  entry), and the scan completes with the readable branches correct.
- **Deep nesting:** a synthetic tree at least 50 levels deep rolls up
  file/page totals correctly at every level.
- **Cycle safety:** a revisited directory path is not traversed twice (build a
  self-referential structure where the platform allows, or unit-test the visited
  guard directly); scan terminates.
- Existing duplicate / error / cancellation tests remain green.

Build:
- `dotnet build PdfPageCounter/PdfPageCounter.sln` succeeds.
- `dotnet test PdfPageCounter/PdfPageCounter.sln` passes.

Manual:
- Theme: switch System/Light/Dark; confirm the whole window (incl. title bar and
  flyout) updates; restart and confirm the choice persisted.
- Scale: scan a deep synthetic tree (hundreds of folders, 5+ levels) and a large
  real folder; confirm no crash and that counts on deep folders match a manual
  tally.

## 6. Out of Scope

- Changing the duplicate definition (content hashing, size matching).
- x86 support.
- Any redesign of the summary cards or file-detail columns.
- Persisting window size / other settings.
