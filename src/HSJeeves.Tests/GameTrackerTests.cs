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
			int games = 93, int wins = 43, int fmtForMedal = Formats.Standard, int streak = 0)
		{
			var s = new Snapshot
			{
				Time = T0.AddSeconds(sec), Scene = scene, GameType = type, Format = format, HasMatchInfo = match,
				PlayerBattleTag = match ? "Deesnow#2794" : null, OpponentBattleTag = match ? "operator36#2161" : null,
				Region = "EU",
			};
			s.Medals[fmtForMedal] = new Medal { LeagueId = 5, StarLevel = 41, Stars = 2, SeasonGames = games, SeasonWins = wins, WinStreak = streak };
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
		public void Win_CarriesWinStreakBeforeAndAfter()
		{
			var t = new GameTracker();
			var g = Run(t,
				Snap(10, 4, 7, 2, true, streak: 2),
				Snap(200, 4, 7, 2, true, 94, 44, streak: 3));
			Assert.NotNull(g);
			Assert.Equal(2, g!.Before.WinStreak);
			Assert.Equal(3, g.After.WinStreak);
		}

		[Fact]
		public void Loss_ResetsWinStreakToZero()
		{
			var t = new GameTracker();
			var g = Run(t,
				Snap(10, 4, 7, 2, true, streak: 2),
				Snap(200, 4, 7, 2, true, 94, 43, streak: 0));
			Assert.NotNull(g);
			Assert.False(g!.Won);
			Assert.Equal(2, g.Before.WinStreak);
			Assert.Equal(0, g.After.WinStreak);
		}

		[Fact]
		public void WinStreak_IsTakenFromTheSnapshotThatCompletesTheGame()
		{
			// The streak read together with SeasonGames+1 is the post-game value; an earlier snapshot is not used.
			var t = new GameTracker();
			var g = Run(t,
				Snap(10, 4, 7, 2, true, streak: 2),
				Snap(100, 4, 7, 2, true, streak: 2),
				Snap(200, 4, 7, 2, true, 94, 44, streak: 3),
				Snap(210, 4, 7, 2, true, 94, 44, streak: 3));
			Assert.Equal(3, g!.After.WinStreak);
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

		private static Snapshot WithAccount(Snapshot s, string region, string key)
		{
			s.Region = region;
			s.AccountKey = key;
			return s;
		}

		[Theory]
		[InlineData("US")]
		[InlineData("EU")]
		[InlineData("AP")]
		public void GameCarriesTheRegionOfItsAccount(string region)
		{
			var t = new GameTracker();
			var g = Run(t,
				WithAccount(Snap(10, 4, 7, 2, true), region, "acc"),
				WithAccount(Snap(200, 4, 7, 2, true, 94, 44), region, "acc"));
			Assert.NotNull(g);
			Assert.Equal(region, g!.Region);
		}

		[Fact]
		public void RegionSwitchWhileGamePending_DropsTheGame()
		{
			var t = new GameTracker();
			Run(t, WithAccount(Snap(10, 4, 7, 2, true), "EU", "eu-acc"));
			// Hearthstone restarted on the US account; its medals must not be compared with the EU game.
			var ev = t.Update(WithAccount(Snap(100, 3, 0, 0, false, 94, 44), "US", "us-acc"));
			Assert.Single(ev);
			Assert.Null(ev[0].Completed);
			Assert.Contains("Account changed", ev[0].Dropped);
			Assert.False(t.GameInProgress);
		}

		[Fact]
		public void GamesInDifferentRegions_EachKeepTheirOwnRegionAndMedals()
		{
			var t = new GameTracker();
			var eu = Run(t,
				WithAccount(Snap(10, 4, 7, 2, true, 93, 43), "EU", "eu-acc"),
				WithAccount(Snap(200, 4, 7, 2, true, 94, 44), "EU", "eu-acc"));
			t.Update(WithAccount(Snap(300, 7), "EU", "eu-acc")); // leaves the end screen, restarts on the US account
			var us = Run(t,
				WithAccount(Snap(1000, 4, 7, 2, true, 10, 5), "US", "us-acc"),
				WithAccount(Snap(1200, 4, 7, 2, true, 11, 5), "US", "us-acc"));
			Assert.Equal("EU", eu!.Region);
			Assert.True(eu.Won);
			Assert.Equal("US", us!.Region);
			Assert.False(us.Won);
			Assert.Equal(10, us.Before.SeasonGames);
		}

		// Real behaviour on 2026-11-23: after the rank update the player stays on the defeat screen
		// (scene GAMEPLAY, game type still ranked) until clicking away.
		[Fact]
		public void EndScreenAfterFinishedGame_IsNotANewGame()
		{
			var t = new GameTracker();
			var g = Run(t, Snap(10, 4, 7, 2, true), Snap(200, 4, 7, 2, true, 94, 43));
			Assert.NotNull(g);
			Assert.Empty(t.Update(Snap(201, 4, 7, 2, true, 94, 43)));
			Assert.Empty(t.Update(Snap(215, 4, 7, 2, true, 94, 43)));
			Assert.False(t.GameInProgress);

			// Leaving the game screen re-arms the tracker; the next ranked game is tracked normally.
			t.Update(Snap(220, 7));
			t.Update(Snap(300, 4, 7, 2, true, 94, 43));
			Assert.True(t.GameInProgress);
			var g2 = Run(t, Snap(400, 4, 7, 2, true, 95, 44));
			Assert.NotNull(g2);
			Assert.Equal(94, g2!.Before.SeasonGames);
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
