using System;
using System.Linq;
using HSJeeves.Tracking;
using Xunit;

namespace HSJeeves.Tests
{
	public class GameTrackerTests
	{
		private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.FromHours(1));

		private static Snapshot Snap(double sec, int scene, int type = 0, int format = 0, bool match = false,
			int games = 93, int wins = 43, int fmtForMedal = Formats.Standard)
		{
			var s = new Snapshot
			{
				Time = T0.AddSeconds(sec), Scene = scene, GameType = type, Format = format, HasMatchInfo = match,
				PlayerBattleTag = match ? "Deesnow#2794" : null, OpponentBattleTag = match ? "operator36#2161" : null,
				Region = "EU",
			};
			s.Medals[fmtForMedal] = new Medal { LeagueId = 5, StarLevel = 41, Stars = 2, SeasonGames = games, SeasonWins = wins };
			return s;
		}

		private static CompletedGame? Run(GameTracker t, params Snapshot[] snaps)
		{
			CompletedGame? done = null;
			foreach(var s in snaps)
				foreach(var e in t.Update(s))
					done = e.Completed ?? done;
			return done;
		}

		[Fact]
		public void RankedWin_IsDetectedWhenSeasonGamesIncreases()
		{
			var t = new GameTracker();
			var g = Run(t,
				Snap(0, 3), Snap(10, 4, 7, 2), Snap(14, 4, 7, 2, true), Snap(300, 4, 7, 2, true),
				Snap(310, 4, 7, 2, true, 94, 44), Snap(322, 7));
			Assert.NotNull(g);
			Assert.True(g!.Won);
			Assert.Equal("operator36#2161", g.OpponentBattleTag);
			Assert.Equal("EU", g.Region);
			Assert.Equal(93, g.Before.SeasonGames);
			Assert.Equal(94, g.After.SeasonGames);
			Assert.True(g.EndTime > g.StartTime);
			Assert.False(t.GameInProgress);
		}

		[Fact]
		public void RankedLoss_WinsUnchanged()
		{
			var t = new GameTracker();
			var g = Run(t, Snap(10, 4, 7, 2, true), Snap(200, 4, 7, 2, true, 94, 43));
			Assert.NotNull(g);
			Assert.False(g!.Won);
		}

		[Fact]
		public void Casual_IsIgnored()
		{
			var t = new GameTracker();
			var g = Run(t, Snap(10, 4, 8, 1, true), Snap(200, 4, 8, 1, true, 94, 44), Snap(210, 7));
			Assert.Null(g);
			Assert.False(t.GameInProgress);
		}

		[Fact]
		public void OnlyPlayedFormatIsCompared()
		{
			var t = new GameTracker();
			var s1 = Snap(10, 4, 7, 2, true);
			var s2 = Snap(200, 4, 7, 2, true);
			s2.Medals[Formats.Wild] = new Medal { SeasonGames = 196 }; // Wild drifts, Standard unchanged
			Assert.Null(Run(t, s1, s2));
			Assert.True(t.GameInProgress);
		}

		[Fact]
		public void ClearedGameTypeAfterLeaving_DoesNotLoseTheGame()
		{
			var t = new GameTracker();
			var g = Run(t, Snap(10, 4, 7, 2, true), Snap(300, 7, 0, 0), Snap(305, 7, 0, 0, false, 94, 44));
			Assert.NotNull(g);
			Assert.Equal("operator36#2161", g!.OpponentBattleTag);
		}

		[Fact]
		public void FailedReads_AreNotAGameEnd()
		{
			var t = new GameTracker();
			Run(t, Snap(10, 4, 7, 2, true));
			var bad = new Snapshot { Time = T0.AddSeconds(50), Valid = false };
			Assert.Empty(t.Update(bad));
			Assert.True(t.GameInProgress);
		}

		[Fact]
		public void NetworkCut_KeepsPendingThenCompletesIfRankArrives()
		{
			var t = new GameTracker();
			Run(t, Snap(10, 4, 7, 2, true), Snap(100, 9, 0, 0));
			Assert.True(t.GameInProgress);
			var g = Run(t, Snap(400, 7, 0, 0, false, 94, 43));
			Assert.NotNull(g);
		}

		[Fact]
		public void NoRankUpdate_DropsAfterTimeout()
		{
			var t = new GameTracker();
			Run(t, Snap(10, 4, 7, 2, true), Snap(100, 9, 0, 0));
			Assert.Empty(t.Update(Snap(100 + 9 * 60, 7, 0, 0)));
			var ev = t.Update(Snap(100 + 10 * 60, 7, 0, 0));
			Assert.Single(ev);
			Assert.NotNull(ev[0].Dropped);
			Assert.False(t.GameInProgress);
		}

		[Fact]
		public void CrashThenRestartedGame_StartsFresh()
		{
			var t = new GameTracker();
			Run(t, Snap(10, 4, 7, 2, true));
			// Hearthstone killed: unreadable, then the app is back at login and a new game starts.
			t.Update(new Snapshot { Time = T0.AddSeconds(60), Valid = false });
			var ev = t.Update(Snap(120, 3));
			Assert.Empty(ev);
		}

		[Fact]
		public void AppStartedMidGame_PicksUpTheGame()
		{
			var t = new GameTracker();
			var g = Run(t, Snap(0, 4, 7, 2, true), Snap(120, 4, 7, 2, true, 94, 44));
			Assert.NotNull(g);
			Assert.True(g!.Won);
		}

		[Fact]
		public void SeasonGamesJumpsByMoreThanOne_IsDropped()
		{
			var t = new GameTracker();
			Run(t, Snap(10, 4, 7, 2, true));
			var ev = t.Update(Snap(100, 4, 7, 2, true, 96, 45));
			Assert.Single(ev);
			Assert.NotNull(ev[0].Dropped);
		}
	}
}
