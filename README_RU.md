
# 🗃️ BackupFiles

**BackupFiles** — это простое консольное приложение на C#, предназначенное для создания резервных копий исходных файлов проектов и восстановления структуры проекта из этих бэкапов.  
Программа собирает все файлы с указанными расширениями из заданных каталогов, формирует текстовый бэкап-файл со структурой проекта и при необходимости упаковывает результат в ZIP-архив.

В актуальных версиях добавлены несколько конфигов, wildcard-паттерны в исключениях, принудительное включение файлов, проверка обновлений, dry-run режим, уровни логирования, лимиты по размеру/возрасту и автоочистка старых бэкапов.

---

## 🚀 Возможности

- 🔍 Поиск файлов по указанным расширениям  
- 📁 Поддержка include/exclude путей  
- ✨ Поддержка wildcard-паттернов в исключениях (например, `*.min.js`, `*/node_modules/*`, `backup.*.config.xml`)  
- ❗ Приоритетные include-файлы с `!` в конце строки (игнорируют `ExcludePaths`)  
- 🧰 Несколько конфигураций:
  - Запуск без аргументов → используется `default.backupconfig`, с fallback на legacy `backup.config.xml`
  - Перетаскивание `.backupconfig`- или XML-файла на `BackupFiles.exe` → используется этот конфиг
- ?? Инкрементальный бэкап (только измененные файлы)
- ?? Dry-run режим (предпросмотр без записи бэкапа)
- ?? Уровни логирования (quiet/normal/verbose) + лог в файл
- ?? Лимиты по размеру/возрасту для пропуска файлов
- ?? Автоочистка старых бэкапов по количеству или возрасту
- 🧩 Генерация структуры каталогов в древовидном формате  
- 🧾 Автоматическое сохранение содержимого файлов в `.bak.txt`  
- 🗜️ Опциональное архивирование результата (ZIP)  
- 🔄 Автоинкремент версии проекта  
- ♻️ Возможность восстановления проекта из `.bak.txt` или `.zip`

---

## 🏗️ Структура проекта

```
BackupFiles/
├── app.config
├── app.version.cs
└── src/
    ├── classes.cs
    └── Main.cs
```

---

## ⚙️ Конфигурация

Основные параметры сборки задаются в файле **`app.config`**:

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

---

### 🔧 Пользовательский конфиг `default.backupconfig`

Пользовательские настройки бэкапа задаются в файле **`default.backupconfig`**. Конфиг может лежать рядом с `exe`, в корне проекта или в отдельной папке.

Если конфиг не найден, при первом запуске автоматически создаётся шаблон с подробной инструкцией в комментарии:

```xml
<!-- HOW TO USE THIS FILE
1. This file defines which files and folders will be included in your backup.
2. IncludePaths – folders scanned recursively.
3. IncludeFiles – specific files added manually.
   - If a file ends with "!", it ignores ExcludePaths and will ALWAYS be included.
4. ExcludePaths – wildcard patterns for files/folders to exclude.
5. Extensions - allowed file formats.
6. ResultPath - folder where backups will be saved.
7. ResultFilenameMask - pattern used to build the backup filename.
8. Created - last backup timestamp (updated automatically).
9. UpdateCheckMinutes - update check interval in minutes (0 disables).
10. UpdateCheckTimeoutSeconds - update check timeout in seconds.
11. UpdateCheckVerbose - detailed update check logs (true/false).
12. DryRun - preview files without writing a backup (true/false).
13. LogLevel - quiet | normal | verbose.
14. LogToFile - enable log file output (true/false).
15. LogFilePath - log file path.
16. IncrementalBackup - include only files changed since last backup (true/false).
17. MaxFileSizeMB - exclude files larger than this size (0 disables).
18. MaxFileAgeDays - exclude files older than N days (0 disables).
19. CleanupKeepLast - keep only last N backups (0 disables).
20. CleanupMaxAgeDays - delete backups older than N days (0 disables).
21. IsExample=1 disables work. Set it to 0 before using.
22. To use this config, drag & drop it onto the BackupFiles.exe application.
Path base modes:
  - root = relative to RootPath
  - config = relative to config XML folder
  - exe = relative to executable folder
  - current = relative to process working directory
Default base is root.
END OF INSTRUCTIONS -->
```

Пример (упрощённый):

```xml
<configuration>
  <ProjectName>MyProject</ProjectName>
  <Version>1.0.0</Version>
  <Created>YYYY-MM-DD hh:mm:ss</Created>
  <RootPath>./</RootPath>
  <UpdateCheckMinutes>1440</UpdateCheckMinutes>
  <UpdateCheckTimeoutSeconds>5</UpdateCheckTimeoutSeconds>
  <UpdateCheckVerbose>false</UpdateCheckVerbose>
  <DryRun>false</DryRun>
  <LogLevel>normal</LogLevel>
  <LogToFile>false</LogToFile>
  <LogFilePath base="root">./backup.log</LogFilePath>
  <IncrementalBackup>false</IncrementalBackup>
  <MaxFileSizeMB>0</MaxFileSizeMB>
  <MaxFileAgeDays>0</MaxFileAgeDays>
  <CleanupKeepLast>0</CleanupKeepLast>
  <CleanupMaxAgeDays>0</CleanupMaxAgeDays>

  <extensions>
    <extension>.config</extension>
    <extension>.cs</extension>
  </extensions>

  <includePaths>
    <includePath>./include</includePath>
    <includePath base="config">./lib</includePath>
    <includePath base="exe">./assets</includePath>
  </includePaths>

  <excludePaths>
    <excludePath>./exclude</excludePath>
    <excludePath base="config">./temp</excludePath>
  </excludePaths>

  <ResultPath base="root">./output</ResultPath>
  <ResultFilenameMask>@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt</ResultFilenameMask>

  <EnableZip>true</EnableZip>
  <DeleteUnziped>true</DeleteUnziped>

  <IsExample>0</IsExample>
</configuration>
```

Дополнение по `extensions`: теперь это маски файлов.  
Пример: `.js` ⇒ `*.js`, `.min.js` ⇒ `*.min.js`.  
Правила применяются от самых длинных масок к коротким, поэтому `*.min.js` сработает раньше, чем `*.js`.

#### Расширенные настройки (актуальные версии)

- **Wildcard-паттерны в `ExcludePaths`**:
  - `*.min.js` — исключить все минифицированные JS-файлы  
  - `*/node_modules/*` — исключить содержимое любых папок `node_modules`  
  - `backup.*.config.xml` — исключить все варианты конфигов по маске  
- **Приоритетные файлы в `IncludeFiles`**:
  - `./backup.web.config.xml` — будет включён только если не попадает под `ExcludePaths`  
  - `./backup.api.config.xml !` — будет включён всегда, даже если совпадает с маской в `ExcludePaths`  
- **Несколько конфигов**:
  - Можно хранить несколько конфигов (`default.backupconfig`, `backup.web.config.xml`, `backup.api.config.xml` и т.п.) рядом с приложением и запускать бэкап с нужным конфигом, просто перетаскивая его на `BackupFiles.exe`.
- **Разрешение путей**:
  - `RootPath` задаёт корень проекта.
  - Относительные пути по умолчанию используют `root`, а для переопределения можно использовать `base="config"`, `base="exe"` и `base="current"`.
- **Проверка обновлений**:
  - `UpdateCheckMinutes` - интервал проверки в минутах (0 отключает).
  - `UpdateCheckTimeoutSeconds` - таймаут запроса в секундах.
  - `UpdateCheckVerbose` - подробные логи проверки обновлений.
  - `Created` - отметка времени последнего бэкапа, обновляется автоматически.
- **Dry-run**:
  - `DryRun` - предпросмотр без записи бэкапа.
- **Инкрементальный бэкап**:
  - `IncrementalBackup` - включать только файлы, измененные после последнего бэкапа (по `Created`).
- **Логирование**:
  - `LogLevel` - quiet | normal | verbose.
  - `LogToFile` / `LogFilePath` - запись лога в файл.
- **Лимиты**:
  - `MaxFileSizeMB` - пропуск файлов больше указанного размера.
  - `MaxFileAgeDays` - пропуск файлов старше N дней.
- **Очистка**:
  - `CleanupKeepLast` - хранить последние N бэкапов.
  - `CleanupMaxAgeDays` - удалять бэкапы старше N дней.

---

## 💡 Как использовать

### 1️⃣ Создание бэкапа (дефолтный конфиг)

1. Убедитесь, что `default.backupconfig` настроен.  
2. Запустите программу **без аргументов**:

   ```bash
   BackupFiles.exe
   ```

3. Приложение создаст `.bak.txt` (и при необходимости `.zip`) в директории `ResultPath`.

### 2️⃣ Создание бэкапа с альтернативным конфигом

1. Создайте отдельный конфиг, например `backup.web.config.xml`.  
2. Перетащите этот файл XML на `BackupFiles.exe`.  
3. Бэкап будет выполнен с использованием именно этого файла.

### 3️⃣ Восстановление проекта из `.bak.txt` или `.zip`

1. Для восстановления из текстового бэкапа:

   ```bash
   BackupFiles.exe MyBackup_1.0.0_20251011.bak.txt
   ```

2. Для восстановления из ZIP-архива, созданного приложением:

   ```bash
   BackupFiles.exe MyBackup_1.0.0_20251011.bak.txt.zip
   ```

   Приложение автоматически разархивирует файл и восстановит структуру проекта.

Восстановленные файлы появятся в новой папке с тем же именем, что и у файла бэкапа.

---

## 🧮 Формат имени файла бэкапа

Имя итогового файла формируется по маске из конфига:

```
@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt
```

Пример:

```
BackupFiles_1.0.7_20251011104647.bak.txt
```

Если включено ZIP-сжатие, создаётся файл:

```
BackupFiles_1.0.7_20251011104647.bak.txt.zip
```

---

## 🪄 Команды PowerShell для архивации

При включённом `EnableZip = true` внутренне используются команды:

Упаковка:

```powershell
Compress-Archive -Path "sourcefile" -DestinationPath "destination.zip"
```

Распаковка:

```powershell
Expand-Archive -Path "backup.zip" -DestinationPath "folder"
```

---

## 🖼️ Примеры

### 🔸 Сравнение двух резервных копий

Вы можете легко сравнить разные версии резервных копий с помощью инструментов, таких как *WinMerge*, *Notepad++* или *Beyond Compare* — удобно для просмотра изменений в файлах и структуре без Git.

![Пример сравнения резервных копий](img/backup_comparison_example.jpg)

---

### 🔸 Примеры хранения резервных копий

Резервные копии хранятся в виде текстовых файлов `.bak.txt` и при необходимости сжимаются в `.zip`.

![Список файлов резервных копий](img/backup_files_list.jpg)

---

### 🔸 Восстановление проекта

Вы можете восстановить всю структуру проекта (файлы и каталоги) непосредственно из `.bak.txt` или `.zip`.  
Ниже показаны три этапа восстановления:

1. Запуск BackupFiles и выбор архива  
   ![Открыть с помощью BackupFiles](img/open_with_backup_files.jpg)

2. Вывод в консоль во время извлечения и создания файлов  
   ![Процесс восстановления в консоли](img/restoration_process_in_console.jpg)

3. Полностью восстановленная структура проекта в проводнике Windows  
   ![Восстановленный вид папки проекта](img/restored_project_folder_view.jpg)

---

## 🧑‍💻 Автор

**Maxim Hegel © 2025**  
📧 [i@hgl.mx](mailto:i@hgl.mx)  
🔗 [LinkedIn](https://www.linkedin.com/in/maximhegel)

---

## 📜 Лицензия

MIT License — свободное использование и модификация с указанием автора.

