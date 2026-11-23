using System;

namespace HSJeeves.Upload
{
	public enum UploadOutcome { Done, Retry, Rejected, Unauthorized }

	public static class UploadRules
	{
		public const int MaxAttempts = 20;
		public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

		// 200 done; 401 stop (key problem); 400 drop; network errors (0), 408, 429, 5xx retry.
		public static UploadOutcome Classify(int status)
		{
			if(status >= 200 && status < 300) return UploadOutcome.Done;
			if(status == 401) return UploadOutcome.Unauthorized;
			if(status == 400) return UploadOutcome.Rejected;
			return UploadOutcome.Retry;
		}
	}
}
