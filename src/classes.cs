using System.Collections.Generic;
using System.Xml.Serialization;

namespace BackupFiles
{
	[XmlRoot("configuration")]
	public class Config
	{
		public Config() {
			UpdateCheckMinutes = 1440;
			UpdateCheckTimeoutSeconds = 5;
		}

		public string ProjectName { get; set; }
		public string Version { get; set; }
		public string Created { get; set; }

		[XmlElement("extensions")]
		public ExtensionsConfig Extensions { get; set; }

		[XmlArray("includePaths")]
		[XmlArrayItem("includePath")]
		public List<ConfigItem> IncludePaths { get; set; }

		[XmlArray("includeFiles")]
		[XmlArrayItem("includeFile")]
		public List<ConfigItem> IncludeFiles { get; set; }

		[XmlArray("excludePaths")]
		[XmlArrayItem("excludePath")]
		public List<string> ExcludePaths { get; set; }

		public string ResultPath { get; set; }
		public string ResultFilenameMask { get; set; }
		
		public int UpdateCheckMinutes { get; set; }
		public int UpdateCheckTimeoutSeconds { get; set; }

		public bool EnableZip { get; set; }
		public bool DeleteUnziped { get; set; }
		
		public int IsExample { get; set; }
	}
	
	public class ConfigItem
	{
		public ConfigItem() {
			Enable = true;
		}

		[XmlText]
		public string Value { get; set; }
		
		[XmlAttribute("tree_only")]
		public bool TreeOnly { get; set; }

		[XmlAttribute("enable")]
		public bool Enable { get; set; }
	}

	public class ExtensionsConfig
	{
		[XmlElement("extension")]
		public List<ConfigItem> Extensions { get; set; }

		[XmlElement("group")]
		public List<ConfigGroup> Groups { get; set; }

		public List<ConfigItem> GetAllExtensions() {
			var items = new List<ConfigItem>();
			if (Extensions != null) {
				items.AddRange(Extensions);
			}
			if (Groups != null) {
				foreach (var group in Groups) {
					if (group != null && group.Extensions != null) {
						foreach (var extension in group.Extensions) {
							if (extension != null && group.TreeOnly) {
								extension.TreeOnly = extension.TreeOnly || group.TreeOnly;
							}
							items.Add(extension);
						}
					}
				}
			}

			return items;
		}
	}

	public class ConfigGroup
	{
		[XmlAttribute("name")]
		public string Name { get; set; }

		[XmlAttribute("tree_only")]
		public bool TreeOnly { get; set; }

		[XmlElement("extension")]
		public List<ConfigItem> Extensions { get; set; }
	}
}
