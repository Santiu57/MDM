using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Windows.Forms;
using WK.Libraries.SharpClipboardNS;
using static Mari_Downloads.Main;
using static Mari_Downloads.Main.Manager;

namespace Mari_Downloads
{
    public partial class Main : Form
    {
        public Main()
        {
            //Notification Actions
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                ToastArguments args = ToastArguments.Parse(toastArgs.Argument);

                Application.OpenForms?[0]?.Invoke(new Action(() =>
                {
                    if (args.Contains("action"))
                    {
                        string value = args["action"];

                        var window = Application.OpenForms[0];
                        if (window?.WindowState == FormWindowState.Minimized)
                            window.WindowState = FormWindowState.Normal;

                        window?.Activate();
                        window?.Focus();
                    }
                }));
            };

            //Panels Loaders
            UrlPnl = UrlsPanel();

            InitializeComponent();
            BaseComponents();
            SetImages();
            SetWindowConfig();
            MiniPanelManager.SetHost(this);
            MiniPanelManager.Show(UrlPnl);
            AppCustomization.FontChange(this, Properties.Settings.Default.MainFont);
            AppCustomization.ColorComponents(this, Properties.Settings.Default.MainBackColor, Properties.Settings.Default.MainForeColor);

            _downloadChannel = Channel.CreateUnbounded<(Manager.Url, DataGridViewRow)>();

            _semaphore = new SemaphoreSlim(Properties.Settings.Default.SimultaneousDownloads);

            StartWorkers();
        }

        //Panel Constructor

        public class MiniPanel : Panel
        {
            private readonly Panel _contentPanel;
            private readonly FlowLayoutPanel _downPanel;
            private readonly FlowLayoutPanel _upPanel;

            public Panel ContentPanel => _contentPanel;

            public bool IsVisible => this.Visible;

            public MiniPanel(bool bottom = false, bool up = false)
            {
                this.Dock = DockStyle.Fill;
                this.Visible = false;

                MiniPanelManager.Register(this);

                this.Padding = new Padding(10);
                this.MinimumSize = new Size(100, 100);

                _contentPanel = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true
                };

                _downPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    FlowDirection = FlowDirection.RightToLeft,
                    Height = 45,
                    Padding = new Padding(5)
                };

                _upPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    FlowDirection = FlowDirection.RightToLeft,
                    Height = 45,
                    Padding = new Padding(5)
                };

                Controls.Add(_contentPanel);

                if (bottom)
                    Controls.Add(_downPanel);
                if (up)
                    Controls.Add(_upPanel);
            }


            // =========================
            // Controles
            // =========================

            public void AddControl(Control control, DockStyle dock = DockStyle.Top)
            {
                control.Dock = dock;
                _contentPanel.Controls.Add(control);
                _contentPanel.Controls.SetChildIndex(control, 0);
            }

            public void SetMainControl(Control control)
            {
                _contentPanel.Controls.Clear();
                control.Dock = DockStyle.Fill;
                _contentPanel.Controls.Add(control);
            }


            // =========================
            // Botones
            // =========================

            public void AddDownControls(Control[] controls)
            {
                var row = new FlowLayoutPanel
                {
                    AutoSize = true,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false
                };
                foreach (Control control in controls)
                {
                    row.Controls.Add(control);
                }
                _downPanel.Controls.Add(row);
            }
            public void AddUpControls(Control[] controls)
            {
                var row = new FlowLayoutPanel
                {
                    AutoSize = true,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false
                };
                foreach (Control control in controls)
                {
                    row.Controls.Add(control);
                }
                _downPanel.Controls.Add(row);
            }

            // =========================
            // Rows
            // =========================

            public void AddRow(List<Control> controls)
            {
                var row = new FlowLayoutPanel
                {
                    AutoSize = true,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false
                };
                foreach (Control control in controls)
                {
                    row.Controls.Add(control);
                }
                _contentPanel.Controls.Add(row);
            }


            // =========================
            // Estilo
            // =========================

            public void FontAndColorMini()
            {
                AppCustomization.FontChange(this, Properties.Settings.Default.MainFont);
                AppCustomization.ColorComponents(this, Properties.Settings.Default.MainBackColor, Properties.Settings.Default.MainForeColor);
            }
        }

        //Panels Manager

        public static class MiniPanelManager
        {
            private static readonly List<MiniPanel> _panels = new();

            private static MiniPanel _current;

            private static Control _host;

            public static MiniPanel Current => _current;


            public static void SetHost(Control host)
            {
                _host = host;
            }


            public static void Register(MiniPanel panel)
            {
                if (!_panels.Contains(panel))
                    _panels.Add(panel);
            }


            public static void Show(MiniPanel panel)
            {
                if (_host == null)
                    throw new Exception("MiniPanelManager host not set");


                foreach (var p in _panels)
                    p.Visible = false;


                if (!_host.Controls.Contains(panel))
                    _host.Controls.Add(panel);


                panel.Dock = DockStyle.Fill;
                panel.Visible = true;
                panel.BringToFront();

                _current = panel;
            }


            public static void HideCurrent()
            {
                if (_current != null)
                {
                    _current.Visible = false;
                    _current = null;
                }
            }


            public static bool IsShowing(MiniPanel panel)
            {
                return _current == panel;
            }
        }

        //Customization
        public static class AppCustomization
        {
            public static void TraverseAllControls(Control parent, Action<Control> action)
            {
                action(parent);

                foreach (Control control in parent.Controls)
                {
                    TraverseAllControls(control, action);
                }
            }
            public static void ColorComponents(Control parent, Color back, Color fore)
            {
                TraverseAllControls(parent, control =>
                {
                    if (control is not Panel)
                    {
                        control.BackColor = back;
                        control.ForeColor = fore;
                    }
                    if (control is DataGridView dgv)
                    {
                        dgv.BackgroundColor = back;
                        dgv.DefaultCellStyle.BackColor = back;
                        dgv.DefaultCellStyle.ForeColor = fore;
                        dgv.ColumnHeadersDefaultCellStyle.BackColor = back;
                        dgv.ColumnHeadersDefaultCellStyle.ForeColor = fore;
                    }
                });
            }
            public static void WindowConfig(Form window)
            {
                window.FormBorderStyle = FormBorderStyle.FixedSingle;
                window.MaximizeBox = false;
                window.MinimizeBox = true;
                window.ControlBox = true;
                window.ShowIcon = true;
            }
            public static void FontChange(Control form, Font font)
            {
                if (font == null)
                    return;

                TraverseAllControls(form, control =>
                {
                    control.Font = null;
                });

                var newFont = font;

                float scale = newFont.Size / form.Font.Size;

                form.SuspendLayout();

                form.Font = newFont;
                form.Scale(new SizeF(scale, scale));

                form.ResumeLayout();
            }
            public static void ForceToolStripRefresh(Control parent)
            {
                foreach (Control control in parent.Controls)
                {
                    if (control is ToolStrip ts)
                    {
                        ts.SuspendLayout();
                        ts.Font = parent.Font;

                        foreach (ToolStripItem item in ts.Items)
                        {
                            item.Font = parent.Font;
                            item.AutoSize = true;
                        }

                        ts.PerformLayout();
                        ts.ResumeLayout();
                    }

                    if (control.HasChildren)
                        ForceToolStripRefresh(control);
                }
            }
        }

        private void SetImages()
        {

        }

        private void SetWindowConfig()
        {
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.ControlBox = true;
            this.ShowIcon = true;
            this.Icon = new Icon("media/icon.ico");
            this.Text = "(ᓀ‸ᓂ)";
        }

        public static class Notifications
        {
            public static void Show(string title, string desc, ToastDuration duration = ToastDuration.Short, string action = "focus")
            {
                if (!Properties.Settings.Default.ShowNotifs)
                    return;
                try
                {
                    var builder = new ToastContentBuilder()
                        .AddText(title)
                        .AddText(desc)
                        .AddArgument("action", action);

                    builder.AddAppLogoOverride(new Uri(Path.Combine(Environment.CurrentDirectory, "media/icon.png")), ToastGenericAppLogoCrop.Default);

                    builder.SetToastDuration(duration);
                    builder.Show();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error showing notification:\n{ex.Message}", "Notification Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public static class Manager
        {
            public class Url
            {
                public string url { get; set; }
                public string site { get; set; }
                public Status status { get; set; }

                public Url(string url)
                {
                    var basestatus = new Manager.Status(); basestatus.Change(Status.StatusType.Pending);
                    this.url = url;
                    this.site = ExtractSiteName();
                    this.status = basestatus;
                }
                public static List<Url> UrlExtractor(string input)
                {
                    var urls = new List<Url>();

                    if (string.IsNullOrWhiteSpace(input))
                        return urls;

                    string pattern = @"https?://[^\s<>""'\)\]\}]+";

                    var matches = Regex.Matches(input, pattern, RegexOptions.IgnoreCase);

                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    char[] trimChars = new[] { '<', '>', '(', ')', '[', ']', '{', '}', '"', '\'', '.', ',', ';', ':', '!', '?' };

                    foreach (Match match in matches)
                    {
                        if (match == null || string.IsNullOrWhiteSpace(match.Value))
                            continue;

                        var candidate = match.Value.Trim(trimChars);

                        if (string.IsNullOrWhiteSpace(candidate))
                            continue;

                        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri))
                        {
                            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                                continue;

                            string normalized = uri.AbsoluteUri;

                            if (seen.Add(normalized))
                            {
                                urls.Add(new Url(normalized));
                            }
                        }
                    }

                    return urls;
                }
                public string ExtractSiteName()
                {
                    if (string.IsNullOrWhiteSpace(url))
                        return string.Empty;

                    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                        return string.Empty;

                    string host = uri.Host.ToLower();

                    if (host.StartsWith("www."))
                        host = host.Substring(4);

                    string[] parts = host.Split('.');

                    if (parts.Length < 2)
                        return host;

                    if (parts.Length >= 3 && parts[^2].Length <= 3)
                        return parts[^3];

                    return parts[^2];
                }
            }
            public class UrlFilter
            {
                public string From { get; }
                public string To { get; }

                public UrlFilter(string from, string to)
                {
                    From = from;
                    To = to;
                }

                public bool Match(string url)
                {
                    return url.Contains(From, StringComparison.OrdinalIgnoreCase);
                }

                public string Apply(string url)
                {
                    return url.Replace(From, To, StringComparison.OrdinalIgnoreCase);
                }
            }
            public class LogEntry
            {
                public DateTime Date { get; set; }
                public string Url { get; set; }
                public string Site { get; set; }
            }
            public static class Log
            {
                static string file =
                    Path.Combine(Environment.CurrentDirectory, "log.json");

                public static void Save(Url url)
                {
                    List<LogEntry> logs = new();

                    if (File.Exists(file))
                    {
                        string json = File.ReadAllText(file);
                        logs = JsonSerializer.Deserialize<List<LogEntry>>(json) ?? new();
                    }

                    logs.Add(new LogEntry
                    {
                        Date = DateTime.Now,
                        Url = url.url,
                        Site = url.site
                    });

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };

                    File.WriteAllText(file,
                        JsonSerializer.Serialize(logs, options));
                }
            }
            public class Status
            {
                public enum StatusType
                {
                    Pending,
                    Downloading,
                    Done,
                    Queued,
                    Error
                }
                public StatusType Current { get; private set; } = StatusType.Pending;

                public void Change(StatusType status)
                {
                    Current = status;
                }

                public bool Is(StatusType status) => Current == status;

                public string GetDisplay()
                {
                    return Current switch
                    {
                        StatusType.Pending => "Sleeping",
                        StatusType.Downloading => "Downloading",
                        StatusType.Done => "Done",
                        StatusType.Error => "Error",
                        StatusType.Queued => "Queued",
                        _ => "Unknown"
                    };
                }
            }

            public static class Gallery_Dl
            {
                public static async Task<Url> Run(Url url, string arguments)
                {
                    var startInfo = new ProcessStartInfo()
                    {
                        FileName = "gallery-dl",
                        Arguments = $"{arguments} {url.url}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = new Process())
                    {
                        process.StartInfo = startInfo;
                        process.Start();
                        await process.WaitForExitAsync();
                    }

                    return url;
                }
            }
            public static class YT_Dlp
            {
                public static async Task<Url> Run(Url url, string arguments)
                {
                    var startInfo = new ProcessStartInfo()
                    {
                        FileName = "yt-dlp",
                        Arguments = $"{arguments} {url.url}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = new Process())
                    {
                        process.StartInfo = startInfo;
                        process.Start();
                        await process.WaitForExitAsync();
                    }

                    return url;
                }
            }
            public static class Downloader
            {
                static readonly HashSet<string> ytSites = new()
                {
                    "youtube",
                    "youtu",
                    "twitch"
                };

                public static async Task Run(Url url)
                {
                    if (ytSites.Contains(url.site))
                    {
                        await YT_Dlp.Run(url, YTDLPArgs.Build());
                    }
                    else
                    {
                        await Gallery_Dl.Run(url, GalleryDLArgs.Build());
                    }
                }
            }
            public static class Errors
            {
                public static bool Check()
                {
                    string errorLogPath = "ErrorLog.txt";
                    if (!File.Exists(errorLogPath))
                        return false;
                    return true;
                }
            }
            public class Argument
            {
                public string Name { get; }
                public string Command { get; }
                public string Value { get; set; }
                public bool Enabled { get; set; }

                public Argument(string name, string command, string value = "", bool enabled = true)
                {
                    Name = name;
                    Command = command;
                    Value = value;
                    Enabled = enabled;
                }

                public string Build()
                {
                    if (!Enabled)
                        return "";

                    if (string.IsNullOrWhiteSpace(Value))
                        return Command;

                    return $"{Command} {Value}";
                }
            }
            public class ArgumentProfile
            {
                private readonly List<Argument> _args = new();

                public void Add(Argument arg)
                {
                    _args.Add(arg);
                }

                public string Build()
                {
                    return string.Join(" ",
                        _args
                        .Where(a => a.Enabled)
                        .Select(a => a.Build())
                        .Where(a => !string.IsNullOrWhiteSpace(a)));
                }

                public Argument Get(string name)
                {
                    return _args.FirstOrDefault(a => a.Name == name);
                }
            }
            public static class GalleryDLArgs
            {
                public static ArgumentProfile Profile = new ArgumentProfile();

                public static void Init()
                {
                    Profile = new ArgumentProfile();

                    Profile.Add(new Argument(
                        "Directory",
                        "-d",
                        Properties.Settings.Default.DownloadPath));

                    Profile.Add(new Argument(
                        "ErrorLog",
                        "-e",
                        "ErrorLog.txt"));

                    Profile.Add(new Argument(
                        "Retries",
                        "-R",
                        Properties.Settings.Default.Retries));

                    Profile.Add(new Argument(
                        "Sleep",
                        "--sleep",
                        Properties.Settings.Default.Sleep));
                }

                public static string Build()
                {
                    return Profile.Build();
                }
            }
            public static class YTDLPArgs
            {
                public static ArgumentProfile Profile = new ArgumentProfile();

                public static void Init()
                {
                    Profile = new ArgumentProfile();

                    Profile.Add(new Argument(
                        "Path",
                        "-P",
                        Properties.Settings.Default.YTOutput));

                    Profile.Add(new Argument(
                        "Format",
                        "-f",
                        Properties.Settings.Default.YTResolution));

                    Profile.Add(new Argument(
                        "MergeFormat",
                        "--merge-output-format",
                        Properties.Settings.Default.YTFormat));

                    if (Properties.Settings.Default.YTExtractAu)
                    {
                        Profile.Add(new Argument(
                            "ExtractAudio",
                            "--extract-audio"));

                        Profile.Add(new Argument(
                            "AudioFormat",
                            "--audio-format",
                            Properties.Settings.Default.YTAuFormat));
                    }
                }

                public static string Build()
                {
                    return Profile.Build();
                }
            }
        }

        private bool RepetedUrl(Manager.Url url)
        {
            foreach (DataGridViewRow row in _urls.Rows)
            {
                if (row.Cells["Url"].Value != null)
                {
                    string existingUrl = row.Cells["Url"].Value.ToString();
                    if (string.Equals(existingUrl, url.url, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }
        public void AddUrl(Manager.Url url)
        {
            if (RepetedUrl(url))
                return;

            url.status.Change(Manager.Status.StatusType.Pending);

            int index = _urls.Rows.Add(url.status.GetDisplay(), url.site, url.url);

            var row = _urls.Rows[index];

            if (Properties.Settings.Default.AutoStart)
            {
                _downloadChannel.Writer.TryWrite((url, row));
            }
        }
        private void UpdateRowStatus(DataGridViewRow row, string status)
        {
            if (InvokeRequired)
            {
                Invoke(() => row.Cells["Status"].Value = status);
                return;
            }

            row.Cells["Status"].Value = status;
        }
        private int[] UrlCount()
        {
            int total = _urls.RowCount;
            int pending = 0;
            int downloading = 0;
            int done = 0;
            int error = 0;
            int queued = 0;

            foreach (DataGridViewRow row in _urls.Rows)
            {
                if (row.IsNewRow) continue;

                var statusValue = row.Cells["Status"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(statusValue))
                    continue;

                switch (statusValue)
                {
                    case "Sleeping":
                        pending++;
                        break;

                    case "Downloading":
                        downloading++;
                        break;

                    case "Done":
                        done++;
                        break;

                    case "Error":
                        error++;
                        break;

                    case "Queued":
                        queued++; 
                        break;
                }
            }
            int[] count = { total, pending, downloading, done, queued, error };
            return count;
        }
        public void StatusChange(string text)
        {
            if (_statusLabel == null)
                return;

            if (InvokeRequired)
            {
                Invoke(new Action(() => _statusLabel.Text = text));
                return;
            }

            _statusLabel.Text = text;
        }

        private void StartWorkers()
        {
            int workers = Properties.Settings.Default.SimultaneousDownloads;

            for (int i = 0; i < workers; i++)
            {
                Task.Run(WorkerLoop);
            }
        }
        private async Task WorkerLoop()
        {
            await foreach (var job in _downloadChannel.Reader.ReadAllAsync())
            {
                UpdateRowStatus(job.row, "Queued");
                await _semaphore.WaitAsync();

                try
                {
                    UpdateRowStatus(job.row, "Downloading");

                    await Manager.Downloader.Run(job.url);

                    if (Errors.Check())
                    {
                        if (File.Exists("ErrorLog.txt"))
                        {
                            UpdateRowStatus(job.row, "Error");
                            try { File.Delete("ErrorLog.txt"); } catch { }
                        }
                    }
                    else
                    {
                        UpdateRowStatus(job.row, "Done");
                        Manager.Log.Save(job.url);
                    }
                    UrlsStatusUpdate();
                }
                catch (Exception ex) 
                {
                    MessageBox.Show(ex.Message);
                }
                finally
                {
                    _semaphore.Release();
                }
            }
        }

        private void Start()
        {
            foreach (DataGridViewRow row in _urls.Rows)
            {
                if (row.IsNewRow) continue;

                string status = row.Cells["Status"].Value?.ToString();

                if (status == "Sleeping")
                {
                    string url = row.Cells["Url"].Value?.ToString();
                    var u = new Manager.Url(url);

                    _downloadChannel.Writer.TryWrite((u, row));
                }
            }
        }

        private void UrlsStatusUpdate()
        {
            int[] count = UrlCount();
            StatusChange($"Total: {count[0]} | Sleeping: {count[1]} | Downloading: {count[2]} | Done: {count[3]} | Queued: {count[4]} | Errors: {count[5]}");
        }

        private void BaseComponents()
        {
            ToolStrip tools = new ToolStrip
            {
                Dock = DockStyle.Left
            };

            ToolStripButton UrlsShow = new ToolStripButton
            {
                Image = Image.FromFile("media/icon.png")
            };

            tools.Items.Add(UrlsShow);

            _statusStrip = new StatusStrip { Name = "StatusStrip" };

            _statusLabel = new ToolStripStatusLabel
            {
                Name = "Status",
                Text = "Sleeping"
            };

            _statusBar = new ToolStripProgressBar
            {
                Name = "Progress",
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            SharpClipboard clipboard = new SharpClipboard();
            clipboard.ObservableFormats.All = false;
            clipboard.ObservableFormats.Texts = true;

            clipboard.ClipboardChanged += (sender, e) =>
            {
                if (e.Content is string text)
                {
                    var urls = Manager.Url.UrlExtractor(text);
                    foreach (var Url in urls)
                    {
                        AddUrl(Url);
                    }
                }
            };

            _statusStrip.Items.Add(_statusBar);
            _statusStrip.Items.Add(_statusLabel);

            this.Controls.Add(tools);
            this.Controls.Add(_statusStrip);
        }
        private MiniPanel UrlsPanel()
        {
            MiniPanel urlpnl = new MiniPanel(true);

            //Urls DGV
            _urls = new DataGridView
            {
                Name = "Urls",
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AllowUserToAddRows = false,
            };

            _urls.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells });
            _urls.Columns.Add(new DataGridViewTextBoxColumn { Name = "Site", HeaderText = "Site", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells });
            _urls.Columns.Add(new DataGridViewTextBoxColumn { Name = "Url", HeaderText = "Url", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells });

            _urls.RowsAdded += (s, e) =>
            {
                UrlsStatusUpdate();

                if (Properties.Settings.Default.AutoStart == true)
                {
                    Start();
                }
            };

            _urls.RowsRemoved += (s, e) =>
            {
                UrlsStatusUpdate();
            };

            //Down
            CheckBox autoStart = new CheckBox
            {
                Text = "Auto Start",
                TextAlign = ContentAlignment.MiddleCenter,
                Checked = Properties.Settings.Default.AutoStart,
                CheckAlign = ContentAlignment.MiddleLeft,
                Size = new Size(100, 35)
            };
            autoStart.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.AutoStart = autoStart.Checked;
                Properties.Settings.Default.Save();
            };

            Button start = new Button { Size = new Size(170, 35), Text = "Start Downloads", TextAlign = ContentAlignment.MiddleCenter };
            start.Click += (e, r) =>
            {
                Start();
            };

            Button clear = new Button { BackgroundImage = Image.FromFile("media/clear.png"), BackgroundImageLayout = ImageLayout.Stretch, Size = new Size(35, 35) };
            clear.Click += (e, r) =>
            {
                _urls.Rows.Clear();
            };

            Control[] Down = new Control[] { clear, autoStart, start };


            urlpnl.SetMainControl(_urls);
            urlpnl.AddDownControls(Down);

            return urlpnl;
        }

        public static T FindControl<T>(Control parent, Func<T, bool> predicate) where T : class
        {
            foreach (Control control in parent.Controls)
            {
                if (control is T t && predicate(t))
                    return t;

                var result = FindControl(control, predicate);
                if (result != null)
                    return result;
            }

            if (parent is ToolStrip ts)
            {
                foreach (ToolStripItem item in ts.Items)
                {
                    if (item is T t && predicate(t))
                        return t;

                    if (item is ToolStripControlHost host)
                    {
                        var result = FindControl(host.Control, predicate);
                        if (result != null)
                            return result;
                    }
                }
            }

            return null;
        }

        private SemaphoreSlim _semaphore;
        private Channel<(Manager.Url url, DataGridViewRow row)> _downloadChannel;

        private DataGridView _urls;

        private string _gallery_dlArgs = "-e ErrorLog.txt -d C:\\Users\\santi\\Downloads\\gallery-dl";

        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private ToolStripProgressBar _statusBar;

        MiniPanel UrlPnl;

        private void Main_Load(object sender, EventArgs e)
        {

        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.Save();
        }
    }
}
