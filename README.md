# FileRouter.NET

A fast Windows desktop tool for naming and sorting scanned PDFs / faxes.
Type a name, press a route button — the file moves. No PDF renderer bundled;
the built-in Edge (WebView2) engine, already on your machine, displays every PDF.

## ⬇️ Download

**[Download the latest release (.exe)](../../releases/latest)**

Single-file, self-contained Windows executable — no installer, no .NET runtime needed.

## Requirements

- Windows 10 or 11
- Microsoft Edge (WebView2) — already present on any up-to-date Windows machine

## Getting started

1. Download `FileRouter.App.exe` from the [Releases](../../releases/latest) page.
2. Place it anywhere (a network share works fine).
3. Run it. On first launch it creates `config.json` next to the exe with default values.
4. Open **File → Settings…** to set your inbox folder, set-aside folder, and filing routes — no JSON editing required.

## Features

### Filing loop
- Opens PDFs one at a time from the inbox folder in a built-in viewer.
- Type a name (with autocomplete from history, ranked by recency and frequency, seeded from `names.txt`), then click a route button to move the file — or use keyboard shortcuts.
- Live **"will be filed as"** preview updates as you type.
- Reentrancy-safe: a fast double-press can never mislabel a document.

### Keyboard shortcuts
| Key | Action |
|---|---|
| `Ctrl+1`, `Ctrl+2`, … | File to route 1, 2, … |
| `Enter` | File to the last-used route |
| `Ctrl+K` | Set the current file aside |
| `Ctrl+Shift+Z` | Undo the last filing |

### Ready dashboard
- Monitored-folder tiles appear while a watched folder holds matching files, showing a live count and color.
- Filename **alert terms** flash a tile (and the inbox count) red until the matching file is cleared.
- Configure watched folders and alert terms in **File → Settings…**.

### Audit history
- Every file move is logged to `history.sqlite` (network-safe: rollback journal, never WAL).
- Multiple workstations can file into one shared database without lock errors.
- **Daily point-in-time backup** is taken automatically.
- **File → Export history…** saves a CSV of all activity.

### Tools menu
- **Unlock PDFs** — decrypt password-protected PDFs in place or to a copy.
- **Bulk rename** — batch-rename a folder of files with a hand-editable preview; includes a "Review files" name transform.
- **Match & merge** — match a folder of PDFs against a roster CSV by name or Control ID, with an in-app triage viewer for ambiguous matches.

### Settings dialog
**File → Settings…** lets you configure every option without touching JSON:
- Inbox and set-aside folders
- Filing routes (label, destination path, hotkey, suffix, naming mode, color)
- Naming mode (`insert` or `replace`) and sort order
- Monitored folders (dashboard tiles) and alert terms
- Options: write route label into PDF metadata, `Enter` commits to last route, uppercase names, flash alerts

## Configuration reference (`config.json`)

The file is created automatically on first run and is fully editable via **File → Settings…**.

| Key | Default | Description |
|---|---|---|
| `inbox` | *(empty)* | Folder to scan for incoming files |
| `deferred` | *(empty)* | Set-aside folder |
| `names_file` | `names.txt` | Seed file for name autocomplete (one name per line) |
| `history_db` | `history.sqlite` | Audit log path (absolute or relative to config) |
| `naming_mode` | `insert` | `insert` — name precedes suffix · `replace` — name only |
| `sort` | `size_desc` | Order files are presented (`filename_asc/desc`, `mtime_asc/desc`, `size_asc/desc`) |
| `enter_commits` | `true` | `Enter` files to the last-used route |
| `uppercase_names` | `true` | Auto-capitalise typed names |
| `tag_with_route` | `true` | Write the route label into the PDF keywords/subject field |
| `flash_alerts` | `true` | Flash alert tiles; `false` for a steady highlight |
| `routes` | `[]` | Array of filing destinations (see below) |
| `watch_folders` | `[]` | Array of monitored-folder dashboard tiles |
| `alert_texts` | `[]` | Filename substrings that trigger red alert highlighting |
| `monitor_title` | `"Monitored folders"` | Heading shown above the dashboard tiles |

**Route fields:** `label`, `path`, `hotkey` (e.g. `"1"`), `suffix`, `append_suffix`, `naming_mode` (overrides global), `color`

**Watch-folder fields:** `label`, `path`, `filetypes` (e.g. `"pdf"` or `"pdf,txt"`; blank = any), `recursive`, `color`
