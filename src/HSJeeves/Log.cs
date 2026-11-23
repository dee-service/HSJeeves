using System;
using System.IO;

namespace HSJeeves
{
	public static class Log
	{
		private static readonly object Gate = new object();

		public static void Info(string msg) => Write("INFO", msg);
		public static void Warn(string msg) => Write("WARN", msg);

		private static void Write(string level, string msg)
		{
			try
			{
				lock(Gate)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.Log)!);
					File.AppendAllText(AppPaths.Log, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {msg}{Environment.NewLine}");
				}
			}
			catch(IOException) { }
		}
	}
}
