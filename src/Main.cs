using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace BackupFiles
{
	class TreeNode {
		public string Name;
		public bool IsFile;
		public bool IsSkipped;
		public Dictionary<string, TreeNode> Children = new Dictionary<string, TreeNode>();
		public bool IsCollapsible;
	}
	
	class FileEntry {
		public string Path;
		public bool TreeOnly;
	}

	class PatternRule {
		public string Pattern;
		public bool TreeOnly;
		public int Length;
		public int Index;
	}

	enum LogLevel
	{
		Quiet = 0,
		Normal = 1,
		Verbose = 2
	}

	enum AssociationStatus
	{
		NotRegistered = 0,
		RegisteredToCurrentExe = 1,
		RegisteredToOtherExe = 2
	}

	class BackupStats
	{
		public DateTime StartTime;
		public DateTime EndTime;
		public int ScannedFiles;
		public int IncludedFiles;
		public int TreeOnlyFiles;
		public int ExcludedFiles;
		public int SkippedByPattern;
		public int SkippedByUnchanged;
		public int SkippedBySize;
		public int SkippedByAge;
		public int SkippedBinary;
		public int PackedFiles;
		public long PackedBytes;
	}

	class PathContext
	{
		public string ConfigFilePath;
		public string ConfigFolder;
		public string ExeFolder;
		public string CurrentFolder;
		public string RootFolder;
	}
	
	class Program
	{
		const string DefaultConfigFileName = "default.backupconfig";
		const string LegacyDefaultConfigFileName = "backup.config.xml";
		const string AssociationExtension = ".backupconfig";
		const string AssociationProgId = "BackupFiles.Config";

		static LogLevel _logLevel = LogLevel.Normal;
		static StreamWriter _logWriter;
		static string _logFilePath;
		static bool _logToFile;
		static bool _progressActive;
		static int _progressLastLength;

		static void WriteColored(ConsoleColor color, string format, params object[] args) {
			if (_progressActive) {
				Console.WriteLine();
				_progressActive = false;
				_progressLastLength = 0;
			}

			string message = (args != null && args.Length > 0)
				? string.Format(format, args)
				: format;
			ConsoleColor previous = Console.ForegroundColor;
			Console.ForegroundColor = color;
			Console.WriteLine(message);
			Console.ForegroundColor = previous;
			WriteToLogFile(message);
		}

		static void WriteToLogFile(string message) {
			if (_logWriter == null || string.IsNullOrEmpty(message)) {
				return;
			}

			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
			_logWriter.WriteLine("[{0}] {1}", timestamp, message);
		}
		
		static void LogInfo(string format, params object[] args) {
			if (_logLevel >= LogLevel.Normal) {
				WriteColored(ConsoleColor.Cyan, format, args);
			}
		}
		
		static void LogSuccess(string format, params object[] args) {
			if (_logLevel >= LogLevel.Normal) {
				WriteColored(ConsoleColor.Green, format, args);
			}
		}
		
		static void LogWarning(string format, params object[] args) {
			WriteColored(ConsoleColor.Yellow, format, args);
		}
		
		static void LogError(string format, params object[] args) {
			WriteColored(ConsoleColor.Red, format, args);
		}

		static void LogDebug(string format, params object[] args) {
			if (_logLevel >= LogLevel.Verbose) {
				WriteColored(ConsoleColor.DarkGray, format, args);
			}
		}

		static void WriteProgress(string message, bool isFinal) {
			if (_logLevel < LogLevel.Normal) {
				return;
			}

			if (message == null) {
				message = string.Empty;
			}

			string padded = message;
			if (padded.Length < _progressLastLength) {
				padded = padded + new string(' ', _progressLastLength - padded.Length);
			}

			ConsoleColor previous = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.Write("\r" + padded);
			Console.ForegroundColor = previous;

			_progressLastLength = padded.Length;
			_progressActive = !isFinal;

			if (isFinal) {
				Console.WriteLine();
				_progressLastLength = 0;
			}
		}

		static void InitLogging(Config config, PathContext pathContext) {
			if (config == null) {
				return;
			}

			string level = config.LogLevel ?? "normal";
			string normalized = level.Trim().ToLowerInvariant();
			if (normalized == "quiet") {
				_logLevel = LogLevel.Quiet;
			}
			else if (normalized == "verbose") {
				_logLevel = LogLevel.Verbose;
			}
			else {
				_logLevel = LogLevel.Normal;
			}

			_logToFile = config.LogToFile;
			if (!_logToFile) {
				return;
			}

			string path = string.IsNullOrWhiteSpace(GetPathValue(config.LogFilePath))
				? "./backup.log"
				: GetPathValue(config.LogFilePath).Trim();
			path = ResolvePath(path, GetPathBase(config.LogFilePath, config.LegacyLogFilePathBase), pathContext, "root");

			try {
				string dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(dir)) {
					Directory.CreateDirectory(dir);
				}
				_logWriter = new StreamWriter(path, true);
				_logWriter.AutoFlush = true;
				_logFilePath = path;
				LogDebug("Log file enabled: {0}", _logFilePath);
			}
			catch (Exception ex) {
				_logWriter = null;
				_logToFile = false;
				LogWarning("Failed to open log file: {0}", ex.Message);
			}
		}

		static void CloseLogWriter() {
			if (_logWriter == null) {
				return;
			}

			try {
				_logWriter.Flush();
				_logWriter.Close();
			}
			catch {
				// ignore log close errors
			}
			finally {
				_logWriter = null;
			}
		}
		
		static string FormatBytes(long bytes) {
			string[] sizes = new string[] { "B", "KB", "MB", "GB", "TB" };
			double len = bytes;
			int order = 0;
			while (len >= 1024 && order < sizes.Length - 1) {
				order++;
				len = len / 1024;
			}
			return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", len, sizes[order]);
		}

		static void PrintSummary(BackupStats stats, bool dryRun) {
			stats.EndTime = DateTime.Now;
			TimeSpan duration = stats.EndTime - stats.StartTime;
			int skippedTotal = stats.ExcludedFiles + stats.SkippedByPattern + stats.SkippedByUnchanged + stats.SkippedBySize + stats.SkippedByAge + stats.SkippedBinary;

			if (dryRun) {
				LogInfo("Dry-run: no backup file written.");
			}

			LogInfo(
				"Summary: scanned {0}, included {1} (tree-only {2}), packed {3}, skipped {4}, size {5}, time {6:0.0}s.",
				stats.ScannedFiles,
				stats.IncludedFiles,
				stats.TreeOnlyFiles,
				stats.PackedFiles,
				skippedTotal,
				FormatBytes(stats.PackedBytes),
				duration.TotalSeconds
			);

			if (stats.ExcludedFiles > 0 || stats.SkippedByPattern > 0 || stats.SkippedByUnchanged > 0 || stats.SkippedBySize > 0 || stats.SkippedByAge > 0 || stats.SkippedBinary > 0) {
				LogDebug(
					"Skipped details: excluded {0}, pattern mismatch {1}, unchanged {2}, size {3}, age {4}, binary {5}.",
					stats.ExcludedFiles,
					stats.SkippedByPattern,
					stats.SkippedByUnchanged,
					stats.SkippedBySize,
					stats.SkippedByAge,
					stats.SkippedBinary
				);
			}
		}
		
		static void Main(string[] args) {
			try {
				if (args.Length != 0) {
					string firstArg = args[0];

					if (string.Equals(firstArg, "--register-association", StringComparison.OrdinalIgnoreCase)) {
						RegisterFileAssociation();
					}
					else if (string.Equals(firstArg, "--unregister-association", StringComparison.OrdinalIgnoreCase)) {
						UnregisterFileAssociation();
					}
					else if (IsConfigFile(firstArg)) {
						LogInfo("Using config file: {0}", firstArg);
						RunBackupWithConfig(firstArg);
					}
					else {
						RunRestoreFromBackup(firstArg);
					}
				}
				else {
					EnsureFileAssociationInteractive();
					RunBackupWithDefaultConfig();
				}
			}
			catch (Exception ex) {
				LogError("An unexpected error occurred: {0}", ex.Message);
			}
			
			WaitForUserInput();
			CloseLogWriter();
		}

		static void RunBackupWithDefaultConfig() {
			string exeFolder = GetNormalizedDirectory(AppDomain.CurrentDomain.BaseDirectory);
			string defaultConfigPath = Path.Combine(exeFolder, DefaultConfigFileName);
			string legacyConfigPath = Path.Combine(exeFolder, LegacyDefaultConfigFileName);

			if (File.Exists(defaultConfigPath)) {
				LogInfo("Using default config file: {0}", defaultConfigPath);
				RunBackupWithConfig(defaultConfigPath);
				return;
			}

			if (File.Exists(legacyConfigPath)) {
				LogInfo("Using legacy default config file: {0}", legacyConfigPath);
				RunBackupWithConfig(legacyConfigPath);
				return;
			}

			CreateConfigTemplate(defaultConfigPath);
			LogWarning("Default config template created: {0}", defaultConfigPath);
			LogWarning("Please configure it and update the 'is_example' parameter to 0.");
		}

		static bool IsConfigFile(string path) {
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
				return false;
			}

			string extension = Path.GetExtension(path);
			return string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(extension, AssociationExtension, StringComparison.OrdinalIgnoreCase);
		}
		
		static void RunBackupWithConfig(string configPath) {
			configPath = Path.GetFullPath(configPath);
			string exeFolder = GetNormalizedDirectory(AppDomain.CurrentDomain.BaseDirectory);
			Config config;
			
			bool isDefaultConfig = string.Equals(
				configPath,
				Path.Combine(exeFolder, DefaultConfigFileName),
				StringComparison.OrdinalIgnoreCase
			) || string.Equals(
				configPath,
				Path.Combine(exeFolder, LegacyDefaultConfigFileName),
				StringComparison.OrdinalIgnoreCase
			);
			
			if (!File.Exists(configPath)) {
				if (isDefaultConfig) {
					// create a template
					CreateConfigTemplate(configPath);
					LogWarning("Config file is missing! A template has been created. Please update the 'is_example' parameter to 0.");
				}
				else {
					LogError("Config file '{0}' not found.", configPath);
				}
				return;
			}
			
			config = Deserialize<Config>(configPath);
			EnsureConfigRuntimeDefaults(config);
			
			if (config.IsExample == 1) {
				LogWarning("Config file is not set. Please update the 'is_example' parameter to 0.");
				return;
			}

			EnsureConfigDefaults(configPath, config);
			PathContext pathContext = CreatePathContext(configPath, config);
			InitLogging(config, pathContext);
			LogDebug("Config file: {0}", pathContext.ConfigFilePath);
			LogDebug("Exe folder: {0}", pathContext.ExeFolder);
			LogDebug("Current folder: {0}", pathContext.CurrentFolder);
			LogDebug("Root folder: {0}", pathContext.RootFolder);
			EnableTls12();

			string updateReason;
			if (ShouldCheckForUpdates(config, out updateReason)) {
				if (config.UpdateCheckVerbose && !string.IsNullOrWhiteSpace(updateReason)) {
					LogDebug(updateReason);
				}
				CheckForUpdates(config.UpdateCheckTimeoutSeconds, config.UpdateCheckVerbose);
			}
			else if (config.UpdateCheckVerbose && !string.IsNullOrWhiteSpace(updateReason)) {
				LogDebug(updateReason);
			}

			if (string.IsNullOrEmpty(config.ProjectName) || string.IsNullOrEmpty(config.Version)) {
				LogError("ProjectName or Version is missing in the config file.");
				return;
			}
			
			var extensionItems = config.Extensions == null
				? new List<ConfigItem>()
				: config.Extensions.GetAllExtensions();

			int enabledExtensionCount = extensionItems
				.Count(item => item != null && item.Enable && !string.IsNullOrWhiteSpace(item.Value));

			if (enabledExtensionCount == 0 ||
				config.IncludePaths == null || config.IncludePaths.Count == 0) {
				LogError("Extensions or IncludePaths are missing in the config file.");
				return;
			}
			
			// Ensure result path exists
			string resultDir = ResolvePath(
				GetPathValueOrDefault(config.ResultPath, "./backup"),
				GetPathBase(config.ResultPath, config.LegacyResultPathBase),
				pathContext,
				"root"
			);
			try {
				Directory.CreateDirectory(resultDir);
			}
			catch (Exception ex) {
				LogError("Failed to create result directory: {0}", ex.Message);
				return;
			}
			
			// Collect files
			List<FileEntry> files;
			var stats = new BackupStats { StartTime = DateTime.Now };
			try {
				files = GetFiles(config, pathContext, extensionItems, stats);
			}
			catch (Exception ex) {
				LogError("Error while collecting files: {0}", ex.Message);
				return;
			}

			if (files.Count == 0) {
				LogWarning("Backup skipped: no files to pack.");
				PrintSummary(stats, false);
				return;
			}
			
			// Write result file
			string resultFilePath;
			try {
				resultFilePath = GetResultFilePath(config, pathContext);
			}
			catch (Exception ex) {
				LogError("Error while generating result file path: {0}", ex.Message);
				return;
			}
			
			if (config.DryRun) {
				RunDryMode(files, stats, pathContext.RootFolder);
				PrintSummary(stats, true);
				return;
			}

			try {
				if (WriteResultFile(config, files, resultFilePath, pathContext.RootFolder, stats)) {
					// After successful backup, increment the version
					IncrementVersion(configPath);
					LogSuccess("Backup completed successfully. Files are packed in {0}", resultFilePath);
					CleanupOldBackups(config, resultDir, resultFilePath);
				}
			}
			catch (Exception ex) {
				LogError("Error while writing the result file: {0}", ex.Message);
				return;
			}

			PrintSummary(stats, false);
		}

		static void EnsureFileAssociationInteractive() {
			try {
				AssociationStatus status = GetFileAssociationStatus();
				if (status == AssociationStatus.RegisteredToCurrentExe) {
					return;
				}

				if (status == AssociationStatus.NotRegistered) {
					if (AskYesNo("File association for .backupconfig is not registered." + Environment.NewLine + "Register it for this EXE? [Y/N]")) {
						RegisterFileAssociation();
					}
					return;
				}

				string registeredPath = GetAssociatedExecutablePath();
				string currentExePath = GetCurrentExecutablePath();
				string message = string.Format(
					CultureInfo.InvariantCulture,
					".backupconfig is currently associated with another executable:{0}{1}{0}{0}Replace association with this EXE?{0}{2}{0}{0}[Y/N]",
					Environment.NewLine,
					string.IsNullOrWhiteSpace(registeredPath) ? "(unknown)" : registeredPath,
					currentExePath
				);
				if (AskYesNo(message)) {
					RegisterFileAssociation();
				}
			}
			catch (Exception ex) {
				LogWarning("File association check failed: {0}", ex.Message);
			}
		}

		static AssociationStatus GetFileAssociationStatus() {
			try {
				string associatedExePath = GetAssociatedExecutablePath();
				if (string.IsNullOrWhiteSpace(associatedExePath)) {
					return AssociationStatus.NotRegistered;
				}

				string currentExePath = GetCurrentExecutablePath();
				string normalizedAssociated = Path.GetFullPath(associatedExePath);
				string normalizedCurrent = Path.GetFullPath(currentExePath);
				return string.Equals(normalizedAssociated, normalizedCurrent, StringComparison.OrdinalIgnoreCase)
					? AssociationStatus.RegisteredToCurrentExe
					: AssociationStatus.RegisteredToOtherExe;
			}
			catch (Exception ex) {
				throw new InvalidOperationException("Unable to determine file association status.", ex);
			}
		}

		static string GetAssociatedExecutablePath() {
			try {
				using (RegistryKey classesKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + AssociationExtension)) {
					string progId = classesKey == null ? null : classesKey.GetValue(null) as string;
					if (string.IsNullOrWhiteSpace(progId)) {
						return null;
					}

					using (RegistryKey commandKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + progId + @"\shell\open\command")) {
						string command = commandKey == null ? null : commandKey.GetValue(null) as string;
						return ExtractExecutablePathFromCommand(command);
					}
				}
			}
			catch (Exception ex) {
				throw new InvalidOperationException("Unable to read file association from registry.", ex);
			}
		}

		static bool AskYesNo(string message) {
			while (true) {
				Console.WriteLine(message);
				string input = Console.ReadLine();
				if (input == null) {
					return false;
				}

				string normalized = input.Trim().ToLowerInvariant();
				if (normalized == "y" || normalized == "yes") {
					return true;
				}
				if (normalized == "n" || normalized == "no") {
					return false;
				}

				Console.WriteLine("Please answer Y or N.");
			}
		}

		static string ExtractExecutablePathFromCommand(string command) {
			if (string.IsNullOrWhiteSpace(command)) {
				return null;
			}

			string trimmed = command.Trim();
			Match quoted = Regex.Match(trimmed, "^\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase);
			if (quoted.Success) {
				return quoted.Groups["path"].Value;
			}

			Match unquoted = Regex.Match(trimmed, @"^(?<path>\S+?\.exe)(?:\s|$)", RegexOptions.IgnoreCase);
			return unquoted.Success ? unquoted.Groups["path"].Value : null;
		}

		static void RegisterFileAssociation() {
			try {
				string currentExePath = GetCurrentExecutablePath();
				string command = string.Format(CultureInfo.InvariantCulture, "\"{0}\" \"%1\"", currentExePath);

				using (RegistryKey extensionKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + AssociationExtension)) {
					extensionKey.SetValue(null, AssociationProgId, RegistryValueKind.String);
				}

				using (RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + AssociationProgId + @"\shell\open\command")) {
					commandKey.SetValue(null, command, RegistryValueKind.String);
				}

				LogSuccess("File association registered for {0}", AssociationExtension);
			}
			catch (Exception ex) {
				LogWarning("Failed to register file association: {0}", ex.Message);
			}
		}

		static void UnregisterFileAssociation() {
			try {
				Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + AssociationExtension, false);
				Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + AssociationProgId, false);
				LogSuccess("File association removed for {0}", AssociationExtension);
			}
			catch (Exception ex) {
				LogWarning("Failed to unregister file association: {0}", ex.Message);
			}
		}

		static string GetCurrentExecutablePath() {
			return Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
		}
		
		static void RunRestoreFromBackup(string filePath) {
			try {
				if (!File.Exists(filePath)) {
					LogError("The specified file does not exist.");
					return;
				}
				
				string extension                = Path.GetExtension(filePath).ToLower();
				string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
				string newFolder                = Path.Combine(Path.GetDirectoryName(filePath), fileNameWithoutExtension);
				string unzipFilePath            = "";
				
				if (!Directory.Exists(newFolder)) {
					Directory.CreateDirectory(newFolder);
				}
				
				if (extension == ".zip") {
					unzipFilePath = filePath.Replace(".zip", "");
					LogInfo("Unzipping file: {0}", filePath);
					UnzipFileUsingPowerShell(filePath, unzipFilePath);
					string[] files = Directory.GetFiles(unzipFilePath);
					if (files.Length > 0) {
						filePath     = files[0];
						unzipFilePath = filePath;
					}
					else {
						LogError("The error occurred while unziping file");
						return;
					}
				}
				
				LogInfo("Processing text file: {0}", filePath);
				CreateFileStructureFromText(filePath, newFolder);
				
				//Delete unziped file
				if (!string.IsNullOrEmpty(unzipFilePath) && File.Exists(unzipFilePath)) {
					File.Delete(unzipFilePath);
				}
			}
			catch (Exception ex) {
				LogError("Error restoring from backup: {0}", ex.Message);
			}
		}
		
		// Increment the version in the XML file
		static void IncrementVersion(string configPath) {
			try {
				XDocument doc = XDocument.Load(configPath);
				var configElement = doc.Element("configuration");
				
				if (configElement != null) {
					// Get the current version string
					XElement versionElement = configElement.Element("Version");
					string versionString = versionElement.Value;
					
					// Split the version string (UserDefinedPart.Major.Minor)
					string[] versionParts = versionString.Split('.');
					
					if (versionParts.Length == 3) {
						string userDefinedPart = versionParts[0];
						int majorVersion = int.Parse(versionParts[1]);
						int minorVersion = int.Parse(versionParts[2]);
						
						// Increment logic: increase minor until 99, then increment major and reset minor to 0
						if (minorVersion < 99) {
							minorVersion++;
						}
						else {
							minorVersion = 0;
							majorVersion++;
						}
						
						// Construct the new version string
						string newVersion = userDefinedPart+"."+majorVersion+"."+minorVersion;
						
					// Update the XML with the new version
					versionElement.Value = newVersion;

					// Update or add Created timestamp
					string createdValue = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
					XElement createdElement = configElement.Element("Created");
					if (createdElement != null) {
						createdElement.Value = createdValue;
					}
					else {
						var newCreated = new XElement("Created", createdValue);
						if (versionElement != null) {
							versionElement.AddAfterSelf(newCreated);
						}
						else {
							configElement.Add(newCreated);
						}
					}
						
						// Save the updated configuration back to the file
						doc.Save(configPath);
						
						LogSuccess("Version updated to {0}", newVersion);
					}
					else {
						LogError("Invalid version format.");
					}
				}
				else {
					LogError("Configuration section not found.");
				}
			}
			catch (Exception ex) {
				LogError("Error updating version: {0}", ex.Message);
			}
		}
		
		static void CreateConfigTemplate(string configPath) {
			try {
				string configFolder = GetNormalizedDirectory(Path.GetDirectoryName(Path.GetFullPath(configPath)));
				string currentFolder = GetNormalizedDirectory(Environment.CurrentDirectory);
				string suggestedRootPath = GetSuggestedTemplateRootPath(configFolder, currentFolder);
				string xml = string.Format(CultureInfo.InvariantCulture, @"<?xml version=""1.0"" encoding=""utf-8""?>
<!-- HOW TO USE THIS FILE
	1. This file defines which files and folders will be included in your backup.
	2. IncludePaths - folders scanned recursively.
	3. IncludeFiles - specific files added manually.
	- If a file ends with ""!"", it ignores ExcludePaths and will ALWAYS be included.
	4. ExcludePaths - wildcard patterns for files/folders to exclude:
		* *.min.js
		* */node_modules/*
		* backup.*.config.xml
	Path base modes:
		* root = relative to RootPath
		* config = relative to config XML folder
		* exe = relative to executable folder
		* current = relative to process working directory
	Default base is root.
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
END OF INSTRUCTIONS -->
<configuration>
  <IsExample>1</IsExample>

  <!-- Main -->
  <ProjectName>MyProject</ProjectName>
  <Version>1.0.0</Version>
  <Created>{0}</Created>
  <RootPath>{1}</RootPath>
  <ResultPath base=""root"">./backup</ResultPath><!-- folder where backups will be saved -->
  <ResultFilenameMask>@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt</ResultFilenameMask>

  <!-- Backup filter and settings -->
  <IncrementalBackup>false</IncrementalBackup>
  <MaxFileSizeMB>0</MaxFileSizeMB>
  <MaxFileAgeDays>0</MaxFileAgeDays>
  <EnableZip>false</EnableZip>
  <DeleteUnziped>false</DeleteUnziped>
  <CleanupKeepLast>0</CleanupKeepLast>
  <CleanupMaxAgeDays>0</CleanupMaxAgeDays>

  <!-- Online update -->
  <UpdateCheckMinutes>1440</UpdateCheckMinutes>
  <UpdateCheckTimeoutSeconds>5</UpdateCheckTimeoutSeconds>
  <UpdateCheckVerbose>false</UpdateCheckVerbose>

  <!-- Debug -->
  <DryRun>false</DryRun>
  <LogLevel>normal</LogLevel><!-- quiet | normal | verbose -->
  <LogToFile>false</LogToFile>
  <LogFilePath base=""root"">./backup.log</LogFilePath>

  <!-- Folders to include - scanned recursively -->
  <includePaths>
    <includePath>./</includePath>
  </includePaths>
  
  <!-- Specific files added manually -->
  <includeFiles>
    <includeFile base=""config"">./default.backupconfig</includeFile>
  </includeFiles>
  
  <!-- Wildcard patterns for files/folders to exclude -->
  <excludePaths>
    <excludePath>./.git</excludePath>
    <excludePath>./backup</excludePath>
    <excludePath>./archive</excludePath>
    <excludePath base=""config"">./temp</excludePath>
    <excludePath base=""exe"">./logs</excludePath>
    <excludePath>*/node_modules</excludePath>
    <excludePath>*/vendor</excludePath>
    <excludePath>*/bin</excludePath>
	<excludePath>*/dist</excludePath>
	<excludePath>*/build</excludePath>
	<excludePath>*/obj</excludePath>
    <excludePath>./backup.*.config.xml</excludePath>
	<excludePath>./backup.log</excludePath>
  </excludePaths>

  <!-- Allowed file formats -->
  <extensions>
    <!-- Web technologies and frontend -->
    <group name=""Web and Frontend"">
      <extension>.mhtml</extension>
	  <extension>.html</extension>
	  <extension>.htm</extension>
      <extension>.js</extension>
	  <extension tree_only=""true"">.min.js</extension>
      <extension>.ts</extension>
      <extension>.tsx</extension>
      <extension>.css</extension>
      <extension>.scss</extension>
      <extension>.less</extension>
      <extension>.mjml</extension>
      <extension>.liquid</extension>
      <extension>.twig</extension>
      <extension>.webmanifest</extension>
      <extension>.map</extension>
    </group>

    <!-- Source code and development -->
    <group name=""Programming Languages"">
      <extension>.py</extension>
      <extension>.cs</extension>
      <extension>.csproj</extension>
      <extension>.sln</extension>
      <extension>.php</extension>
      <extension>.asp</extension>
      <extension>.rb</extension>
      <extension>.go</extension>
      <extension>.rs</extension>
      <extension>.java</extension>
      <extension>.kt</extension>
      <extension>.swift</extension>
      <extension>.m</extension>
      <extension>.dart</extension>
    </group>

    <!-- Configs and automation -->
    <group name=""Config and Automation"">
      <extension>.config</extension>
      <extension>.yaml</extension>
      <extension>.yml</extension>
      <extension>.json</extension>
      <extension>.xml</extension>
      <extension>.toml</extension>
      <extension>.ini</extension>
      <extension>.htaccess</extension>
      <extension>.editorconfig</extension>
      <extension>.prettierrc</extension>
      <extension>.eslintrc</extension>
      <extension>.babelrc</extension>
      <extension>.stylelintrc</extension>
      <extension>.dockerignore</extension>
      <extension>.gradle</extension>
      <extension>.sh</extension>
      <extension>.bat</extension>
      <extension>.ps1</extension>
    </group>

    <!-- Data and databases -->
    <group name=""Data and Databases"">
      <extension>.sql</extension>
      <extension>.csv</extension>
      <extension>.dsv</extension>
      <extension>.cube</extension>
      <extension>.dwproj</extension>
      <extension>.partitions</extension>
      <extension>.ds</extension>
      <extension>.dim</extension>
      <extension>.role</extension>
      <extension>.user</extension>
      <extension tree_only=""true"">.db</extension>
      <extension tree_only=""true"">.sqlite</extension>
	  <extension tree_only=""true"">.accdb</extension>
	  <extension tree_only=""true"">.accde</extension>
    </group>

    <!-- Docs and shader files -->
    <group name=""Docs and Shaders"">
      <extension>.md</extension>
      <extension>.txt</extension>
      <extension>.rst</extension>
      <extension>.glsl</extension>
      <extension>.hlsl</extension>
      <extension>.wgsl</extension>
    </group>

    <!-- Images and video -->
    <group name=""Media Assets"" tree_only=""true"">
      <extension>.jpg</extension>
      <extension>.jpeg</extension>
      <extension>.png</extension>
      <extension>.webp</extension>
      <extension>.gif</extension>
      <extension>.ico</extension>
      <extension>.svg</extension>
      <extension>.mp4</extension>
      <extension>.webm</extension>
      <extension>.mp3</extension>
      <extension>.wav</extension>
    </group>

    <!-- Fonts -->
    <group name=""Fonts"" tree_only=""true"">
      <extension>.woff</extension>
      <extension>.woff2</extension>
      <extension>.ttf</extension>
      <extension>.otf</extension>
      <extension>.eot</extension>
    </group>

    <!-- Office documents -->
    <group name=""Docs and Shaders"" tree_only=""true"">
	  <extension>.pdf</extension>
      <extension>.doc</extension>
      <extension>.docx</extension>
	  <extension>.dotm</extension>
      <extension>.xls</extension>
      <extension>.xlsx</extension>
      <extension>.xlsm</extension>
	  <extension>.xltx</extension>
	  <extension>.xltm</extension>
	  <extension>.xlam</extension>
      <extension>.ppt</extension>
	  <extension>.pptx</extension>
	  <extension>.ppam</extension>
    </group>

    <!-- Compiled and archives -->
    <group name=""Binary and Archives"" tree_only=""true"">
      <extension>.dll</extension>
      <extension>.exe</extension>
      <extension>.jar</extension>
      <extension>.aar</extension>
      <extension>.apk</extension>
      <extension>.zip</extension>
      <extension>.tar</extension>
      <extension>.gz</extension>
      <extension>.7z</extension>
      <extension>.rar</extension>
    </group>

    <!-- Secrets and certificates -->
    <group name=""Security"" tree_only=""true"">
      <extension>.env</extension>
      <extension>.crt</extension>
      <extension>.pem</extension>
      <extension>.key</extension>
      <extension>.p12</extension>
    </group>

    <!-- Design and system files -->
    <group name=""Design and System"" tree_only=""true"">
      <extension>.psd</extension>
      <extension>.ai</extension>
      <extension>.sketch</extension>
      <extension>.fig</extension>
      <extension>.DS_Store</extension>
      <extension enable=""false"">Thumbs.db</extension>
      <extension>.log</extension>
    </group>
  </extensions>
</configuration>",
					DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
					SecurityElement.Escape(suggestedRootPath));

				File.WriteAllText(configPath, xml);

				LogSuccess("Config template created with instructions: {0}", configPath);
			}
			catch (Exception ex) {
				LogError("Error creating config template: {0}", ex.Message);
			}
		}
		
		static T Deserialize<T>(string filePath) {
			try {
				XmlSerializer serializer = new XmlSerializer(typeof(T));
				using (FileStream fileStream = new FileStream(filePath, FileMode.Open)) {
					return (T)serializer.Deserialize(fileStream);
				}
			}
			catch (Exception ex) {
				LogError("Error deserializing config file: {0}", ex.Message);
				throw;
			}
		}

		static void EnsureConfigRuntimeDefaults(Config config) {
			if (config == null) {
				return;
			}

			if (string.IsNullOrWhiteSpace(config.RootPath)) {
				config.RootPath = "./";
			}
			EnsurePathConfigItem(config, true);
			EnsurePathConfigItem(config, false);
		}

		static void EnsurePathConfigItem(Config config, bool isResultPath) {
			if (config == null) {
				return;
			}

			PathConfigItem item = isResultPath ? config.ResultPath : config.LogFilePath;
			string defaultValue = isResultPath ? "./backup" : "./backup.log";
			string legacyBase = isResultPath ? config.LegacyResultPathBase : config.LegacyLogFilePathBase;

			if (item == null) {
				item = new PathConfigItem();
				if (isResultPath) {
					config.ResultPath = item;
				}
				else {
					config.LogFilePath = item;
				}
			}

			if (string.IsNullOrWhiteSpace(item.Value)) {
				item.Value = defaultValue;
			}
			if (string.IsNullOrWhiteSpace(item.Base)) {
				item.Base = string.IsNullOrWhiteSpace(legacyBase) ? "root" : legacyBase.Trim();
			}
		}

		static PathContext CreatePathContext(string configPath, Config config) {
			string normalizedConfigPath = Path.GetFullPath(configPath);
			string configFolder = GetNormalizedDirectory(Path.GetDirectoryName(normalizedConfigPath));
			string exeFolder = GetNormalizedDirectory(AppDomain.CurrentDomain.BaseDirectory);
			string currentFolder = GetNormalizedDirectory(Environment.CurrentDirectory);
			string rootFolder = string.IsNullOrWhiteSpace(config != null ? config.RootPath : null)
				? configFolder
				: ResolvePath(config.RootPath, "config", new PathContext {
					ConfigFilePath = normalizedConfigPath,
					ConfigFolder = configFolder,
					ExeFolder = exeFolder,
					CurrentFolder = currentFolder,
					RootFolder = configFolder
				}, "config");

			return new PathContext {
				ConfigFilePath = normalizedConfigPath,
				ConfigFolder = configFolder,
				ExeFolder = exeFolder,
				CurrentFolder = currentFolder,
				RootFolder = GetNormalizedDirectory(rootFolder)
			};
		}

		static string ResolvePath(string path, string baseMode, PathContext pathContext, string defaultBaseMode) {
			if (string.IsNullOrWhiteSpace(path)) {
				return GetBaseFolder(baseMode, pathContext, defaultBaseMode);
			}

			string trimmedPath = path.Trim();
			if (Path.IsPathRooted(trimmedPath)) {
				return Path.GetFullPath(trimmedPath);
			}

			string baseFolder = GetBaseFolder(baseMode, pathContext, defaultBaseMode);
			return Path.GetFullPath(Path.Combine(baseFolder, trimmedPath));
		}

		static string GetBaseFolder(string baseMode, PathContext pathContext, string defaultBaseMode) {
			string normalized = string.IsNullOrWhiteSpace(baseMode)
				? defaultBaseMode
				: baseMode.Trim().ToLowerInvariant();

			switch (normalized) {
				case "root":
					return GetNormalizedDirectory(pathContext.RootFolder);
				case "config":
					return GetNormalizedDirectory(pathContext.ConfigFolder);
				case "exe":
					return GetNormalizedDirectory(pathContext.ExeFolder);
				case "current":
					return GetNormalizedDirectory(pathContext.CurrentFolder);
				default:
					throw new InvalidOperationException(string.Format(
						CultureInfo.InvariantCulture,
						"Unsupported base mode '{0}'. Supported values: root, config, exe, current.",
						baseMode
					));
			}
		}

		static string GetNormalizedDirectory(string path) {
			return Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "." : path);
		}

		static string GetSuggestedTemplateRootPath(string configFolder, string currentFolder) {
			if (string.IsNullOrWhiteSpace(configFolder) || string.IsNullOrWhiteSpace(currentFolder)) {
				return "./";
			}

			string relativePath = GetRelativePath(configFolder, currentFolder);
			if (string.IsNullOrWhiteSpace(relativePath) || string.Equals(relativePath, ".", StringComparison.OrdinalIgnoreCase)) {
				return "./";
			}

			string normalized = relativePath.Replace('\\', '/');
			if (!normalized.StartsWith(".", StringComparison.Ordinal)) {
				normalized = "./" + normalized;
			}

			return normalized;
		}

		static string GetPathValue(PathConfigItem item) {
			return item == null ? null : item.Value;
		}

		static string GetPathValueOrDefault(PathConfigItem item, string defaultValue) {
			return string.IsNullOrWhiteSpace(GetPathValue(item)) ? defaultValue : item.Value;
		}

		static string GetPathBase(PathConfigItem item, string legacyBase) {
			if (item != null && !string.IsNullOrWhiteSpace(item.Base)) {
				return item.Base;
			}

			return string.IsNullOrWhiteSpace(legacyBase) ? "root" : legacyBase.Trim();
		}

		static bool HasWildcard(string value) {
			return !string.IsNullOrWhiteSpace(value) && (value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0);
		}

		static IEnumerable<string> ResolveIncludeDirectories(string includePath, string baseMode, PathContext pathContext) {
			var results = new List<string>();
			if (string.IsNullOrWhiteSpace(includePath)) {
				return results;
			}

			string trimmedPath = includePath.Trim();
			if (!HasWildcard(trimmedPath)) {
				string fullPath = ResolvePath(trimmedPath, baseMode, pathContext, "root");
				if (!Directory.Exists(fullPath)) {
					LogWarning("Include path does not exist: {0}", fullPath);
					return results;
				}

				results.Add(fullPath);
				return results;
			}

			if (Path.IsPathRooted(trimmedPath)) {
				LogWarning("Wildcard include path is not supported for absolute paths: {0}", trimmedPath);
				return results;
			}

			string baseFolder = GetBaseFolder(baseMode, pathContext, "root");
			if (!Directory.Exists(baseFolder)) {
				LogWarning("Include base folder does not exist: {0}", baseFolder);
				return results;
			}

			string normalizedPattern = trimmedPath.Replace('\\', '/');
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var directory in Directory.GetDirectories(baseFolder, "*", SearchOption.AllDirectories)) {
				string relativePath;
				if (!TryGetRelativePathInsideBase(baseFolder, directory, out relativePath)) {
					continue;
				}

				if (WildcardMatch(relativePath, normalizedPattern)) {
					string fullDirectory = Path.GetFullPath(directory);
					if (seen.Add(fullDirectory)) {
						results.Add(fullDirectory);
					}
				}
			}

			return results;
		}

		static bool TryGetRelativePathInsideBase(string baseFolder, string path, out string relativePath) {
			relativePath = null;
			string candidate = GetRelativePath(baseFolder, path);
			if (string.IsNullOrWhiteSpace(candidate)) {
				return false;
			}

			if (candidate.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(candidate)) {
				return false;
			}

			relativePath = candidate.Replace('\\', '/');
			return true;
		}
		
		static List<FileEntry> GetFiles(Config config, PathContext pathContext, List<ConfigItem> extensionItems, BackupStats stats) {
			var files = new List<FileEntry>();
			string rootFolder = pathContext.RootFolder;
			
			var patternRules = BuildPatternRules(extensionItems);
			DateTime incrementalCutoff;
			bool useIncremental = TryGetIncrementalCutoff(config, out incrementalCutoff);
			if (config != null && config.IncrementalBackup && !useIncremental) {
				LogWarning("Incremental backup enabled, but Created timestamp is missing or invalid. Full backup will be used.");
			}
			
			// === 1. INCLUDE PATHS ===
			foreach (var includePathItem in config.IncludePaths ?? new List<ConfigItem>()) {
				string includePath = includePathItem == null ? null : includePathItem.Value;
				if (string.IsNullOrWhiteSpace(includePath)) {
					continue;
				}

				foreach (var fullPath in ResolveIncludeDirectories(includePath, includePathItem != null ? includePathItem.Base : null, pathContext)) {
					var includedFiles = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories);
					foreach (var file in includedFiles) {
						stats.ScannedFiles++;
						if (IsFileExcluded(file, pathContext, config.ExcludePaths)) {
							stats.ExcludedFiles++;
							continue;
						}
						
						if (ShouldExcludeByLimits(file, config, stats)) {
							continue;
						}

						bool ruleTreeOnly;
						if (!TryMatchPatterns(file, rootFolder, patternRules, out ruleTreeOnly)) {
							stats.SkippedByPattern++;
							continue;
						}

						if (ShouldSkipByIncremental(file, useIncremental, incrementalCutoff, stats)) {
							continue;
						}
						
						bool treeOnly = includePathItem != null && includePathItem.TreeOnly;
						treeOnly = treeOnly || ruleTreeOnly;
						
						stats.IncludedFiles++;
						if (treeOnly) {
							stats.TreeOnlyFiles++;
						}
						files.Add(new FileEntry { Path = file, TreeOnly = treeOnly });
					}
				}
			}
			
			// === 2. INCLUDE FILES (support "!" override) ===
			if (config.IncludeFiles != null && config.IncludeFiles.Count > 0) {
				foreach (var includeFileItem in config.IncludeFiles) {
					string includeValue = includeFileItem == null ? null : includeFileItem.Value;
					if (string.IsNullOrWhiteSpace(includeValue))
						continue;
					
					string includeFile = includeValue.Trim();
					
					// Check if there is a "!" at the end - this is a sign of a "forced" include
					bool forceInclude = false;
					if (includeFile.EndsWith("!")) {
						forceInclude = true;
						// remove "!" and any space before it
						includeFile = includeFile.Substring(0, includeFile.Length - 1).TrimEnd();
					}
					
					string fullPath = ResolvePath(includeFile, includeFileItem != null ? includeFileItem.Base : null, pathContext, "root");
					
					if (File.Exists(fullPath)) {
						stats.ScannedFiles++;
						// Если стоит "!", игнорируем ExcludePaths
						if (forceInclude || !IsFileExcluded(fullPath, pathContext, config.ExcludePaths)) {
							if (ShouldExcludeByLimits(fullPath, config, stats)) {
								continue;
							}
							bool treeOnly = includeFileItem != null && includeFileItem.TreeOnly;
							bool ruleTreeOnly;
							if (TryMatchPatterns(fullPath, rootFolder, patternRules, out ruleTreeOnly)) {
								treeOnly = treeOnly || ruleTreeOnly;
							}
							else {
								stats.SkippedByPattern++;
								continue;
							}

							if (ShouldSkipByIncremental(fullPath, useIncremental, incrementalCutoff, stats)) {
								continue;
							}
							
							stats.IncludedFiles++;
							if (treeOnly) {
								stats.TreeOnlyFiles++;
							}
							files.Add(new FileEntry { Path = fullPath, TreeOnly = treeOnly });
							LogInfo(
								forceInclude
									? "Including file (override): {0}"
									: "Including file: {0}",
								includeFile
							);
						}
						else {
							stats.ExcludedFiles++;
							LogWarning("File excluded by filter: {0}", includeFile);
						}
					}
					else {
						LogWarning("Include file does not exist: {0}", fullPath);
					}
				}
			}
			
			// === 3. FINISHING ===
			if (files.Count == 0) {
				LogWarning("No files found matching the specified criteria.");
			}
			
			return files;
		}
		
		static string GetResultFilePath(Config config, PathContext pathContext) {
			string resultPath = ResolvePath(
				GetPathValueOrDefault(config.ResultPath, "./backup"),
				GetPathBase(config.ResultPath, config.LegacyResultPathBase),
				pathContext,
				"root"
			);
			string resultFilename = config.ResultFilenameMask
				.Replace("@PROJECTNAME", config.ProjectName)
				.Replace("@VER", config.Version)
				.Replace("#YYYYMMDDhhmmss#", DateTime.Now.ToString("yyyyMMddHHmmss"));
			return Path.Combine(resultPath, resultFilename);
		}

		static List<PatternRule> BuildPatternRules(List<ConfigItem> items) {
			var rules = new List<PatternRule>();
			int index = 0;
			
			foreach (var item in items ?? new List<ConfigItem>()) {
				string raw = item == null ? null : item.Value;
				if (string.IsNullOrWhiteSpace(raw) || (item != null && !item.Enable)) {
					index++;
					continue;
				}
				
				string pattern = raw.Trim();
				bool hasWildcard = pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0;
				if (!hasWildcard) {
					pattern = "*" + pattern;
				}
				
				rules.Add(new PatternRule {
					Pattern = pattern,
					TreeOnly = item != null && item.TreeOnly,
					Length = pattern.Length,
					Index = index
				});
				
				index++;
			}
			
			rules.Sort((a, b) => {
				int cmp = b.Length.CompareTo(a.Length);
				if (cmp != 0) return cmp;
				return a.Index.CompareTo(b.Index);
			});
			
			return rules;
		}

		static bool TryMatchPatterns(string filePath, string rootFolder, List<PatternRule> rules, out bool treeOnly) {
			treeOnly = false;
			if (rules == null || rules.Count == 0) {
				return false;
			}
			
			string relativePath = GetRelativePath(rootFolder, filePath).Replace('\\', '/');
			string fileName = Path.GetFileName(filePath);
			
			foreach (var rule in rules) {
				if (rule == null || string.IsNullOrEmpty(rule.Pattern)) {
					continue;
				}
				
				if (WildcardMatch(relativePath, rule.Pattern) || WildcardMatch(fileName, rule.Pattern)) {
					treeOnly = rule.TreeOnly;
					return true;
				}
			}
			
			return false;
		}

		static bool TryGetIncrementalCutoff(Config config, out DateTime cutoff) {
			cutoff = DateTime.MinValue;
			if (config == null || !config.IncrementalBackup) {
				return false;
			}
			
			if (string.IsNullOrWhiteSpace(config.Created)) {
				return false;
			}
			
			return DateTime.TryParseExact(
				config.Created.Trim(),
				"yyyy-MM-dd HH:mm:ss",
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeLocal,
				out cutoff
			);
		}
		
		static void EnsureConfigDefaults(string configPath, Config config) {
			if (config == null || string.IsNullOrWhiteSpace(configPath)) {
				return;
			}
			
			bool changed = false;
			var doc = new XmlDocument();
			doc.PreserveWhitespace = true;
			doc.Load(configPath);
			
			XmlElement root = doc.DocumentElement;
			if (root == null || !string.Equals(root.Name, "configuration", StringComparison.OrdinalIgnoreCase)) {
				return;
			}
			
			string rootPathValue = string.IsNullOrWhiteSpace(config.RootPath) ? "./" : config.RootPath;
			string resultPathValue = GetPathValueOrDefault(config.ResultPath, "./backup");
			string resultPathBaseValue = GetPathBase(config.ResultPath, config.LegacyResultPathBase);
			string logFilePathValue = GetPathValueOrDefault(config.LogFilePath, "./backup.log");
			string logFilePathBaseValue = GetPathBase(config.LogFilePath, config.LegacyLogFilePathBase);
			string resultMaskValue = string.IsNullOrWhiteSpace(config.ResultFilenameMask)
				? "@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt"
				: config.ResultFilenameMask;
			
			changed |= EnsureElement(doc, root, "RootPath", rootPathValue);
			changed |= EnsureElement(doc, root, "UpdateCheckMinutes", config.UpdateCheckMinutes.ToString(CultureInfo.InvariantCulture));
			changed |= EnsureElement(doc, root, "UpdateCheckTimeoutSeconds", config.UpdateCheckTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
			changed |= EnsureElement(doc, root, "UpdateCheckVerbose", config.UpdateCheckVerbose.ToString().ToLowerInvariant());
			changed |= EnsureElement(doc, root, "DryRun", config.DryRun.ToString().ToLowerInvariant());
			changed |= EnsureElement(doc, root, "LogLevel", config.LogLevel ?? "normal");
			changed |= EnsureElement(doc, root, "LogToFile", config.LogToFile.ToString().ToLowerInvariant());
			changed |= EnsurePathElement(doc, root, "LogFilePath", logFilePathValue, logFilePathBaseValue);
			changed |= EnsureElement(doc, root, "IncrementalBackup", config.IncrementalBackup.ToString().ToLowerInvariant());
			changed |= EnsureElement(doc, root, "MaxFileSizeMB", config.MaxFileSizeMB.ToString(CultureInfo.InvariantCulture));
			changed |= EnsureElement(doc, root, "MaxFileAgeDays", config.MaxFileAgeDays.ToString(CultureInfo.InvariantCulture));
			changed |= EnsureElement(doc, root, "CleanupKeepLast", config.CleanupKeepLast.ToString(CultureInfo.InvariantCulture));
			changed |= EnsureElement(doc, root, "CleanupMaxAgeDays", config.CleanupMaxAgeDays.ToString(CultureInfo.InvariantCulture));
			changed |= EnsurePathElement(doc, root, "ResultPath", resultPathValue, resultPathBaseValue);
			changed |= EnsureElement(doc, root, "ResultFilenameMask", resultMaskValue);
			changed |= EnsureElement(doc, root, "EnableZip", config.EnableZip.ToString().ToLowerInvariant());
			changed |= EnsureElement(doc, root, "DeleteUnziped", config.DeleteUnziped.ToString().ToLowerInvariant());
			
			if (changed) {
				config.RootPath = rootPathValue;
				EnsurePathConfigItem(config, true);
				EnsurePathConfigItem(config, false);
				config.ResultPath.Value = resultPathValue;
				config.ResultPath.Base = resultPathBaseValue;
				config.LogFilePath.Value = logFilePathValue;
				config.LogFilePath.Base = logFilePathBaseValue;
				config.ResultFilenameMask = resultMaskValue;
				doc.Save(configPath);
				LogInfo("Config updated with missing defaults: {0}", configPath);
			}
		}
		
		static bool EnsureElement(XmlDocument doc, XmlElement root, string name, string value) {
			if (root[name] != null) {
				return false;
			}
			
			var element = doc.CreateElement(name);
			element.InnerText = value ?? string.Empty;
			InsertWithIndent(doc, root, element);
			return true;
		}

		static bool EnsurePathElement(XmlDocument doc, XmlElement root, string name, string value, string baseMode) {
			if (root[name] != null) {
				return false;
			}

			var element = doc.CreateElement(name);
			element.InnerText = value ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(baseMode)) {
				element.SetAttribute("base", baseMode);
			}
			InsertWithIndent(doc, root, element);
			return true;
		}
		
		static void InsertWithIndent(XmlDocument doc, XmlElement root, XmlElement element) {
			XmlNode insertBefore = root.LastChild;
			bool hasTrailingWhitespace = insertBefore != null
				&& (insertBefore.NodeType == XmlNodeType.Whitespace
					|| (insertBefore.NodeType == XmlNodeType.Text && string.IsNullOrWhiteSpace(insertBefore.Value)));
			
			string indent = Environment.NewLine + "  ";
			string newline = Environment.NewLine;
			
			if (hasTrailingWhitespace) {
				root.InsertBefore(doc.CreateWhitespace(indent), insertBefore);
				root.InsertBefore(element, insertBefore);
			}
			else {
				root.AppendChild(doc.CreateWhitespace(indent));
				root.AppendChild(element);
				root.AppendChild(doc.CreateWhitespace(newline));
			}
		}

		static bool ShouldSkipByIncremental(string filePath, bool useIncremental, DateTime cutoff, BackupStats stats) {
			if (!useIncremental) {
				return false;
			}
			
			try {
				DateTime lastWrite = File.GetLastWriteTime(filePath);
				if (lastWrite <= cutoff) {
					stats.SkippedByUnchanged++;
					return true;
				}
			}
			catch {
				// ignore incremental checks on errors
			}
			
			return false;
		}

		static bool ShouldExcludeByLimits(string filePath, Config config, BackupStats stats) {
			if (config == null) {
				return false;
			}
			
			try {
				if (config.MaxFileSizeMB > 0) {
					long maxBytes = (long)config.MaxFileSizeMB * 1024L * 1024L;
					long size = new FileInfo(filePath).Length;
					if (size > maxBytes) {
						stats.SkippedBySize++;
						LogDebug("Skipping by size limit: {0}", filePath);
						return true;
					}
				}
				
				if (config.MaxFileAgeDays > 0) {
					DateTime lastWrite = File.GetLastWriteTime(filePath);
					if (DateTime.Now - lastWrite > TimeSpan.FromDays(config.MaxFileAgeDays)) {
						stats.SkippedByAge++;
						LogDebug("Skipping by age limit: {0}", filePath);
						return true;
					}
				}
			}
			catch {
				// ignore limit checks on errors
			}
			
			return false;
		}

		static void ReportProgress(string label, int current, int total, ref int lastBucket) {
			if (total <= 0) {
				return;
			}
			
			int percent = (int)Math.Floor((current * 100.0) / total);
			int bucket = percent / 10;
			if (bucket != lastBucket || percent >= 100) {
				lastBucket = bucket;
				bool isFinal = percent >= 100;
				WriteProgress(
					string.Format(CultureInfo.InvariantCulture, "{0}: {1}/{2} ({3}%)", label, current, total, percent),
					isFinal
				);
			}
		}
		
		static void RunDryMode(List<FileEntry> files, BackupStats stats, string rootFolder) {
			int total = files.Count(item => item != null && !item.TreeOnly);
			int processed = 0;
			int lastBucket = -1;
			
			foreach (var file in files) {
				if (file.TreeOnly) {
					continue;
				}
				
				string relativePath = GetRelativePath(rootFolder, file.Path);
				if (!IsLikelyTextFile(file.Path)) {
					stats.SkippedBinary++;
					LogWarning("Dry-run skip binary: {0}", relativePath);
					processed++;
					ReportProgress("Dry-run progress", processed, total, ref lastBucket);
					continue;
				}
				
				long size = 0;
				try {
					size = new FileInfo(file.Path).Length;
				}
				catch {
					// keep size as 0
				}
				
				stats.PackedFiles++;
				stats.PackedBytes += size;
				LogInfo("Dry-run include: {0} ({1})", relativePath, FormatBytes(size));
				processed++;
				ReportProgress("Dry-run progress", processed, total, ref lastBucket);
			}
		}
		
		static bool WriteResultFile(Config config, List<FileEntry> files, string resultFilePath, string rootFolder, BackupStats stats) {
			try {
				int total = files.Count(item => item != null && !item.TreeOnly);
				int processed = 0;
				int lastBucket = -1;
				
				using (StreamWriter writer = new StreamWriter(resultFilePath, false, new UTF8Encoding(true))) {
					// Write folder structure from filtered files first
					string folderStructure = GenerateFolderStructureFromFilteredFiles(files, rootFolder);
					writer.WriteLine(folderStructure);
					
					foreach (var file in files) {
					if (file.TreeOnly) {
						continue;
					}
					
					string relativePath = GetRelativePath(rootFolder, file.Path);
					if (!IsLikelyTextFile(file.Path)) {
						stats.SkippedBinary++;
						LogWarning("Skipping binary file: {0}", relativePath);
						processed++;
						ReportProgress("Packing progress", processed, total, ref lastBucket);
						continue;
					}
					
					long size = 0;
					try {
						size = new FileInfo(file.Path).Length;
					}
					catch {
						// keep size as 0
					}
					
					string fileContent = File.ReadAllText(file.Path);
						
					LogInfo("Packing file: {0}", relativePath);
					stats.PackedFiles++;
					stats.PackedBytes += size;
					processed++;
					ReportProgress("Packing progress", processed, total, ref lastBucket);
						
						writer.WriteLine("-----------------------------------------");
						writer.WriteLine("#########################################");
						writer.WriteLine(string.Format("##>{0}", relativePath));
						writer.WriteLine("#########################################");
						writer.WriteLine(fileContent);
						writer.WriteLine("#########################################");
						writer.WriteLine("## END OF FILE");
						writer.WriteLine("#########################################");
						writer.WriteLine("-----------------------------------------");
					}
				}
				
				// Check if we need to zip the result file
				if (config.EnableZip) {
					string zipFilePath = resultFilePath+".zip";
					ZipUsingPowerShell(resultFilePath, zipFilePath);
					LogInfo("Backup file zipped successfully: {0}", zipFilePath);
					
					// Verify if the ZIP file was successfully created
					if (File.Exists(zipFilePath)) {
						if (config.DeleteUnziped) {
							File.Delete(resultFilePath);
						}
						LogSuccess("ZIP file created successfully at: {0}", zipFilePath);
						return true;
					}
					else {
						LogError("Failed to create ZIP file at: {0}", zipFilePath);
						return false;
					}
				}
				
				return true;
			}
			catch (Exception ex) {
				LogError("Error writing to result file: {0}", ex.Message);
				return false;
			}
		}

		static void CleanupOldBackups(Config config, string resultDir, string currentResultFilePath) {
			if (config == null) {
				return;
			}
			
			if (config.CleanupKeepLast <= 0 && config.CleanupMaxAgeDays <= 0) {
				return;
			}
			
			if (!Directory.Exists(resultDir)) {
				return;
			}
			
			string mask = config.ResultFilenameMask ?? string.Empty;
			string token = "#YYYYMMDDhhmmss#";
			int index = mask.IndexOf(token, StringComparison.OrdinalIgnoreCase);
			string prefix = null;
			string prefixNoVersion = null;
			string suffix = null;
			if (index >= 0) {
				string rawPrefix = mask.Substring(0, index);
				suffix = mask.Substring(index + token.Length);
				prefix = rawPrefix.Replace("@PROJECTNAME", config.ProjectName ?? string.Empty);
				prefix = prefix.Replace("@VER", config.Version ?? string.Empty);
				prefix = prefix.Trim();
				
				prefixNoVersion = rawPrefix.Replace("@PROJECTNAME", config.ProjectName ?? string.Empty);
				prefixNoVersion = prefixNoVersion.Replace("@VER", string.Empty);
				while (prefixNoVersion.Contains("__")) {
					prefixNoVersion = prefixNoVersion.Replace("__", "_");
				}
				prefixNoVersion = prefixNoVersion.Trim('_', '-', '.', ' ');
				if (prefixNoVersion.Length == 0 || prefixNoVersion == prefix) {
					prefixNoVersion = null;
				}
				suffix = suffix.Trim();
				if (prefix.Length == 0) {
					prefix = null;
				}
			}
			
			var suffixes = new List<string>();
			if (!string.IsNullOrEmpty(suffix)) {
				suffixes.Add(suffix);
			}
			if (config.EnableZip && !suffixes.Contains(".zip")) {
				suffixes.Add(".zip");
			}
			if (suffixes.Count == 0) {
				string ext = Path.GetExtension(currentResultFilePath);
				if (!string.IsNullOrEmpty(ext)) {
					suffixes.Add(ext);
				}
				if (config.EnableZip && !suffixes.Contains(".zip")) {
					suffixes.Add(".zip");
				}
			}
			
			if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(prefixNoVersion) && suffixes.Count == 0) {
				LogWarning("Cleanup skipped: unable to determine backup filename pattern.");
				return;
			}
			
			var currentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			currentPaths.Add(currentResultFilePath);
			if (config.EnableZip) {
				currentPaths.Add(currentResultFilePath + ".zip");
			}
			
			var candidates = new List<FileInfo>();
			foreach (var file in Directory.GetFiles(resultDir)) {
				if (currentPaths.Contains(file)) {
					continue;
				}
				
				string name = Path.GetFileName(file);
				bool prefixMatch = string.IsNullOrEmpty(prefix) || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
				if (!prefixMatch && !string.IsNullOrEmpty(prefixNoVersion)) {
					prefixMatch = name.StartsWith(prefixNoVersion, StringComparison.OrdinalIgnoreCase);
				}
				if (!prefixMatch) {
					continue;
				}
				
				bool suffixMatch = suffixes.Count == 0;
				foreach (var sfx in suffixes) {
					if (!string.IsNullOrEmpty(sfx) && name.EndsWith(sfx, StringComparison.OrdinalIgnoreCase)) {
						suffixMatch = true;
						break;
					}
				}
				if (!suffixMatch) {
					continue;
				}
				
				candidates.Add(new FileInfo(file));
			}
			
			int removed = 0;
			if (config.CleanupMaxAgeDays > 0) {
				var remaining = new List<FileInfo>();
				foreach (var file in candidates) {
					if (DateTime.Now - file.LastWriteTime > TimeSpan.FromDays(config.CleanupMaxAgeDays)) {
						if (TryDeleteFile(file.FullName)) {
							removed++;
						}
					}
					else {
						remaining.Add(file);
					}
				}
				candidates = remaining;
			}
			
			if (config.CleanupKeepLast > 0) {
				var ordered = candidates.OrderByDescending(f => f.LastWriteTime).ToList();
				for (int i = config.CleanupKeepLast; i < ordered.Count; i++) {
					if (TryDeleteFile(ordered[i].FullName)) {
						removed++;
					}
				}
			}
			
			if (removed > 0) {
				LogInfo("Cleanup removed {0} file(s).", removed);
			}
		}

		static bool TryDeleteFile(string path) {
			try {
				File.Delete(path);
				LogDebug("Cleanup deleted: {0}", path);
				return true;
			}
			catch (Exception ex) {
				LogWarning("Cleanup failed for {0}: {1}", path, ex.Message);
				return false;
			}
		}
		
		static string GetRelativePath(string relativeTo, string path) {
			try {
				string fromFullPath = Path.GetFullPath(relativeTo);
				string toFullPath = Path.GetFullPath(path);
				string fromRoot = Path.GetPathRoot(fromFullPath);
				string toRoot = Path.GetPathRoot(toFullPath);
				if (!string.Equals(fromRoot, toRoot, StringComparison.OrdinalIgnoreCase)) {
					return path;
				}

				bool targetIsDirectory = path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
					|| path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
					|| Directory.Exists(path);

				string[] fromParts = fromFullPath
					.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
					.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
				string[] toParts = toFullPath
					.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
					.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

				int commonLength = 0;
				int maxCommon = Math.Min(fromParts.Length, toParts.Length);
				while (commonLength < maxCommon &&
					string.Equals(fromParts[commonLength], toParts[commonLength], StringComparison.OrdinalIgnoreCase)) {
					commonLength++;
				}

				var parts = new List<string>();
				for (int i = commonLength; i < fromParts.Length; i++) {
					parts.Add("..");
				}
				for (int i = commonLength; i < toParts.Length; i++) {
					parts.Add(toParts[i]);
				}

				if (parts.Count == 0) {
					return targetIsDirectory ? "." : Path.GetFileName(toFullPath);
				}

				return string.Join(Path.DirectorySeparatorChar.ToString(), parts.ToArray());
			}
			catch (Exception ex) {
				LogError("Error getting relative path: {0}", ex.Message);
				throw;
			}
		}

		static bool ShouldCheckForUpdates(Config config, out string reason) {
			reason = null;
			if (config == null) {
				reason = "Update check skipped: config not loaded.";
				return false;
			}

			if (config.UpdateCheckMinutes <= 0) {
				reason = "Update check disabled: UpdateCheckMinutes is 0.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(config.Created)) {
				reason = "Update check: Created timestamp is missing, checking now.";
				return true;
			}

			DateTime created;
			bool parsed = DateTime.TryParseExact(
				config.Created.Trim(),
				"yyyy-MM-dd HH:mm:ss",
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeLocal,
				out created
			);
			if (!parsed) {
				reason = "Update check: Created timestamp invalid, checking now.";
				return true;
			}

			TimeSpan elapsed = DateTime.Now - created;
			TimeSpan interval = TimeSpan.FromMinutes(config.UpdateCheckMinutes);
			if (elapsed >= interval) {
				reason = "Update check: interval reached, checking now.";
				return true;
			}
			
			TimeSpan remaining = interval - elapsed;
			int remainingMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
			reason = string.Format(
				CultureInfo.InvariantCulture,
				"Update check skipped: next check in {0} minutes.",
				remainingMinutes
			);
			return false;
		}

		static void CheckForUpdates(int timeoutSeconds, bool verbose) {
			const string versionUrl = "https://raw.githubusercontent.com/hegelmax/BackupFiles/main/app.version.cs";
			const string downloadUrl = "https://github.com/hegelmax/BackupFiles/tree/main/bin/Release";
			
			try {
				int timeoutMs = Math.Max(1000, timeoutSeconds * 1000);
				if (verbose) {
					LogDebug("Update check: downloading version (timeout {0}s).", timeoutMs / 1000);
				}
				else {
					LogInfo("Checking for updates...");
				}
				string content = DownloadStringWithTimeout(versionUrl, timeoutMs);
				if (string.IsNullOrWhiteSpace(content)) {
					LogWarning("Update check failed: empty response.");
					return;
				}
				
				string remoteVersionText = ExtractAssemblyFileVersion(content);
				if (string.IsNullOrWhiteSpace(remoteVersionText)) {
					LogWarning("Update check failed: no AssemblyFileVersion found.");
					return;
				}
				
				Version localVersion = Assembly.GetExecutingAssembly().GetName().Version;
				Version remoteVersion;
				if (!Version.TryParse(remoteVersionText, out remoteVersion)) {
					LogWarning("Update check failed: invalid remote version '{0}'.", remoteVersionText);
					return;
				}
				
				if (localVersion == null) {
					LogWarning("Update check failed: local version not available.");
					return;
				}
				
				if (localVersion != null && remoteVersion > localVersion) {
					LogSuccess("Update available: {0} (current {1})", remoteVersion, localVersion);
					LogInfo("Download: {0}", downloadUrl);
				}
				else {
					if (verbose) {
						LogDebug("Update check: no updates. Current {0}, latest {1}.", localVersion, remoteVersion);
					}
					else {
						LogInfo("No updates found. Current {0}, latest {1}.", localVersion, remoteVersion);
					}
				}
			}
			catch (Exception ex) {
				LogError("Update check failed: {0}", ex.Message);
			}
		}
		
		static void EnableTls12() {
			try {
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			}
			catch {
				// ignore TLS setup errors
			}
		}

		static string DownloadStringWithTimeout(string url, int timeoutMs) {
			var request = (HttpWebRequest)WebRequest.Create(url);
			request.Method = "GET";
			request.UserAgent = "BackupFiles";
			request.Timeout = timeoutMs;
			request.ReadWriteTimeout = timeoutMs;

			using (var response = (HttpWebResponse)request.GetResponse()) {
				using (var stream = response.GetResponseStream()) {
					if (stream == null) {
						return null;
					}
					using (var reader = new StreamReader(stream)) {
						return reader.ReadToEnd();
					}
				}
			}
		}

		static string ExtractAssemblyFileVersion(string content) {
			if (string.IsNullOrEmpty(content)) {
				return null;
			}
			
			Match match = Regex.Match(
				content,
				@"AssemblyFileVersionAttribute\(\s*""([^""]+)""\s*\)",
				RegexOptions.IgnoreCase
			);
			
			return match.Success ? match.Groups[1].Value : null;
		}
		
		static bool IsLikelyTextFile(string filePath) {
			const int sampleSize = 4096;
			byte[] buffer;
			
			try {
				using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
					int length = (int)Math.Min(sampleSize, stream.Length);
					buffer = new byte[length];
					int read = stream.Read(buffer, 0, length);
					if (read <= 0) {
						return true;
					}
				}
			}
			catch {
				return false;
			}
			
			int controlCount = 0;
			for (int i = 0; i < buffer.Length; i++) {
				byte b = buffer[i];
				if (b == 0) {
					return false;
				}
				
				bool isControl = (b < 0x09) || (b > 0x0D && b < 0x20);
				if (isControl) {
					controlCount++;
					if (controlCount > 4) {
						return false;
					}
				}
			}
			
			return true;
		}
		
		// >>>>> MAKE TREE section >>>>>
		static TreeNode BuildTree(List<FileEntry> filteredFiles, string rootFolder) {
			var rootNode = new TreeNode { Name = ".", IsFile = false };
			
			foreach (var entry in filteredFiles) {
				if (entry == null || string.IsNullOrWhiteSpace(entry.Path)) {
					continue;
				}
				
				var relativePath = GetRelativePath(rootFolder, entry.Path);
				var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
				var current = rootNode;
				for (int i = 0; i < parts.Length; i++) {
					string part = parts[i];
					bool isFile = (i == parts.Length - 1);
					if (!current.Children.ContainsKey(part)) {
						var node = new TreeNode { Name = part, IsFile = isFile };
						current.Children[part] = node;
					}
					current = current.Children[part];
					if (isFile) {
						current.IsFile = true;
						current.IsSkipped = current.IsSkipped || entry.TreeOnly;
					}
				}
			}
			return rootNode;
		}
		
		static void TraverseTree(TreeNode node, string indent, List<string> lines, bool isLast, bool isRoot) {
			if (node.IsFile) {
				string suffix = node.IsSkipped ? " (skipped)" : string.Empty;
				lines.Add(indent + (isLast ? "└── " : "├── ") + node.Name + suffix);
				return;
			}
			
			if (node.IsCollapsible && !isRoot) {
				// Collapse the path
				var pathParts = new List<string>();
				var currentNode = node;
				while (currentNode.IsCollapsible && !currentNode.IsFile) {
					pathParts.Add(currentNode.Name);
					currentNode = currentNode.Children.Values.First();
				}
				if (currentNode.IsFile) {
					pathParts.Add(currentNode.Name);
				}
				var collapsedPath = string.Join("/", pathParts);
				if (currentNode.IsFile && currentNode.IsSkipped) {
				    collapsedPath += " (skipped)";
				}
				lines.Add(indent + (isLast ? "└── " : "├── ") + collapsedPath);
			}
			else {
				// Add current directory
				if (isRoot) {
					lines.Add("./");
				}
				else {
					lines.Add(indent + (isLast ? "└── " : "├── ") + node.Name + "/");
				}
				
				var children = node.Children.Values.ToList();
				
				// List directories first
				var dirChildren = children.Where(c => !c.IsFile).ToList();
				var fileChildren = children.Where(c => c.IsFile).ToList();
				
				int totalChildren = dirChildren.Count + fileChildren.Count;
				
				int index = 0;
				foreach (var child in dirChildren) {
					index++;
					bool childIsLast = (index == totalChildren);
					var subIndent = indent + (isLast ? "    " : "│   ");
					TraverseTree(child, subIndent, lines, childIsLast, false); // Changed here
				}
				foreach (var child in fileChildren) {
					index++;
					bool childIsLast = (index == totalChildren);
					var subIndent = indent + (isLast ? "    " : "│   ");
					string suffix = child.IsSkipped ? " (skipped)" : string.Empty;
					lines.Add(subIndent + (childIsLast ? "└── " : "├── ") + child.Name + suffix);
				}
			}
		}
		
		static string GenerateFolderStructureFromFilteredFiles(List<FileEntry> filteredFiles, string rootFolder) {
			// Build the tree structure
			TreeNode rootNode = BuildTree(filteredFiles, rootFolder);
			
			// Mark collapsible nodes
			MarkCollapsibleNodes(rootNode, true); // Changed here
			
			// Traverse the tree and generate the folder structure lines
			List<string> lines = new List<string>();
			TraverseTree(rootNode, "", lines, true, true); // Changed here
			
			return string.Join(Environment.NewLine, lines);
		}
		
		static void MarkCollapsibleNodes(TreeNode node, bool isRoot) {
			if (node.IsFile) {
				node.IsCollapsible = true;
				return;
			}
			
			if (!isRoot && node.Children.Count == 1) {
				var child = node.Children.Values.First();
				MarkCollapsibleNodes(child, false);
				node.IsCollapsible = child.IsCollapsible;
			}
			else {
				node.IsCollapsible = false;
				foreach (var child in node.Children.Values) {
					MarkCollapsibleNodes(child, false);
				}
			}
		}
		// <<<<< MAKE TREE section <<<<<
		
		static void CreateFileStructureFromText(string textFilePath, string rootFolder) {
			try {
				var lines = File.ReadAllLines(textFilePath);
				bool isFileContent = false;
				string currentFilePath = string.Empty;
				List<string> fileContent = new List<string>();
				
				foreach (var line in lines) {
					string trimmedLine = line.Trim();
					
					// Skip lines that are just "#########################################"
					if (trimmedLine == "#########################################") {
						continue;
					}
					
					// Detect the start of a file block
					if (trimmedLine.StartsWith("##>")) {
						// Save the current file content if any
						if (!string.IsNullOrEmpty(currentFilePath) && fileContent.Count > 0) {
							SaveFileContent(rootFolder, currentFilePath, fileContent);
							fileContent.Clear();  // Clear for the next file
						}
						
						// Get the new file path
						currentFilePath = trimmedLine.Replace("##>", "").Trim();
						isFileContent = true;  // Start collecting content
					}
					else if (trimmedLine.StartsWith("## END OF FILE")) {
						// When end of file is reached, save the content to the file
						if (!string.IsNullOrEmpty(currentFilePath) && fileContent.Count > 0) {
							SaveFileContent(rootFolder, currentFilePath, fileContent);
							fileContent.Clear();
							currentFilePath = string.Empty;
							isFileContent = false;
						}
					}
					else if (isFileContent) {
						// Collect the lines that are part of the file content
						fileContent.Add(line);
					}
				}
			}
			catch (Exception ex) {
				LogError("Error processing text file: {0}", ex.Message);
			}
		}
		
		// Method to save content to a file
		static void SaveFileContent(string rootFolder, string relativeFilePath, List<string> fileContent) {
			try {
				string fullPath = Path.Combine(rootFolder, relativeFilePath);
				string directory = Path.GetDirectoryName(fullPath);
				
				// Create directory if it doesn't exist
				if (!Directory.Exists(directory)) {
					Directory.CreateDirectory(directory);
				}
				
				// Write the content to the file
				File.WriteAllLines(fullPath, fileContent);
				LogSuccess("Created file: {0}", fullPath);
			}
			catch (Exception ex) {
				LogError("Error saving file {0}: {1}", relativeFilePath, ex.Message);
			}
		}
		
		static void WaitForUserInput() {
			LogInfo("Press any key to exit...");
			Console.ReadKey();
		}
		
		// Function to invoke PowerShell and zip the file
		static void ZipUsingPowerShell(string sourceFile, string destinationZip) {
			// Prepare the PowerShell command for zipping the file
			string powershellCmd = "Compress-Archive -Path \""+sourceFile+"\" -DestinationPath \""+destinationZip+"\"";
			
			// Set up the process to execute PowerShell
			Process process = new Process();
			process.StartInfo.FileName = "powershell.exe";
			process.StartInfo.Arguments = powershellCmd;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			
			// Start the PowerShell process
			process.Start();
			process.WaitForExit();
			
			// Optionally handle output or error streams
			string output = process.StandardOutput.ReadToEnd();
			string errors = process.StandardError.ReadToEnd();
			
			if (!string.IsNullOrEmpty(errors)) {
				LogError("Error during zipping: {0}", errors);
			}
			else {
				LogSuccess("Zipping process completed.");
			}
		}
		
		// Method to unzip using PowerShell command
		static void UnzipFileUsingPowerShell(string zipFilePath, string destinationFolder) {
			// PowerShell command to unzip
			string powershellCmd = "Expand-Archive -Path \""+zipFilePath+"\" -DestinationPath \""+destinationFolder+"\"";
			
			// Set up the process to execute PowerShell
			Process process = new Process();
			process.StartInfo.FileName = "powershell.exe";
			process.StartInfo.Arguments = powershellCmd;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			
			// Start the PowerShell process
			process.Start();
			process.WaitForExit();
			
			// Optionally handle output or error streams
			string output = process.StandardOutput.ReadToEnd();
			string errors = process.StandardError.ReadToEnd();
			
			if (!string.IsNullOrEmpty(errors)) {
				LogError("Error during unzipping: {0}", errors);
			}
			else {
				LogSuccess("Unzipped successfully to {0}", destinationFolder);
			}
		}
		
		static bool WildcardMatch(string text, string pattern) {
			if (string.IsNullOrEmpty(pattern)) return false;
			
			// Making relative paths "unix-style"
			text    = text.Replace('\\', '/');
			pattern = pattern.Replace('\\', '/');
			
			// Remove leading "./" and "/"
			while (pattern.StartsWith("./") || pattern.StartsWith("/"))
				pattern = pattern.Substring(1);
			
			string regexPattern = "^" + Regex.Escape(pattern)
				.Replace("\\*", ".*")
				.Replace("\\?", ".") + "$";
			
			return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
		}
		
		static bool IsFileExcluded(string filePath, PathContext pathContext, List<ConfigItem> excludePaths) {
			if (excludePaths == null || excludePaths.Count == 0) return false;
			
			string fullFilePath = Path.GetFullPath(filePath);
			
			foreach (var item in excludePaths) {
				string raw = item == null ? null : item.Value;
				if (string.IsNullOrWhiteSpace(raw)) continue;
				
				string pattern = raw.Trim();
				string baseMode = item == null ? null : item.Base;
				
				bool hasWildcard = pattern.Contains("*") || pattern.Contains("?");
				
				if (hasWildcard) {
					string baseFolder = GetBaseFolder(baseMode, pathContext, "root");
					string relativePath;
					if (TryGetRelativePathInsideBase(baseFolder, fullFilePath, out relativePath) &&
						WildcardMatch(relativePath, pattern)) {
						return true;
					}
					
					if (TryGetRelativePathInsideBase(baseFolder, fullFilePath, out relativePath)) {
						string normalizedPattern = pattern.Replace('\\', '/');
						if (!normalizedPattern.EndsWith("*", StringComparison.Ordinal)) {
							string folderPattern = normalizedPattern.TrimEnd('/') + "/*";
							if (WildcardMatch(relativePath, folderPattern)) {
								return true;
							}
						}
					}

					if (pattern.StartsWith("./", StringComparison.Ordinal) || pattern.StartsWith(".\\", StringComparison.Ordinal)) {
						string anchoredPattern = ResolveWildcardPath(pattern, baseMode, pathContext, "root");
						string normalizedFullPath = fullFilePath.Replace('\\', '/');
						if (WildcardMatch(normalizedFullPath, anchoredPattern)) {
							return true;
						}
					}
				}
				else {
					string excludeFull = ResolvePath(pattern, baseMode, pathContext, "root");
					if (fullFilePath.StartsWith(excludeFull, StringComparison.OrdinalIgnoreCase)) {
						return true;
					}
				}
			}
			
			return false;
		}

		static string ResolveWildcardPath(string path, string baseMode, PathContext pathContext, string defaultBaseMode) {
			if (string.IsNullOrWhiteSpace(path)) {
				return string.Empty;
			}

			string trimmedPath = path.Trim().Replace('\\', '/');
			if (Path.IsPathRooted(trimmedPath)) {
				return trimmedPath;
			}

			string baseFolder = GetBaseFolder(baseMode, pathContext, defaultBaseMode).Replace('\\', '/').TrimEnd('/');
			string relativePath = trimmedPath.TrimStart('.');
			relativePath = relativePath.TrimStart('/');
			return baseFolder + "/" + relativePath;
		}
	}
}

