using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace HSJeeves.Upload
{
	public enum KeyCheck { Valid, Rejected, Unsupported, Unreachable }

	public sealed class KeyCheckResult
	{
		public KeyCheck Kind { get; set; }
		public string Message { get; set; } = "";
	}

	public static class ApiClient
	{
		// "Test connection": GET {ApiUrl}/check with the bearer key. 200 valid, 401 invalid or revoked.
		public static async Task<KeyCheckResult> CheckKeyAsync(HttpClient http, string apiUrl, string key)
		{
			if(string.IsNullOrWhiteSpace(key))
				return new KeyCheckResult { Kind = KeyCheck.Rejected, Message = "No API key entered." };
			if(!Uri.TryCreate(apiUrl.TrimEnd('/') + "/check", UriKind.Absolute, out var uri))
				return new KeyCheckResult { Kind = KeyCheck.Unreachable, Message = "The API URL is not valid." };
			try
			{
				var req = new HttpRequestMessage(HttpMethod.Get, uri);
				req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
				var status = (int)(await http.SendAsync(req).ConfigureAwait(false)).StatusCode;
				if(status == 200)
					return new KeyCheckResult { Kind = KeyCheck.Valid, Message = "Key accepted." };
				if(status == 401)
					return new KeyCheckResult { Kind = KeyCheck.Rejected, Message = "Key rejected. Run /hdttoken in Discord to get a new one." };
				if(status == 404 || status == 405)
					return new KeyCheckResult { Kind = KeyCheck.Unsupported, Message = "The server cannot verify keys yet (no /check). Uploads may still work." };
				return new KeyCheckResult { Kind = KeyCheck.Unreachable, Message = "Server answered " + status + "." };
			}
			catch(Exception ex) when(ex is HttpRequestException || ex is TaskCanceledException)
			{
				return new KeyCheckResult { Kind = KeyCheck.Unreachable, Message = "Could not reach the server: " + ex.Message };
			}
		}
	}
}
