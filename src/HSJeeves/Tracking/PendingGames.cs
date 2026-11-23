using System;
using System.Collections.Generic;

namespace HSJeeves.Tracking
{
	public sealed class ResolvedGame
	{
		public CompletedGame Game { get; set; } = new CompletedGame();
		public bool Conceded { get; set; }
		// true when the wait ran out without the log confirming anything (concede unknown, sent as false)
		public bool TimedOut { get; set; }
	}

	// A finished game waits briefly for the concede line: the log line is written at about the same moment
	// as the rank update, but the file may be flushed a little later. A normal loss has no such line, so it
	// is released as "not conceded" when the wait ends.
	public sealed class PendingGames
	{
		public static readonly TimeSpan Wait = TimeSpan.FromSeconds(6);

		private sealed class Item
		{
			public CompletedGame Game = new CompletedGame();
			public int Baseline;
			public DateTimeOffset Deadline;
		}

		private readonly List<Item> _items = new List<Item>();

		public int Count => _items.Count;

		public void Add(CompletedGame game, int concedeCountAtGameStart, DateTimeOffset now) =>
			_items.Add(new Item { Game = game, Baseline = concedeCountAtGameStart, Deadline = now + Wait });

		public IReadOnlyList<ResolvedGame> Resolve(int currentConcedeCount, DateTimeOffset now)
		{
			var done = new List<ResolvedGame>();
			for(var i = _items.Count - 1; i >= 0; i--)
			{
				var it = _items[i];
				if(currentConcedeCount > it.Baseline)
					done.Add(new ResolvedGame { Game = it.Game, Conceded = true });
				else if(now >= it.Deadline)
					done.Add(new ResolvedGame { Game = it.Game, Conceded = false, TimedOut = true });
				else
					continue;
				_items.RemoveAt(i);
			}
			done.Reverse();
			return done;
		}
	}
}
