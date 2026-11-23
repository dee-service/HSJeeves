using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HSJeeves.Tracking;
using HSJeeves.Upload;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HSJeeves.Tests
{
	public class UploadTests
	{
		static UploadTests()
		{
			Environment.SetEnvironmentVariable("HSJEEVES_DATA", Path.Combine(Path.GetTempPath(), "hsjeeves-tests-" + Guid.NewGuid()));
		}

		private sealed class FakeHandler : HttpMessageHandler
		{
			public int Status;
			public bool Throw;
			public int Calls;
			public string? Auth;
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
			{
				Calls++;
				Auth = request.Headers.Authorization?.ToString();
				if(Throw) throw new HttpRequestException("down");
				return Task.FromResult(new HttpResponseMessage((HttpStatusCode)Status));
			}
		}

		private static CompletedGame Game() => new CompletedGame
		{
			GameId = Guid.NewGuid(),
			StartTime = new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.FromHours(1)),
			EndTime = new DateTimeOffset(2026, 11, 23, 9, 10, 0, TimeSpan.FromHours(1)),
			Format = Formats.Standard, Won = true, PlayerBattleTag = "Deesnow#2794", OpponentBattleTag = "", Region = "EU",
			Before = new Medal { LeagueId = 5, StarLevel = 41, Stars = 2 },
			After = new Medal { LeagueId = 5, StarLevel = 41, Stars = 3 },
		};

		private static JObject Payload() => PayloadBuilder.Build(Game(), false);

		private static void Reset()
		{
			foreach(var d in new[] { AppPaths.Queue, AppPaths.Rejected, AppPaths.DryRun })
				if(Directory.Exists(d)) Directory.Delete(d, true);
		}

		[Fact]
		public void Payload_MatchesBackendContract()
		{
			var p = Payload();
			Assert.Equal(1, (int)p["schemaVersion"]!);
			Assert.Equal("2026-11-23T09:00:00+01:00", (string)p["startTime"]!);
			Assert.Equal("Standard", (string)p["format"]!);
			Assert.Equal("Win", (string)p["result"]!);
			Assert.Equal(JTokenType.Boolean, p["wasConceded"]!.Type);
			Assert.Equal("", (string)p["opponentBattleTag"]!);
			Assert.Equal(0, (int)p["rank"]!["Rank"]!);
			Assert.Equal(3, (int)p["rankAfter"]!["StarsAfter"]!);
		}

		[Theory]
		[InlineData(200, UploadOutcome.Done)]
		[InlineData(401, UploadOutcome.Unauthorized)]
		[InlineData(400, UploadOutcome.Rejected)]
		[InlineData(0, UploadOutcome.Retry)]
		[InlineData(408, UploadOutcome.Retry)]
		[InlineData(429, UploadOutcome.Retry)]
		[InlineData(503, UploadOutcome.Retry)]
		public void Classify(int status, UploadOutcome expected) => Assert.Equal(expected, UploadRules.Classify(status));

		[Fact]
		public void Success_RemovesFromQueue_AndSendsBearerKey()
		{
			Reset();
			var h = new FakeHandler { Status = 200 };
			var q = new UploadQueue(new Settings { Key = "k" }, new HttpClient(h));
			q.Enqueue(Payload());
			q.Flush();
			Assert.Empty(Directory.GetFiles(AppPaths.Queue));
			Assert.Equal("Bearer k", h.Auth);
		}

		[Fact]
		public void NetworkError_KeepsItemAndCountsAttempt_ThenSucceeds()
		{
			Reset();
			var h = new FakeHandler { Throw = true };
			var q = new UploadQueue(new Settings { Key = "k" }, new HttpClient(h));
			q.Enqueue(Payload());
			q.Flush();
			var file = Directory.GetFiles(AppPaths.Queue)[0];
			Assert.Equal(1, (int)JObject.Parse(File.ReadAllText(file))["attempts"]!);
			h.Throw = false; h.Status = 200;
			q.Flush();
			Assert.Empty(Directory.GetFiles(AppPaths.Queue));
		}

		[Fact]
		public void BadRequest_MovesToRejected()
		{
			Reset();
			var q = new UploadQueue(new Settings { Key = "k" }, new HttpClient(new FakeHandler { Status = 400 }));
			q.Enqueue(Payload());
			q.Flush();
			Assert.Empty(Directory.GetFiles(AppPaths.Queue));
			Assert.Single(Directory.GetFiles(AppPaths.Rejected));
		}

		[Fact]
		public void Unauthorized_PausesUploadsAndKeepsItems()
		{
			Reset();
			var h = new FakeHandler { Status = 401 };
			var q = new UploadQueue(new Settings { Key = "k" }, new HttpClient(h));
			q.Enqueue(Payload());
			q.Enqueue(Payload());
			q.Flush();
			Assert.True(q.KeyRejected);
			Assert.Equal(1, h.Calls); // second item not even tried
			Assert.Equal(2, Directory.GetFiles(AppPaths.Queue).Length);
		}

		[Fact]
		public void DryRun_WritesPayloadAndNeverCallsServer()
		{
			Reset();
			var h = new FakeHandler { Status = 200 };
			var q = new UploadQueue(new Settings { Key = "k", DryRun = true }, new HttpClient(h));
			q.Enqueue(Payload());
			q.Flush();
			Assert.Equal(0, h.Calls);
			Assert.Single(Directory.GetFiles(AppPaths.DryRun));
			Assert.Empty(Directory.GetFiles(AppPaths.Queue));
		}
	}
}
