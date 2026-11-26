using System;
using System.IO;
using System.Text;
using HSJeeves.Tracking;
using Xunit;

namespace HSJeeves.Tests
{
	public class ConcedeTests
	{
		// Lines copied from a real Power.log (2026-11-23, concede). Both appear for one concede.
		private const string GameStateLine = "D 12:04:04.3325586 GameState.DebugPrintPower() - TAG_CHANGE Entity=Deesnow#2794 tag=PLAYSTATE value=CONCEDED \n";
		private const string TaskListLine = "D 12:04:04.3325586 PowerTaskList.DebugPrintPower() -     TAG_CHANGE Entity=Deesnow#2794 tag=PLAYSTATE value=CONCEDED \n";
		private const string OtherLine = "D 12:04:04.4 GameState.DebugPrintPower() - TAG_CHANGE Entity=GameEntity tag=STATE value=COMPLETE \n";

		private static string NewLogsDir() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hsj-logs-" + Guid.NewGuid())).FullName;

		private static string NewSession(string logs, string stamp, string content = "")
		{
			var dir = Directory.CreateDirectory(Path.Combine(logs, "Hearthstone_" + stamp)).FullName;
			var file = Path.Combine(dir, "Power.log");
			File.WriteAllText(file, content, new UTF8Encoding(false));
			return file;
		}

		private static void Append(string file, string text)
		{
			using(var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
			{
				var b = new UTF8Encoding(false).GetBytes(text);
				fs.Write(b, 0, b.Length);
			}
		}

		[Fact]
		public void CountsOnlyTheGameStateConcedeLine()
		{
			var logs = NewLogsDir();
			var file = NewSession(logs, "2026_11_23_11_34_51");
			var w = new ConcedeWatcher(() => logs);
			w.Poll();
			Append(file, OtherLine + GameStateLine + TaskListLine + OtherLine);
			w.Poll();
			Assert.Equal(1, w.Count);
		}

		[Fact]
		public void OldLinesAtStartup_AreIgnored()
		{
			var logs = NewLogsDir();
			NewSession(logs, "2026_11_23_11_34_51", GameStateLine);
			var w = new ConcedeWatcher(() => logs);
			w.Poll();
			Assert.Equal(0, w.Count);
			Assert.True(w.Available);
		}

		[Fact]
		public void PartialLine_IsCompletedByTheNextRead()
		{
			var logs = NewLogsDir();
			var file = NewSession(logs, "2026_11_23_11_34_51");
			var w = new ConcedeWatcher(() => logs);
			w.Poll();
			Append(file, GameStateLine.Substring(0, 60));
			w.Poll();
			Assert.Equal(0, w.Count);
			Append(file, GameStateLine.Substring(60));
			w.Poll();
			Assert.Equal(1, w.Count);
		}

		[Fact]
		public void NewHearthstoneLaunch_NewFolderIsReadFromTheStart()
		{
			var logs = NewLogsDir();
			NewSession(logs, "2026_11_23_11_34_51");
			var w = new ConcedeWatcher(() => logs);
			w.Poll();
			// Hearthstone restarted: a new session folder appears already holding a concede line.
			NewSession(logs, "2026_11_23_13_00_00", GameStateLine);
			typeof(ConcedeWatcher).GetField("_lastScan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
				.SetValue(w, DateTime.MinValue); // do not wait for the 5 s rescan
			w.Poll();
			Assert.Equal(1, w.Count);
		}

		[Fact]
		public void MissingLogsFolder_IsNotAvailableAndDoesNotThrow()
		{
			var w = new ConcedeWatcher(() => null);
			w.Poll();
			Assert.False(w.Available);
			Assert.Equal(0, w.Count);
		}

		[Fact]
		public void LogConfig_MissingFile_IsCreatedWithPowerSection()
		{
			var path = Path.Combine(NewLogsDir(), "log.config");
			Assert.Equal(LogConfigResult.Created, LogConfigUpdater.EnsurePowerSection(path));
			Assert.Contains("[Power]", File.ReadAllText(path));
		}

		[Fact]
		public void LogConfig_ExistingPowerSection_IsLeftUntouched()
		{
			var path = Path.Combine(NewLogsDir(), "log.config");
			const string original = "[Arena]\r\nLogLevel=1\r\n[Power]\r\nLogLevel=1\r\nVerbose=True\r\n";
			File.WriteAllText(path, original);
			Assert.Equal(LogConfigResult.AlreadyPresent, LogConfigUpdater.EnsurePowerSection(path));
			Assert.Equal(original, File.ReadAllText(path));
		}

		[Fact]
		public void LogConfig_WithoutPowerSection_IsAppendedNeverOverwritten()
		{
			var path = Path.Combine(NewLogsDir(), "log.config");
			const string original = "[Arena]\r\nLogLevel=1";
			File.WriteAllText(path, original);
			File.SetAttributes(path, FileAttributes.ReadOnly);
			Assert.Equal(LogConfigResult.Appended, LogConfigUpdater.EnsurePowerSection(path));
			var text = File.ReadAllText(path);
			Assert.StartsWith(original, text);
			Assert.Contains("[Power]", text);
			Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0); // read-only flag restored
		}

		[Fact]
		public void FindLogsDir_UsesConfiguredFolder_AndIgnoresOneWithoutLogs()
		{
			var root = Path.Combine(Path.GetTempPath(), "hsj-hs-" + Guid.NewGuid());
			Directory.CreateDirectory(Path.Combine(root, "Logs"));
			Assert.Equal(Path.Combine(root, "Logs"), HearthstonePaths.FindLogsDir(root));
			var empty = Path.Combine(Path.GetTempPath(), "hsj-hs-" + Guid.NewGuid());
			Directory.CreateDirectory(empty);
			Assert.NotEqual(Path.Combine(empty, "Logs"), HearthstonePaths.FindLogsDir(empty));
		}

		[Fact]
		public void PendingGame_WithConcedeLine_IsReleasedAsConceded()
		{
			var p = new PendingGames();
			var t0 = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
			p.Add(new CompletedGame(), concedeCountAtGameStart: 3, now: t0);
			Assert.Empty(p.Resolve(3, t0.AddSeconds(2)));
			var r = p.Resolve(4, t0.AddSeconds(3));
			Assert.Single(r);
			Assert.True(r[0].Conceded);
			Assert.Equal(0, p.Count);
		}

		[Fact]
		public void PendingGame_WithoutConcedeLine_IsReleasedAsNotConcededAfterTheWait()
		{
			var p = new PendingGames();
			var t0 = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
			p.Add(new CompletedGame(), 3, t0);
			Assert.Empty(p.Resolve(3, t0 + PendingGames.Wait - TimeSpan.FromSeconds(1)));
			var r = p.Resolve(3, t0 + PendingGames.Wait);
			Assert.Single(r);
			Assert.False(r[0].Conceded);
			Assert.True(r[0].TimedOut);
		}
	}
}
