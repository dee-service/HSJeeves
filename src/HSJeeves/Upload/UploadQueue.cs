using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSJeeves.Upload
{
	// Disk-backed queue: one file per game in <data>\queue. Survives restarts.
	public sealed class UploadQueue
	{
		private readonly Settings _settings;
		private readonly HttpClient _http;
		private readonly object _gate = new object();

		public bool KeyRejected { get; private set; }

		public UploadQueue(Settings settings, HttpClient? http = null)
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			_settings = settings;
			_http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		}

		public void Enqueue(JObject payload)
		{
			Directory.CreateDirectory(AppPaths.Queue);
			var item = new JObject { ["queuedAt"] = DateTimeOffset.Now, ["attempts"] = 0, ["payload"] = payload };
			File.WriteAllText(Path.Combine(AppPaths.Queue, (string)payload["gameId"]! + ".json"), item.ToString(Formatting.Indented));
			Log.Info("queued game " + payload["gameId"]);
		}

		public void ResetKeyRejected() => KeyRejected = false;

		public void Flush()
		{
			lock(_gate)
			{
				if(!Directory.Exists(AppPaths.Queue))
					return;
				foreach(var file in Directory.GetFiles(AppPaths.Queue, "*.json"))
					Process(file);
			}
		}

		private void Process(string file)
		{
			var item = JObject.Parse(File.ReadAllText(file));
			var payload = (JObject)item["payload"]!;
			var id = (string)payload["gameId"]!;

			if(_settings.DryRun)
			{
				Directory.CreateDirectory(AppPaths.DryRun);
				File.WriteAllText(Path.Combine(AppPaths.DryRun, id + ".json"), payload.ToString(Formatting.Indented));
				File.Delete(file);
				Log.Info("dry run: payload written for game " + id);
				return;
			}
			if(KeyRejected)
				return; // wait for a new key instead of hammering the server

			int status;
			try
			{
				var req = new HttpRequestMessage(HttpMethod.Post, _settings.ApiUrl)
				{
					Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json"),
				};
				req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
				status = (int)_http.SendAsync(req).GetAwaiter().GetResult().StatusCode;
			}
			catch(Exception ex) when(ex is HttpRequestException || ex is TaskCanceledException)
			{
				status = 0; // network error or timeout: retry
				Log.Warn("upload of " + id + " failed (network): " + ex.Message);
			}

			switch(UploadRules.Classify(status))
			{
				case UploadOutcome.Done:
					File.Delete(file);
					Log.Info("uploaded game " + id);
					break;
				case UploadOutcome.Unauthorized:
					KeyRejected = true;
					Log.Warn("server rejected the API key (401). Run /hdttoken in Discord and update the key. Uploads paused.");
					break;
				case UploadOutcome.Rejected:
					MoveToRejected(file, id);
					Log.Warn("server rejected game " + id + " (400); copy kept in rejected\\");
					break;
				default:
					var attempts = (int)item["attempts"]! + 1;
					var age = DateTimeOffset.Now - item["queuedAt"]!.ToObject<DateTimeOffset>();
					if(attempts >= UploadRules.MaxAttempts || age > UploadRules.MaxAge)
					{
						MoveToRejected(file, id);
						Log.Warn("giving up on game " + id + " after " + attempts + " attempts");
					}
					else
					{
						item["attempts"] = attempts;
						File.WriteAllText(file, item.ToString(Formatting.Indented));
						Log.Info("will retry game " + id + " (status " + status + ", attempt " + attempts + ")");
					}
					break;
			}
		}

		private static void MoveToRejected(string file, string id)
		{
			Directory.CreateDirectory(AppPaths.Rejected);
			var target = Path.Combine(AppPaths.Rejected, id + ".json");
			if(File.Exists(target)) File.Delete(target);
			File.Move(file, target);
		}
	}
}
