# 🗃️ BackupFiles  
**BackupFiles** is a C# console tool for creating structured backups of project files and restoring them from `.bak.txt` or `.zip`.

It scans directories, filters files by extensions and wildcard rules, builds a complete tree of the project, and optionally compresses the result.  
Supports alternative configs via drag & drop.

---

## 🚀 Features

- 🔍 File scanning by extensions  
- 📁 Include/Exclude path filtering  
- ✨ Wildcard support (`*.min.js`, `*/node_modules/*`, etc.)  
- ❗ Forced include files via `!` (ignore exclude rules)  
- 🧩 Generated project tree structure  
- 🧾 Saves all file contents into `.bak.txt`  
- 🗜️ Optional ZIP compression  
- 🔄 Automatic version increment after backup  
- ♻️ Restore from `.bak.txt` or `.zip`  
- 🧰 Multiple configurations:
  - No arguments → use default `backup.config.xml`
  - Drag & drop XML → use that config

---

## ⚙️ Configuration

If `backup.config.xml` does not exist, the application generates a template with a detailed instruction block:

```xml
<!-- HOW TO USE THIS FILE
1. This file defines which files and folders will be included in your backup.
2. IncludePaths – folders scanned recursively.
3. IncludeFiles – specific files added manually.
   - If a file ends with "!", it ignores ExcludePaths and will ALWAYS be included.
4. ExcludePaths – wildcard patterns for files/folders to exclude.
5. Extensions – allowed file formats.
6. ResultPath – folder where backups will be saved.
7. ResultFilenameMask – pattern used to build the backup filename.
8. IsExample=1 disables work. Set it to 0 before using.
9. To use this config, drag & drop it onto the BackupFiles.exe application.
END OF INSTRUCTIONS -->
```

### Example:

```xml
<configuration>
  <ProjectName>MyProject</ProjectName>
  <Version>1.0.0</Version>

  <extensions>
    <extension>.js</extension>
    <extension>.ts</extension>
    <extension>.css</extension>
    <extension>.cs</extension>
  </extensions>

  <includePaths>
    <includePath>./src</includePath>
    <includePath>./wwwroot</includePath>
  </includePaths>

  <includeFiles>
    <includeFile>./backup.web.config.xml</includeFile>
    <includeFile>./backup.api.config.xml !</includeFile>
  </includeFiles>

  <excludePaths>
    <excludePath>*/node_modules/*</excludePath>
    <excludePath>*.min.js</excludePath>
    <excludePath>./backup.*.config.xml</excludePath>
  </excludePaths>

  <ResultPath>./backup</ResultPath>
  <ResultFilenameMask>@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt</ResultFilenameMask>

  <EnableZip>true</EnableZip>
  <DeleteUnziped>true</DeleteUnziped>

  <IsExample>0</IsExample>
</configuration>
```

---

## 💡 Usage

### 📌 Create Backup  
Run:

```bash
BackupFiles.exe
```

Uses default config.

### 📌 Use Alternate Config  
Drag & drop any `.xml` file onto the executable.

### 📌 Restore Project

```bash
BackupFiles.exe MyBackup_1.0.7_20251011104647.bak.txt
```

or ZIP:

```bash
BackupFiles.exe MyBackup.zip
```

---

## 🧮 Filename Mask

```
@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt
```

Example:

```
MyProject_1.0.7_20251011104647.bak.txt
```

---

## 🪄 PowerShell Commands

```powershell
Compress-Archive -Path "source" -DestinationPath "result.zip"
Expand-Archive -Path "backup.zip" -DestinationPath "folder"
```

---

## 🧑‍💻 Author

**Maxim Hegel © 2025**  
📧 i@hgl.mx  
🔗 linkedin.com/in/maximhegel

---

## 📜 License

MIT License — free to use and modify with attribution.
