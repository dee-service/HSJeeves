using System;
using System.IO;
using System.Text.RegularExpressions;

namespace HSJeeves.Tracking
{
	public enum LogConfigResult { AlreadyPresent, Created, Appended, Failed }

	// Makes sure log.config has a [Power] section so Hearthstone writes Power.log (needed for the concede flag).
	// Add only, never overwrite: HDT manages the same file.
	public static class LogConfigUpdater
	{
		private const string Section =
			"[Power]\r\nLogLevel=1\r\nFilePrinting=True\r\nConsolePrinting=False\r\nScreenPrinting=False\r\nVerbose=True\r\n";

		public static string DefaultPath => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blizzard", "Hearthstone", "log.config");

		public static LogConfigResult EnsurePowerSection(string path)
		{
			try
			{
				if(!File.Exists(path))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(path)!);
					File.WriteAllText(path, Section);
					return LogConfigResult.Created;
				}
				var text = File.ReadAllText(path);
				if(Regex.IsMatch(text, @"^\s*\[Power\]\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
					return LogConfigResult.AlreadyPresent;

				var attrs = File.GetAttributes(path);
				var readOnly = (attrs & FileAttributes.ReadOnly) != 0;
				if(readOnly)
					File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
				try
				{
					var sep = text.Length == 0 || text.EndsWith("\n") ? "" : "\r\n";
					File.AppendAllText(path, sep + Section);
				}
				finally
				{
					if(readOnly)
						File.SetAttributes(path, attrs);
				}
				return LogConfigResult.Appended;
			}
			catch(Exception ex) when(ex is IOException || ex is UnauthorizedAccessException)
			{
				Log.Warn("could not update log.config: " + ex.Message);
				return LogConfigResult.Failed;
			}
		}
	}
}
