using HSJeeves.Tracking;
using Newtonsoft.Json.Linq;

namespace HSJeeves.Upload
{
	// CompletedGame -> payload of the existing backend contract (payload-definition.md, schemaVersion 1).
	public static class PayloadBuilder
	{
		public static JObject Build(CompletedGame g, bool wasConceded) => new JObject
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
