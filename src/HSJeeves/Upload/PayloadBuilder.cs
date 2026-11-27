using HSJeeves.Tracking;
using Newtonsoft.Json.Linq;

namespace HSJeeves.Upload
{
	// CompletedGame -> payload of the backend contract (jeeves-api docs/payload-definition.md).
	// schemaVersion 2 adds the season stats (top-level seasonWins / seasonGames / winStreak); a game whose
	// stats are missing or implausible is sent as schemaVersion 1 without them (the backend accepts both).
	public static class PayloadBuilder
	{
		public static JObject Build(CompletedGame g, bool wasConceded)
		{
			var p = BuildV1(g, wasConceded);
			if(HasSeasonStats(g))
			{
				p["schemaVersion"] = 2;
				p["seasonWins"] = g.After.SeasonWins;
				p["seasonGames"] = g.After.SeasonGames;
				p["winStreak"] = g.After.WinStreak;
			}
			return p;
		}

		// The values come from the snapshot that completed the game (SeasonGames + 1), so they are post-game.
		// Sanity check before trusting them: a win must leave a streak of at least 1, a loss exactly 0.
		// A reader that does not provide Streak yields 0, which fails the check for a win and so degrades to v1.
		public static bool HasSeasonStats(CompletedGame g)
		{
			var a = g.After;
			return a.SeasonGames >= 1
				&& a.SeasonWins >= 0 && a.SeasonWins <= a.SeasonGames
				&& (g.Won ? a.WinStreak >= 1 : a.WinStreak == 0);
		}

		private static JObject BuildV1(CompletedGame g, bool wasConceded) => new JObject
		{
			["schemaVersion"] = 1,
			["gameId"] = g.GameId.ToString(),
			["startTime"] = g.StartTime.ToString("yyyy-MM-ddTHH:mm:sszzz"),
			["endTime"] = g.EndTime.ToString("yyyy-MM-ddTHH:mm:sszzz"),
			["gameMode"] = "Ranked",
			["format"] = FormatName(g.Format),
			["result"] = g.Won ? "Win" : "Loss",
			["wasConceded"] = wasConceded,
			["region"] = g.Region,
			["playerBattleTag"] = g.PlayerBattleTag,
			["opponentBattleTag"] = g.OpponentBattleTag,
			["rank"] = new JObject
			{
				["LeagueId"] = g.Before.LeagueId,
				["Rank"] = 0,
				["StarLevel"] = g.Before.StarLevel,
				["Stars"] = g.Before.Stars,
				["LegendRank"] = g.Before.LegendRank,
			},
			["rankAfter"] = new JObject
			{
				["StarLevelAfter"] = g.After.StarLevel,
				["StarsAfter"] = g.After.Stars,
				["LegendRankAfter"] = g.After.LegendRank,
			},
		};

		public static string FormatName(int f)
		{
			switch(f)
			{
				case Formats.Wild: return "Wild";
				case Formats.Standard: return "Standard";
				case Formats.Classic: return "Classic";
				case Formats.Twist: return "Twist";
				default: return "Standard";
			}
		}
	}
}
