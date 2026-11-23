using System;
using System.Diagnostics;
using HearthMirror;
using Newtonsoft.Json.Linq;

namespace HSJeeves.Tracking
{
	public interface ISnapshotSource
	{
		Snapshot Read(DateTimeOffset now);
	}

	// Live reader: HearthMirror -> Snapshot. A failed read gives Valid=false, never "game ended".
	public sealed class RankReader : ISnapshotSource
	{
		public Snapshot Read(DateTimeOffset now)
		{
			try
			{
				if(Process.GetProcessesByName("Hearthstone").Length == 0)
					return new Snapshot { Time = now, Valid = false };

				var c = Reflection.Client;
				return SnapshotBuilder.Build(now,
					ToJson(c.GetAccountId()), ToJson(c.GetBattleTag()), ToJson(c.GetSceneMgrState()),
					(int)c.GetGameType(), (int)c.GetFormat(), ToJson(c.GetMatchInfo()), ToJson(c.GetMedalInfo()));
			}
			catch(Exception)
			{
				// HearthMirror throws (RuntimeBinderException etc.) while Hearthstone is loading.
				return new Snapshot { Time = now, Valid = false };
			}
		}

		private static JObject? ToJson(object? o) => o == null ? null : JObject.FromObject(o);
	}
}
