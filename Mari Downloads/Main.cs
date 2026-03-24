using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Diagnostics;
using System.DirectoryServices.ActiveDirectory;
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
using static Mari_Downloads.Main.Manager.Filter;

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
            AppConfiguration = AppConfig();
            AppPersonalization = AppPersonalizationBuild();
            AppDependencies = AppDependenciesBuild();
            NotificationsConfig = NotificationsConfigBuilder();
            Arguments = Args_GDL();

            //Filters Loader
            Filter.Loader.Load("filters.json");

            //Arguments Loader
            GalleryDLArgs.Init();

            InitializeComponent();
            BaseComponents();
            SetImages();
            SetWindowConfig();
            MiniPanelManager.SetHost(this);
            MiniPanelManager.Show(UrlPnl);
            AppCustomization.FontChange(this, Properties.Settings.Default.MainFont);
            AppCustomization.ColorComponents(this, Properties.Settings.Default.MainBackColor, Properties.Settings.Default.MainForeColor);

            _downloadChannel = Channel.CreateUnbounded<(Manager.Url, DataGridViewRow)>();

            _maxDownloads = Properties.Settings.Default.SimultaneousDownloads;
            _semaphore = new SemaphoreSlim(_maxDownloads);

            StartWorkers();

            Properties.Settings.Default.DownloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "gallery-dl");
        }

        //Panel Constructor

        public class MiniPanel : Panel
        {
            private readonly Panel _contentPanel;
            private readonly FlowLayoutPanel _downPanel;
            private readonly FlowLayoutPanel _upPanel;
            private readonly FlowLayoutPanel _rowsContainer;

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
                    FlowDirection = FlowDirection.LeftToRight,
                    Height = 45,
                    Padding = new Padding(5)
                };

                _rowsContainer = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoSize = true
                };

                _contentPanel.Controls.Add(_rowsContainer);

                Controls.Add(_contentPanel);

                if (bottom)
                    Controls.Add(_downPanel);
                if (up)
                    Controls.Add(_upPanel);
            }

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
                _upPanel.Controls.Add(row);
            }

            public void AddRow(Control[] controls)
            {
                var row = new FlowLayoutPanel
                {
                    AutoSize = true,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    Margin = new Padding(0, 0, 0, 10)
                };

                foreach (Control control in controls)
                    row.Controls.Add(control);

                _rowsContainer.Controls.Add(row);
            }

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
        private void ApplyRowColor(DataGridViewRow row, string status)
        {
            Color back = Properties.Settings.Default.MainBackColor;
            Color fore = Properties.Settings.Default.MainForeColor;

            switch (status)
            {
                case "Sleeping":
                    back = Properties.Settings.Default.ColorSleepingBack;
                    fore = Properties.Settings.Default.ColorSleepingFore;
                    break;

                case "Queued":
                    back = Properties.Settings.Default.ColorQueuedBack;
                    fore = Properties.Settings.Default.ColorQueuedFore;
                    break;

                case "Downloading":
                    back = Properties.Settings.Default.ColorDownloadingBack;
                    fore = Properties.Settings.Default.ColorDownloadingFore;
                    break;

                case "Done":
                    back = Properties.Settings.Default.ColorDoneBack;
                    fore = Properties.Settings.Default.ColorDoneFore;
                    break;

                case "Error":
                    back = Properties.Settings.Default.ColorErrorBack;
                    fore = Properties.Settings.Default.ColorErrorFore;
                    break;
            }

            row.DefaultCellStyle.BackColor = back;
            row.DefaultCellStyle.ForeColor = fore;
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
            public static class Filter
            {
                public class Context
                {
                    public string Url { get; set; }

                    public string Site { get; set; }

                    public bool StopProcessing { get; set; }
                }
                public interface IUrlFilter
                {
                    bool Match(Context ctx);

                    void Apply(Context ctx);

                }
                public static class Engine
                {
                    private static readonly List<IUrlFilter> _filters = new();

                    private static readonly List<Entry> _entries = new();

                    public static IReadOnlyList<Entry> Entries => _entries;

                    public static void Register(IUrlFilter filter, Entry entry)
                    {
                        _filters.Add(filter);
                        _entries.Add(entry);
                    }

                    public static Context Process(string url, string site)
                    {
                        var ctx = new Context
                        {
                            Url = url,
                            Site = site,
                            StopProcessing = false
                        };

                        foreach (var filter in _filters)
                        {
                            if (!filter.Match(ctx))
                                continue;

                            filter.Apply(ctx);

                            if (ctx.StopProcessing)
                                break;
                        }
                        return ctx;
                    }
                }
                public class SiteAlias : IUrlFilter
                {
                    private readonly string _from;
                    private readonly string _to;

                    public SiteAlias(string from, string to)
                    {
                        _from = from;
                        _to = to;
                    }

                    public bool Match(Context ctx)
                    {
                        return ctx.Site == _from;
                    }

                    public void Apply(Context ctx)
                    {
                        ctx.Site = _to;

                        if (Uri.TryCreate(ctx.Url, UriKind.Absolute, out var uri))
                        {
                            var builder = new UriBuilder(uri);

                            if (builder.Host.Contains(_from))
                                builder.Host = builder.Host.Replace(_from, _to);

                            ctx.Url = builder.Uri.ToString();
                        }
                    }
                }
                public class SiteRateLimit : IUrlFilter
                {
                    private readonly string _site;
                    private readonly int _limit;

                    public SiteRateLimit(string site, int limit)
                    {
                        _site = site;
                        _limit = limit;

                        Manager.SiteRateLimiter.SetLimit(site, limit);
                    }

                    public bool Match(Context ctx)
                    {
                        return ctx.Site == _site;
                    }

                    public void Apply(Context ctx)
                    {
                        // Doesn't modify anything
                    }
                }
                public static class Loader
                {
                    public static void Load(string file)
                    {
                        if (!File.Exists(file))
                            return;

                        var json = File.ReadAllText(file);

                        var filters = JsonSerializer.Deserialize<List<Entry>>(json);

                        foreach (var f in filters)
                        {
                            switch (f.Type)
                            {
                                case "alias":

                                    Engine.Register(
                                        new SiteAlias(f.From, f.To),
                                        f
                                    );

                                    break;

                                case "ratelimit":

                                    Engine.Register(
                                        new SiteRateLimit(f.Site, int.Parse(f.Replace)),
                                        f
                                    );

                                    break;
                            }
                        }
                    }
                }
                public static class Saver
                {
                    public static void Save(string file)
                    {
                        try
                        {
                            var options = new JsonSerializerOptions
                            {
                                WriteIndented = true
                            };

                            var json = JsonSerializer.Serialize(
                                Engine.Entries,
                                options
                            );

                            File.WriteAllText(file, json);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(
                                $"Error saving filters:\n{ex.Message}",
                                "Filters Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error
                            );
                        }
                    }
                }
                public class Entry
                {
                    public string Type { get; set; }

                    public string From { get; set; }

                    public string To { get; set; }

                    public string Site { get; set; }

                    public int Limit { get; set; }

                    public string Replace { get; set; }
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
                public static async Task<int> Run(Url url, string arguments)
                {
                    var startInfo = new ProcessStartInfo()
                    {
                        FileName = "gallery-dl",
                        Arguments = $"{arguments} {url.url}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = new Process();
                    process.StartInfo = startInfo;

                    process.Start();
                    await process.WaitForExitAsync();

                    return process.ExitCode;
                }
            }
            public static class YT_Dlp
            {
                public static async Task<int> Run(Url url, string arguments)
                {
                    var startInfo = new ProcessStartInfo()
                    {
                        FileName = "yt-dlp",
                        Arguments = $"{arguments} {url.url}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = new Process();
                    process.StartInfo = startInfo;

                    process.Start();
                    await process.WaitForExitAsync();

                    return process.ExitCode;
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

                public static async Task<int> Run(Url url)
                {
                    if (ytSites.Contains(url.site))
                        return await YT_Dlp.Run(url, YTDLPArgs.Build());
                    else
                        return await Gallery_Dl.Run(url, GalleryDLArgs.Build());
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
            public static class SiteRateLimiter
            {
                private static readonly Dictionary<string, SemaphoreSlim> _siteSemaphores = new();
                private static readonly Dictionary<string, int> _limits = new();

                public static void SetLimit(string site, int limit)
                {
                    lock (_siteSemaphores)
                    {
                        _limits[site] = limit;

                        if (_siteSemaphores.ContainsKey(site))
                            _siteSemaphores[site] = new SemaphoreSlim(limit);
                    }
                }

                public static SemaphoreSlim Get(string site)
                {
                    lock (_siteSemaphores)
                    {
                        if (!_siteSemaphores.ContainsKey(site))
                        {
                            int limit = _limits.ContainsKey(site) ? _limits[site] : 2;
                            _siteSemaphores[site] = new SemaphoreSlim(limit);
                        }

                        return _siteSemaphores[site];
                    }
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
            var ctx = Filter.Engine.Process(url.url, url.site);

            if (ctx.StopProcessing)
                return;

            url.url = ctx.Url;
            url.site = ctx.Site;

            if (RepetedUrl(url))
                return;

            url.status.Change(Manager.Status.StatusType.Pending);

            int index = _urls.Rows.Add(url.status.GetDisplay(), url.site, url.url);

            var row = _urls.Rows[index];

            ApplyRowColor(row, url.status.GetDisplay());
        }
        private void UpdateRowStatus(DataGridViewRow row, string status)
        {
            if (InvokeRequired)
            {
                Invoke(() => UpdateRowStatus(row, status));
                return;
            }

            row.Cells["Status"].Value = status;

            ApplyRowColor(row, status);

            if (status == "Done")
            {
                int eraseDelay = Properties.Settings.Default.EraseDone;

                if (eraseDelay != -1)
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(eraseDelay * 1000);

                        try
                        {
                            if (row.DataGridView != null)
                            {
                                Invoke(() =>
                                {
                                    if (!row.IsNewRow && row.DataGridView != null)
                                        row.DataGridView.Rows.Remove(row);
                                });
                            }
                        }
                        catch { }
                    });
                }
            }
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
            int workers = Environment.ProcessorCount * 2;

            for (int i = 0; i < workers; i++)
                Task.Run(WorkerLoop);
        }
        private async Task WorkerLoop()
        {
            try
            {
                await foreach (var job in _downloadChannel.Reader.ReadAllAsync(_cts.Token))
                {
                    _pauseEvent.Wait();

                    SemaphoreSlim currentSemaphore;
                    lock (_semaphoreLock)
                    {
                        currentSemaphore = _semaphore;
                    }

                    await currentSemaphore.WaitAsync(_cts.Token);

                    var siteSemaphore = Manager.SiteRateLimiter.Get(job.url.site);
                    await siteSemaphore.WaitAsync();

                    try
                    {
                        UpdateRowStatus(job.row, "Downloading");

                        int exit = await Manager.Downloader.Run(job.url);

                        if (exit != 0)
                            UpdateRowStatus(job.row, "Error");
                        else
                        {
                            UpdateRowStatus(job.row, "Done");
                            Manager.Log.Save(job.url);
                        }

                        UrlsStatusUpdate();
                    }
                    finally
                    {
                        siteSemaphore.Release();
                        currentSemaphore.Release();
                    }
                }
            }
            catch (OperationCanceledException) { }
        }
        private void StopDownloads()
        {
            _cts.Cancel();
            _cts = new CancellationTokenSource();
        }
        public void PauseDownloads()
        {
            _pauseEvent.Reset();
        }

        public void ResumeDownloads()
        {
            _pauseEvent.Set();
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

                    UpdateRowStatus(row, "Queued");
                    _downloadChannel.Writer.TryWrite((u, row));
                }
            }
        }

        private void UrlsStatusUpdate()
        {
            int[] count = UrlCount();
            StatusChange($"Total: {count[0]} | Sleeping: {count[1]} | Downloading: {count[2]} | Done: {count[3]} | Queued: {count[4]} | Errors: {count[5]}");
        }
        private void ChangeSimultaneousDownloads(int newValue)
        {
            int diff = newValue - _maxDownloads;

            if (diff > 0)
            {
                _semaphore.Release(diff);
            }
            else if (diff < 0)
            {
                PauseDownloads();
                for (int i = 0; i < -diff; i++)
                    _semaphore.WaitAsync();
                ResumeDownloads();
            }

            _maxDownloads = newValue;

            Properties.Settings.Default.SimultaneousDownloads = newValue;
            Properties.Settings.Default.Save();
        }
        public string[] CheckDlUpdates()
        {
            string galleryInstalled = null;
            string galleryLatest = null;
            string ytdlpInstalled = null;
            string ytdlpLatest = null;

            try
            {
                galleryInstalled = GetInstalledVersion("gallery-dl", "--version");
                galleryLatest = GetLatestPipVersion("gallery-dl");

                ytdlpInstalled = GetInstalledVersion("yt-dlp", "--version");
                ytdlpLatest = GetLatestPipVersion("yt-dlp");
            }
            catch
            {

            }

            return new string[]
            {
                galleryInstalled,
                galleryLatest,
                ytdlpInstalled,
                ytdlpLatest
            };
        }

        private string GetInstalledVersion(string exe, string args)
        {
            var info = new ProcessStartInfo()
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var process = new Process())
            {
                process.StartInfo = info;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();

                var match = Regex.Match(output, @"\d+\.\d+\.\d+");
                if (match.Success)
                    return match.Value;
            }

            return null;
        }

        private string GetLatestPipVersion(string package)
        {
            var info = new ProcessStartInfo()
            {
                FileName = "py",
                Arguments = $"-m pip index versions {package}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var process = new Process())
            {
                process.StartInfo = info;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();

                var match = Regex.Match(output, @"LATEST:\s+(\d+\.\d+\.\d+)");
                if (match.Success)
                    return match.Groups[1].Value;
            }

            return null;
        }
        public static bool InstallPackage(string package)
        {
            try
            {
                var info = new ProcessStartInfo()
                {
                    FileName = "py",
                    Arguments = $"-m pip install {package}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = new Process())
                {
                    process.StartInfo = info;
                    process.Start();

                    process.WaitForExit();

                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
        public static bool UpdatePackage(string package)
        {
            try
            {
                var info = new ProcessStartInfo()
                {
                    FileName = "py",
                    Arguments = $"-m pip install --upgrade {package}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = new Process())
                {
                    process.StartInfo = info;
                    process.Start();

                    process.WaitForExit();

                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void DependenciesCheck()
        {
            var GDLstate = FindControl<Label>(AppDependencies, l => l.Name == "GDLState");
            var YTstate = FindControl<Label>(AppDependencies, l => l.Name == "YTState");


            if (_galleryInstalled != null && _galleryLatest != null)
            {
                if (new Version(_galleryInstalled) < new Version(_galleryLatest))
                {
                    Notifications.Show(
                        "Update Available",
                        $"gallery-dl update available\nInstalled: {_galleryInstalled}\nLatest: {_galleryLatest}"
                    );
                    GDLstate.Text = $"{_galleryInstalled}: Outdated. {_galleryLatest}: Lastest";
                    GDLstate.ForeColor = Color.Red;
                }
                else
                {
                    GDLstate.Text = $"{_galleryInstalled}: Lastest ✓";
                    GDLstate.ForeColor = Color.Green;
                }
            }
            if (_ytdlpInstalled != null && _ytdlpLatest != null)
            {
                if (new Version(_ytdlpInstalled) < new Version(_ytdlpLatest))
                {
                    Notifications.Show(
                        "Update Available",
                        $"YT-dlp update available\nInstalled: {_ytdlpInstalled}\nLatest: {_ytdlpLatest}"
                    );
                    YTstate.Text = $"{_ytdlpInstalled}: Outdated  {_ytdlpLatest}: Lastest";
                    YTstate.ForeColor = Color.Red;
                }
                else
                {
                    YTstate.Text = $"{_ytdlpInstalled}: Lastest ✓";
                    YTstate.ForeColor = Color.Green;
                }
            }
        }

        private async void GetDependenciesVersions()
        {
            var versions = await Task.Run(() => CheckDlUpdates());

            _galleryInstalled = versions[0];
            _galleryLatest = versions[1];
            _ytdlpInstalled = versions[2];
            _ytdlpLatest = versions[3];
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
            UrlsShow.Click += (sender, e) => { MiniPanelManager.Show(UrlPnl); };

            ToolStripButton Config = new ToolStripButton
            {
                Image = Image.FromFile("media/icon.png"),
                Alignment = ToolStripItemAlignment.Right
            };
            Config.Click += (sender, e) => { MiniPanelManager.Show(AppConfiguration); };

            ToolStripButton Args = new ToolStripButton
            {
                Image = Image.FromFile("media/icon.png"),
                Alignment = ToolStripItemAlignment.Left
            };
            Args.Click += (sender, e) => { MiniPanelManager.Show(Arguments); };

            tools.Items.Add(UrlsShow);
            tools.Items.Add(Args);
            tools.Items.Add(Config);

            _statusStrip = new StatusStrip { Name = "StatusStrip" };

            _statusLabel = new ToolStripStatusLabel
            {
                Name = "Status",
                Text = "Sleeping..."
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

        private void ConfigNav(MiniPanel reference)
        {
            Button AppC = new Button { Size = new Size(170, 35), Text = "App Configuration", TextAlign = ContentAlignment.MiddleCenter };
            AppC.Click += (s, e) => { MiniPanelManager.Show(AppConfiguration); };

            Button AppP = new Button { Size = new Size(170, 35), Text = "App Personalization", TextAlign = ContentAlignment.MiddleCenter };
            AppP.Click += (s, e) => { MiniPanelManager.Show(AppPersonalization); };

            Button AppD = new Button { Size = new Size(170, 35), Text = "App Dependencies", TextAlign = ContentAlignment.MiddleCenter };
            AppD.Click += (s, e) => { MiniPanelManager.Show(AppDependencies); };

            Button Notifs = new Button { Size = new Size(170, 35), Text = "Notifications", TextAlign = ContentAlignment.MiddleCenter };
            Notifs.Click += (s, e) => { MiniPanelManager.Show(NotificationsConfig); };

            Control[] up = { AppC, AppP, AppD, Notifs };
            reference.AddUpControls(up);

            Control[] space = { new Label() };
            reference.AddRow(space);
        }

        private MiniPanel AppConfig()
        {
            MiniPanel AppConfig = new MiniPanel(false,true);

            ConfigNav(AppConfig);

            NumericUpDown SDNud = new NumericUpDown { Minimum = 1, Maximum = 20, Value = Properties.Settings.Default.SimultaneousDownloads, Width = 80 };
            SDNud.ValueChanged += (s, e) =>
            {
                ChangeSimultaneousDownloads((int)SDNud.Value);
            };
            Control[] SDownloads = { new Label { Text = "Simultaneous Downloads", Size = new Size(200, 35), TextAlign = ContentAlignment.MiddleCenter }, SDNud};

            NumericUpDown AutoDeleteDone = new NumericUpDown { Minimum = -1, Maximum = 1000, Value = Properties.Settings.Default.EraseDone, Width = 80 };
            AutoDeleteDone.ValueChanged += (s, e) =>
            {
                Properties.Settings.Default.EraseDone = (int)AutoDeleteDone.Value;
                Properties.Settings.Default.Save();
            };
            Control[] AutoDelete = { new Label { Text = "Auto Delete Done Downloads After", Size = new Size(250, 35), TextAlign = ContentAlignment.MiddleCenter }, AutoDeleteDone, 
                new Label { Text = "Seconds (-1 To never)", TextAlign = ContentAlignment.MiddleCenter, Size = new Size(160, 35) } };

            AppConfig.AddRow(SDownloads);
            AppConfig.AddRow(AutoDelete);

            return AppConfig;
        }
        private MiniPanel AppDependenciesBuild()
        {
            MiniPanel AppDependencies = new MiniPanel(false, true);

            ConfigNav(AppDependencies);

            Control[] Gallery_dl = { new Label { Text = "Gallery-Dl:", Size = new Size(100, 35), TextAlign = ContentAlignment.MiddleCenter } };
            Label GDLstate = new Label { Size = new Size(150, 35), TextAlign = ContentAlignment.MiddleCenter, Text = "Obtaining...", Name = "GDLState" };

            Control[] GDL_info = { new Label { Text = "Current Version: ", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter }, GDLstate };

            Button GDL_install = new Button { Text = "Install Gallery-Dl", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter };
            GDL_install.Click += async (s, e) =>
            {
                if (await Task.Run(() => InstallPackage("gallery-dl")))
                {
                    GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show("Gallery-dl Installed", "Gallery-dl Installed Succesfully");
                }
            };
            Button GDL_Update = new Button { Text = "Update Gallery-Dl", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter };
            GDL_install.Click += async (s, e) =>
            {
                if (await Task.Run(() => UpdatePackage("gallery-dl")))
                {
                    GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show("Gallery-dl Updated", "Gallery-dl Updated Succesfully");
                }
            };
            Control[] GDL_Btns = { GDL_install,GDL_Update };

            Control[] YT_dlp = { new Label { Text = "YT-dlp:", Size = new Size(100, 35), TextAlign = ContentAlignment.MiddleCenter } };
            Label YTState = new Label { Size = new Size(150, 35), TextAlign = ContentAlignment.MiddleCenter, Text = "Obtaining...", Name = "YTState" };

            Control[] YT_info = { new Label { Text = "Current Version: ", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter }, YTState };

            Button YT_install = new Button { Text = "Install YT-dlp", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter };
            YT_install.Click += async (s, e) =>
            {
                if (await Task.Run(() => InstallPackage("yt-dlp")))
                {
                    GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show("YT-dlp Installed", "YT-dlp Installed Succesfully");
                }
            };
            Button YT_Update = new Button { Text = "Update YT-dlp", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter };
            YT_Update.Click += async (s, e) =>
            {
                if (await Task.Run(() => UpdatePackage("yt-dlp")))
                {
                    GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show("YT-dlp Updated", "YT-dlp Updated Succesfully");
                }
            };
            Control[] YT_Btns = { YT_install, YT_Update };

            AppDependencies.AddRow(Gallery_dl);
            AppDependencies.AddRow(GDL_info);
            AppDependencies.AddRow(GDL_Btns);
            AppDependencies.AddRow(YT_dlp);
            AppDependencies.AddRow(YT_info);
            AppDependencies.AddRow(YT_Btns);

            return AppDependencies;
        }

        private MiniPanel NotificationsConfigBuilder()
        {
            MiniPanel NotificationsConfig = new MiniPanel(false, true);

            ConfigNav(NotificationsConfig);

            NumericUpDown SDNud = new NumericUpDown { Minimum = 1, Maximum = 20, Value = Properties.Settings.Default.SimultaneousDownloads, Width = 80 };
            SDNud.ValueChanged += (s, e) =>
            {
                ChangeSimultaneousDownloads((int)SDNud.Value);
            };

            Control[] SDownloads = { new Label { Text = "d Downloads", Size = new Size(100, 35), TextAlign = ContentAlignment.MiddleCenter }, SDNud };

            NotificationsConfig.AddRow(SDownloads);

            return NotificationsConfig;
        }

        private void ArgsNav(MiniPanel reference)
        {
            Button gdl = new Button { Size = new Size(170, 35), Text = "Gallery-DL Arguments", TextAlign = ContentAlignment.MiddleCenter };
            gdl.Click += (s, e) => { MiniPanelManager.Show(Arguments); };

            Button ytdlp = new Button { Size = new Size(170, 35), Text = "YT-dlp Arguments", TextAlign = ContentAlignment.MiddleCenter };
            ytdlp.Click += (s, e) => { MiniPanelManager.Show(AppPersonalization); };

            Button alias = new Button { Size = new Size(170, 35), Text = "Alias Override", TextAlign = ContentAlignment.MiddleCenter };
            alias.Click += (s, e) => { MiniPanelManager.Show(AppPersonalization); };

            Button ratelimiter = new Button { Size = new Size(170, 35), Text = "Rate Limiter", TextAlign = ContentAlignment.MiddleCenter };
            ratelimiter.Click += (s, e) => { MiniPanelManager.Show(NotificationsConfig); };

            Control[] up = { gdl, ytdlp, alias, ratelimiter };
            reference.AddUpControls(up);

            Control[] space = { new Label() };
            reference.AddRow(space);
        }

        private MiniPanel Args_GDL()
        {
            MiniPanel Args_GDL = new MiniPanel(false, true);

            ArgsNav(Args_GDL);

            return Args_GDL;
        }
        private MiniPanel AppPersonalizationBuild()
        {
            MiniPanel AppCustom = new MiniPanel(false, true);

            ConfigNav(AppCustom);

            NumericUpDown SDNud = new NumericUpDown { Minimum = 1, Maximum = 20, Value = Properties.Settings.Default.SimultaneousDownloads, Width = 80 };
            SDNud.ValueChanged += (s, e) =>
            {
                ChangeSimultaneousDownloads((int)SDNud.Value);
            };

            Control[] SDownloads = { new Label { Text = "b Downloads", Size = new Size(100, 35), TextAlign = ContentAlignment.MiddleCenter }, SDNud };

            AppCustom.AddRow(SDownloads);

            return AppCustom;
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
        private readonly object _semaphoreLock = new();
        private int _maxDownloads;
        private CancellationTokenSource _cts = new();
        private ManualResetEventSlim _pauseEvent = new(true);
        private Channel<(Manager.Url url, DataGridViewRow row)> _downloadChannel;

        private DataGridView _urls;

        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;

        private string _galleryInstalled;
        string _galleryLatest;
        string _ytdlpInstalled;
        string _ytdlpLatest;

        MiniPanel UrlPnl;
        MiniPanel AppConfiguration;
        MiniPanel AppPersonalization;
        MiniPanel AppDependencies;
        MiniPanel NotificationsConfig;
        MiniPanel Arguments;

        private async void Main_Load(object sender, EventArgs e)
        {
            GetDependenciesVersions();
            DependenciesCheck();
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.Save();
            Filter.Saver.Save("filters.json");
        }
    }
}
