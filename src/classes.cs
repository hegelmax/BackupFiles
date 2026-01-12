using System.Collections.Generic;
using System.Xml.Serialization;

namespace BackupFiles
{
	[XmlRoot("configuration")]
	public class Config
	{
		public string ProjectName { get; set; }
		public string Version { get; set; }

		[XmlArray("extensions")]
		[XmlArrayItem("extension")]
		public List<ConfigItem> Extensions { get; set; }

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
		
		public bool EnableZip { get; set; }
		public bool DeleteUnziped { get; set; }
		
		public int IsExample { get; set; }
	}
	
	public class ConfigItem
	{
		[XmlText]
		public string Value { get; set; }
		
		[XmlAttribute("tree_only")]
		public bool TreeOnly { get; set; }
	}
}
