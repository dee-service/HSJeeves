using System;
using System.Collections.Generic;

namespace HSJeeves.Tracking
{
	public static class GameTypes { public const int Ranked = 7; }
	public static class Scenes { public const int Gameplay = 4; }
	public static class Formats { public const int Wild = 1, Standard = 2, Classic = 3, Twist = 4; }

	// Rank values of one format, as read from MedalInfo.
	public sealed class Medal
	{
		public int LeagueId { get; set; }
		public int StarLevel { get; set; }
		public int Stars { get; set; }
		public int LegendRank { get; set; }
		public int SeasonGames { get; set; }
		public int SeasonWins { get; set; }
		// Current win streak. HSMagic's GetMedalInfo() names it "Streak" (not "WinStreak").
		public int WinStreak { get; set; }
	}

	// One sample of everything the tracker needs. Built by the reader, faked in tests.
	public sealed class Snapshot
	{
		public DateTimeOffset Time { get; set; }
		// false when Hearthstone is not running or the read failed: never means "game ended".
		public bool Valid { get; set; } = true;
		public int Scene { get; set; }
		public int GameType { get; set; }
		public int Format { get; set; }
		public string? PlayerBattleTag { get; set; }
		public string? OpponentBattleTag { get; set; }
		public bool HasMatchInfo { get; set; }
		public string Region { get; set; } = "UNKNOWN";
		// Hi:Lo of the logged-in account. Ranks are per account, so a change means a different player/region.
		public string AccountKey { get; set; } = "";
		public Dictionary<int, Medal> Medals { get; set; } = new Dictionary<int, Medal>();
	}

	public sealed class CompletedGame
	{
		public Guid GameId { get; set; }
		public DateTimeOffset StartTime { get; set; }
		public DateTimeOffset EndTime { get; set; }
		public int Format { get; set; }
		public bool Won { get; set; }
		public string PlayerBattleTag { get; set; } = "";
		public string OpponentBattleTag { get; set; } = "";
		public string Region { get; set; } = "UNKNOWN";
		public Medal Before { get; set; } = new Medal();
		public Medal After { get; set; } = new Medal();
	}

	public sealed class TrackerEvent
	{
		public CompletedGame? Completed { get; set; }
		public string? Dropped { get; set; }
	}
}
