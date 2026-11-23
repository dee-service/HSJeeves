using System.Windows.Forms;
using Microsoft.Win32;

namespace HSJeeves
{
	// "Start with Windows": a per-user Run key (no admin rights needed).
	public static class Autostart
	{
		private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
		private const string ValueName = "HSJeeves";

		public static void Apply(bool enabled)
		{
			try
			{
				using(var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
				{
					if(key == null)
						return;
					if(enabled)
						key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
					else if(key.GetValue(ValueName) != null)
						key.DeleteValue(ValueName);
				}
			}
			catch(System.Exception ex)
			{
				Log.Warn("could not change autostart: " + ex.Message);
			}
		}
	}
}
