# FileRouter.NET

A small, network-friendly rewrite of FileRouter for Windows — a desktop tool
for naming and sorting scanned PDFs / faxes fast. C# / .NET 8 + WinForms, with
the PDF viewer hosted in **WebView2** (Edge's engine, already on the machine),
so **no PDF rendering library is bundled**.

Why the rewrite: the Python/PySide6 original carries ~650 MB of dependencies
(PySide6 alone) and is awkward to run over a network share. This version's own
code is **~400 KB**; the .NET Desktop and WebView2 runtimes are already present
on modern Windows, so deployment is a few MB.

## Design goals

- **Small.** No bundled Qt, no bundled PDF renderer — WebView2 renders PDFs
  natively.
- **Network-safe.** The audit database uses a rollback journal (never WAL,
  which corrupts over SMB) with a `busy_timeout`, so several workstations can
  file into one `history.sqlite` on a share without lock errors or corruption.
- **Never loses a file.** Files are only ever *moved*, never deleted or
  overwritten; a taken name gets a Windows-style ` (2)` counter. Illegal
  filename characters (a colon, `< > : " / \ | ? *`) are rejected up front —
  the original app could silently hide a document in an NTFS stream.

## Structure

```
src/FileRouter.Core/     pure logic — no UI, no Windows dependency, unit-tested
  Naming.cs              filename construction + reserved-char guard
  BulkRename.cs          batch rename + the review-file name parser
  MatchMerge.cs          roster CSV matching + Control-ID merge
  Config.cs  Scanner.cs  Commit.cs  Session.cs
  History.cs             network-safe SQLite audit log
src/FileRouter.App/      WinForms shell + WebView2 PDF viewer (the filing loop:
                         Ready → Processing → Done, live inbox monitoring,
                         set-aside alert, live "will be filed as" preview)
tests/FileRouter.Core.Tests/   xUnit — 183 tests
tools/FileRouter.Smoke/        headless UI smoke: drives the real form and
                               proves Edge releases the PDF handle so the move
                               succeeds (commit / set-aside / undo / history)
```

## Build & test

```
dotnet build
dotnet test
```

## Run the demo

Run `reset.bat` once to generate the demo workspace (5 PDFs + 2 routes +
`demo\config.json`, which is machine-generated and not tracked), then launch
against it:

```
dotnet run --project src/FileRouter.App -- --config demo\config.json
```

Type a name, press a route button (or `Ctrl+1`/`Ctrl+2`, or `Enter` for the
last-used route). `Ctrl+K` sets a file aside; `Ctrl+Shift+Z` undoes.

## Status

Feature-complete for the core workflow; 183 unit tests + a UI smoke, all green.

**Done:**
- Filing loop — Naming, Scanner, Commit, Session — with the Ready →
  Processing → Done lifecycle, live inbox monitoring, set-aside alert, a live
  filename preview, and **name autocomplete** (history ranked by recency then
  frequency, seeded from `names.txt`). Verified end-to-end against the real
  WebView2 viewer. Commit/skip/undo are guarded against reentrancy so a fast
  double-press can never mislabel a document (regression-tested).
- Network-safe audit DB (History): rollback journal + busy_timeout, never WAL,
  with a **daily point-in-time backup** and **CSV export** (File menu).
- PDF metadata tagging (PdfSharp), wired into commit/undo.
- **Tools menu** — Unlock PDFs (decrypt in place or to a copy), Bulk rename
  (incl. the "Review files" transform + hand-editable preview), and Match &
  merge (roster CSV matching + Control-ID merge, with an in-app triage viewer
  that opens each ambiguous PDF in Edge beside the candidate roster rows).
- **Ready dashboard** — monitored-folder tiles (`watch_folders`) that appear
  only while a folder holds matching files, colored, clickable, with a live
  count; filename **alerts** (`alert_texts`) flash a tile (and the inbox count)
  red until the file clears.
- **Settings dialog** (File → Settings…) — edits every field, the routes
  table, and the monitored-folders table, so first-time setup no longer means
  hand-editing `config.json`.

The .NET app is now at feature parity with the Python original for the core
workflow.
