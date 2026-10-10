# SDD ledger — plan: docs/superpowers/plans/2026-10-10-pcount-theme-scale-hardening.md

Spec: docs/superpowers/specs/2026-10-10-pcount-theme-scale-hardening-design.md

Pre-flight: Tasks 1→2 share FolderNode/PdfFileRecord (Task 2 widens what Task 1 produces) — no conflict. Task 3→4 share App.ThemeManager/AppTheme — names consistent — no conflict. No other shared interfaces.
Environment note: executing-plans scripts/sdd-workspace, task-start, task-done are bash scripts and not runnable on this Windows host; ledger maintained manually; test commands run directly with `C:\dotnet\dotnet.exe`.

Task status:
- Task 1: Ruling: test expectations (bad folder is a node; self-ref lister needs distinct child) — corrected plan's expected values — cost if wrong: nil (test-only)
- Task 1: complete (tests: dotnet test Tests -> 46/46 pass)
- Task 2: complete (tests: dotnet test Tests -> 46/46 pass)
- Task 3: complete (dotnet build App x64 -> succeeded)
- Task 4: complete (dotnet build App x64 -> succeeded). Ruling: manual GUI verification (theme switch/restart) deferred to human — no interactive desktop in this environment — cost if wrong: theme not visually confirmed, but builds + logic reviewed
- Task 5: complete (App build succeeded; tests 46/46). Manual GUI check deferred (no interactive desktop)
- Task 6: complete (sln build 0/0; tests 46/46). Ruling: also updated PdfPageCounter.sln App mapping x86->x64 (plan only changed csproj; solution referenced removed x86) — cost if wrong: none, needed for solution build
- Final: fixed reparse handling — Scan_UnreadableDirectoryAttributes_SkipsClosedAndReports + Scan_JunctionDirectory_IsSkippedReportedAndNotDoubleCounted RED->GREEN, suite 48/48; sln build 0/0
- Final: fixed settings JSON case-insensitivity (SettingsStore) — no unit test (App not testable from net8.0)
- Final: fixed dead code (FolderNodeViewModel.SyncFromModel removed)
- Final: Ruling: skip junctions + report vs traverse — chose spec-mandated skip plus FolderSkipped signal — cost if wrong: junctioned content undercounted but now visible in Skipped
- Final: Ruling: kept ThemeService.Changed (spec requires it); flyout/titlebar theme not GUI-verified — cost if wrong: flyout may need explicit RequestedTheme
- Final: minor (deferred): vestigial OneWay bindings; presentation-layer recursion depth = tree depth
