using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace HSJeeves
{
	internal static class Program
	{
		// One instance per Windows user, so two copies never upload the same game twice.
		// A second launch simply exits (the first one is already in the tray).
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

	internal sealed class TrayContext : ApplicationContext
	{
		private readonly NotifyIcon _icon;
		private readonly Settings _settings;
		private readonly Tracking.TrackerService _service;
		private readonly Tracking.ConcedeWatcher _concede;
		private bool _warnedFolder;
		private int _dryRunSeen;
		private readonly ToolStripMenuItem _statusItem = new ToolStripMenuItem { Enabled = false };
		private readonly ToolStripMenuItem _gameItem = new ToolStripMenuItem { Enabled = false };
		private readonly ToolStripMenuItem _uploadItem = new ToolStripMenuItem { Enabled = false };
		private readonly Icon _normal;
		private readonly Icon _warning = SystemIcons.Warning;
		private readonly System.Windows.Forms.Timer _refresh = new System.Windows.Forms.Timer { Interval = 2000 };
		private SettingsForm? _form;
		private bool _warned;
		private Update.UpdateInfo? _update;
		private bool _updateBalloon;
		private readonly ToolStripMenuItem _updateItem = new ToolStripMenuItem();
		private readonly System.Windows.Forms.Timer _updateTimer = new System.Windows.Forms.Timer { Interval = 20000 };
		private static readonly System.Net.Http.HttpClient UpdateHttp = CreateUpdateHttp();

		private static System.Net.Http.HttpClient CreateUpdateHttp()
		{
			System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
			var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
			http.DefaultRequestHeaders.UserAgent.ParseAdd("HSJeeves/" + Application.ProductVersion);
			return http;
		}

		// Informs only: the user downloads and runs the installer themselves.
		private async System.Threading.Tasks.Task CheckForUpdateAsync()
		{
			if(!_settings.CheckForUpdates || string.IsNullOrWhiteSpace(_settings.UpdateUrl))
			{
				_updateItem.Visible = false;
				return;
			}
			var current = Update.UpdateChecker.Normalize(new Version(Application.ProductVersion.Split('-', '+')[0]));
			var info = await Update.UpdateChecker.CheckAsync(UpdateHttp, _settings.UpdateUrl, current);
			_update = info;
			_updateItem.Visible = info != null;
			if(info == null)
			{
				Log.Info("update check done: nothing newer than v" + current + " (or the site could not be read)");
				return;
			}
			_updateItem.Text = "Update available: v" + info.Version + " - open download page";
			Log.Info("update available: v" + info.Version + " (installed v" + current + ")");
			if(_settings.UpdateNotified != info.Version.ToString())
			{
				_settings.UpdateNotified = info.Version.ToString();
				_settings.Save();

				Balloon(10000, "HSJeeves update available", "Version " + info.Version + " is available. Click to open the download page." +
					(string.IsNullOrEmpty(info.Notes) ? "" : " " + info.Notes), ToolTipIcon.Info);
				_updateBalloon = true; // a click on this balloon opens the download page (set after Balloon() cleared it)
			}
		}

		private void Balloon(int ms, string title, string text, ToolTipIcon icon)
		{
			_updateBalloon = false;
			_icon.ShowBalloonTip(ms, title, text, icon);
		}

		private static void OpenUrl(string url)
		{
			try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
			catch(Exception ex) { Log.Warn("cannot open " + url + ": " + ex.Message); }
		}

		public TrayContext()
		{
			_settings = Settings.Load();
			Log.Info("HSJeeves started. ApiUrl=" + _settings.ApiUrl + " DryRun=" + _settings.DryRun + " key " + (_settings.Key.Length > 0 ? "set" : "EMPTY"));

			var logConfig = Tracking.LogConfigUpdater.EnsurePowerSection(Tracking.LogConfigUpdater.DefaultPath);
			Log.Info("log.config [Power] section: " + logConfig +
				(logConfig == Tracking.LogConfigResult.Created || logConfig == Tracking.LogConfigResult.Appended
					? " - restart Hearthstone once to enable concede detection" : ""));
			_concede = new Tracking.ConcedeWatcher(() => Tracking.HearthstonePaths.FindLogsDir(_settings.HearthstoneDirectory));
			_service = new Tracking.TrackerService(new Tracking.RankReader(), _concede, new Upload.UploadQueue(_settings));

			_normal = LoadIcon();
			var menu = new ContextMenuStrip();
			menu.Items.Add(_statusItem);
			menu.Items.Add(_gameItem);
			menu.Items.Add(_uploadItem);
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add("Settings...", null, (s, e) => OpenSettings());
			menu.Items.Add("Open logs folder", null, (s, e) => OpenLogs());
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add(new ToolStripMenuItem("HSJeeves v" + Application.ProductVersion) { Enabled = false });
			menu.Items.Add("Exit", null, (s, e) => ExitThread());

			_icon = new NotifyIcon { Icon = _normal, Text = "HSJeeves", ContextMenuStrip = menu, Visible = true };
			_icon.DoubleClick += (s, e) => OpenSettings();
			_icon.BalloonTipClicked += (s, e) => { if(_updateBalloon && _update != null) OpenUrl(_update.PageUrl); else OpenSettings(); };

			_updateItem.Visible = false;
			_updateItem.Click += (s, e) => { if(_update != null) OpenUrl(_update.PageUrl); };
			menu.Items.Insert(3, _updateItem);

			_refresh.Tick += (s, e) => Refresh();
			_refresh.Start();
			Refresh();

			// First check 20 s after start (not while the PC is still logging in), then once a day.
			_updateTimer.Tick += async (s, e) =>
			{
				_updateTimer.Interval = 24 * 60 * 60 * 1000;
				await CheckForUpdateAsync();
			};
			_updateTimer.Start();

			_dryRunSeen = _service.Queue.DryRunWritten;
			if(string.IsNullOrEmpty(_settings.Key))
			{
				_warned = true;
				Balloon(8000, "HSJeeves needs an API key", "Click here and paste the key from /hdttoken in Discord.", ToolTipIcon.Warning);
			}
			else if(_settings.DryRun)
				Balloon(10000, "HSJeeves is in dry-run mode", DryRunAdvice, ToolTipIcon.Warning);
		}

		// The old HDT plugin uploads the same games; two uploaders would count a game twice.
		private static readonly string PluginDll = System.IO.Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HearthstoneDeckTracker", "Plugins", "RankTrackerPlugin.dll");

		private static string DryRunAdvice =>
			"Nothing is uploaded yet. " +
			(System.IO.File.Exists(PluginDll) ? "The HDT RankTracker plugin is installed: switch it off in HDT (Options > Plugins) so games are not uploaded twice, then " : "To go live, ") +
			"untick Dry run in HSJeeves Settings.";

		// Icon states: normal, or warning when uploads cannot work (no key, or key rejected).
		private void Refresh()
		{
			var q = _service.Queue;
			var noKey = string.IsNullOrEmpty(_settings.Key) && !_settings.DryRun;
			var warn = noKey || q.KeyRejected || _settings.DryRun; // dry run keeps the warning icon as a reminder

			// Every dry-run game gets a notice: it was NOT uploaded.
			if(q.DryRunWritten != _dryRunSeen)
			{
				_dryRunSeen = q.DryRunWritten;
				Balloon(10000, "Game NOT uploaded (dry run)", "This was a test run, nothing was sent. " + DryRunAdvice, ToolTipIcon.Warning);
			}

			_statusItem.Text = _service.HearthstoneStatus + (_service.InGame ? " - ranked game in progress" : "");
			_gameItem.Text = "Last game: " + _service.LastGame;
			_uploadItem.Text = noKey ? "API key missing - open Settings"
				: q.KeyRejected ? "API key rejected - run /hdttoken, update Settings"
				: _settings.DryRun ? "DRY RUN - nothing uploaded. Switch off HDT plugin, untick Dry run"
				: "Waiting to upload: " + q.PendingCount;
			// Hearthstone is running but its Logs folder is not found: concede detection stays off until the user points us to it.
			if(_service.HearthstoneStatus.Contains("connected") && !_concede.LogsDirFound)
			{
				_gameItem.Text += " | Hearthstone folder not found - open Settings";
				if(!_warnedFolder)
				{
					_warnedFolder = true;
					Balloon(8000, "HSJeeves cannot find the Hearthstone folder",
						"Click here and set the folder in Settings so concedes are detected.", ToolTipIcon.Warning);
				}
			}
			else if(_concede.LogsDirFound)
				_warnedFolder = false;
			_icon.Icon = warn ? _warning : _normal;
			_icon.Text = Truncate("HSJeeves - " + (warn ? _uploadItem.Text : _service.HearthstoneStatus), 63); // tray tooltip limit

			if(q.KeyRejected && !_warned)
			{
				_warned = true;
				Balloon(8000, "HSJeeves: API key rejected", "Run /hdttoken in Discord and enter the new key in Settings.", ToolTipIcon.Warning);
			}
			if(!q.KeyRejected && !noKey)
				_warned = false;
		}

		private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

		private void OpenSettings()
		{
			if(_form != null && !_form.IsDisposed)
			{
				_form.Activate();
				return;
			}
			_form = new SettingsForm(_settings, _service.Queue);
			_form.FormClosed += (s, e) => { _form = null; Refresh(); };
			_form.Show();
		}

		private static void OpenLogs()
		{
			System.IO.Directory.CreateDirectory(AppPaths.LogDir);
			Process.Start("explorer.exe", "\"" + AppPaths.LogDir + "\"");
		}

		// The .ico holds 16..256 px frames; Windows picks the one that fits the current DPI.
		private static Icon LoadIcon()
		{
			using(var s = typeof(Program).Assembly.GetManifestResourceStream("HSJeeves.ico"))
				return s == null ? SystemIcons.Application : new Icon(s);
		}

		protected override void ExitThreadCore()
		{
			_refresh.Stop();
			_updateTimer.Stop();
			_service.Dispose();
			_icon.Visible = false;
			_icon.Dispose();
			base.ExitThreadCore();
		}
	}
}
