using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HSJeeves.Tracking
{
	// Tails Hearthstone's Power.log and counts PLAYSTATE=CONCEDED events. Nothing else is read from the log.
	// Same signal HDT uses (TagChangeActions.PlaystateChange); meaning is "either player conceded".
	public sealed class ConcedeWatcher
	{
		// Use the GameState line only: PowerTaskList repeats the same event.
		private static readonly Regex ConcedeLine = new Regex(
			@"GameState\.DebugPrintPower\(\) - TAG_CHANGE Entity=.* tag=PLAYSTATE value=CONCEDED", RegexOptions.Compiled);

		private readonly Func<string?> _findLogsDir;
		private string? _file;
		private long _position;
		private string _partial = "";
		private DateTime _lastScan = DateTime.MinValue;

		public ConcedeWatcher(Func<string?> findLogsDir)
		{
			_findLogsDir = findLogsDir;
		}

		// Number of concede events seen since the watcher started. Compare with a baseline taken at game start.
		public int Count { get; private set; }
		// True while a Power.log is being followed.
		public bool Available => _file != null;
		// True while a Hearthstone Logs folder exists (even if no Power.log is in it yet).
		public bool LogsDirFound { get; private set; }

		public void Poll()
		{
			try
			{
				if(_file == null || DateTime.UtcNow - _lastScan > TimeSpan.FromSeconds(5))
				{
					_lastScan = DateTime.UtcNow;
					SelectFile();
				}
				if(_file != null)
					ReadNew();
			}
			catch(IOException) { }
			catch(UnauthorizedAccessException) { }
		}

		// Newest Hearthstone_<timestamp>\Power.log. A new Hearthstone launch creates a new folder: read it from the start.
		// The first file found at startup is read from its end (older games are not ours to judge).
		private void SelectFile()
		{
			var dir = _findLogsDir();
			LogsDirFound = dir != null && Directory.Exists(dir);
			if(!LogsDirFound)
			{
				_file = null;
				return;
			}
			var newest = Directory.GetDirectories(dir, "Hearthstone_*")
				.OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
				.Select(d => Path.Combine(d, "Power.log"))
				.FirstOrDefault(File.Exists);
			if(newest == null || newest == _file)
				return;
			var first = !_started;
			_started = true;
			_file = newest;
			_position = first ? new FileInfo(newest).Length : 0;
			_partial = "";
			Log.Info("following " + newest + (first ? " (from end)" : " (from start)"));
		}

		private bool _started;

		private void ReadNew()
		{
			using(var fs = new FileStream(_file!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			{
				if(fs.Length < _position)
				{
					_position = 0; // truncated
					_partial = "";
				}
				if(fs.Length == _position)
					return;
				fs.Seek(_position, SeekOrigin.Begin);
				var buf = new byte[fs.Length - _position];
				var n = fs.Read(buf, 0, buf.Length);
				_position += n;
				var text = _partial + Encoding.UTF8.GetString(buf, 0, n);
				var lines = text.Split('\n');
				_partial = lines[lines.Length - 1]; // incomplete last line waits for the next read
				for(var i = 0; i < lines.Length - 1; i++)
				{
					if(ConcedeLine.IsMatch(lines[i]))
						Count++;
				}
			}
		}
	}

	public static class HearthstonePaths
	{
		// Logs folder of the Hearthstone install: configured folder, then the running process, then standard places.
		public static string? FindLogsDir(string? configuredInstallDir)
		{
			var candidates = new[]
			{
				configuredInstallDir,
				FromProcess(),
				FromRegistry(),
				@"C:\Program Files (x86)\Hearthstone",
				@"C:\Program Files\Hearthstone",
			};
			foreach(var c in candidates)
			{
				if(string.IsNullOrWhiteSpace(c))
					continue;
				var logs = Path.Combine(c, "Logs");
				if(Directory.Exists(logs))
					return logs;
			}
			return null;
		}

		// Battle.net registers Hearthstone's uninstall entry (32-bit view); InstallLocation is the game folder.
		private static string? FromRegistry()
		{
			try
			{
				foreach(var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
					using(var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
					using(var k = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone"))
					{
						var v = k?.GetValue("InstallLocation") as string;
						if(!string.IsNullOrWhiteSpace(v))
							return v;
					}
			}
			catch(Exception) { }
			return null;
		}

		private static string? FromProcess()
		{
			try
			{
				var p = Process.GetProcessesByName("Hearthstone").FirstOrDefault();
				return p == null ? null : Path.GetDirectoryName(p.MainModule?.FileName);
			}
			catch(Exception)
			{
				return null; // access denied (elevated game) or process gone
			}
		}
	}
}
