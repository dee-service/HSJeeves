using System;
using System.Collections.Generic;

namespace HSJeeves.Tracking
{
	// Detects finished ranked games from memory snapshots only (see design doc).
	// Start = GAMEPLAY scene with ranked game type. End = SeasonGames of the played format +1.
	public sealed class GameTracker
	{
		public static readonly TimeSpan PendingTimeout = TimeSpan.FromMinutes(10);

		private sealed class Current
		{
			public DateTimeOffset Start;
			public int Format;
			public string Player = "";
			public string Opponent = "";
			public Medal Before = new Medal();
			public DateTimeOffset? LeftGameplayAt;
		}

		private Current? _game;

		public bool GameInProgress => _game != null;

		public IReadOnlyList<TrackerEvent> Update(Snapshot s)
		{
			var events = new List<TrackerEvent>();
			if(!s.Valid)
				return events;

			if(_game == null)
			{
				if(s.Scene == Scenes.Gameplay && s.GameType == GameTypes.Ranked && s.Format != 0
					&& s.Medals.TryGetValue(s.Format, out var before))
				{
					_game = new Current { Start = s.Time, Format = s.Format, Before = Clone(before) };
					Cache(s);
				}
				return events;
			}

			var g = _game;
			// GameType/Format/MatchInfo are cleared when the player leaves: only refresh while present.
			if(s.Scene == Scenes.Gameplay)
				g.LeftGameplayAt = null;
			else if(g.LeftGameplayAt == null)
				g.LeftGameplayAt = s.Time;
			Cache(s);

			if(s.Medals.TryGetValue(g.Format, out var now))
			{
				var delta = now.SeasonGames - g.Before.SeasonGames;
				if(delta == 1)
				{
					events.Add(new TrackerEvent { Completed = Build(g, s, now) });
					_game = null;
					return events;
				}
				if(delta > 1 || delta < 0)
				{
					events.Add(new TrackerEvent { Dropped = "SeasonGames changed unexpectedly (" + g.Before.SeasonGames + " -> " + now.SeasonGames + ")" });
					_game = null;
					return events;
				}
			}

			if(g.LeftGameplayAt != null && s.Time - g.LeftGameplayAt.Value >= PendingTimeout)
			{
				events.Add(new TrackerEvent { Dropped = "No rank update within " + (int)PendingTimeout.TotalMinutes + " min after leaving the game" });
				_game = null;
			}
			return events;
		}

		private void Cache(Snapshot s)
		{
			if(_game == null)
				return;

			if(s.HasMatchInfo)
			{
				if(!string.IsNullOrEmpty(s.PlayerBattleTag)) _game.Player = s.PlayerBattleTag!;
				if(!string.IsNullOrEmpty(s.OpponentBattleTag)) _game.Opponent = s.OpponentBattleTag!;
			}
			else if(string.IsNullOrEmpty(_game.Player) && !string.IsNullOrEmpty(s.PlayerBattleTag))
				_game.Player = s.PlayerBattleTag!;
		}

		private static CompletedGame Build(Current g, Snapshot s, Medal after) => new CompletedGame
		{
			GameId = Guid.NewGuid(),
			StartTime = g.Start,
			EndTime = s.Time > g.Start ? s.Time : g.Start.AddSeconds(1),
			Format = g.Format,
			Won = after.SeasonWins - g.Before.SeasonWins >= 1,
			PlayerBattleTag = g.Player,
			OpponentBattleTag = g.Opponent,
			Region = s.Region,
			Before = g.Before,
			After = Clone(after),
		};

		private static Medal Clone(Medal m) => new Medal
		{
			LeagueId = m.LeagueId, StarLevel = m.StarLevel, Stars = m.Stars,
			LegendRank = m.LegendRank, SeasonGames = m.SeasonGames, SeasonWins = m.SeasonWins,
		};
	}
}
