using System;
using System.IO;
using System.Runtime.CompilerServices;

// The tests share one data folder (queue, settings, logs), so test classes must not run in parallel.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace System.Runtime.CompilerServices
{
	// Polyfill: net472 does not ship this attribute, the compiler only needs the type to exist.
	[AttributeUsage(AttributeTargets.Method, Inherited = false)]
	internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace HSJeeves.Tests
{
	internal static class TestEnvironment
	{
		// AppPaths.Data is read once per process, so this must run before any test code touches it.
		// Without it a test can fall back to the real %APPDATA%\HSJeeves folder (queue, settings, logs).
		[ModuleInitializer]
		internal static void RedirectDataFolder()
		{
			Environment.SetEnvironmentVariable("HSJEEVES_DATA", Path.Combine(Path.GetTempPath(), "hsjeeves-tests-" + Guid.NewGuid()));
		}
	}
}
