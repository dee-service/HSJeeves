using System.IO;
using Newtonsoft.Json;

namespace HSJeeves
{
	public sealed class Settings
	{
		public string ApiUrl { get; set; } = "http://localhost:8000/API";
		// DEV default (local test API). DPAPI storage and the real key come with the settings UI.
		public string ApiKey { get; set; } = "hsjeeves-local-test-key";
		public bool DryRun { get; set; } = false;

		public static Settings Load()
		{
			try
			{
				if(File.Exists(AppPaths.SettingsFile))
					return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings();
			}
			catch(JsonException ex)
			{
				Log.Warn("settings.json unreadable, using defaults: " + ex.Message);
				return new Settings();
			}
			var s = new Settings();
			Directory.CreateDirectory(AppPaths.Data);
			File.WriteAllText(AppPaths.SettingsFile, JsonConvert.SerializeObject(s, Formatting.Indented));
			return s;
		}
	}
}
