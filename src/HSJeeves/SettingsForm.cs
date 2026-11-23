using System;
using System.Drawing;
using System.Net.Http;
using System.Windows.Forms;
using HSJeeves.Upload;

namespace HSJeeves
{
	// Settings window: API key, Test connection, dry run, autostart, Hearthstone folder.
	internal sealed class SettingsForm : Form
	{
		private readonly Settings _settings;
		private readonly UploadQueue _queue;
		private readonly TextBox _url = new TextBox();
		private readonly TextBox _key = new TextBox { UseSystemPasswordChar = true };
		private readonly CheckBox _show = new CheckBox { Text = "Show key", AutoSize = true };
		private readonly Label _result = new Label { AutoSize = false };
		private readonly CheckBox _dry = new CheckBox { Text = "Dry run - nothing is uploaded (untick after switching off the HDT plugin)", AutoSize = true };
		private readonly CheckBox _auto = new CheckBox { Text = "Start with Windows", AutoSize = true };
		private readonly CheckBox _updates = new CheckBox { Text = "Check for updates", AutoSize = true };
		private readonly TextBox _hsDir = new TextBox();

		public SettingsForm(Settings settings, UploadQueue queue)
		{
			_settings = settings;
			_queue = queue;

			Text = "HSJeeves settings";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			StartPosition = FormStartPosition.CenterScreen;
			ClientSize = new Size(520, 330);
			using(var s = typeof(SettingsForm).Assembly.GetManifestResourceStream("HSJeeves.ico"))
				if(s != null) Icon = new Icon(s);

			AddLabel("API URL", 16, 16);
			_url.SetBounds(16, 36, 488, 24);
			AddLabel("API key (from /hdttoken in Discord)", 16, 70);
			_key.SetBounds(16, 90, 400, 24);
			_show.Location = new Point(424, 92);
			_show.CheckedChanged += (s, e) => _key.UseSystemPasswordChar = !_show.Checked;

			var test = new Button { Text = "Test connection", Location = new Point(16, 124), Size = new Size(120, 28) };
			test.Click += async (s, e) => await TestAsync(test);
			_result.SetBounds(146, 124, 358, 28);
			_result.TextAlign = ContentAlignment.MiddleLeft;

			_dry.Location = new Point(16, 168);
			_auto.Location = new Point(16, 194);
			_updates.Location = new Point(300, 194);

			AddLabel("Hearthstone folder (only if not found automatically)", 16, 224);
			_hsDir.SetBounds(16, 244, 400, 24);
			var browse = new Button { Text = "Browse...", Location = new Point(424, 242), Size = new Size(80, 28) };
			browse.Click += (s, e) =>
			{
				using(var d = new FolderBrowserDialog { Description = "Select the Hearthstone install folder" })
					if(d.ShowDialog(this) == DialogResult.OK)
					{
						if(System.IO.File.Exists(System.IO.Path.Combine(d.SelectedPath, "Hearthstone.exe")))
							_hsDir.Text = d.SelectedPath;
						else
							MessageBox.Show(this, "Hearthstone.exe was not found in that folder. Select the folder that contains Hearthstone.exe, for example C:\\Program Files (x86)\\Hearthstone.",
								"HSJeeves", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
			};

			var save = new Button { Text = "Save", Location = new Point(344, 288), Size = new Size(76, 28) };
			var cancel = new Button { Text = "Cancel", Location = new Point(428, 288), Size = new Size(76, 28) };
			save.Click += (s, e) => { Apply(); Close(); };
			cancel.Click += (s, e) => Close();
			AcceptButton = save;
			CancelButton = cancel;

			Controls.AddRange(new Control[] { _url, _key, _show, test, _result, _dry, _auto, _updates, _hsDir, browse, save, cancel });
			_url.Text = settings.ApiUrl;
			_key.Text = settings.Key;
			_dry.Checked = settings.DryRun;
			_auto.Checked = settings.StartWithWindows;
			_updates.Checked = settings.CheckForUpdates;
			_hsDir.Text = settings.HearthstoneDirectory ?? "";
		}

		private void AddLabel(string text, int x, int y) =>
			Controls.Add(new Label { Text = text, Location = new Point(x, y), AutoSize = true });

		private async System.Threading.Tasks.Task TestAsync(Button test)
		{
			test.Enabled = false;
			_result.ForeColor = SystemColors.ControlText;
			_result.Text = "Testing...";
			using(var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
			{
				var r = await ApiClient.CheckKeyAsync(http, _url.Text.Trim(), _key.Text);
				_result.Text = r.Message;
				_result.ForeColor = r.Kind == KeyCheck.Valid ? Color.ForestGreen : r.Kind == KeyCheck.Unsupported ? Color.DarkOrange : Color.Firebrick;
			}
			test.Enabled = true;
		}

		private void Apply()
		{
			_settings.ApiUrl = _url.Text.Trim();
			_settings.Key = _key.Text.Trim();
			_settings.DryRun = _dry.Checked;
			_settings.StartWithWindows = _auto.Checked;
			_settings.CheckForUpdates = _updates.Checked;
			_settings.HearthstoneDirectory = string.IsNullOrWhiteSpace(_hsDir.Text) ? null : _hsDir.Text.Trim();
			_settings.Save();
			Autostart.Apply(_settings.StartWithWindows);
			_queue.ResetKeyRejected(); // a new key deserves a new try
			Log.Info("settings saved (ApiUrl=" + _settings.ApiUrl + ", DryRun=" + _settings.DryRun + ", key " + (_settings.Key.Length > 0 ? "set" : "empty") + ")");
		}
	}
}
