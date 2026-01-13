using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace BackupFiles
{
	class TreeNode {
		public string Name;
		public bool IsFile;
		public Dictionary<string, TreeNode> Children = new Dictionary<string, TreeNode>();
		public bool IsCollapsible;
	}
	
	class FileEntry {
		public string Path;
		public bool TreeOnly;
	}
	
	class Program
	{
		static void Main(string[] args) {
			try {
				if (args.Length != 0) {
					string firstArg = args[0];
					
					// If we drag XML, we consider it a config and run a backup
					if (File.Exists(firstArg) &&
						string.Equals(Path.GetExtension(firstArg), ".xml", StringComparison.OrdinalIgnoreCase)) {
						Console.WriteLine("Using config file: " + firstArg);
						RunBackupWithConfig(firstArg);
					}
					else {
						// Otherwise, this is a txt/zip backup, we work in recovery mode
						RunRestoreFromBackup(firstArg);
					}
				}
				else {
					// Old behavior – work with backup.config.xml next to EXE
					string rootFolder = AppDomain.CurrentDomain.BaseDirectory;
					string configPath = Path.Combine(rootFolder, "backup.config.xml");
					Console.WriteLine("Using default config file: " + configPath);
					RunBackupWithConfig(configPath);
				}
			}
			catch (Exception ex) {
				Console.WriteLine("An unexpected error occurred: {0}", ex.Message);
			}
			
			WaitForUserInput();
		}
		
		static void RunBackupWithConfig(string configPath) {
			string rootFolder = AppDomain.CurrentDomain.BaseDirectory;
			Config config;
			
			bool isDefaultConfig = string.Equals(
				configPath,
				Path.Combine(rootFolder, "backup.config.xml"),
				StringComparison.OrdinalIgnoreCase
			);
			
			if (!File.Exists(configPath)) {
				if (isDefaultConfig) {
					// create a template
					CreateConfigTemplate(configPath);
					Console.WriteLine("Config file is missing! A template has been created. Please update the 'is_example' parameter to 0.");
				}
				else {
					Console.WriteLine("Config file '{0}' not found.", configPath);
				}
				return;
			}
			
			config = Deserialize<Config>(configPath);
			
			if (config.IsExample == 1) {
				Console.WriteLine("Config file is not set. Please update the 'is_example' parameter to 0.");
				return;
			}

			if (ShouldCheckForUpdates(config)) {
				CheckForUpdates(config.UpdateCheckTimeoutSeconds);
			}

			if (string.IsNullOrEmpty(config.ProjectName) || string.IsNullOrEmpty(config.Version)) {
				Console.WriteLine("ProjectName or Version is missing in the config file.");
				return;
			}
			
			var extensionItems = config.Extensions == null
				? new List<ConfigItem>()
				: config.Extensions.GetAllExtensions();

			int enabledExtensionCount = extensionItems
				.Count(item => item != null && item.Enable && !string.IsNullOrWhiteSpace(item.Value));

			if (enabledExtensionCount == 0 ||
				config.IncludePaths == null || config.IncludePaths.Count == 0) {
				Console.WriteLine("Extensions or IncludePaths are missing in the config file.");
				return;
			}
			
			// Ensure result path exists
			string resultDir = Path.Combine(rootFolder, config.ResultPath);
			try {
				Directory.CreateDirectory(resultDir);
			}
			catch (Exception ex) {
				Console.WriteLine("Failed to create result directory: {0}", ex.Message);
				return;
			}
			
			// Collect files
			List<FileEntry> files;
			try {
				files = GetFiles(config, rootFolder, extensionItems);
			}
			catch (Exception ex) {
				Console.WriteLine("Error while collecting files: {0}", ex.Message);
				return;
			}
			
			// Write result file
			string resultFilePath;
			try {
				resultFilePath = GetResultFilePath(config, rootFolder);
			}
			catch (Exception ex) {
				Console.WriteLine("Error while generating result file path: {0}", ex.Message);
				return;
			}
			
			try {
				if (WriteResultFile(config, files, resultFilePath, rootFolder)) {
					// After successful backup, increment the version
					IncrementVersion(configPath);
					Console.WriteLine("Backup completed successfully. Files are packed in {0}", resultFilePath);
				}
			}
			catch (Exception ex) {
				Console.WriteLine("Error while writing the result file: {0}", ex.Message);
			}
		}
		
		static void RunRestoreFromBackup(string filePath) {
			try {
				if (!File.Exists(filePath)) {
					Console.WriteLine("The specified file does not exist.");
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
					Console.WriteLine("Unzipping file: " + filePath);
					UnzipFileUsingPowerShell(filePath, unzipFilePath);
					string[] files = Directory.GetFiles(unzipFilePath);
					if (files.Length > 0) {
						filePath     = files[0];
						unzipFilePath = filePath;
					}
					else {
						Console.WriteLine("The error occurred while unziping file");
						return;
					}
				}
				
				Console.WriteLine("Processing text file: " + filePath);
				CreateFileStructureFromText(filePath, newFolder);
				
				//Delete unziped file
				if (!string.IsNullOrEmpty(unzipFilePath) && File.Exists(unzipFilePath)) {
					File.Delete(unzipFilePath);
				}
			}
			catch (Exception ex) {
				Console.WriteLine("Error restoring from backup: " + ex.Message);
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
						
						Console.WriteLine("Version updated to "+newVersion);
					}
					else {
						Console.WriteLine("Invalid version format.");
					}
				}
				else {
					Console.WriteLine("Configuration section not found.");
				}
			}
			catch (Exception ex) {
				Console.WriteLine("Error updating version: "+ex.Message);
			}
		}
		
		static void CreateConfigTemplate(string configPath) {
			try {
				string xml = string.Format(CultureInfo.InvariantCulture, @"<?xml version=""1.0"" encoding=""utf-8""?>
<!--HOW TO USE THIS FILE
1. This file defines which files and folders will be included in your backup.
2. IncludePaths - folders scanned recursively.
3. IncludeFiles - specific files added manually.
   - If a file ends with ""!"", it ignores ExcludePaths and will ALWAYS be included.
4. ExcludePaths - wildcard patterns for files/folders to exclude:
    * *.min.js
    * */node_modules/*
    * backup.*.config.xml
5. Extensions - allowed file formats.
6. ResultPath - folder where backups will be saved.
7. ResultFilenameMask - pattern used to build the backup filename.
8. Created - last backup timestamp (updated automatically).
9. UpdateCheckMinutes - update check interval in minutes (0 disables).
10. UpdateCheckTimeoutSeconds - update check timeout in seconds.
11. IsExample=1 disables work. Set it to 0 before using.
12. To use this config, drag & drop it onto the BackupFiles.exe application.
END OF INSTRUCTIONS-->
<configuration>
  <ProjectName>MyProject</ProjectName>
  <Version>1.0.0</Version>
  <Created>{0}</Created>
  <UpdateCheckMinutes>1440</UpdateCheckMinutes>
  <UpdateCheckTimeoutSeconds>5</UpdateCheckTimeoutSeconds>
  <extensions>
    <!-- Web technologies and frontend -->
    <group name=""Web and Frontend"">
      <extension>.html</extension>
      <extension>.js</extension>
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
      <extension>.pdf</extension>
      <extension>.psd</extension>
      <extension>.ai</extension>
      <extension>.sketch</extension>
      <extension>.fig</extension>
      <extension>.DS_Store</extension>
      <extension enable=""false"">Thumbs.db</extension>
      <extension>.log</extension>
    </group>
  </extensions>
  <includePaths>
    <includePath>./public</includePath>
    <includePath>./src</includePath>
    <includePath>./lib</includePath>
    <includePath>./assets</includePath>
    <includePath tree_only=""true"">*/res</includePath>
    <includePath tree_only=""true"">*/bin</includePath>
    <includePath tree_only=""true"">*/img</includePath>
  </includePaths>
  <includeFiles>
    <includeFile>./backup.config.xml</includeFile>
    <includeFile>./backup.web.config.xml !</includeFile>
  </includeFiles>
  <excludePaths>
    <excludePath>./backup</excludePath>
    <excludePath>./archive</excludePath>
    <excludePath>*/node_modules</excludePath>
    <excludePath>*/vendor</excludePath>
    <excludePath>./bin</excludePath>
    <excludePath>./backup.*.config.xml</excludePath>
    <excludePath>*.min.js</excludePath>
  </excludePaths>
  <ResultPath>./backup</ResultPath>
  <ResultFilenameMask>@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt</ResultFilenameMask>
  <EnableZip>false</EnableZip>
  <DeleteUnziped>false</DeleteUnziped>
  <IsExample>1</IsExample>
</configuration>", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

				File.WriteAllText(configPath, xml);

				Console.WriteLine("Config template created with instructions: " + configPath);
			}
			catch (Exception ex) {
				Console.WriteLine("Error creating config template: {0}", ex.Message);
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
				Console.WriteLine("Error deserializing config file: {0}", ex.Message);
				throw;
			}
		}
		
		static List<FileEntry> GetFiles(Config config, string rootFolder, List<ConfigItem> extensionItems) {
			var files = new List<FileEntry>();
			
			var extensionTreeOnlyMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			foreach (var extItem in extensionItems ?? new List<ConfigItem>()) {
				string extValue = extItem == null ? null : extItem.Value;
				if (string.IsNullOrWhiteSpace(extValue) || (extItem != null && !extItem.Enable)) {
					continue;
				}
				
				bool existing;
				if (extensionTreeOnlyMap.TryGetValue(extValue, out existing)) {
					extensionTreeOnlyMap[extValue] = existing || extItem.TreeOnly;
				}
				else {
					extensionTreeOnlyMap[extValue] = extItem.TreeOnly;
				}
			}
			
			// === 1. INCLUDE PATHS ===
			foreach (var includePathItem in config.IncludePaths ?? new List<ConfigItem>()) {
				string includePath = includePathItem == null ? null : includePathItem.Value;
				if (string.IsNullOrWhiteSpace(includePath)) {
					continue;
				}
				
				string fullPath = Path.Combine(rootFolder, includePath);
				
				if (!Directory.Exists(fullPath)) {
					Console.WriteLine("Include path does not exist: {0}", fullPath);
					continue;
				}
				
				var includedFiles = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories)
					.Where(file => extensionTreeOnlyMap.ContainsKey(Path.GetExtension(file)))
					.Where(file => !IsFileExcluded(file, rootFolder, config.ExcludePaths))
					.ToList();
				
				foreach (var file in includedFiles) {
					string extension = Path.GetExtension(file);
					bool treeOnly = includePathItem != null && includePathItem.TreeOnly;
					bool extTreeOnly;
					if (extensionTreeOnlyMap.TryGetValue(extension, out extTreeOnly)) {
						treeOnly = treeOnly || extTreeOnly;
					}
					
					files.Add(new FileEntry { Path = file, TreeOnly = treeOnly });
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
					
					string fullPath = Path.Combine(rootFolder, includeFile);
					
					if (File.Exists(fullPath)) {
						// Если стоит "!", игнорируем ExcludePaths
						if (forceInclude || !IsFileExcluded(fullPath, rootFolder, config.ExcludePaths)) {
							string extension = Path.GetExtension(fullPath);
							bool treeOnly = includeFileItem != null && includeFileItem.TreeOnly;
							bool extTreeOnly;
							if (extensionTreeOnlyMap.TryGetValue(extension, out extTreeOnly)) {
								treeOnly = treeOnly || extTreeOnly;
							}
							
							files.Add(new FileEntry { Path = fullPath, TreeOnly = treeOnly });
							Console.WriteLine(
								forceInclude
									? "Including file (override): {0}"
									: "Including file: {0}",
								includeFile
							);
						}
						else {
							Console.WriteLine("File excluded by filter: {0}", includeFile);
						}
					}
					else {
						Console.WriteLine("Include file does not exist: {0}", fullPath);
					}
				}
			}
			
			// === 3. FINISHING ===
			if (files.Count == 0) {
				Console.WriteLine("No files found matching the specified criteria.");
			}
			
			return files;
		}
		
		static string GetResultFilePath(Config config, string rootFolder) {
			string resultPath = Path.Combine(rootFolder, config.ResultPath);
			string resultFilename = config.ResultFilenameMask
				.Replace("@PROJECTNAME", config.ProjectName)
				.Replace("@VER", config.Version)
				.Replace("#YYYYMMDDhhmmss#", DateTime.Now.ToString("yyyyMMddHHmmss"));
			return Path.Combine(resultPath, resultFilename);
		}
		
		static bool WriteResultFile(Config config, List<FileEntry> files, string resultFilePath, string rootFolder) {
			try {
				using (StreamWriter writer = new StreamWriter(resultFilePath)) {
					// Write folder structure from filtered files first
					string folderStructure = GenerateFolderStructureFromFilteredFiles(files, rootFolder);
					writer.WriteLine(folderStructure);
					
					foreach (var file in files) {
					if (file.TreeOnly) {
						continue;
					}
					
					string relativePath = GetRelativePath(rootFolder, file.Path);
					if (!IsLikelyTextFile(file.Path)) {
						Console.WriteLine("Skipping binary file: {0}", relativePath);
						continue;
					}
					
					string fileContent = File.ReadAllText(file.Path);
						
						Console.WriteLine("Packing file: {0}", relativePath);
						
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
					Console.WriteLine("Backup file zipped successfully: "+zipFilePath);
					
					// Verify if the ZIP file was successfully created
					if (File.Exists(zipFilePath)) {
						if (config.DeleteUnziped) {
							File.Delete(resultFilePath);
						}
						Console.WriteLine("ZIP file created successfully at: "+zipFilePath);
						return true;
					}
					else {
						Console.WriteLine("Failed to create ZIP file at: "+zipFilePath);
						return false;
					}
				}
				
				return true;
			}
			catch (Exception ex) {
				Console.WriteLine("Error writing to result file: {0}", ex.Message);
				return false;
			}
		}
		
		static string GetRelativePath(string relativeTo, string path) {
			try {
				Uri fromUri	= new Uri(relativeTo.EndsWith(Path.DirectorySeparatorChar.ToString()) ? relativeTo : relativeTo + Path.DirectorySeparatorChar);
				Uri toUri	= new Uri(path);
				
				if (fromUri.Scheme != toUri.Scheme) { return path; } // path can't be made relative.
				
				Uri relativeUri		= fromUri.MakeRelativeUri(toUri);
				string relativePath	= Uri.UnescapeDataString(relativeUri.ToString());
				
				if (toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase)) {
					relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				}
				
				return relativePath;
			}
			catch (Exception ex) {
				Console.WriteLine("Error getting relative path: {0}", ex.Message);
				throw;
			}
		}

		static bool ShouldCheckForUpdates(Config config) {
			if (config == null) {
				return false;
			}

			if (config.UpdateCheckMinutes <= 0) {
				return false;
			}

			if (string.IsNullOrWhiteSpace(config.Created)) {
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
				return true;
			}

			return (DateTime.Now - created) >= TimeSpan.FromMinutes(config.UpdateCheckMinutes);
		}

		static void CheckForUpdates(int timeoutSeconds) {
			const string versionUrl = "https://raw.githubusercontent.com/hegelmax/BackupFiles/main/app.version.cs";
			const string downloadUrl = "https://github.com/hegelmax/BackupFiles/tree/main/bin/Release";
			
			try {
				int timeoutMs = Math.Max(1000, timeoutSeconds * 1000);
				string content = DownloadStringWithTimeout(versionUrl, timeoutMs);
				if (string.IsNullOrWhiteSpace(content)) {
					return;
				}
				
				string remoteVersionText = ExtractAssemblyFileVersion(content);
				if (string.IsNullOrWhiteSpace(remoteVersionText)) {
					return;
				}
				
				Version localVersion = Assembly.GetExecutingAssembly().GetName().Version;
				Version remoteVersion;
				if (!Version.TryParse(remoteVersionText, out remoteVersion)) {
					return;
				}
				
				if (localVersion != null && remoteVersion > localVersion) {
					Console.WriteLine(
						"Update available: {0} (current {1})",
						remoteVersion,
						localVersion
					);
					Console.WriteLine("Download: {0}", downloadUrl);
				}
			}
			catch {
				// ignore update check errors
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
		static TreeNode BuildTree(List<string> filteredFiles, string rootFolder) {
			var rootNode = new TreeNode { Name = Path.GetFileName(rootFolder), IsFile = false };
			
			foreach (var filePath in filteredFiles) {
				var relativePath = GetRelativePath(rootFolder, filePath);
				var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
				var current = rootNode;
				foreach (var part in parts) {
					if (!current.Children.ContainsKey(part)) {
						var isFile = (part == parts.Last());
						var node = new TreeNode { Name = part, IsFile = isFile };
						current.Children[part] = node;
					}
					current = current.Children[part];
				}
			}
			return rootNode;
		}
		
		static void TraverseTree(TreeNode node, string indent, List<string> lines, bool isLast, bool isRoot) {
			if (node.IsFile) {
				lines.Add(indent + (isLast ? "└── " : "├── ") + node.Name);
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
				lines.Add(indent + (isLast ? "└── " : "├── ") + collapsedPath);
			}
			else {
				// Add current directory
				if (isRoot) {
					lines.Add(node.Name + "./");
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
					lines.Add(subIndent + (childIsLast ? "└── " : "├── ") + child.Name);
				}
			}
		}
		
		static string GenerateFolderStructureFromFilteredFiles(List<FileEntry> filteredFiles, string rootFolder) {
			// Build the tree structure
			var allPaths = filteredFiles.Select(f => f.Path).ToList();
			TreeNode rootNode = BuildTree(allPaths, rootFolder);
			
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
				Console.WriteLine("Error processing text file: "+ex.Message);
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
				Console.WriteLine("Created file: "+fullPath);
			}
			catch (Exception ex) {
				Console.WriteLine("Error saving file "+relativeFilePath+": "+ex.Message);
			}
		}
		
		static void WaitForUserInput() {
			Console.WriteLine("Press any key to exit...");
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
				Console.WriteLine("Error during zipping: "+errors);
			}
			else {
				Console.WriteLine("Zipping process completed.");
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
				Console.WriteLine("Error during unzipping: "+errors);
			}
			else {
				Console.WriteLine("Unzipped successfully to "+destinationFolder);
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
		
		static bool IsFileExcluded(string filePath, string rootFolder, List<string> excludePaths) {
			if (excludePaths == null || excludePaths.Count == 0) return false;
			
			string fullFilePath  = Path.GetFullPath(filePath);
			string relativePath  = GetRelativePath(rootFolder, fullFilePath).Replace('\\', '/');
			
			foreach (var raw in excludePaths) {
				if (string.IsNullOrWhiteSpace(raw)) continue;
				
				string pattern = raw.Trim();
				
				bool hasWildcard = pattern.Contains("*") || pattern.Contains("?");
				
				if (hasWildcard) {
					// wildcard by relative path
					string normalizedPattern = pattern.Replace('\\', '/');
					if (WildcardMatch(relativePath, normalizedPattern)) {
						return true;
					}
					
					// If pattern points to a folder, also match anything under it.
					if (!normalizedPattern.EndsWith("*")) {
						string folderPattern = normalizedPattern.TrimEnd('/') + "/*";
						if (WildcardMatch(relativePath, folderPattern)) {
							return true;
						}
					}
				}
				else {
					// by full path prefix
					string excludeFull = Path.GetFullPath(Path.Combine(rootFolder, pattern));
					if (fullFilePath.StartsWith(excludeFull, StringComparison.OrdinalIgnoreCase)) {
						return true;
					}
				}
			}
			
			return false;
		}
	}
}
