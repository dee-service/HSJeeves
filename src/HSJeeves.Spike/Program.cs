using System;
using System.Diagnostics;
using System.Threading;
using HearthMirror;
using HearthMirror.Objects;
using Newtonsoft.Json;

namespace HSJeeves.Spike
{
	// Milestone 1: read rank data through HearthMirror with no HDT involved.
	// Run while Hearthstone is open (ideally also with HDT open, to test coexistence).
	internal static class Program
	{
		private static int Main()
		{
			Console.WriteLine("HSJeeves spike - HearthMirror standalone test");
			Console.WriteLine($"HearthMirror: {typeof(Reflection).Assembly.GetName().Version}");

			Reflection.LogMessage += m => Console.WriteLine($"[mirror] {m}");
			Reflection.Exception += e => Console.WriteLine($"[mirror-exception] {e.Message}");
			Reflection.OnIpcServerExit += c => Console.WriteLine($"[mirror] IPC server exited: {c}");
			Reflection.StdErr += (s, e) => Console.WriteLine($"[mirror-stderr] {e.Data}");

			try
			{
				for(var i = 1; i <= 5; i++)
				{
					Console.WriteLine($"\n--- sample {i}/5 ---");
					var hs = Process.GetProcessesByName("Hearthstone");
					Console.WriteLine($"Hearthstone running: {hs.Length > 0}");

					var c = Reflection.Client;
					Dump("AccountId", () => c.GetAccountId());
					Dump("BattleTag", () => c.GetBattleTag());
					Dump("SceneMgrState", () => c.GetSceneMgrState());
					Dump("GameType", () => c.GetGameType());
					Dump("Format", () => c.GetFormat());
					Dump("MatchInfo", () => c.GetMatchInfo());
					Dump("MedalInfo", () => c.GetMedalInfo());
					Thread.Sleep(2000);
				}
			}
			finally
			{
				Reflection.StopIpcClient();
			}
			return 0;
		}

		private static void Dump<T>(string label, Func<T> read)
		{
			try
			{
				var v = read();
				Console.WriteLine($"{label}: {(v == null ? "null" : JsonConvert.SerializeObject(v))}");
			}
			catch(Exception ex)
			{
				Console.WriteLine($"{label}: ERROR {ex.GetType().Name}: {ex.Message}");
			}
		}
	}
}
