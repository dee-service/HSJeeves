using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace HSJeeves
{
	// %AppData%\HSJeeves\settings.json. The API key is stored DPAPI-protected (current user) in ApiKeyProtected.
	// The installer may write a plain ApiKey; it is protected and removed from the file on first load.
	public sealed class Settings
	{
		public string ApiUrl { get; set; } = "https://dee-service.cc/API";
		public bool DryRun { get; set; } = true; // safe default: no uploads until the HDT plugin is off (H2)
		public bool StartWithWindows { get; set; } = true;
		// Optional: Hearthstone install folder, only needed when it cannot be found automatically (B1).
		public string? HearthstoneDirectory { get; set; }

		// File fields for the key. Use Key in code.
		public string? ApiKey { get; set; }
		public string? ApiKeyProtected { get; set; }

		// The key in plain text, in memory only. Never log it.
		[JsonIgnore]
		public string Key { get; set; } = "";

		public static Settings Load()
		{
			Settings s;
			try
			{
				s = File.Exists(AppPaths.SettingsFile)
					? JsonConvert.DeserializeObject<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings()
					: new Settings();
			}
			catch(JsonException ex)
			{
				Log.Warn("settings.json unreadable, using defaults: " + ex.Message);
				return new Settings();
			}

			if(!string.IsNullOrEmpty(s.ApiKeyProtected))
			{
				try { s.Key = Unprotect(s.ApiKeyProtected!); }
				catch(CryptographicException)
				{
					Log.Warn("stored API key cannot be decrypted (different Windows user?). Enter it again in Settings.");
				}
			}
			else if(!string.IsNullOrEmpty(s.ApiKey))
				s.Key = s.ApiKey!;

			// First run, or a plain key written by the installer: write the protected form, drop the plain one.
			if(!File.Exists(AppPaths.SettingsFile) || !string.IsNullOrEmpty(s.ApiKey))
				s.Save();
			return s;
		}

		public void Save()
		{
			ApiKeyProtected = string.IsNullOrEmpty(Key) ? null : Protect(Key);
			ApiKey = null;
			Directory.CreateDirectory(AppPaths.Data);
			File.WriteAllText(AppPaths.SettingsFile,
				JsonConvert.SerializeObject(this, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
		}

		private static string Protect(string plain) =>
			Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

		private static string Unprotect(string protectedB64) =>
			Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedB64), null, DataProtectionScope.CurrentUser));
	}
}
