# 🗃️ BackupFiles

**BackupFiles** is a simple C# console application designed to create backups of project source files.  
It collects all files with the specified extensions from defined directories, generates a single text backup file containing the entire project structure, and optionally compresses the result into a ZIP archive.

---

## 🚀 Features

- 🔍 Scans files by specified extensions  
- 📁 Supports include/exclude path filters  
- 🧩 Generates a tree view of folder structure  
- 🧾 Saves all file contents into a `.bak.txt` file  
- 🗜️ Optional ZIP compression of the result  
- 🔄 Automatic version increment after backup  
- 🧰 Can restore the entire project from a `.bak.txt` file

---

## 🏗️ Project Structure

```
BackupFiles/
├── app.config
├── app.version.cs
└── src/
    ├── classes.cs
    └── Main.cs
```

---

## ⚙️ Configuration

Main parameters are defined in **`app.config`**.

```xml
<configuration>
  <company_name>LEMEX</company_name>
  <project_name>BackupFiles</project_name>
  <project_title>Backup Files</project_title>
  <description>Simple Backup of Project Files</description>

  <major_version>1</major_version>
  <minor_version>0</minor_version>
  <build_configuration>Release</build_configuration>
  <build_target>exe</build_target>

  <lib_folders>lib,src</lib_folders>
  <icon_file_path>.\res\icon.ico</icon_file_path>

  <release_version>24</release_version>
  <build_version>24</build_version>
</configuration>
```

### 🔧 User Configuration File `backup.config.xml`

Example of the auto-generated configuration file template (created on first launch):

```xml
<configuration>
  <ProjectName>MyProject</ProjectName>
  <Version>1.0.0</Version>

  <extensions>
    <extension>.config</extension>
    <extension>.cs</extension>
  </extensions>

  <includePaths>
    <includePath>./include</includePath>
  </includePaths>

  <excludePaths>
    <excludePath>./exclude</excludePath>
  </excludePaths>

  <ResultPath>./output</ResultPath>
  <ResultFilenameMask>@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt</ResultFilenameMask>

  <EnableZip>true</EnableZip>
  <DeleteUnziped>true</DeleteUnziped>

  <IsExample>0</IsExample>
</configuration>
```

---

## 💡 Usage

### 1️⃣ Create a Backup
1. Make sure `backup.config.xml` is properly configured.  
2. Run the program **without arguments**:
   ```bash
   BackupFiles.exe
   ```
3. The application will generate a backup file in the configured output folder (`ResultPath`).

### 2️⃣ Restore a Project from `.bak.txt`
1. Run the program **with the backup file as argument**:
   ```bash
   BackupFiles.exe MyBackup_1.0.0_20251011.bak.txt
   ```
2. The program will restore the full project structure in a new folder with the same name as the backup file.

---

## 🧮 Backup Filename Format

The backup file name is generated based on the mask in the config:

```
@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt
```

Example output:
```
BackupFiles_1.0.7_20251011104647.bak.txt
```

---

## 🪄 PowerShell Commands for Archiving

If `EnableZip = true` is enabled, the following commands are used internally:

To zip:
```powershell
Compress-Archive -Path "sourcefile" -DestinationPath "destination.zip"
```
To unzip:
```powershell
Expand-Archive -Path "backup.zip" -DestinationPath "folder"
```

---

## 🖼️ Examples

### 🔸 Comparing Two Backups
You can easily compare different versions of backups in a diff tool such as *WinMerge*, *Notepad++* or *Beyond Compare*  
— perfect for seeing file and structure changes without using Git.

![Backup comparison example](img/backup_comparison_example.jpg)

---

### 🔸 Backup Storage Examples
Backups are stored as plain text `.bak.txt` files and optionally compressed into `.zip` archives.

![Backup files list](img/backup_files_list.jpg)

---

### 🔸 Project Restoration
You can restore an entire project structure (files and directories) directly from `.bak.txt` or `.zip`.  
Below are three stages of restoration:

1. Launching BackupFiles and selecting the archive  
   ![Open with BackupFiles](img/open_with_backup_files.jpg)

2. Console output during extraction and file creation  
   ![Restoration process in console](img/restoration_process_in_console.jpg)

3. Fully restored project structure in Windows Explorer  
   ![Restored project folder view](img/restored_project_folder_view.jpg)

---

## 🧑‍💻 Author

**Maxim Hegel © 2025**  
📧 [i@hgl.mx](mailto:i@hgl.mx)  
🔗 [LinkedIn](https://www.linkedin.com/in/maximhegel)

---

## 📜 License

MIT License — free to use and modify with attribution.
