using System;
using Newtonsoft.Json.Linq;

namespace HSJeeves.Tracking
{
	// Pure conversion of HearthMirror values (as JSON) into a Snapshot. No process access here.
	public static class SnapshotBuilder
	{
		private static readonly string[] MedalNames = { null!, "Wild", "Standard", "Classic", "Twist" }; // index = Format value

		public static Snapshot Build(DateTimeOffset now, JObject? account, JObject? battleTag, JObject? scene,
			int gameType, int format, JObject? matchInfo, JObject? medalInfo)
		{
			// Scene/account are null while Hearthstone is starting or after a failed read: not usable.
			if(scene == null || account == null)
				return new Snapshot { Time = now, Valid = false };

			var s = new Snapshot
			{
				Time = now,
				Scene = scene.Value<int?>("Mode") ?? 0,
				GameType = gameType,
				Format = format,
				Region = Region.FromAccountHi(account.Value<ulong?>("Hi") ?? 0),
				AccountKey = (account.Value<ulong?>("Hi") ?? 0) + ":" + (account.Value<ulong?>("Lo") ?? 0),
				PlayerBattleTag = Tag(battleTag),
				HasMatchInfo = matchInfo != null,
			};
			if(matchInfo != null)
			{
				s.PlayerBattleTag = Tag(matchInfo["LocalPlayer"]?["BattleTag"] as JObject) ?? s.PlayerBattleTag;
				s.OpponentBattleTag = Tag(matchInfo["OpposingPlayer"]?["BattleTag"] as JObject);
			}
			if(medalInfo != null)
			{
				for(var f = 1; f < MedalNames.Length; f++)
				{
					if(!(medalInfo[MedalNames[f]] is JObject m))
						continue;
					s.Medals[f] = new Medal
					{
						LeagueId = m.Value<int?>("LeagueId") ?? 0,
						StarLevel = m.Value<int?>("StarLevel") ?? 0,
						Stars = m.Value<int?>("Stars") ?? 0,
						LegendRank = m.Value<int?>("LegendRank") ?? 0,
						SeasonGames = m.Value<int?>("SeasonGames") ?? 0,
						SeasonWins = m.Value<int?>("SeasonWins") ?? 0,
						WinStreak = m.Value<int?>("Streak") ?? 0,
					};
				}
			}
			return s;
		}

		private static string? Tag(JObject? t)
		{
			var name = t?.Value<string>("Name");
			return string.IsNullOrEmpty(name) ? null : name + "#" + t!.Value<string>("Number");
		}
	}
}
