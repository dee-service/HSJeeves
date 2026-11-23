using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HSJeeves.Update;
using Xunit;

namespace HSJeeves.Tests
{
	public class UpdateTests
	{
		private const string Url = "https://example.test/downloads/versions.json";

		private const string Manifest = @"{
			""latest"": ""0.2.0"",
			""versions"": [
				{ ""version"": ""0.2.0"", ""file"": ""HSJeeves-Setup-0.2.0.exe"", ""sha256"": ""aa"", ""size"": 1, ""date"": ""2026-12-01"", ""notes"": ""Fixes Wild"" },
				{ ""version"": ""0.1.0"", ""file"": ""HSJeeves-Setup-0.1.0.exe"", ""sha256"": ""bb"", ""size"": 1, ""date"": ""2026-11-23"", ""notes"": """" }
			]
		}";

		[Fact]
		public void NewerVersion_IsReported_WithDownloadAndPageLinks()
		{
			var u = UpdateChecker.Parse(Manifest, Url, new Version(0, 1, 0, 0));
			Assert.NotNull(u);
			Assert.Equal(new Version(0, 2, 0), u!.Version);
			Assert.Equal("Fixes Wild", u.Notes);
			Assert.Equal("https://example.test/downloads/HSJeeves-Setup-0.2.0.exe", u.FileUrl);
			Assert.Equal("https://example.test/#hsjeeves", u.PageUrl);
		}

		[Theory]
		[InlineData(0, 2, 0, 0)] // same (assembly versions have 4 parts)
		[InlineData(0, 3, 0, 0)] // installed is newer (rollback or dev build)
		public void SameOrOlderManifest_ReportsNothing(int a, int b, int c, int d) =>
			Assert.Null(UpdateChecker.Parse(Manifest, Url, new Version(a, b, c, d)));

		[Theory]
		[InlineData("")]
		[InlineData("not json")]
		[InlineData("{}")]
		[InlineData("{\"versions\":[]}")]
		[InlineData("{\"versions\":[{\"version\":\"x.y\"}]}")]
		public void MalformedManifest_ReportsNothing(string json) =>
			Assert.Null(UpdateChecker.Parse(json, Url, new Version(0, 1, 0, 0)));

		private sealed class Handler : HttpMessageHandler
		{
			public int Status = 200;
			public string Body = Manifest;
			public bool Throw;
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
			{
				if(Throw) throw new HttpRequestException("offline");
				return Task.FromResult(new HttpResponseMessage((HttpStatusCode)Status) { Content = new StringContent(Body) });
			}
		}

		[Fact]
		public async Task Check_Success_ReturnsUpdate() =>
			Assert.NotNull(await UpdateChecker.CheckAsync(new HttpClient(new Handler()), Url, new Version(0, 1, 0)));

		[Fact]
		public async Task Check_NetworkErrorOr404_ReturnsNullWithoutThrowing()
		{
			Assert.Null(await UpdateChecker.CheckAsync(new HttpClient(new Handler { Throw = true }), Url, new Version(0, 1, 0)));
			Assert.Null(await UpdateChecker.CheckAsync(new HttpClient(new Handler { Status = 404 }), Url, new Version(0, 1, 0)));
		}

		[Fact]
		public async Task Check_EmptyUrl_DoesNothing() =>
			Assert.Null(await UpdateChecker.CheckAsync(new HttpClient(new Handler()), "", new Version(0, 1, 0)));
	}
}
