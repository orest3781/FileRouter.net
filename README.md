# FileRouter.NET

A small, network-friendly rewrite of FileRouter for Windows — a desktop tool
for naming and sorting scanned PDFs / faxes fast. C# / .NET 8 + **WPF**, with
the PDF viewer hosted in **WebView2** (Edge's engine, already on the machine),
so **no PDF rendering library is bundled**.

Why the rewrite: the Python/PySide6 original carries ~650 MB of dependencies
(PySide6 alone) and is awkward to run over a network share. This version's own
code is small; the .NET Desktop and WebView2 runtimes are already present on
modern Windows, so deployment is a few MB.

## Design goals

- **Small.** No bundled Qt, no bundled PDF renderer — WebView2 renders PDFs
  natively. No MVVM framework either; ~120 lines of hand-rolled primitives.
- **Network-safe.** The audit database uses a rollback journal (never WAL,
  which corrupts over SMB) with a `busy_timeout`, so several workstations can
  file into one `history.sqlite` on a share without lock errors or corruption.
  A 30-second poll backstops folder watching where SMB drops notifications.
- **Never loses a file.** Files are only ever *moved*, never deleted or
  overwritten; a taken name gets a Windows-style ` (2)` counter. Illegal
  filename characters (a colon, `< > : " / \ | ? *`) are rejected up front —
  the original app could silently hide a document in an NTFS stream.
- **Looks after the eyes.** Follows Windows light/dark mode live; every text
  color pairing in the theme is enforced to WCAG AA 4.5:1 **by a unit test**;
  route buttons and dashboard tiles pick black/white text by real contrast
  ratio; app font and base text size are configurable.

## Structure

```
src/FileRouter.Core/     pure logic — no UI, no Windows dependency, unit-tested
  Naming.cs              filename construction + reserved-char guard
  BulkRename.cs          batch rename + the review-file name parser
  MatchMerge.cs          roster CSV matching + Control-ID merge
  Config.cs  Scanner.cs  Commit.cs  Session.cs
  History.cs             network-safe SQLite audit log
src/FileRouter.Wpf/      the app: MVVM view models (headless-tested) + XAML
  Theme/                 WCAG-enforced light/dark palette, live OS switching
  ViewModels/            Shell (Ready → Processing → Done), Settings, History,
                         Unlock, Bulk rename, Match & merge
  Views/ Windows/        thin XAML over the view models
tests/FileRouter.Core.Tests/   xUnit — the filing rules, adversarially
tests/FileRouter.Wpf.Tests/    xUnit — the whole app logic, headless (theme
                               contrast, filing loop, reentrancy, settings
                               validation, dashboards, tools)
tools/FileRouter.Smoke/        UI proofs against the real WebView2: the commit
                               succeeds because Edge released the handle;
                               double-fire files exactly once; every window
                               constructs (dialogs / reentrancy / reset-demo)
```

## Download

Portable single-file builds are attached to every
[release](../../releases) (and every CI run uploads one under the run's
Artifacts):

- **`FileRouter-vX-win-x64.exe`** (~5 MB) — needs the .NET 8 Desktop
  Runtime, which modern Windows 10/11 machines already have (Windows
  offers the download link if it's missing).
- **`…-selfcontained.exe`** (~70 MB) — carries the runtime; nothing to
  install.

Both are truly portable: put the exe anywhere and run it — it reads (or
creates on first run) a `config.json` beside itself, or takes
`--config <path>`. Locally, `publish.bat` builds the same portable exe
into `publish\`.

To cut a release: `git tag v1.0.0 && git push origin v1.0.0` — the
Release workflow tests, builds both exes, and publishes them.

## Build & test

```
dotnet build
dotnet test
```

## Run the demo

Run `reset.bat` once to generate the demo workspace (5 PDFs + 2 routes +
`demo\config.json`, which is machine-generated and not tracked), then launch
with `run.bat`, or:

```
dotnet run --project src/FileRouter.Wpf -- --config demo\config.json
```

Type a name (Tab completes suggestions a word at a time), press a route button
(or its configured hotkey, or `Enter` for the last-used route). `Ctrl+K` sets a
file aside; `Ctrl+Shift+Z` undoes; `Esc` stops the session with nothing lost.

## Features

- **Filing loop** — Ready → Processing → Done, live inbox monitoring (new
  arrivals join the running queue), a live "will be filed as" preview that
  warns about illegal names before the button, name autocomplete ranked by
  recency then frequency (seeded from `names.txt`), uppercase/word-separator
  polishing, per-route naming modes and filename suffixes. Commit/skip/undo
  are reentrancy-guarded so a fast double-press can never mislabel a document
  (regression-tested headless *and* against the real viewer).
- **Route hotkeys that really bind** — the config `hotkey` field ("Ctrl+1",
  "F2", "Ctrl+Shift+M") registers actual key bindings; unset routes fall back
  to Ctrl+1-9. The Settings window captures hotkeys by pressing them.
- **Ready dashboard** — monitored-folder tiles (`watch_folders`) that appear
  only while a folder holds matching files: colored, keyboard-focusable,
  screen-reader-named, with live counts; filename alerts (`alert_texts`) flash
  a tile (and the inbox count) red until the file clears — or highlight
  steadily when flashing is turned off.
- **Settings window** — six sections (General / Filing / Destinations /
  Dashboard / Appearance / Tools & data), Browse… on every path, live
  destination validation, duplicate-hotkey / duplicate-label / bad-color
  checks with hard errors blocking OK and "Save anyway?" for warnings-only.
  Saving is a clone-and-patch: unknown hand-edited config keys survive by
  construction.
- **History** — network-safe SQLite audit log with a daily point-in-time
  backup, an in-app viewer (newest 500, Show all, live filter), and CSV export
  with a formula-injection guard.
- **Tools** — Unlock PDFs (verified in-place decrypt or suffixed copy, saved
  passwords **DPAPI-encrypted** — the Python original kept them in plain
  text), Bulk rename (find/replace/affixes/case, the "Review files" transform,
  hand-editable preview, batch undo), Match & merge (roster CSV with
  remembered header mapping, one-click merges, and a Triage window that shows
  each ambiguous PDF beside the full candidate roster rows).
- **Crash discipline** — config load/save failures and a locked/corrupt
  history DB surface as readable dialogs, never silent exits; uncaught
  exceptions append to `crash.log` beside the config and the app survives.

## Status

Feature-complete: the WPF app replaced the WinForms shell after reaching
parity and beyond it (history viewer, theming, bound hotkeys, settings
validation, DPAPI passwords). 315 unit tests (198 core + 117 app) plus the
three UI smoke modes, all green.
