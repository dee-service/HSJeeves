using System;
using System.Threading;
using HSJeeves.Upload;

namespace HSJeeves.Tracking
{
	// Polls the reader once per second, feeds the tracker, queues finished games.
	public sealed class TrackerService : IDisposable
	{
		private readonly ISnapshotSource _source;
		private readonly ConcedeWatcher _concede;
		private readonly GameTracker _tracker = new GameTracker();
		private readonly PendingGames _pending = new PendingGames();
		private readonly UploadQueue _queue;
		private readonly Timer _timer;
		private DateTimeOffset _lastFlush = DateTimeOffset.MinValue;
		private bool _wasInGame;
		private bool? _readable;
		private int _concedeBaseline;
		private int _busy;

		// For the tray menu (read from the UI thread).
		public volatile string HearthstoneStatus = "Hearthstone: checking...";
		public volatile string LastGame = "No game yet";
		public volatile bool InGame;
		public UploadQueue Queue => _queue;

		public TrackerService(ISnapshotSource source, ConcedeWatcher concede, UploadQueue queue)
		{
			_source = source;
			_concede = concede;
			_queue = queue;
			_timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
		}

		private void Tick()
		{
			if(Interlocked.Exchange(ref _busy, 1) == 1)
				return;
			try
			{
				var now = DateTimeOffset.Now;
				_concede.Poll();
				var snap = _source.Read(now);
				if(_readable != snap.Valid)
				{
					_readable = snap.Valid;
					HearthstoneStatus = snap.Valid ? "Hearthstone: connected (" + snap.Region + ")" : "Hearthstone: not running";
					Log.Info(snap.Valid ? "Hearthstone readable (region " + snap.Region + ", " + snap.PlayerBattleTag + ")" : "Hearthstone not readable (not running or loading)");
				}
				foreach(var e in _tracker.Update(snap))
				{
					if(e.Completed != null)
					{
						var g = e.Completed;
						Log.Info("game finished: " + PayloadBuilder.FormatName(g.Format) + " " + (g.Won ? "Win" : "Loss") + " vs " + g.OpponentBattleTag);
						_pending.Add(g, _concedeBaseline, now);
					}
					else if(e.Dropped != null)
						Log.Warn("game dropped: " + e.Dropped);
				}
				if(_tracker.GameInProgress != _wasInGame)
				{
					_wasInGame = _tracker.GameInProgress;
					InGame = _wasInGame;
					if(_wasInGame)
					{
						_concedeBaseline = _concede.Count;
						Log.Info("ranked game started");
					}
				}

				var queued = false;
				foreach(var r in _pending.Resolve(_concede.Count, now))
				{
					if(r.TimedOut && !_concede.Available)
						Log.Warn("Power.log not available: wasConceded sent as false for game " + r.Game.GameId);
					Log.Info("game " + r.Game.GameId + " wasConceded=" + r.Conceded);
					LastGame = PayloadBuilder.FormatName(r.Game.Format) + " " + (r.Game.Won ? "win" : "loss") + (r.Conceded ? " (conceded)" : "")
						+ " at " + r.Game.EndTime.ToString("HH:mm");
					if(!PayloadBuilder.HasSeasonStats(r.Game))
						Log.Warn("season stats missing or inconsistent (wins " + r.Game.After.SeasonWins + ", games "
							+ r.Game.After.SeasonGames + ", streak " + r.Game.After.WinStreak + "): game " + r.Game.GameId + " uploaded without them");
					_queue.Enqueue(PayloadBuilder.Build(r.Game, r.Conceded));
					queued = true;
				}
				if(queued || now - _lastFlush > TimeSpan.FromMinutes(1))
				{
					_lastFlush = now;
					_queue.Flush();
				}
			}
			catch(Exception ex)
			{
				Log.Warn("tracker tick failed: " + ex);
			}
			finally
			{
				Interlocked.Exchange(ref _busy, 0);
			}
		}

		public void Dispose() => _timer.Dispose();
	}
}
