using System;
using System.IO;

namespace HSJeeves
{
	public static class AppPaths
	{
		// HSJEEVES_DATA overrides the folder (tests, side-by-side runs).
		public static string Data { get; } = Environment.GetEnvironmentVariable("HSJEEVES_DATA")
			?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HSJeeves");
		public static string Queue => Path.Combine(Data, "queue");
		public static string Rejected => Path.Combine(Data, "rejected");
		public static string DryRun => Path.Combine(Data, "dryrun");
		public static string LogDir => Path.Combine(Data, "logs");
		public static string LogFileFor(DateTime day) => Path.Combine(LogDir, "hsjeeves-" + day.ToString("yyyy-MM-dd") + ".log");
		public static string SettingsFile => Path.Combine(Data, "settings.json");
	}
}
