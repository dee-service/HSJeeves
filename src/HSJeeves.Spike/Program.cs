using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using HearthMirror;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HSJeeves.Spike
{
	// Milestone 1: read rank data through HearthMirror with no HDT involved.
	// Samples once per second and prints ONLY when something changes, so play a
	// ranked game while it runs and stop it with Ctrl+C afterwards.
	// Everything printed is also appended to <repo>/logs/log-spike.log.
	internal static class Program
	{
		private static StreamWriter? _file;

		// Values copied from HearthDb / HDT enums (GameType, FormatType, Mode).
		private static readonly Dictionary<int, string> GameTypes = new Dictionary<int, string>
		{
			[0] = "GT_UNKNOWN", [1] = "GT_VS_AI", [2] = "GT_VS_FRIEND", [4] = "GT_TUTORIAL", [5] = "GT_ARENA",
			[7] = "GT_RANKED", [8] = "GT_CASUAL", [16] = "GT_TAVERNBRAWL", [30] = "GT_BATTLEGROUNDS",
		};
		private static readonly Dictionary<int, string> Formats = new Dictionary<int, string>
		{
			[0] = "FT_UNKNOWN", [1] = "FT_WILD", [2] = "FT_STANDARD", [3] = "FT_CLASSIC", [4] = "FT_TWIST",
		};
		private static readonly Dictionary<int, string> Modes = new Dictionary<int, string>
		{
			[0] = "INVALID", [1] = "STARTUP", [2] = "LOGIN", [3] = "HUB", [4] = "GAMEPLAY", [5] = "COLLECTIONMANAGER",
			[6] = "PACKOPENING", [7] = "TOURNAMENT", [8] = "FRIENDLY", [9] = "FATAL_ERROR", [10] = "DRAFT", [11] = "CREDITS", [12] = "RESET", [13] = "ADVENTURE", [14] = "TAVERN_BRAWL", [15] = "BACON", [16] = "GAME_MODE", [17] = "PVP_DUNGEON_RUN",
		};
		private static readonly string[] MedalFormats = { "Standard", "Wild", "Classic", "Twist" };

		private static int Main()
		{
			var logPath = Path.Combine(FindRepoRoot(), "logs", "log-spike.log");
			Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
			_file = new StreamWriter(logPath, append: true) { AutoFlush = true };

			Out("HSJeeves spike - HearthMirror standalone test (prints on change only; Ctrl+C to stop)");
			Out($"HearthMirror: {typeof(Reflection).Assembly.GetName().Version}");
			Out($"Elevated: {IsElevated()}   Log file: {logPath}");

			Reflection.LogMessage += m => Out($"[mirror] {m}");
			Reflection.Exception += e => Out($"[mirror-exception] {e.Message}");
			Reflection.OnIpcServerExit += c => Out($"[mirror] IPC server exited: {c}");
			Reflection.StdErr += (s, e) => Out($"[mirror-stderr] {e.Data}");

			var stop = false;
			Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop = true; };

			var last = new Dictionary<string, string>();
			var hsWas = (bool?)null;

			try
			{
				while(!stop)
				{
					var hs = Process.GetProcessesByName("Hearthstone").Length > 0;
					if(hsWas != hs)
					{
						Out($"Hearthstone running: {hs}");
						hsWas = hs;
					}

					if(hs)
					{
						var c = Reflection.Client;
						Report(last, "AccountId/Region", () =>
						{
							var id = JObject.FromObject(c.GetAccountId());
							var hi = id.Value<ulong>("Hi");
							var region = (hi >> 32) & 0xFF; // same rule as HDT Helper.GetRegion
							return $"Hi={hi} Lo={id.Value<ulong>("Lo")} -> region byte {region} ({RegionName(region)})";
						});
						Report(last, "BattleTag", () => JsonConvert.SerializeObject(c.GetBattleTag()));
						Report(last, "Scene", () =>
						{
							var s = JObject.FromObject(c.GetSceneMgrState());
							var mode = s.Value<int>("Mode");
							return $"Mode={mode} ({Name(Modes, mode)}) PrevMode={s.Value<int>("PrevMode")} NextMode={s.Value<int>("NextMode")} " +
							       $"SceneLoaded={s.Value<bool>("SceneLoaded")} Transitioning={s.Value<bool>("Transitioning")}";
						});
						Report(last, "GameType", () => { var v = c.GetGameType(); return $"{v} ({Name(GameTypes, (int)v)})"; });
						Report(last, "Format", () => { var v = c.GetFormat(); return $"{v} ({Name(Formats, (int)v)})"; });
						Report(last, "MatchInfo", () => c.GetMatchInfo() is { } m ? JsonConvert.SerializeObject(m) : "null");
						Report(last, "MedalInfo", () =>
						{
							var medal = c.GetMedalInfo();
							if(medal == null)
								return "null";
							var j = JObject.FromObject(medal);
							return string.Join(" | ", MedalFormats.Select(f =>
							{
								var m = j[f];
								return m == null || m.Type == JTokenType.Null
									? $"{f}: null"
									: $"{f}: League={m["LeagueId"]} StarLevel={m["StarLevel"]} Stars={m["Stars"]} Legend={m["LegendRank"]} " +
									  $"Streak={m["Streak"]} Games={m["SeasonGames"]} Wins={m["SeasonWins"]}";
							}));
						});
					}

					Thread.Sleep(1000);
				}
			}
			finally
			{
				Reflection.StopIpcClient();
				Out("stopped.");
				_file.Dispose();
			}
			return 0;
		}

		private static void Report(Dictionary<string, string> last, string label, Func<string> read)
		{
			string text;
			try
			{
				text = read();
			}
			catch(Exception ex)
			{
				text = $"ERROR {ex.GetType().Name}: {ex.Message}";
			}
			if(last.TryGetValue(label, out var prev) && prev == text)
				return;
			last[label] = text;
			Out($"{label}: {text}");
		}

		private static void Out(string line)
		{
			var s = $"{DateTime.Now:HH:mm:ss.fff} {line}";
			Console.WriteLine(s);
			_file?.WriteLine(s);
		}

		// Walks up from the exe to the repo root (the folder holding both "design" and "src").
		private static string FindRepoRoot()
		{
			var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
			while(dir != null)
			{
				if(Directory.Exists(Path.Combine(dir.FullName, "design")) && Directory.Exists(Path.Combine(dir.FullName, "src")))
					return dir.FullName;
				dir = dir.Parent;
			}
			return AppDomain.CurrentDomain.BaseDirectory;
		}

		private static string Name(Dictionary<int, string> map, int v) => map.TryGetValue(v, out var n) ? n : "?";

		private static string RegionName(ulong b) => b switch { 1 => "US", 2 => "EU", 3 => "ASIA", 5 => "CHINA", _ => "UNKNOWN" };

		private static bool IsElevated()
		{
			using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
			return new System.Security.Principal.WindowsPrincipal(id)
				.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
		}
	}
}
