using Xunit;

namespace HSJeeves.Tests
{
	public class RegionTests
	{
		// Value read by the spike from a real EU account (docs/open-questions.md, C5).
		[Fact]
		public void RealEuAccountIsEu() => Assert.Equal("EU", Region.FromAccountHi(144115198130930503UL));

		// Value read by the spike on 2026-11-23 after switching the same BattleTag to the US region.
		[Fact]
		public void RealUsAccountIsUs() => Assert.Equal("US", Region.FromAccountHi(144115193835963207UL));

		[Theory]
		[InlineData(1UL << 32, "US")]
		[InlineData(2UL << 32, "EU")]
		[InlineData(3UL << 32, "AP")]
		[InlineData(0UL, "UNKNOWN")]
		public void MapsRegionByte(ulong hi, string expected) => Assert.Equal(expected, Region.FromAccountHi(hi));
	}
}
