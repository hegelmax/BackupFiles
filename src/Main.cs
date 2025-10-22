using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

	class Program
	{
		static void Main(string[] args) {
			try {
				if (args.Length != 0) {
					string unzipFilePath = "";
					string filePath = args[0];

					if (!File.Exists(filePath)) {
						Console.WriteLine("The specified file does not exist.");
						WaitForUserInput();
						return;
					}

					string extension				= Path.GetExtension(filePath).ToLower();
					string fileNameWithoutExtension	= Path.GetFileNameWithoutExtension(filePath);
					string newFolder				= Path.Combine(Path.GetDirectoryName(filePath), fileNameWithoutExtension);

					if (!Directory.Exists(newFolder)) {
						Directory.CreateDirectory(newFolder);
					}

					if (extension == ".zip") {
						unzipFilePath = filePath.Replace(".zip", "");
						Console.WriteLine("Unzipping file: "+filePath);
						UnzipFileUsingPowerShell(filePath, unzipFilePath);
						string[] files = Directory.GetFiles(unzipFilePath);
						if (files.Length > 0) {
							filePath		= files[0];
							unzipFilePath	= filePath;
						} else {
							Console.WriteLine("The error occurred while unziping file");
							WaitForUserInput();
							return;
						}
					}
					
					Console.WriteLine("Processing text file: "+filePath);
					CreateFileStructureFromText(filePath, newFolder);
					
					//Delete unziped file
					if (File.Exists(unzipFilePath)) {
						File.Delete(unzipFilePath);
					}
				}
				else {
					string rootFolder = AppDomain.CurrentDomain.BaseDirectory;

					// Read or create configuration
					string configPath = Path.Combine(rootFolder, "backup.config.xml");
					Config config;

					if (!File.Exists(configPath)) {
						CreateConfigTemplate(configPath);
						Console.WriteLine("Config file is missing! A template has been created. Please update the 'is_example' parameter to 0.");
						WaitForUserInput();
						return;
					}
					else {
						config = Deserialize<Config>(configPath);

						if (config.IsExample == 1) {
							Console.WriteLine("Config file is not set. Please update the 'is_example' parameter to 0.");
							WaitForUserInput();
							return;
						}

						if (string.IsNullOrEmpty(config.ProjectName) || string.IsNullOrEmpty(config.Version)) {
							Console.WriteLine("ProjectName or Version is missing in the config file.");
							WaitForUserInput();
							return;
						}

						if (config.Extensions == null || config.Extensions.Count == 0 ||
							config.IncludePaths == null || config.IncludePaths.Count == 0
						) {
							Console.WriteLine("Extensions or IncludePaths are missing in the config file.");
							WaitForUserInput();
							return;
						}
					}

					// Ensure result path exists
					string resultDir = Path.Combine(rootFolder, config.ResultPath);
					try {
						Directory.CreateDirectory(resultDir);
					}
					catch (Exception ex) {
						Console.WriteLine("Failed to create result directory: {0}", ex.Message);
						WaitForUserInput();
						return;
					}

					// Collect files
					List<string> files;
					try {
						files = GetFiles(config, rootFolder);
					}
					catch (Exception ex) {
						Console.WriteLine("Error while collecting files: {0}", ex.Message);
						WaitForUserInput();
						return;
					}

					// Write result file
					string resultFilePath;
					try {
						resultFilePath = GetResultFilePath(config, rootFolder);
					}
					catch (Exception ex) {
						Console.WriteLine("Error while generating result file path: {0}", ex.Message);
						WaitForUserInput();
						return;
					}

					try {
						if(WriteResultFile(config, files, resultFilePath, rootFolder)){
							// After successful backup, increment the version
							IncrementVersion(configPath);
							Console.WriteLine("Backup completed successfully. Files are packed in {0}", resultFilePath);
						}
					}
					catch (Exception ex) {
						Console.WriteLine("Error while writing the result file: {0}", ex.Message);
					}
				}
			}
			catch (Exception ex) {
				Console.WriteLine("An unexpected error occurred: {0}", ex.Message);
			}

			// Wait for user input before closing
			WaitForUserInput();
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
			var config = new Config {
				ProjectName = "MyProject",
				Version = "1.0.0",
				Extensions = new List<string> { ".config", ".cs" },
				IncludePaths = new List<string> { "./included_folder" },
				IncludeFiles = new List<string> { "./example.file.config" },
				ExcludePaths = new List<string> { "./excluded_folder" },
				ResultPath = "./backup",
				ResultFilenameMask = "@PROJECTNAME_@VER_#YYYYMMDDhhmmss#.bak.txt",
				IsExample = 1
			};

			try {
				XmlSerializer serializer = new XmlSerializer(typeof(Config));
				using (StreamWriter writer = new StreamWriter(configPath)) {
					serializer.Serialize(writer, config);
				}
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

		static List<string> GetFiles(Config config, string rootFolder) {
			var files = new List<string>();

			foreach (string includePath in config.IncludePaths) {
				string fullPath = Path.Combine(rootFolder, includePath);

				if (!Directory.Exists(fullPath)) {
					Console.WriteLine("Include path does not exist: {0}", fullPath);
					continue;
				}

				var includedFiles = Directory.GetFiles(fullPath, "*.*", SearchOption.AllDirectories)
											 .Where(file => config.Extensions.Contains(Path.GetExtension(file)))
											 .Where(file => !config.ExcludePaths.Any(excludePath =>
												 Path.GetFullPath(file).StartsWith(Path.GetFullPath(Path.Combine(rootFolder, excludePath)), StringComparison.OrdinalIgnoreCase)))
											 .ToList();
				files.AddRange(includedFiles);
			}
			
			if (config.IncludeFiles != null && config.IncludeFiles.Count > 0) {
				foreach (string includeFile in config.IncludeFiles) {
					string fullPath = Path.Combine(rootFolder, includeFile);

					if (File.Exists(fullPath)) {
						// Проверка на исключения
						bool isExcluded = config.ExcludePaths.Any(excludePath =>
							Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(Path.Combine(rootFolder, excludePath)), StringComparison.OrdinalIgnoreCase));

						if (!isExcluded) {
							files.Add(fullPath);
							Console.WriteLine("Including file: {0}", includeFile);
						}
					}
					else {
						Console.WriteLine("Include file does not exist: {0}", fullPath);
					}
				}
			}
			
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

		static bool WriteResultFile(Config config, List<string> files, string resultFilePath, string rootFolder) {
			try {
				using (StreamWriter writer = new StreamWriter(resultFilePath)) {
					// Write folder structure from filtered files first
					string folderStructure = GenerateFolderStructureFromFilteredFiles(files, rootFolder);
					writer.WriteLine(folderStructure);
					
					foreach (string file in files) {
						string relativePath = GetRelativePath(rootFolder, file);
						string fileContent = File.ReadAllText(file);

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

		static string GenerateFolderStructureFromFilteredFiles(List<string> filteredFiles, string rootFolder) {
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
	}
}