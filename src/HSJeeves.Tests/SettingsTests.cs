using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HSJeeves.Upload;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HSJeeves.Tests
{
	public class SettingsTests
	{
		private static void ResetSettingsFile()
		{
			if(File.Exists(AppPaths.SettingsFile)) File.Delete(AppPaths.SettingsFile);
		}

		[Fact]
		public void Key_IsStoredProtected_AndNeverAsPlainText()
		{
			ResetSettingsFile();
			var s = new Settings { DryRun = false, Key = "super-secret-key" };
			s.Save();
			var raw = File.ReadAllText(AppPaths.SettingsFile);
			Assert.DoesNotContain("super-secret-key", raw);
			Assert.Contains("ApiKeyProtected", raw);
			Assert.Equal("super-secret-key", Settings.Load().Key);
		}

		[Fact]
		public void PlainKeyFromInstaller_IsMigratedAndRemovedFromFile()
		{
			ResetSettingsFile();
			Directory.CreateDirectory(AppPaths.Data);
			File.WriteAllText(AppPaths.SettingsFile, "{\"apiUrl\":\"https://x/API\",\"ApiKey\":\"installer-key\"}");
			var s = Settings.Load();
			Assert.Equal("installer-key", s.Key);
			Assert.Equal("https://x/API", s.ApiUrl);
			var raw = File.ReadAllText(AppPaths.SettingsFile);
			Assert.DoesNotContain("installer-key", raw);
			Assert.Equal("installer-key", Settings.Load().Key);
		}

		[Fact]
		public void FirstRun_WritesDefaultsWithProductionUrlAndNoKey()
		{
			ResetSettingsFile();
			var s = Settings.Load();
			Assert.Equal("https://dee-service.cc/API", s.ApiUrl);
			Assert.Equal("", s.Key);
			Assert.True(s.DryRun); // safe default until the HDT plugin is switched off
			Assert.True(File.Exists(AppPaths.SettingsFile));
		}

		[Fact]
		public void CorruptProtectedKey_GivesEmptyKeyInsteadOfCrashing()
		{
			ResetSettingsFile();
			Directory.CreateDirectory(AppPaths.Data);
			File.WriteAllText(AppPaths.SettingsFile, "{\"ApiKeyProtected\":\"AAAA\"}");
			Assert.Equal("", Settings.Load().Key);
		}

		private sealed class Handler : HttpMessageHandler
		{
			public int Status = 200;
			public bool Throw;
			public Uri? Uri;
			public string? Auth;
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
			{
				Uri = r.RequestUri;
				Auth = r.Headers.Authorization?.ToString();
				if(Throw) throw new HttpRequestException("no route");
				return Task.FromResult(new HttpResponseMessage((HttpStatusCode)Status));
			}
		}

		[Theory]
		[InlineData(200, KeyCheck.Valid)]
		[InlineData(401, KeyCheck.Rejected)]
		[InlineData(404, KeyCheck.Unsupported)]
		[InlineData(500, KeyCheck.Unreachable)]
		public async Task TestConnection_MapsStatus(int status, KeyCheck expected)
		{
			var h = new Handler { Status = status };
			var r = await ApiClient.CheckKeyAsync(new HttpClient(h), "https://host/API/", " key ");
			Assert.Equal(expected, r.Kind);
			Assert.Equal("https://host/API/check", h.Uri!.ToString());
			Assert.Equal("Bearer key", h.Auth);
		}

		[Fact]
		public async Task TestConnection_NetworkError_IsUnreachable()
		{
			var r = await ApiClient.CheckKeyAsync(new HttpClient(new Handler { Throw = true }), "https://host/API", "k");
			Assert.Equal(KeyCheck.Unreachable, r.Kind);
		}

		[Fact]
		public async Task TestConnection_EmptyKey_IsRejectedWithoutCallingServer()
		{
			var h = new Handler();
			var r = await ApiClient.CheckKeyAsync(new HttpClient(h), "https://host/API", "  ");
			Assert.Equal(KeyCheck.Rejected, r.Kind);
			Assert.Null(h.Uri);
		}

		[Fact]
		public void Queue_WithoutKey_KeepsItemsAndDoesNotCallServer()
		{
			foreach(var d in new[] { AppPaths.Queue, AppPaths.DryRun })
				if(Directory.Exists(d)) Directory.Delete(d, true);
			var h = new Handler();
			var q = new UploadQueue(new Settings { DryRun = false, Key = "" }, new HttpClient(h));
			q.Enqueue(new JObject { ["gameId"] = Guid.NewGuid().ToString() });
			q.Flush();
			Assert.True(q.MissingKey);
			Assert.Null(h.Uri);
			Assert.Equal(1, q.PendingCount);
		}
	}
}
