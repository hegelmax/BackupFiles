# 🗃️ BackupFiles  
**BackupFiles** — консольное приложение на C#, предназначенное для создания резервных копий исходных файлов проекта и восстановления структуры проекта из `.bak.txt` или `.zip`.

Приложение сканирует директории, отбирает файлы по расширениям, применяет фильтры include/exclude, формирует единый текстовый бэкап и при необходимости архивирует его.  
Поддерживается загрузка альтернативных конфигураций через drag & drop.

---

## 🚀 Основные возможности

- 🔍 Поиск файлов по расширениям  
- 📁 Поддержка include/exclude путей  
- ✨ Поддержка wildcard-паттернов (`*.min.js`, `*/node_modules/*`, и т.д.)  
- ❗ Приоритетные include-файлы с `!` (игнорируют exclude)  
- 🧩 Автогенерация древовидной структуры проекта  
- 🧾 Формирование `.bak.txt` с содержимым всех файлов  
- 🗜️ ZIP-сжатие результата (опционально)  
- 🔄 Автоинкремент версии  
- ♻️ Восстановление проекта из `.bak.txt` или `.zip`  
- 🧰 Поддержка нескольких конфигураций:
  - Без аргументов → используется `backup.config.xml`
  - Drag & drop XML → используется конкретный конфиг

---

## ⚙️ Конфигурация

Приложение использует файл `backup.config.xml`.  
Если он отсутствует, при первом запуске автоматически создаётся шаблон **с подробной инструкцией**:

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

### Пример:

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

## 💡 Как пользоваться

### 📌 Вариант 1 — стандартный бэкап
Запустите:

```cmd
BackupFiles.exe
```

Будет использован `backup.config.xml`.

### 📌 Вариант 2 — бэкап с альтернативным конфигом  
Перетащите файл `*.xml` на `BackupFiles.exe`.

### 📌 Вариант 3 — восстановление  
Запуск с файлом:

```cmd
BackupFiles.exe MyBackup_1.0.7_20251011104647.bak.txt
```

или ZIP:

```cmd
BackupFiles.exe MyBackup.zip
```

---

## 🧮 Формирование имени файла

Имя создаётся по шаблону:

```
@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt
```

Пример:

```
BackupFiles_1.0.7_20251011104647.bak.txt
```

---

## 🪄 PowerShell команды (упаковка/распаковка)

```powershell
Compress-Archive -Path "source" -DestinationPath "result.zip"
Expand-Archive -Path "backup.zip" -DestinationPath "folder"
```

---

## 🧑‍💻 Автор

**Maxim Hegel © 2025**  
📧 i@hgl.mx  
🔗 linkedin.com/in/maximhegel

---

## 📜 Лицензия

MIT — свободное использование с указанием автора.
