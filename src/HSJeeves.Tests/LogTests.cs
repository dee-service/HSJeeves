using System;
using System.IO;
using Xunit;

namespace HSJeeves.Tests
{
	public class LogTests
	{
		[Fact]
		public void DeleteOld_RemovesFilesOlderThanRetention_KeepsOthers()
		{
			var dir = Path.Combine(Path.GetTempPath(), "hsjeeves-logtest-" + Guid.NewGuid());
			Directory.CreateDirectory(dir);
			var today = new DateTime(2026, 11, 23);
			foreach(var n in new[] { "2026-11-23", "2026-11-17", "2026-11-16", "2026-11-01" })
				File.WriteAllText(Path.Combine(dir, "hsjeeves-" + n + ".log"), "x");
			File.WriteAllText(Path.Combine(dir, "hsjeeves-notadate.log"), "x");

			Log.DeleteOld(dir, today, 7);

			var left = Directory.GetFiles(dir);
			Assert.Equal(3, left.Length); // today, 6 days old, and the unrecognised name are kept; 7+ days old removed
			Assert.DoesNotContain(left, f => f.Contains("2026-11-16") || f.Contains("2026-11-01"));
			Assert.Contains(left, f => f.Contains("notadate"));
		}
	}
}
