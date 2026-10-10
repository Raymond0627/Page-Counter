# Pcount Theme & Scale Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Lumeed Pcount scan 300k+ PDFs without crashing, show correct per-folder file/page/unique/duplicate counts at any depth, and support a persisted System/Light/Dark theme.

**Architecture:** Harden the Core scanner so a single bad folder can no longer abort discovery and so junctions/symlinks cannot cause infinite traversal; widen counters and trim per-file memory; add a small `IDirectoryLister` seam for deterministic tests. In the App, add a `ThemeService`/`SettingsStore` pair driving `RequestedTheme` on the window root, expose a theme menu in the top bar, and make the folder `TreeView` expand lazily with observable labels (dropping the O(n²) live tree sync).

**Tech Stack:** C# / .NET 8, WinUI 3 (Windows App SDK 2.4), CommunityToolkit.Mvvm, UglyToad.PdfPig, xUnit 2.5.

**Spec:** `docs/superpowers/specs/2026-10-10-pcount-theme-scale-hardening-design.md`

## Global Constraints

- Target: **64-bit only** — `PdfPageCounter.App.csproj` `<Platforms>x64;ARM64</Platforms>` (remove x86).
- Duplicate definition **unchanged**: same filename (case-insensitive) AND same page count; unique pages counts each distinct `(name, pageCount)` once.
- Theme options: **System / Light / Dark**, default **System**, persisted to `%LOCALAPPDATA%\LumeedPcount\settings.json`.
- `FolderScanner` public behavior and existing constructors must keep working; `ScanAsync`, `FileProcessed`, `FolderSkipped`, `Progress` signatures unchanged.
- Core/Tests target `net8.0`; App targets `net8.0-windows10.0.26100.0`.
- `dotnet` must be invoked as `C:\dotnet\dotnet.exe` in this environment.
- No new NuGet packages.

## Review Focus

Inputs/conditions the spec implies but whose tests need explicit pinning (each gets a test in its owning task):

1. A file vanishes between listing and reading → recorded as an error record, scan continues (Task 1).
2. A directory listing throws (permissions/IO) → that folder is skipped, sibling branches still counted (Task 1).
3. A self-referential/junction directory → traversal terminates, no duplicate nodes (Task 1).
4. Very deep nesting (60+ levels) → counts roll up correctly at every level (Task 1).
5. A corrupt/unwritable settings file → theme falls back to System and the app still starts (Task 3; verified manually — theme code lives in the Windows-targeted App project and is not unit-testable from the `net8.0` Tests project).

---

### Task 1: Scanner enumeration hardening (abort-resistance + cycle safety)

**Files:**
- Create: `PdfPageCounter/PdfPageCounter.Core/Services/IDirectoryLister.cs`
- Modify: `PdfPageCounter/PdfPageCounter.Core/Services/FolderScanner.cs`
- Test: `PdfPageCounter/PdfPageCounter.Tests/FolderScannerTests.cs`

**Interfaces:**
- Consumes: existing `PdfPageCounterService`, `DuplicateDetector`, `FolderNode`, `PdfFileRecord`.
- Produces:
  - `public sealed record DirectoryListing(IReadOnlyList<string> Directories, IReadOnlyList<string> Files)`
  - `public interface IDirectoryLister { DirectoryListing List(string directory); }`
  - `public sealed class FileSystemDirectoryLister : IDirectoryLister`
  - `FolderScanner(PdfPageCounterService, DuplicateDetector, int channelCapacity = 4096, IDirectoryLister? lister = null)`

- [ ] **Step 1: Write the failing tests**

Add these to `FolderScannerTests.cs`. First add helper fakes near the bottom of the class:

```csharp
private sealed class ThrowingLister : IDirectoryLister
{
    private readonly string _throwFor;
    private readonly IDirectoryLister _inner = new FileSystemDirectoryLister();
    public ThrowingLister(string throwFor) => _throwFor = throwFor;
    public DirectoryListing List(string directory)
    {
        if (string.Equals(directory.TrimEnd('\\'), _throwFor.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("simulated");
        return _inner.List(directory);
    }
}

private sealed class SelfReferentialLister : IDirectoryLister
{
    public DirectoryListing List(string directory) => new(new[] { directory }, Array.Empty<string>());
}
```

Then the tests:

```csharp
[Fact]
public async Task Scan_WhenFolderListingThrows_SkipsFolderAndContinues()
{
    Directory.CreateDirectory(Path.Combine(_tempRoot, "good1"));
    Directory.CreateDirectory(Path.Combine(_tempRoot, "bad"));
    Directory.CreateDirectory(Path.Combine(_tempRoot, "good2"));
    CreatePdf(Path.Combine(_tempRoot, "good1", "x.pdf"), 2);
    CreatePdf(Path.Combine(_tempRoot, "bad", "y.pdf"), 2);
    CreatePdf(Path.Combine(_tempRoot, "good2", "z.pdf"), 3);

    var detector = new DuplicateDetector();
    var scanner = new FolderScanner(new PdfPageCounterService(), detector,
        lister: new ThrowingLister(Path.Combine(_tempRoot, "bad")));

    var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    // "bad" is skipped; both good branches are still discovered.
    Assert.Equal(2, root.TotalFileCount);
    Assert.Equal(5, root.TotalPageCount);
    Assert.Equal(2, root.Children.Count);
}

[Fact]
public async Task Scan_SelfReferentialDirectory_TerminatesWithSingleNode()
{
    var detector = new DuplicateDetector();
    var scanner = new FolderScanner(new PdfPageCounterService(), detector,
        lister: new SelfReferentialLister());

    var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    Assert.Single(root.Children);
    Assert.Equal(0, root.TotalFileCount);
}

[Fact]
public async Task Scan_DeepNesting_RollsUpCountsAtEveryLevel()
{
    var path = _tempRoot;
    for (int i = 0; i < 60; i++)
    {
        path = Path.Combine(path, "d");
        Directory.CreateDirectory(path);
    }
    CreatePdf(Path.Combine(path, "deep.pdf"), 7);

    var detector = new DuplicateDetector();
    var scanner = new FolderScanner(new PdfPageCounterService(), detector);
    var root = await scanner.ScanAsync(_tempRoot, CancellationToken.None);

    var node = root;
    for (int i = 0; i < 60; i++)
    {
        Assert.Equal(1, node.TotalFileCount);
        Assert.Equal(7, node.TotalPageCount);
        Assert.Single(node.Children);
        node = node.Children[0];
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `& "C:\dotnet\dotnet.exe" test PdfPageCounter\PdfPageCounter.Tests\PdfPageCounter.Tests.csproj --nologo`
Expected: FAIL — compile error (`IDirectoryLister` / named argument `lister` not defined).

- [ ] **Step 3: Create `IDirectoryLister.cs`**

```csharp
using System.IO;

namespace PdfPageCounter.Core.Services;

public sealed record DirectoryListing(IReadOnlyList<string> Directories, IReadOnlyList<string> Files);

public interface IDirectoryLister
{
    DirectoryListing List(string directory);
}

public sealed class FileSystemDirectoryLister : IDirectoryLister
{
    public DirectoryListing List(string directory) =>
        new(Directory.GetDirectories(directory), Directory.GetFiles(directory));
}
```

- [ ] **Step 4: Rewrite `FolderScanner` construction and `DiscoverAsync`**

In `FolderScanner.cs`: add field `private readonly IDirectoryLister _lister;`, replace the two constructors with one, and update the field assignment:

```csharp
public FolderScanner(
    PdfPageCounterService pageCounter,
    DuplicateDetector duplicateDetector,
    int channelCapacity = DefaultChannelCapacity,
    IDirectoryLister? lister = null)
{
    _pageCounter = pageCounter;
    _duplicateDetector = duplicateDetector;
    _channelCapacity = Math.Max(16, channelCapacity);
    _lister = lister ?? new FileSystemDirectoryLister();
}
```

Replace the `DiscoverAsync` local function body with (keep the surrounding `try/await producer/finally TryComplete` in `ScanAsync` unchanged):

```csharp
async Task DiscoverAsync(
    string startPath,
    ConcurrentDictionary<string, FolderNode> folderMap,
    ChannelWriter<string> writer,
    ProgressCounter counter,
    CancellationToken ct)
{
    var stack = new Stack<string>();
    stack.Push(startPath);
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    while (stack.Count > 0)
    {
        ct.ThrowIfCancellationRequested();
        var dir = stack.Pop();
        var key = dir.TrimEnd('\\');
        if (!visited.Add(key))
        {
            continue;
        }

        DirectoryListing listing;
        try
        {
            listing = _lister.List(dir);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
            or System.Security.SecurityException or PathTooLongException)
        {
            FolderSkipped?.Invoke(dir, DescribeFolderError(ex));
            continue;
        }

        foreach (var sub in listing.Directories)
        {
            ct.ThrowIfCancellationRequested();
            if (IsReparsePoint(sub))
            {
                continue;
            }

            var subKey = sub.TrimEnd('\\');
            var node = new FolderNode(Path.GetFileName(sub), subKey);
            if (folderMap.TryAdd(subKey, node))
            {
                if (folderMap.TryGetValue(key, out var parent))
                {
                    lock (parent.Children)
                    {
                        parent.Children.Add(node);
                    }
                }
                stack.Push(sub);
            }
        }

        foreach (var file in listing.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            counter.Discovered++;
            await writer.WriteAsync(file, ct).ConfigureAwait(false);
        }
    }
}
```

Add these helper methods to `FolderScanner` (next to `DescribeFolderError`):

```csharp
private static bool IsReparsePoint(string path)
{
    try
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
    catch
    {
        return false;
    }
}
```

Extend `DescribeFolderError` with a `PathTooLongException` arm before the default:

```csharp
PathTooLongException => "Path is too long to enumerate.",
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `& "C:\dotnet\dotnet.exe" test PdfPageCounter\PdfPageCounter.Tests\PdfPageCounter.Tests.csproj --nologo`
Expected: PASS (46 tests).

- [ ] **Step 6: Commit**

```bash
git add PdfPageCounter/PdfPageCounter.Core/Services/IDirectoryLister.cs PdfPageCounter/PdfPageCounter.Core/Services/FolderScanner.cs PdfPageCounter/PdfPageCounter.Tests/FolderScannerTests.cs
git commit -m "fix(scanner): per-folder error isolation and cycle-safe traversal"
```

---

### Task 2: Widen folder counts to `long` and drop redundant per-file path

**Files:**
- Modify: `PdfPageCounter/PdfPageCounter.Core/Models/FolderNode.cs`
- Modify: `PdfPageCounter/PdfPageCounter.Core/Models/PdfFileRecord.cs`
- Modify: `PdfPageCounter/PdfPageCounter.Core/Services/FolderScanner.cs` (`ProcessFile`, `AttachToFolder`)
- Test: `PdfPageCounter/PdfPageCounter.Tests/FolderScannerTests.cs`

**Interfaces:**
- Consumes: `FolderNode`, `PdfFileRecord` from Task 1.
- Produces: `FolderNode.OwnFileCount/TotalFileCount/OwnPageCount/TotalPageCount/DuplicateFileCount/ErrorFileCount` are now `long`; `PdfFileRecord` no longer has `ParentFolderPath`.

- [ ] **Step 1: Update tests to assert against `long` (red)**

In `FolderScannerTests.cs`, append `L` to every integer literal compared against a folder counter, and cast `Files.Count`:

- `Assert.Equal(10, root.TotalPageCount)` → `Assert.Equal(10L, ...)`; same for `3L`, `7L`, `5L` page/total asserts and `Assert.Equal(2, sub.TotalPageCount)` → `2L`.
- File-count asserts: `Assert.Equal(3, root.TotalFileCount)`, `2`, `1`, `OwnFileCount` variants → `3L`, `2L`, `1L`.
- `Assert.Equal(root.TotalFileCount, root.Files.Count)` → `Assert.Equal(root.TotalFileCount, (long)root.Files.Count)`.
- `Assert.Equal(1, root.ErrorFileCount)` → `1L`.
- Duplicate detector asserts already `long`: `Assert.Equal(2, detector.DuplicateFileCount())` → `2L`; `Assert.Equal(4, detector.UniquePageTotal())` → `4L`; `Assert.Equal(0, ...)` → `0L`; `Assert.Equal(13, ...)` → `13L`.

- [ ] **Step 2: Run tests to verify they still pass (green baseline before refactor)**

Run: `& "C:\dotnet\dotnet.exe" test PdfPageCounter\PdfPageCounter.Tests\PdfPageCounter.Tests.csproj --nologo`
Expected: PASS (the `L`-suffixed literals are compatible with the current `int` properties via implicit widening).

- [ ] **Step 3: Widen the counters**

In `FolderNode.cs`, change the six count properties from `int` to `long` (`OwnFileCount`, `TotalFileCount`, `OwnPageCount`, `TotalPageCount`, `DuplicateFileCount`, `ErrorFileCount`). No method-body changes needed.

- [ ] **Step 4: Remove `PdfFileRecord.ParentFolderPath`**

Delete the `public string ParentFolderPath { get; set; } = string.Empty;` line from `PdfFileRecord.cs`. In `FolderScanner.ProcessFile`, remove the `ParentFolderPath = ...` initializer. In `FolderScanner.AttachToFolder`, derive the key:

```csharp
var parentKey = (Path.GetDirectoryName(record.FullPath) ?? string.Empty).TrimEnd('\\');
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `& "C:\dotnet\dotnet.exe" test PdfPageCounter\PdfPageCounter.Tests\PdfPageCounter.Tests.csproj --nologo`
Expected: PASS (46 tests).

- [ ] **Step 6: Commit**

```bash
git add PdfPageCounter/PdfPageCounter.Core/Models/FolderNode.cs PdfPageCounter/PdfPageCounter.Core/Models/PdfFileRecord.cs PdfPageCounter/PdfPageCounter.Core/Services/FolderScanner.cs PdfPageCounter/PdfPageCounter.Tests/FolderScannerTests.cs
git commit -m "refactor(core): widen folder counters to long, drop redundant parent path"
```

---

### Task 3: Theme subsystem (settings store + service + startup apply)

**Files:**
- Create: `PdfPageCounter/PdfPageCounter.App/Services/SettingsStore.cs`
- Create: `PdfPageCounter/PdfPageCounter.App/Services/ThemeService.cs`
- Modify: `PdfPageCounter/PdfPageCounter.App/App.xaml.cs`
- Modify: `PdfPageCounter/PdfPageCounter.App/MainWindow.xaml.cs`

**Interfaces:**
- Produces:
  - `public enum AppTheme { System, Light, Dark }`
  - `public sealed class AppSettings { public string Theme { get; set; } = "System"; }`
  - `public sealed class SettingsStore { public SettingsStore(string? filePath = null); public AppSettings Load(); public void Save(AppSettings settings); }`
  - `public sealed class ThemeService { public ThemeService(SettingsStore store); public AppTheme Current { get; } public event Action? Changed; public void ApplyTo(FrameworkElement root); public void SetTheme(AppTheme theme); }`
  - `App.ThemeManager` (static `ThemeService`).

- [ ] **Step 1: Create `SettingsStore.cs`**

```csharp
using System.Text.Json;

namespace PdfPageCounter_App.Services;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
}

public sealed class SettingsStore
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LumeedPcount", "settings.json");

    private readonly string _filePath;

    public SettingsStore(string? filePath = null) => _filePath = filePath ?? DefaultPath;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath))
                       ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable settings: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings));
        }
        catch
        {
            // Best-effort persistence; never crash the app over settings.
        }
    }
}
```

- [ ] **Step 2: Create `ThemeService.cs`**

```csharp
using Microsoft.UI.Xaml;

namespace PdfPageCounter_App.Services;

public enum AppTheme { System, Light, Dark }

public sealed class ThemeService
{
    private readonly SettingsStore _store;

    public ThemeService(SettingsStore store)
    {
        _store = store;
        var saved = store.Load().Theme;
        Current = Enum.TryParse<AppTheme>(saved, ignoreCase: true, out var parsed)
            ? parsed
            : AppTheme.System;
    }

    public AppTheme Current { get; private set; }

    public event Action? Changed;

    public void ApplyTo(FrameworkElement root)
    {
        root.RequestedTheme = Current switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    public void SetTheme(AppTheme theme)
    {
        Current = theme;
        _store.Save(new AppSettings { Theme = theme.ToString() });
        Changed?.Invoke();
    }
}
```

- [ ] **Step 3: Wire the service into `App.xaml.cs`**

Add the static property (after `MainAppWindow`):

```csharp
/// <summary>Application-wide theme manager.</summary>
public static ThemeService ThemeManager { get; } = new(new SettingsStore());
```

Add `using PdfPageCounter_App.Services;` at the top of `App.xaml.cs`.

- [ ] **Step 4: Apply the saved theme on startup in `MainWindow.xaml.cs`**

After `RootFrame.Navigate(typeof(MainPage));` add:

```csharp
App.ThemeManager.ApplyTo((Microsoft.UI.Xaml.FrameworkElement)Content);
```

- [ ] **Step 5: Build the App to verify it compiles**

Run: `& "C:\dotnet\dotnet.exe" build PdfPageCounter\PdfPageCounter.App\PdfPageCounter.App.csproj -c Debug -p:Platform=x64 --nologo`
Expected: Build succeeded, 0 Errors.

- [ ] **Step 6: Commit**

```bash
git add PdfPageCounter/PdfPageCounter.App/Services/SettingsStore.cs PdfPageCounter/PdfPageCounter.App/Services/ThemeService.cs PdfPageCounter/PdfPageCounter.App/App.xaml.cs PdfPageCounter/PdfPageCounter.App/MainWindow.xaml.cs
git commit -m "feat(app): add persisted System/Light/Dark theme service"
```

---

### Task 4: Theme picker UI in the top bar

**Files:**
- Modify: `PdfPageCounter/PdfPageCounter.App/MainPage.xaml`
- Modify: `PdfPageCounter/PdfPageCounter.App/MainPage.xaml.cs`

**Interfaces:**
- Consumes: `App.ThemeManager`, `AppTheme` (Task 3).

- [ ] **Step 1: Add the theme menu to `MainPage.xaml`**

Inside the first top-bar `StackPanel Orientation="Horizontal" Spacing="8"`, after the `ProgressRing`, add:

```xml
<DropDownButton x:Name="ThemeButton" Content="Theme" ToolTipService.ToolTip="Theme">
    <DropDownButton.Flyout>
        <MenuFlyout Placement="BottomEdgeAlignedRight">
            <RadioMenuFlyoutItem x:Name="ThemeSystem" Text="System" GroupName="Theme" Tag="System" Click="ThemeItem_Click" />
            <RadioMenuFlyoutItem x:Name="ThemeLight" Text="Light" GroupName="Theme" Tag="Light" Click="ThemeItem_Click" />
            <RadioMenuFlyoutItem x:Name="ThemeDark" Text="Dark" GroupName="Theme" Tag="Dark" Click="ThemeItem_Click" />
        </MenuFlyout>
    </DropDownButton.Flyout>
</DropDownButton>
```

- [ ] **Step 2: Add the handler and initial state to `MainPage.xaml.cs`**

Add `using PdfPageCounter_App.Services;`. In the constructor, after `DataContext = _viewModel;`, add:

```csharp
var current = App.ThemeManager.Current;
ThemeSystem.IsChecked = current == AppTheme.System;
ThemeLight.IsChecked = current == AppTheme.Light;
ThemeDark.IsChecked = current == AppTheme.Dark;
```

Add the handler:

```csharp
private void ThemeItem_Click(object sender, RoutedEventArgs e)
{
    if (sender is RadioMenuFlyoutItem { Tag: string tag } &&
        Enum.TryParse<AppTheme>(tag, out var theme))
    {
        App.ThemeManager.SetTheme(theme);
        if (App.MainAppWindow?.Content is FrameworkElement root)
        {
            App.ThemeManager.ApplyTo(root);
        }
    }
}
```

- [ ] **Step 3: Build the App to verify it compiles**

Run: `& "C:\dotnet\dotnet.exe" build PdfPageCounter\PdfPageCounter.App\PdfPageCounter.App.csproj -c Debug -p:Platform=x64 --nologo`
Expected: Build succeeded, 0 Errors.

- [ ] **Step 4: Manual verification (records required behavior)**

Launch the app, open the **Theme** menu, and confirm:
1. Selecting **Dark** turns the whole window (content, cards, title bar, flyout) dark; **Light** turns it light; **System** follows Windows.
2. Close and relaunch — the last choice is restored.
3. Re-select the same option — no exception.

- [ ] **Step 5: Commit**

```bash
git add PdfPageCounter/PdfPageCounter.App/MainPage.xaml PdfPageCounter/PdfPageCounter.App/MainPage.xaml.cs
git commit -m "feat(app): add System/Light/Dark theme picker to the top bar"
```

---

### Task 5: Lazy tree expansion and removal of the O(n²) live tree sync

**Files:**
- Modify: `PdfPageCounter/PdfPageCounter.App/MainPage.xaml`
- Modify: `PdfPageCounter/PdfPageCounter.App/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `FolderNodeViewModel` (unchanged), `BuildTreeFromModel`.
- Produces: tree nodes start collapsed; label/badge bind OneWay; `MainViewModel` no longer has `_treeSyncTimer`, `SyncTreeFromModel`, `Reconcile`, or `GetInsertIndex`.

- [ ] **Step 1: Make the tree expand lazily and labels observable (`MainPage.xaml`)**

In the `TreeView.ItemTemplate`, change the `TreeViewItem` opening tag from:

```xml
<TreeViewItem ItemsSource="{x:Bind Children}" IsExpanded="True">
```

to:

```xml
<TreeViewItem ItemsSource="{x:Bind Children}">
```

Change the label and badge bindings to OneWay:

```xml
<TextBlock Text="{x:Bind DisplayLabel, Mode=OneWay}" TextTrimming="CharacterEllipsis" />
```

```xml
<TextBlock Text="{x:Bind BadgeText, Mode=OneWay}" Foreground="White" FontSize="11" FontWeight="SemiBold" />
```

- [ ] **Step 2: Remove the periodic tree sync (`MainViewModel.cs`)**

Delete the `_treeSyncTimer` field. In `StartTimers`, delete the block that creates `_treeSyncTimer` and calls `Start()`. In `StopTimers`, delete the `if (_treeSyncTimer != null) { ... }` block. Delete the methods `SyncTreeFromModel`, `Reconcile`, and `GetInsertIndex`. Leave `BuildTreeFromModel`, `FindByPath`, and `BuildBranch` unchanged — the tree is now built once when the scan completes or is cancelled.

- [ ] **Step 3: Build the App and run the Core tests**

Run: `& "C:\dotnet\dotnet.exe" build PdfPageCounter\PdfPageCounter.App\PdfPageCounter.App.csproj -c Debug -p:Platform=x64 --nologo`
Expected: Build succeeded, 0 Errors.

Run: `& "C:\dotnet\dotnet.exe" test PdfPageCounter\PdfPageCounter.Tests\PdfPageCounter.Tests.csproj --nologo`
Expected: PASS (46 tests).

- [ ] **Step 4: Manual verification**

Scan a deep synthetic tree (hundreds of folders, 5+ levels). Confirm the tree loads collapsed, stays responsive, expands on demand, and the counts shown on every level are correct. Confirm no live-tree churn during a scan (only the summary counters move).

- [ ] **Step 5: Commit**

```bash
git add PdfPageCounter/PdfPageCounter.App/MainPage.xaml PdfPageCounter/PdfPageCounter.App/ViewModels/MainViewModel.cs
git commit -m "perf(app): lazy tree expansion, observable labels, drop live tree sync"
```

---

### Task 6: 64-bit build config and full verification

**Files:**
- Modify: `PdfPageCounter/PdfPageCounter.App/PdfPageCounter.App.csproj`

**Interfaces:**
- Produces: App builds only for `x64` and `ARM64`.

- [ ] **Step 1: Restrict platforms to 64-bit**

In `PdfPageCounter.App.csproj`, change:

```xml
<Platforms>x86;x64;ARM64</Platforms>
```

to:

```xml
<Platforms>x64;ARM64</Platforms>
```

- [ ] **Step 2: Full clean build**

Run: `& "C:\dotnet\dotnet.exe" build PdfPageCounter\PdfPageCounter.sln -c Debug -p:Platform=x64 --nologo`
Expected: Build succeeded, 0 Errors for Core, App, and Tests.

- [ ] **Step 3: Full test run**

Run: `& "C:\dotnet\dotnet.exe" test PdfPageCounter\PdfPageCounter.Tests\PdfPageCounter.Tests.csproj --nologo`
Expected: PASS (46 tests), 0 Failed.

- [ ] **Step 4: Commit**

```bash
git add PdfPageCounter/PdfPageCounter.App/PdfPageCounter.App.csproj
git commit -m "build(app): target x64 and ARM64 only"
```

---

## Self-Review

**Spec coverage:**
- §3.1 Theme subsystem → Tasks 3 (service/store/startup) + 4 (UI). ✔
- §3.2 Scanner abort-resistance → Task 1; reparse/cycle guard → Task 1; `long` counts → Task 2; drop `ParentFolderPath` → Task 2; `IDirectoryLister` seam → Task 1. ✔
- §3.3 Lazy tree / observable labels / remove live sync → Task 5. ✔
- §3.4 Build config `x64;ARM64` → Task 6. ✔
- §5 Tests (abort-resistance, deep nesting, cycle) → Task 1; build/test commands → Tasks 3–6. ✔
- §6 Out of scope honored (no duplicate-logic or x86 work). ✔

**Type consistency:** `DirectoryListing`/`IDirectoryLister` names match across Task 1 and its tests. `AppTheme`, `AppSettings`, `SettingsStore`, `ThemeService`, `App.ThemeManager` names match across Tasks 3–4. `lister:` named argument matches the constructor in Task 1.

**Review Focus → test mapping:** items 1 (folder enumeration fault → sibling continues), 3 (self-referential loop terminates), 4 (60-level rollup) in Task 1; item 2 (deleted file) is already covered by `ProcessFile`'s catch-all and the existing error tests; item 5 (corrupt settings) is handled by `SettingsStore`'s try/catch and verified manually in Task 4 (theme code is not reachable from the `net8.0` test project).

**Proportion:** Six tasks, each a small independently reviewable change; code blocks are signatures/tests/XAML snippets, not full transcripts.
