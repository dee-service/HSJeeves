using System;
using System.Threading;
using System.Windows.Forms;

namespace HSJeeves
{
	internal static class Program
	{
		// One instance per Windows user, so two copies never upload the same game twice.
		private const string MutexName = @"Local\HSJeeves-single-instance";

		[STAThread]
		private static void Main()
		{
			using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
			if(!isFirst)
				return;

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new TrayContext());
		}
	}

	// Placeholder tray icon with an Exit item; real status and menu come in milestone 5.
	internal sealed class TrayContext : ApplicationContext
	{
		private readonly NotifyIcon _icon;
		private readonly Tracking.TrackerService _service;

		public TrayContext()
		{
			var settings = Settings.Load();
			Log.Info("HSJeeves started. ApiUrl=" + settings.ApiUrl + " DryRun=" + settings.DryRun);
			_service = new Tracking.TrackerService(new Tracking.RankReader(), new Upload.UploadQueue(settings));
			var menu = new ContextMenuStrip();
			menu.Items.Add("Exit", null, (s, e) => ExitThread());
			_icon = new NotifyIcon
			{
				Icon = LoadIcon(),
				Text = "HSJeeves",
				ContextMenuStrip = menu,
				Visible = true,
			};
		}

		// The .ico holds 16..256 px frames; Windows picks the one that fits the current DPI.
		private static System.Drawing.Icon LoadIcon()
		{
			using(var s = typeof(Program).Assembly.GetManifestResourceStream("HSJeeves.ico"))
				return s == null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(s);
		}

		protected override void ExitThreadCore()
		{
			_service.Dispose();
			_icon.Visible = false;
			_icon.Dispose();
			base.ExitThreadCore();
		}
	}
}
