using System;
using System.IO;

namespace HSJeeves
{
	// Daily files: logs\hsjeeves-yyyy-MM-dd.log. Files older than RetentionDays are deleted.
	public static class Log
	{
		public const int RetentionDays = 7;
		private static readonly object Gate = new object();
		private static DateTime _lastCleanup = DateTime.MinValue;

		public static void Info(string msg) => Write("INFO", msg);
		public static void Warn(string msg) => Write("WARN", msg);

		private static void Write(string level, string msg)
		{
			try
			{
				lock(Gate)
				{
					var now = DateTimeOffset.Now;
					Directory.CreateDirectory(AppPaths.LogDir);
					if(_lastCleanup.Date != now.Date)
					{
						_lastCleanup = now.Date;
						DeleteOld(AppPaths.LogDir, now.DateTime, RetentionDays);
					}
					File.AppendAllText(AppPaths.LogFileFor(now.DateTime), $"{now:yyyy-MM-dd HH:mm:ss.fff} {level} {msg}{Environment.NewLine}");
				}
			}
			catch(IOException) { }
			catch(UnauthorizedAccessException) { }
		}

		// Keeps files whose date in the name is within `days` of `today`; ignores files it does not recognise.
		public static void DeleteOld(string dir, DateTime today, int days)
		{
			foreach(var file in Directory.GetFiles(dir, "hsjeeves-*.log"))
			{
				var name = Path.GetFileNameWithoutExtension(file).Substring("hsjeeves-".Length);
				if(DateTime.TryParseExact(name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
					&& (today.Date - d.Date).TotalDays >= days)
				{
					try { File.Delete(file); } catch(IOException) { }
				}
			}
		}
	}
}
