# 📜 Changelog — BackupFiles

All notable changes to this project will be documented in this file.

---

## [1.0.33] - 2026-01-13
### Added
- Update check frequency and timeout are now configurable via `UpdateCheckMinutes` and `UpdateCheckTimeoutSeconds`.
- Added `Created` timestamp in config, updated automatically after backup and used for update-check cadence.
- Extensions now act as filename masks (e.g. `.js` -> `*.js`, `.min.js` -> `*.min.js`) and are applied by longest match first.

### Improved
- Update check now respects configurable timeouts and runs only when the cadence threshold is reached.

---

## [1.0.27] - 2025-11-21
### Added
- Support for multiple configuration files:
  - Drag & drop any `.xml` config onto `BackupFiles.exe` to run backup using it
  - Default `backup.config.xml` is still used when no arguments are provided
- New wildcard path filtering system:
  - Supports patterns such as:
    - `*.min.js`
    - `*/node_modules/*`
    - `backup.*.config.xml`
  - Works for both full paths and relative paths
- New forced include mode for `IncludeFiles`:
  - If a line ends with `!`, the file is always included regardless of `ExcludePaths`
  - Example:
    - `./backup.web.config.xml` → excluded if matches exclude patterns
    - `./backup.api.config.xml !` → always included
- Auto-generated default configuration file now contains a detailed
  `<!-- HOW TO USE THIS FILE -->` instruction block.

### Improved
- Simplified logic for merging include/exclude rules.
- More stable relative path resolution.
- Improved readability of generated backup info.
- More accurate tree structure compiler.
- Better error messages and diagnostics.

### Fixed
- Fixed incorrect exclusion behavior for wildcard patterns.
- Fixed duplicate include behavior when a file appears in both IncludePaths and IncludeFiles.
- Fixed ZIP extraction path handling for backups with nested files.

---

## [1.0.0] — Initial Release
### Added
- Scanning project directories for files with specific extensions.
- Include and exclude directory filters.
- Saving all project files into a single `.bak.txt`.
- Auto-generated tree view of the project structure.
- Optional ZIP compression.
- Project version auto-increment.
- Full restoration engine from `.bak.txt` or `.zip`.

