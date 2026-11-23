using System;
using HSJeeves.Tracking;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HSJeeves.Tests
{
	// Values copied from logs/log-spike.log (real HearthMirror output).
	public class SnapshotBuilderTests
	{
		private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
		private static readonly JObject Account = JObject.Parse(@"{""Hi"":144115198130930503,""Lo"":52368826}");
		private static readonly JObject Tag = JObject.Parse(@"{""Name"":""Deesnow"",""Number"":""2794""}");
		private static readonly JObject Scene = JObject.Parse(@"{""Mode"":4,""PrevMode"":7}");
		private static readonly JObject Match = JObject.Parse(@"{""LocalPlayer"":{""BattleTag"":{""Name"":""Deesnow"",""Number"":""2794""}},""OpposingPlayer"":{""BattleTag"":{""Name"":""operator36"",""Number"":""2161""}}}");
		private static readonly JObject Medal = JObject.Parse(@"{
			""Standard"":{""LeagueId"":5,""StarLevel"":41,""Stars"":2,""LegendRank"":0,""SeasonGames"":93,""SeasonWins"":43},
			""Wild"":{""LeagueId"":5,""StarLevel"":51,""Stars"":0,""LegendRank"":907,""SeasonGames"":195,""SeasonWins"":104},
			""Classic"":null}");

		[Fact]
		public void BuildsFullSnapshot()
		{
			var s = SnapshotBuilder.Build(T0, Account, Tag, Scene, 7, 2, Match, Medal);
			Assert.True(s.Valid);
			Assert.Equal(4, s.Scene);
			Assert.Equal("EU", s.Region);
			Assert.Equal("Deesnow#2794", s.PlayerBattleTag);
			Assert.Equal("operator36#2161", s.OpponentBattleTag);
			Assert.Equal(93, s.Medals[Formats.Standard].SeasonGames);
			Assert.Equal(907, s.Medals[Formats.Wild].LegendRank);
			Assert.False(s.Medals.ContainsKey(Formats.Classic));
		}

		[Fact]
		public void MissingSceneOrAccount_IsInvalid()
		{
			Assert.False(SnapshotBuilder.Build(T0, Account, Tag, null, 0, 0, null, null).Valid);
			Assert.False(SnapshotBuilder.Build(T0, null, Tag, Scene, 0, 0, null, null).Valid);
		}

		[Fact]
		public void NoMatchInfo_HasNoOpponent()
		{
			var s = SnapshotBuilder.Build(T0, Account, Tag, Scene, 0, 0, null, Medal);
			Assert.False(s.HasMatchInfo);
			Assert.Null(s.OpponentBattleTag);
			Assert.Equal("Deesnow#2794", s.PlayerBattleTag);
		}
	}
}
