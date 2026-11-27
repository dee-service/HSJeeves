using System;
using System.Diagnostics;
using System.Linq;
using HearthMirror;
using Newtonsoft.Json.Linq;

namespace HSJeeves.Tracking
{
	public interface ISnapshotSource
	{
		Snapshot Read(DateTimeOffset now);
	}

	// Live reader: HearthMirror -> Snapshot. A failed read gives Valid=false, never "game ended".
	public sealed class RankReader : ISnapshotSource
	{
		// A reader attached while Hearthstone is still loading can stay empty forever without throwing,
		// so until a read succeeds the attachment is dropped and redone every RetryAfter.
		private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(3);
		private int _pid;
		private DateTimeOffset _attachedAt;

		public Snapshot Read(DateTimeOffset now)
		{
			try
			{
				var p = Process.GetProcessesByName("Hearthstone").FirstOrDefault();
				if(p == null)
				{
					Reset();
					return new Snapshot { Time = now, Valid = false };
				}
				if(p.Id != _pid)
				{
					// New Hearthstone process (started after HSJeeves, or restarted): never reuse an old attachment.
					Reset();
					_pid = p.Id;
					_attachedAt = now;
				}

				var c = Reflection.Client;
				var snap = SnapshotBuilder.Build(now,
					ToJson(c.GetAccountId()), ToJson(c.GetBattleTag()), ToJson(c.GetSceneMgrState()),
					(int)c.GetGameType(), (int)c.GetFormat(), ToJson(c.GetMatchInfo()), ToJson(c.GetMedalInfo()));
				if(snap.Valid)
					_attachedAt = DateTimeOffset.MaxValue; // confirmed: keep this attachment
				else
					RetryIfStale(now);
				return snap;
			}
			catch(Exception)
			{
				// HearthMirror throws (RuntimeBinderException etc.) while Hearthstone is loading.
				RetryIfStale(now);
				return new Snapshot { Time = now, Valid = false };
			}
		}

		private void RetryIfStale(DateTimeOffset now)
		{
			if(now - _attachedAt < RetryAfter)
				return;
			Reflection.StopIpcClient();
			_attachedAt = now;
		}

		private void Reset()
		{
			if(_pid != 0)
				Reflection.StopIpcClient();
			_pid = 0;
			_attachedAt = DateTimeOffset.MinValue;
		}

		private static JObject? ToJson(object? o) => o == null ? null : JObject.FromObject(o);
	}
}
