using System;
using System.Threading;
using HSJeeves.Upload;

namespace HSJeeves.Tracking
{
	// Polls the reader once per second, feeds the tracker, queues finished games.
	public sealed class TrackerService : IDisposable
	{
		private readonly ISnapshotSource _source;
		private readonly GameTracker _tracker = new GameTracker();
		private readonly UploadQueue _queue;
		private readonly Timer _timer;
		private DateTimeOffset _lastFlush = DateTimeOffset.MinValue;
		private bool _wasInGame;
		private bool? _readable;
		private int _busy;

		public TrackerService(ISnapshotSource source, UploadQueue queue)
		{
			_source = source;
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
				var snap = _source.Read(now);
				if(_readable != snap.Valid)
				{
					_readable = snap.Valid;
					Log.Info(snap.Valid ? "Hearthstone readable (region " + snap.Region + ", " + snap.PlayerBattleTag + ")" : "Hearthstone not readable (not running or loading)");
				}
				foreach(var e in _tracker.Update(snap))
				{
					if(e.Completed != null)
					{
						var g = e.Completed;
						Log.Info("game finished: " + PayloadBuilder.FormatName(g.Format) + " " + (g.Won ? "Win" : "Loss") + " vs " + g.OpponentBattleTag);
						_queue.Enqueue(PayloadBuilder.Build(g, wasConceded: false)); // ConcedeWatcher comes later
						_queue.Flush();
					}
					else if(e.Dropped != null)
						Log.Warn("game dropped: " + e.Dropped);
				}
				if(_tracker.GameInProgress != _wasInGame)
				{
					_wasInGame = _tracker.GameInProgress;
					if(_wasInGame) Log.Info("ranked game started");
				}
				if(now - _lastFlush > TimeSpan.FromMinutes(1))
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
