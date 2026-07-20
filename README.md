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
tests/FileRouter.Core.Tests/   xUnit — 145 tests
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

Generate a demo inbox (5 PDFs + 2 routes) and launch against it:

```
dotnet run --project src/FileRouter.App -- --config demo\config.json
```

Type a name, press a route button (or `Ctrl+1`/`Ctrl+2`, or `Enter` for the
last-used route). `Ctrl+K` sets a file aside; `Ctrl+Shift+Z` undoes.

## Status

Ported and tested (145 unit tests + a UI smoke): the filing loop (Naming,
Scanner, Commit, Session), the network-safe audit DB (History), and two of the
batch tools' logic (BulkRename incl. the review-file parser, MatchMerge). The
WinForms app runs the full Ready → Processing → Done loop with live inbox
monitoring, the set-aside alert, and a live filename preview — verified
end-to-end against the real WebView2 viewer by the smoke harness.

Not yet ported from the Python original: PDF metadata tagging (needs a managed
PDF library), the Unlock tool (PDF decryption), tool dialogs for Bulk-rename and
Match-&-merge (their logic is done), and Settings.
