# 📜 Changelog — BackupFiles

All notable changes to this project will be documented in this file.

---

## [1.0.39] - 2026-01-13
### Changed
- Skip version increment when no files are collected (backup not created).

---

## [1.0.38] - 2026-01-13
### Added
- Incremental backup support (skip unchanged files since `Created`).
- Auto-add missing config fields on startup for older configs.
- New config options: `IncrementalBackup`, and tracking of unchanged skips in summary.
- Default extensions expanded with HTML/Office and Access formats.

---

## [1.0.37] - 2026-01-13
### Added
- Size/age exclusion limits for files (`MaxFileSizeMB`, `MaxFileAgeDays`).
- Progress updates for packing and dry-run (percent by file count).
- Auto-cleanup of old backups by count/age (`CleanupKeepLast`, `CleanupMaxAgeDays`).

### Changed
- Config template reorganized with clearer sections and comments.
- Summary now includes size/age skip details.

---

## [1.0.36] - 2026-01-13
### Added
- TLS 1.2 enablement before update checks to avoid SSL handshake failures.

---

## [1.0.35] - 2026-01-13
### Added
- Dry-run mode with size-based stats.
- Log levels (quiet/normal/verbose) and optional log file output.
- End-of-run summary (counts, size, time).

### Changed
- Update check failures now log as errors.
- Config template reorganized; `backup.log` excluded by default.

---

## [1.0.34] - 2026-01-13
### Added
- Colored console output with log levels (info/success/warn/error/debug).
- Verbose update-check logging and `UpdateCheckVerbose` config flag.

---

## [1.0.33] - 2026-01-13
### Changed
- Removed `*.min.js` from default exclude list to avoid redundancy with extension pattern rules.

---

## [1.0.32] - 2026-01-13
### Added
- Extension patterns now support wildcard masks and precedence by longest pattern.
- `tree_only` rules can be set via extension patterns (e.g., `.min.js`).
  - Extensions now act as filename masks (e.g. `.js` -> `*.js`, `.min.js` -> `*.min.js`) and are applied by longest match first.
- Config template updated with root include path and common exclude folders.

### Changed
- File filtering now uses pattern rules instead of extension-only matching.

---

## [1.0.31] - 2026-01-13
### Added
- Update check on launch with configurable interval and timeout.
- Auto-update of `<Created>` timestamp after each backup.
- Config template now includes `Created`, `UpdateCheckMinutes`, and `UpdateCheckTimeoutSeconds`.

---

## [1.0.30] - 2026-01-13
### Added
- Binary file detection: skip packing files that look non-text.
- Config template now uses grouped extensions in raw XML.

### Changed
- Disabled extensions are ignored; config validation checks enabled items only.
- Exclude matching now also handles folder patterns without wildcards.

---

## [1.0.29] - 2026-01-11
### Changed
- Wildcard excludes now normalize slashes and match folder patterns without `*`.

---

## [1.0.28] - 2026-01-11
### Added
- `tree_only` support for extensions/paths to include in tree only (no content).
- Per-item config entries (`ConfigItem`) for extensions/include paths/files.

---

## [1.0.27] - 2025-11-20
### Added
- Smarter exclude filtering with wildcard patterns and path-aware matching.
- IncludeFiles now supports force-include `!` suffix, even if excluded.
- Config template expanded with a usage guide and broader default extensions list.

### Changed
- Backup flow now accepts drag-and-drop XML configs or restore files.

---

## [1.0.26] - 2025-11-20
### Added
- Support for running backups by dragging any XML config file onto the exe.
- Wildcard-based exclude patterns (e.g., `*.min.js`, `*/node_modules/*`).
- Force-include `IncludeFiles` with a trailing `!`.
- Auto-generated config template includes a usage guide comment.

### Changed
- Restore/backup flow split into dedicated modes based on input file type.

---

## [1.0.25] - 2025-10-11
### Added
- `IncludeFiles` support for explicitly listed files in the config.
- Updated config template defaults (include/exclude examples and output folder).

---

## [1.0.24] - 2024-10-04
### Added
- Auto-increment project version in `backup.config.xml` after a successful backup.

---

## [1.0.23] - 2024-10-04
### Changed
- Restore skips decorative separator lines when rebuilding files.

---

## [1.0.22] - 2024-10-04
### Changed
- Restore from ZIP now selects the first extracted file inside the temp folder.

---

## [1.0.21] - 2024-10-04
### Added
- Restore mode: pass a `.bak.txt` (or `.zip`) to rebuild files and folders.

### Changed
- Backup writer now supports restore metadata blocks used by the importer.

---

## [1.0.20] - 2024-10-04
### Added
- Optional ZIP output (`EnableZip`, `DeleteUnziped`) plus PowerShell-based compression.

### Changed
- Result writer updated to zip when enabled.

---

## [1.0.19] - 2024-10-04
### Changed
- ExcludePaths filtering now uses full-path prefix checks (more accurate than substring match).

---

## [1.0.18] - 2024-09-15
### Changed
- Tree output root now includes `./` suffix for the top folder.

---

## [1.0.17] - 2024-09-15
### Changed
- Tree generation reworked to build a node graph and render it with collapse logic.
- Directory listing now shows folders first, then files, with stable indentation.

---

## [1.0.16] - 2024-09-15
### Changed
- Folder tree output simplified (no collapsing and no special root handling).

---

## [1.0.15] - 2024-09-15
### Changed
- Root folder detection tweaked to avoid repeating the parent name in tree output.

---

## [1.0.14] - 2024-09-15
### Changed
- Tree output now uses a more structured ASCII layout with collapsing for single-file paths.
- Directory ordering adjusted to be depth-aware (by length then name).

---

## [1.0.13] - 2024-09-15
### Changed
- Folder tree traversal switched to an iterative stack-based walk.

---

## [1.0.12] - 2024-09-15
### Changed
- Folder tree builder revised to list only directories/files that contain filtered items.
- Tree indentation updated to use a simple space-based layout.

---

## [1.0.11] - 2024-09-15
### Changed
- Code formatting/cleanup in folder tree helpers (no behavior changes).

---

## [1.0.10] - 2024-09-15
### Changed
- Folder tree generation now uses only filtered files (excludes ignored paths/files).

---

## [1.0.9] - 2024-09-15
### Added
- Folder tree structure is written at the top of backup files.
- New folder tree generator for recursive directory listing.

---

## [1.0.8] - 2024-07-20
### Added
- Stronger config validation with explicit error messages (missing ProjectName/Version, Extensions, IncludePaths).
- Safer IO: try/catch around directory creation, config read/write, and backup writing.
- Warnings for missing include paths and empty file selection.

---

## [1.0.7] - 2024-07-20
### Added
- Console UX improvements: try/catch around main flow, final "press any key" pause.
- Per-file progress logging during packing.

---

## [1.0.6] - 2024-07-20
### Added
- Added project icon `icon.ico`.

### Changed
- Config template defaults updated (`.config`, `.cs`, and example paths).
- `Config` model reorganized (ProjectName/Version moved to top; `IsExample` moved to bottom).

---

## [1.0.5] - 2024-07-20
### Changed
- Release artifacts updated (version file + exe).
- Dependency links moved out of `_lib` (folder removed).
- Source files still stored as links to the previous version (no code changes).


---

## [1.0.4] - 2024-07-20
### Added
- `_lib` folder with link entries for dependencies.
- Source files in `src` stored as links to the previous version (no code changes).

---

## [1.0.3] - 2024-07-20
### Added
- Switched to XML-based config (`backup.config.xml`) with `XmlSerializer`.
- Added auto-generated config template and `IsExample` guard to prevent running with defaults.

### Changed
- Project name and version now come from config (no `package.json` dependency).

---

## [1.0.2] - 2024-07-20
### Changed
- Release artifacts updated (version file + exe).
- Source snapshot updated (Main/classes).

---

## [1.0.0] - Initial Release
### Added
- JSON config (`backup.config.json`) with extensions, include paths, exclude paths, and output naming.
- Project name/version read from `package.json`.
- Recursive file collection by extension with simple exclude string matching.
- Single `.bak.txt` output with per-file separators and original relative paths.
