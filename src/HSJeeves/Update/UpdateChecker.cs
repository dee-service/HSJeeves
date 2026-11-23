using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HSJeeves.Update
{
	public sealed class UpdateInfo
	{
		public Version Version { get; set; } = new Version(0, 0, 0);
		public string Notes { get; set; } = "";
		// Direct link to the installer, and the website page that shows notes and the checksum.
		public string FileUrl { get; set; } = "";
		public string PageUrl { get; set; } = "";
	}

	// Reads the website's /downloads/versions.json (written by installer\publish.ps1) and tells whether a newer
	// installer exists. It only informs: nothing is downloaded or installed automatically.
	public static class UpdateChecker
	{
		// Assembly versions have 4 parts (0.1.0.0), the manifest has 3 (0.1.0): compare on 3.
		public static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

		public static async Task<UpdateInfo?> CheckAsync(HttpClient http, string url, Version current)
		{
			if(string.IsNullOrWhiteSpace(url))
				return null;
			try
			{
				var json = await http.GetStringAsync(url).ConfigureAwait(false);
				return Parse(json, url, current);
			}
			catch(Exception ex)
			{
				Log.Info("update check failed: " + ex.Message);
				return null;
			}
		}

		// Returns the latest version when it is newer than 'current', otherwise null (also for malformed data).
		public static UpdateInfo? Parse(string json, string url, Version current)
		{
			try
			{
				var root = JObject.Parse(json);
				var versions = root["versions"] as JArray;
				var latestName = (string?)root["latest"];
				var entry = versions?.OfType<JObject>().FirstOrDefault(v => (string?)v["version"] == latestName)
					?? versions?.OfType<JObject>().FirstOrDefault();
				if(entry == null || !Version.TryParse((string?)entry["version"], out var latest))
					return null;
				if(Normalize(latest) <= Normalize(current))
					return null;

				var baseUri = new Uri(url);
				var file = (string?)entry["file"] ?? "";
				return new UpdateInfo
				{
					Version = Normalize(latest),
					Notes = (string?)entry["notes"] ?? "",
					FileUrl = file.Length == 0 ? "" : new Uri(baseUri, Uri.EscapeDataString(file)).ToString(),
					PageUrl = baseUri.GetLeftPart(UriPartial.Authority) + "/#hsjeeves",
				};
			}
			catch(Exception)
			{
				return null;
			}
		}
	}
}
