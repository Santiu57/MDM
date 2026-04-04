using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Configuration;
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
using Windows.Devices.Display.Core;
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
                    var window = Application.OpenForms[0];

                    if (window?.WindowState == FormWindowState.Minimized)
                        window.WindowState = FormWindowState.Normal;

                    window?.Activate();
                    window?.Focus();

                    if (args.Contains("action") && args["action"] == "export")
                    {
                        string path = args["path"];

                        if (File.Exists(path))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = $"/select,\"{path}\"",
                                UseShellExecute = true
                            });
                        }
                    }
                }));
            };

            MiniPanelManager.SetHost(this);

            //Filters Loader
            Filter.Loader.Load("filters.json");

            //Arguments Loader
            GalleryDLArgs.Init();
            YTDLPArgs.Init();

            //Panels Loaders
            UrlPnl = UrlsPanel();
            AppConfiguration = AppConfig();
            AppPersonalization = AppPersonalizationBuild();
            AppDependencies = AppDependenciesBuild();
            NotificationsConfig = NotificationsConfigBuilder();
            Arguments_GDL = Args_GDL();
            Arguments_YTDL = Args_YTDL();
            Alias_Override = Alias();
            Rate_Limiter = Rate();
            YtSites_Panel = YtSitesPanel();

            InitializeComponent();
            BaseComponents();
            SetImages();
            SetWindowConfig();
            MiniPanelManager.Show(UrlPnl);
            AppCustomization.FontChange(this, Properties.Settings.Default.MainFont);
            AppCustomization.ColorComponents(this, Properties.Settings.Default.MainBackColor, Properties.Settings.Default.MainForeColor);

            _downloadChannel = Channel.CreateUnbounded<(Manager.Url, DataGridViewRow)>();

            _maxDownloads = Properties.Settings.Default.SimultaneousDownloads;
            _semaphore = new SemaphoreSlim(_maxDownloads);

            StartWorkers();

            if(string.IsNullOrEmpty(Properties.Settings.Default.DownloadPath))
                Properties.Settings.Default.DownloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "gallery-dl");
            if (string.IsNullOrEmpty(Properties.Settings.Default.YTOutput))
                Properties.Settings.Default.YTOutput = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "gallery-dl", "youtube");
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
                    Padding = new Padding(5),
                    AutoScroll = true
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
        }

        //Panels Manager

        public static class MiniPanelManager
        {
            private static readonly List<MiniPanel> _panels = new();

            private static MiniPanel _current;

            public static Control _host;

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
                AppCustomization.ColorComponents(panel, Properties.Settings.Default.MainBackColor, Properties.Settings.Default.MainForeColor);
                AppCustomization.FontChange(panel, Properties.Settings.Default.MainFont);
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

        public class MiniMenuPanel
        {
            private readonly FlowLayoutPanel Menu;
            private readonly Control Anchor;
            private readonly FlowLayoutPanel _rowsContainer; 

            public MiniMenuPanel(Control anchor)
            {
                Anchor = anchor;
                Menu = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Visible = false
                };

                _rowsContainer = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoSize = true
                };

                Menu.Controls.Add(_rowsContainer);

                Anchor.MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        Menu.Visible = !Menu.Visible;
                        UpdateMenuPosition();
                    }
                };

                Anchor.ParentChanged += Anchor_ParentChanged;
                Anchor.LocationChanged += (s, e) => UpdateMenuPosition();
                Anchor.SizeChanged += (s, e) => UpdateMenuPosition();
                Menu.SizeChanged += (s, e) => UpdateMenuPosition();
            }

            private void Anchor_ParentChanged(object sender, EventArgs e)
            {
                Control host = MiniPanelManager._host ?? Anchor.FindForm();

                // Menu se agrega al host principal
                if (!host.Controls.Contains(Menu))
                    host.Controls.Add(Menu);

                UpdateMenuPosition();

                Menu.BringToFront();
            }

            private void UpdateMenuPosition()
            {
                Menu.BringToFront();
                if (Anchor.Parent == null || Menu.Parent == null) return;
                Point screen = Anchor.PointToScreen(Point.Empty);
                Point client = Menu.Parent.PointToClient(screen);
                Menu.Location = new Point((client.X + (Anchor.Size.Width / 2)) - (Menu.Size.Width / 2), (client.Y - 3) - Menu.Size.Height);
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
                ForceToolStripRefresh(form);
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

            row.DefaultCellStyle.SelectionBackColor = GetSelectionColor(back);
            row.DefaultCellStyle.SelectionForeColor = fore;
        }
        private static Color AdjustColor(Color c, int amount)
        {
            int r = Math.Clamp(c.R + amount, 0, 255);
            int g = Math.Clamp(c.G + amount, 0, 255);
            int b = Math.Clamp(c.B + amount, 0, 255);

            return Color.FromArgb(r, g, b);
        }

        private static Color GetSelectionColor(Color back)
        {
            int brightness = (back.R * 299 + back.G * 587 + back.B * 114) / 1000;

            if (brightness > 140)
                return AdjustColor(back, -40); // fondo claro → oscurecer
            else
                return AdjustColor(back, 40);  // fondo oscuro → aclarar
        }

        public static class Notifications
        {
            public static void Show(
            string title,
            string desc,
            ToastDuration duration = ToastDuration.Short,
            Dictionary<string, string>? args = null)
            {
                if (!Properties.Settings.Default.ShowNotifs)
                    return;

                try
                {
                    var builder = new ToastContentBuilder()
                        .AddText(title)
                        .AddText(desc);

                    if (args != null)
                    {
                        foreach (var kv in args)
                            builder.AddArgument(kv.Key, kv.Value);
                    }

                    builder.AddAppLogoOverride(
                        new Uri(Path.Combine(Environment.CurrentDirectory, "media/icon.png")),
                        ToastGenericAppLogoCrop.Default);

                    builder.SetToastDuration(duration);
                    builder.Show();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error showing notification:\n{ex.Message}",
                        "Notification Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
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
                    public static void Clear()
                    {
                        _filters.Clear();
                        _entries.Clear();
                    }
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
                public class YtSite : IUrlFilter
                {
                    private readonly string _site;

                    public YtSite(string site)
                    {
                        _site = site;
                        Manager.Downloader.RegisterYtSite(site);
                    }

                    public bool Match(Context ctx) => ctx.Site == _site;

                    public void Apply(Context ctx) { } // Solo marca, no modifica
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

                                case "ytsite":
                                    Engine.Register(
                                        new YtSite(f.Site),
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
                public static async Task<(int ExitCode, string Output, string Command)> Run(Url url, string arguments)
                {
                    var sb = new StringBuilder();

                    var startInfo = new ProcessStartInfo()
                    {
                        FileName = "gallery-dl",
                        Arguments = $"{arguments} {url.url}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = new Process();
                    process.StartInfo = startInfo;

                    process.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();

                    return (process.ExitCode, sb.ToString(), $"gallery-dl {arguments} {url.url}");
                }
            }

            public static class YT_Dlp
            {
                public static async Task<(int ExitCode, string Output, string Command)> Run(Url url, string arguments)
                {
                    var sb = new StringBuilder();

                    var startInfo = new ProcessStartInfo()
                    {
                        FileName = "yt-dlp",
                        Arguments = $"{arguments} {url.url}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = new Process();
                    process.StartInfo = startInfo;

                    process.OutputDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync();

                    return (process.ExitCode, sb.ToString(), $"yt-dlp {arguments} {url.url}");
                }
            }

            public static class Downloader
            {
                private static readonly HashSet<string> _ytSites = new(StringComparer.OrdinalIgnoreCase)
                {
                    "youtube", "youtu", "twitch", "bilibili"  // defaults
                };

                public static void RegisterYtSite(string site)
                {
                    lock (_ytSites)
                        _ytSites.Add(site);
                }

                public static void UnregisterYtSite(string site)
                {
                    lock (_ytSites)
                        _ytSites.Remove(site);
                }

                public static IReadOnlyCollection<string> YtSites
                {
                    get { lock (_ytSites) return _ytSites.ToList(); }
                }

                public static async Task<(int ExitCode, string Output, string Command)> Run(Url url)
                {
                    bool isYt;
                    lock (_ytSites)
                        isYt = _ytSites.Contains(url.site);

                    if (isYt)
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

                public enum ControlType { TextBox, FilePath, FolderPath, DropDown }
                public ControlType InputType { get; set; } = ControlType.TextBox;

                public string[] Options { get; set; } = Array.Empty<string>();

                // Mapeo display → value real. Si está vacío, Options se usa directo.
                public Dictionary<string, string> OptionMap { get; set; } = new();

                public Argument(string name, string command, string value = "", bool enabled = true,
                                ControlType inputType = ControlType.TextBox,
                                string[] options = null,
                                Dictionary<string, string> optionMap = null)
                {
                    Name = name;
                    Command = command;
                    Value = value;
                    Enabled = enabled;
                    InputType = inputType;
                    Options = options ?? Array.Empty<string>();
                    OptionMap = optionMap ?? new();
                }

                public string Build()
                {
                    if (!Enabled) return "";
                    if (string.IsNullOrWhiteSpace(Value)) return Command;
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

                public IReadOnlyList<Argument> All()
                {
                    return _args;
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
                static string file = Path.Combine(Environment.CurrentDirectory, "args_gdl.json");
                public static ArgumentProfile Profile = new ArgumentProfile();

                public static void Init()
                {
                    Profile = new ArgumentProfile();

                    Profile.Add(new Argument(
                        "Directory path", "-d",
                        Properties.Settings.Default.DownloadPath,
                        inputType: Argument.ControlType.FolderPath));   // <-- abre FBD

                    Profile.Add(new Argument(
                        "Filename",
                        "-f",
                        Properties.Settings.Default.Filename
                    ));

                    Profile.Add(new Argument(
                        "Retries", "-R",
                        Properties.Settings.Default.Retries));          // TextBox normal

                    Profile.Add(new Argument(
                        "Sleep", "--sleep",
                        Properties.Settings.Default.Sleep));

                    Profile.Add(new Argument(
                        "Range", "--range",
                        Properties.Settings.Default.Range));

                    Profile.Add(new Argument(
                        "Uigora", "--ugoira",
                        Properties.Settings.Default.Uigora,
                        inputType: Argument.ControlType.DropDown,
                        options: new[] { "webm", "gif", "zip", "mp4" })); // <-- dropdown

                    Profile.Add(new Argument(
                        "Cookies",
                        "--cookies",
                        Properties.Settings.Default.Cookies,
                        inputType: Argument.ControlType.FilePath));

                    Profile.Add(new Argument(
                        "Extra Arguments",
                        "",
                        Properties.Settings.Default.ExtraArgs));

                    Load();
                }

                public static void Load()
                {
                    if (!File.Exists(file)) return;

                    var saved = JsonSerializer.Deserialize<List<ArgumentData>>(File.ReadAllText(file));
                    if (saved == null) return;

                    foreach (var d in saved)
                    {
                        var arg = Profile.Get(d.Name);
                        if (arg != null)
                        {
                            arg.Value = d.Value;
                            arg.Enabled = d.Enabled;
                        }
                    }
                }

                public static void Save()
                {
                    var data = Profile.All().Select(a => new ArgumentData
                    {
                        Name = a.Name,
                        Value = a.Value,
                        Enabled = a.Enabled
                    }).ToList();

                    File.WriteAllText(file, JsonSerializer.Serialize(data,
                        new JsonSerializerOptions { WriteIndented = true }));
                }

                public static string Build() => Profile.Build();
            }

            public static class YTDLPArgs
            {
                static string file = Path.Combine(Environment.CurrentDirectory, "args_ytdlp.json");
                public static ArgumentProfile Profile = new ArgumentProfile();

                public static void Init()
                {
                    Profile = new ArgumentProfile();

                    Profile.Add(new Argument(
                        "Out Path", "-P",
                        Properties.Settings.Default.YTOutput,
                        inputType: Argument.ControlType.FolderPath));   // <-- abre FBD

                    Profile.Add(new Argument(
                        "Video Format", "-f",
                        Properties.Settings.Default.YTResolution,
                        inputType: Argument.ControlType.DropDown,
                        optionMap: new Dictionary<string, string>
                        {
                            ["Best"] = "bestvideo+bestaudio/best",
                            ["4320p → 8K"] = "bestvideo[height<=4320]+bestaudio/best[height<=4320]",
                            ["2160p → 4K"] = "bestvideo[height<=2160]+bestaudio/best[height<=2160]",
                            ["1440p → Quad HD"] = "bestvideo[height<=1440]+bestaudio/best[height<=1440]",
                            ["1080p → Full HD"] = "bestvideo[height<=1080]+bestaudio/best[height<=1080]",
                            ["720p → HD"] = "bestvideo[height<=720]+bestaudio/best[height<=720]",
                            ["480p → SD"] = "bestvideo[height<=480]+bestaudio/best[height<=480]",
                            ["360p → SD"] = "bestvideo[height<=360]+bestaudio/best[height<=360]",
                            ["240p → SD"] = "bestvideo[height<=240]+bestaudio/best[height<=240]",
                            ["144p → SD"] = "bestvideo[height<=144]+bestaudio/best[height<=144]",
                        }));

                    Profile.Add(new Argument(
                        "Download playlists", "",
                        Properties.Settings.Default.Playlist,
                        inputType: Argument.ControlType.DropDown,
                        optionMap: new Dictionary<string, string>
                        {
                            ["yes"] = "--yes-playlist",
                            ["no"] = "--no-playlist",
                        }));

                    Profile.Add(new Argument(
                        "Merge Format", "--merge-output-format",
                        Properties.Settings.Default.YTFormat,
                        inputType: Argument.ControlType.DropDown,
                        options: new[] { "mp4", "mkv", "webm" }));

                    Profile.Add(new Argument(
                        "Retries",
                        "-R",
                        Properties.Settings.Default.YTRetries));

                    Profile.Add(new Argument(
                        "Ffmpeg Location",
                        "--ffmpeg-location",
                        Properties.Settings.Default.ffmpeg,
                        inputType: Argument.ControlType.FilePath));

                    Profile.Add(new Argument(
                        "Remote Components",
                        "--remote-components",
                        Properties.Settings.Default.YTRemoteComponents));

                    Profile.Add(new Argument(
                        "Sleep Interval",
                        "--sleep-interval",
                        Properties.Settings.Default.YTSleep));

                    Profile.Add(new Argument(
                        "Cookies",
                        "--cookies",
                        Properties.Settings.Default.YTCookies,
                        inputType: Argument.ControlType.FilePath));

                    Profile.Add(new Argument(
                        "Config Location",
                        "--config-location",
                        Properties.Settings.Default.YTConfigPath,
                        inputType: Argument.ControlType.FilePath));

                    Profile.Add(new Argument(
                        "Extract Audio", "--extract-audio --audio-format",
                        Properties.Settings.Default.YTAuFormat,
                        enabled: Properties.Settings.Default.YTExtractAu,
                        inputType: Argument.ControlType.DropDown,
                        options: new[] { "mp3", "aac", "alac", "opus", "vorbis", "m4a", "flac", "wav" }));

                    Profile.Add(new Argument(
                        "ExtraArguments",
                        "",
                        Properties.Settings.Default.YTExtraArgs));

                    Load();
                }

                public static void Load()
                {
                    if (!File.Exists(file)) return;

                    var saved = JsonSerializer.Deserialize<List<ArgumentData>>(File.ReadAllText(file));
                    if (saved == null) return;

                    foreach (var d in saved)
                    {
                        var arg = Profile.Get(d.Name);
                        if (arg != null)
                        {
                            arg.Value = d.Value;
                            arg.Enabled = d.Enabled;
                        }
                    }
                }

                public static void Save()
                {
                    var data = Profile.All().Select(a => new ArgumentData
                    {
                        Name = a.Name,
                        Value = a.Value,
                        Enabled = a.Enabled
                    }).ToList();

                    File.WriteAllText(file, JsonSerializer.Serialize(data,
                        new JsonSerializerOptions { WriteIndented = true }));
                }

                public static string Build() => Profile.Build();
            }

            // Clase auxiliar para serializar
            public class ArgumentData
            {
                public string Name { get; set; }
                public string Value { get; set; }
                public bool Enabled { get; set; }
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
                            // Solo crear semáforo limitado si el site tiene un ratelimit registrado
                            if (_limits.ContainsKey(site))
                                _siteSemaphores[site] = new SemaphoreSlim(_limits[site]);
                            else
                                _siteSemaphores[site] = new SemaphoreSlim(int.MaxValue, int.MaxValue);
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
            Task.Run(() =>
            {
                var ctx = Filter.Engine.Process(url.url, url.site);

                if (ctx.StopProcessing)
                    return;

                url.url = ctx.Url;
                url.site = ctx.Site;

                Invoke(() =>
                {
                    if (RepetedUrl(url))
                        return;

                    url.status.Change(Manager.Status.StatusType.Pending);

                    int index = _urls.Rows.Add(url.status.GetDisplay(), url.site, url.url);
                    var row = _urls.Rows[index];
                    ApplyRowColor(row, url.status.GetDisplay());
                });
            });
        }
        private void UpdateRowStatus(DataGridViewRow row, string status)
        {
            if (InvokeRequired)
            {
                Invoke(() => UpdateRowStatus(row, status));
                return;
            }

            if (!IsRowAlive(row)) return;

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
            int total = 0, pending = 0, downloading = 0, done = 0, error = 0, queued = 0;

            // Snapshot para evitar modificaciones durante iteración
            var rows = _urls.Rows.Cast<DataGridViewRow>()
                                 .Where(r => !r.IsNewRow)
                                 .ToList();

            total = rows.Count;

            foreach (var row in rows)
            {
                var statusValue = row.Cells["Status"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(statusValue)) continue;

                switch (statusValue)
                {
                    case "Sleeping": pending++; break;
                    case "Downloading": downloading++; break;
                    case "Done": done++; break;
                    case "Error": error++; break;
                    case "Queued": queued++; break;
                }
            }

            return new[] { total, pending, downloading, done, queued, error };
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

        private bool IsRowAlive(DataGridViewRow row)
        {
            return row != null && row.DataGridView != null && !row.IsNewRow;
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
                        currentSemaphore = _semaphore;

                    await currentSemaphore.WaitAsync(_cts.Token);

                    var siteSemaphore = Manager.SiteRateLimiter.Get(job.url.site);
                    await siteSemaphore.WaitAsync();

                    try
                    {
                        UpdateRowStatus(job.row, "Downloading");

                        UrlsStatusUpdate();

                        var (exit, output, command) = await Manager.Downloader.Run(job.url);

                        if (exit != 0)
                        {
                            string tagContent = $"Command:\n{command}\n\nOutput:\n{output}";
                            Invoke(() =>
                            {
                                if (IsRowAlive(job.row))
                                    job.row.Tag = tagContent;
                            });
                            UpdateRowStatus(job.row, "Error");
                        }
                        else
                        {
                            UpdateRowStatus(job.row, "Done");
                            if (IsRowAlive(job.row))
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
            UrlsStatusUpdate();
        }

        private void UrlsStatusUpdate()
        {
            if (InvokeRequired)
            {
                Invoke(UrlsStatusUpdate);
                return;
            }

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
            catch (Exception e) 
            {
                MessageBox.Show(e.Message);
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
            else
            {
                GDLstate.Text = $"Couldn't obtain";
                GDLstate.ForeColor = Color.Yellow;
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
            else
            {
                YTstate.Text = $"Couldn't obtain";
                YTstate.ForeColor = Color.Yellow;
            }
        }

        private async Task GetDependenciesVersions()
        {
            var versions = await Task.Run(() => CheckDlUpdates());

            _galleryInstalled = versions[0];
            _galleryLatest = versions[1];
            _ytdlpInstalled = versions[2];
            _ytdlpLatest = versions[3];
        }

        private Control[] RowColor(string text, Func<Color> getter, Action<Color> setter)
        {
            Label Text = new Label { Text = text, Size = new Size(180, 35), TextAlign = ContentAlignment.MiddleLeft };

            Panel Preview = new Panel
            {
                Size = new Size(35, 35),
                BackColor = getter()
            };

            Button change = new Button { Text = "Change", Size = new Size(100, 35) };

            change.Click += (s, e) =>
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.Color = getter();

                    if (cd.ShowDialog() == DialogResult.OK)
                    {
                        setter(cd.Color);
                        Preview.BackColor = cd.Color;
                        Properties.Settings.Default.Save();
                    }
                }
            };

            return [Text, Preview, change];
        }
        private Control[] ArgumentRow(Manager.Argument arg, Action onChanged)
        {
            CheckBox enabled = new CheckBox { Checked = arg.Enabled, Size = new Size(25, 35), CheckAlign = ContentAlignment.TopLeft };
            Label name = new Label { Text = arg.Name, TextAlign = ContentAlignment.MiddleLeft, AutoSize = true };

            enabled.CheckedChanged += (s, e) => { arg.Enabled = enabled.Checked; onChanged(); };

            // Construir el control de valor según el tipo
            Control[] valueControls = arg.InputType switch
            {
                Manager.Argument.ControlType.FilePath => BuildFilePath(arg, onChanged),
                Manager.Argument.ControlType.FolderPath => BuildFolderPath(arg, onChanged),
                Manager.Argument.ControlType.DropDown => BuildDropDown(arg, onChanged),
                _ => BuildTextBox(arg, onChanged)
            };

            return new Control[] { enabled, name }.Concat(valueControls).ToArray();
        }

        private Control[] BuildTextBox(Manager.Argument arg, Action onChanged)
        {
            TextBox tb = new TextBox { Text = arg.Value, Size = new Size(250, 30) };
            tb.TextChanged += (s, e) => { arg.Value = tb.Text; onChanged(); };
            return new Control[] { tb };
        }

        private Control[] BuildFilePath(Manager.Argument arg, Action onChanged)
        {
            TextBox tb = new TextBox { Text = arg.Value, Size = new Size(200, 30) };
            Button btn = new Button { Text = "...", Size = new Size(35, 28) };

            tb.TextChanged += (s, e) => { arg.Value = tb.Text; onChanged(); };

            btn.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog();
                if (!string.IsNullOrWhiteSpace(arg.Value))
                    ofd.InitialDirectory = Path.GetDirectoryName(arg.Value);

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    tb.Text = ofd.FileName;
                    arg.Value = ofd.FileName;
                    onChanged();
                }
            };

            return [tb, btn];
        }

        private Control[] BuildFolderPath(Manager.Argument arg, Action onChanged)
        {
            TextBox tb = new TextBox { Text = arg.Value, Size = new Size(200, 28) };
            Button btn = new Button { Text = "...", Size = new Size(35, 28) };

            tb.TextChanged += (s, e) => { arg.Value = tb.Text; onChanged(); };

            btn.Click += (s, e) =>
            {
                using var fbd = new FolderBrowserDialog();
                if (!string.IsNullOrWhiteSpace(arg.Value))
                    fbd.InitialDirectory = arg.Value;

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    tb.Text = fbd.SelectedPath;
                    arg.Value = fbd.SelectedPath;
                    onChanged();
                }
            };

            return [tb, btn];
        }

        private Control[] BuildDropDown(Manager.Argument arg, Action onChanged)
        {
            ComboBox cb = new ComboBox
            {
                Size = new Size(200, 35),
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            // Las opciones visibles son las keys del mapa, o Options si no hay mapa
            string[] displayOptions = arg.OptionMap.Count > 0
                ? arg.OptionMap.Keys.ToArray()
                : arg.Options;

            foreach (var opt in displayOptions)
                cb.Items.Add(opt);

            // Seleccionar el item cuyo value real coincide con arg.Value
            string currentDisplay = arg.OptionMap.Count > 0
                ? arg.OptionMap.FirstOrDefault(kv => kv.Value == arg.Value).Key ?? displayOptions.FirstOrDefault()
                : (displayOptions.Contains(arg.Value) ? arg.Value : displayOptions.FirstOrDefault());

            cb.SelectedItem = currentDisplay;
            if (cb.SelectedItem != null)
                arg.Value = arg.OptionMap.Count > 0
                    ? arg.OptionMap[cb.SelectedItem.ToString()]
                    : cb.SelectedItem.ToString();

            cb.SelectedIndexChanged += (s, e) =>
            {
                if (cb.SelectedItem == null) return;
                arg.Value = arg.OptionMap.Count > 0
                    ? arg.OptionMap[cb.SelectedItem.ToString()]
                    : cb.SelectedItem.ToString();
                onChanged();
            };

            return new Control[] { cb };
        }

        string FormatName(string name)
        {
            return Regex.Replace(name, "(\\B[A-Z])", " $1");
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
            Args.Click += (sender, e) => { MiniPanelManager.Show(Arguments_GDL); };

            tools.Items.Add(UrlsShow);
            tools.Items.Add(Args);
            tools.Items.Add(Config);

            _statusStrip = new StatusStrip { Name = "StatusStrip", Font = Properties.Settings.Default.MainFont };

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
                if (e.Content is not string text) return;

                Task.Run(() =>
                {
                    var urls = Manager.Url.UrlExtractor(text);
                    foreach (var url in urls)
                        AddUrl(url);
                });
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

            // Click izquierdo en celda Status con error → mostrar output
            _urls.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                var row = _urls.Rows[e.RowIndex];
                var col = _urls.Columns[e.ColumnIndex];
                var status = row.Cells["Status"].Value?.ToString();

                if (col.Name == "Status" && status == "Error")
                {
                    string output = row.Tag as string;
                    MessageBox.Show(
                        string.IsNullOrWhiteSpace(output) ? "No output captured." : output,
                        "Error Details",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            };

            // Click derecho sobre row → eliminar
            _urls.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;

                var hit = _urls.HitTest(e.X, e.Y);
                if (hit.RowIndex < 0) return;

                _urls.CancelEdit();

                var row = _urls.Rows[hit.RowIndex];
                if (!IsRowAlive(row)) return;

                string status = row.Cells["Status"].Value?.ToString();

                bool safeToDelete = status == "Done" || status == "Error";

                if (!safeToDelete)
                {
                    string warning = status switch
                    {
                        "Downloading" => "This URL is currently downloading.\nRemoving it will not stop the process, but the row will be gone.\nRemove anyway?",
                        "Queued" => "This URL is queued and will start soon.\nRemoving it will not cancel the download if it has already started.\nRemove anyway?",
                        "Sleeping" => "This URL hasn't started yet.\nRemove it?",
                        _ => "Remove this URL?"
                    };

                    var result = MessageBox.Show(
                        warning,
                        "Remove URL",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );

                    if (result != DialogResult.Yes) return;
                }

                _urls.Rows.RemoveAt(hit.RowIndex);
            };

            // Doble click izquierdo en celda Url → abrir en navegador
            _urls.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                var col = _urls.Columns[e.ColumnIndex];
                if (col.Name != "Url") return;

                string url = _urls.Rows[e.RowIndex].Cells["Url"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(url)) return;

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            };

            //Down
            CheckBox autoStart = new CheckBox
            {
                Text = "Auto Start",
                TextAlign = ContentAlignment.MiddleCenter,
                Checked = Properties.Settings.Default.AutoStart,
                CheckAlign = ContentAlignment.MiddleLeft,
                Size = new Size(110, 35)
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
            clear.Click += (s, e) =>
            {
                for (int i = _urls.Rows.Count - 1; i >= 0; i--)
                {
                    var row = _urls.Rows[i];
                    if (row.IsNewRow) continue;

                    string status = row.Cells["Status"].Value?.ToString();

                    bool remove =
                        (status == "Done" && Properties.Settings.Default.ClearDone) ||
                        (status == "Sleeping" && Properties.Settings.Default.ClearSleeping) ||
                        (status == "Downloading" && Properties.Settings.Default.ClearDownloading) ||
                        (status == "Queued" && Properties.Settings.Default.ClearQueued) ||
                        (status == "Error" && Properties.Settings.Default.ClearErrors);

                    if (remove)
                        _urls.Rows.RemoveAt(i);
                }

                UrlsStatusUpdate();
            };

            MiniMenuPanel clearMenu = new MiniMenuPanel(clear);

            CheckBox ClearDone = new CheckBox { Text = "Done", Checked = Properties.Settings.Default.ClearDone, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            CheckBox ClearSleeping = new CheckBox { Text = "Sleeping", Checked = Properties.Settings.Default.ClearSleeping, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            CheckBox ClearDownloading = new CheckBox { Text = "Downloading", Checked = Properties.Settings.Default.ClearDownloading, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            CheckBox ClearError = new CheckBox { Text = "Error", Checked = Properties.Settings.Default.ClearErrors, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            CheckBox ClearQueued = new CheckBox { Text = "Queued", Checked = Properties.Settings.Default.ClearQueued, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };

            ClearDone.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ClearDone = ClearDone.Checked;
                Properties.Settings.Default.Save();
            };

            ClearSleeping.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ClearSleeping = ClearSleeping.Checked;
                Properties.Settings.Default.Save();
            };

            ClearDownloading.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ClearDownloading = ClearDownloading.Checked;
                Properties.Settings.Default.Save();
            };

            ClearQueued.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ClearQueued = ClearQueued.Checked;
                Properties.Settings.Default.Save();
            };

            ClearError.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ClearErrors = ClearError.Checked;
                Properties.Settings.Default.Save();
            };

            clearMenu.AddRow(new Control[] { ClearDone });
            clearMenu.AddRow(new Control[] { ClearSleeping });
            clearMenu.AddRow(new Control[] { ClearDownloading });
            clearMenu.AddRow(new Control[] { ClearQueued });
            clearMenu.AddRow(new Control[] { ClearError });

            Button pauseResume = new Button
            {
                Size = new Size(100, 35),
                Text = "Pause",
                TextAlign = ContentAlignment.MiddleCenter
            };
            pauseResume.Click += (e, r) =>
            {
                if (_pauseEvent.IsSet)
                {
                    PauseDownloads();
                    pauseResume.Text = "Resume";
                }
                else
                {
                    ResumeDownloads();
                    pauseResume.Text = "Pause";
                }
            };

            Button cancel = new Button
            {
                Size = new Size(100, 35),
                Text = "Cancel",
                TextAlign = ContentAlignment.MiddleCenter
            };
            cancel.Click += (e, r) =>
            {
                var result = MessageBox.Show(
                    "Cancel all active downloads?\nQueued and downloading URLs will be reset to Sleeping.",
                    "Cancel Downloads",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result != DialogResult.Yes) return;

                // Si estaba pausado, resumir para que los workers no queden colgados
                bool wasPaused = !_pauseEvent.IsSet;
                if (wasPaused)
                {
                    ResumeDownloads();
                    pauseResume.Text = "Pause";
                }

                StopDownloads();
                StartWorkers();

                // Resetear rows que quedaron en Queued o Downloading a Sleeping
                foreach (DataGridViewRow row in _urls.Rows)
                {
                    if (row.IsNewRow) continue;
                    string status = row.Cells["Status"].Value?.ToString();
                    if (status == "Queued" || status == "Downloading")
                        UpdateRowStatus(row, "Sleeping");
                }

                UrlsStatusUpdate();
            };

            Button export = new Button { Size = new Size(90, 35), Text = "Export" };

            export.Click += (s, e) =>
            {
                List<string> urls = new List<string>();

                foreach (DataGridViewRow row in _urls.Rows)
                {
                    if (row.IsNewRow) continue;

                    string status = row.Cells["Status"].Value?.ToString();

                    bool include =
                        (status == "Done" && Properties.Settings.Default.ExportDone) ||
                        (status == "Sleeping" && Properties.Settings.Default.ExportSleeping) ||
                        (status == "Downloading" && Properties.Settings.Default.ExportDownloading) ||
                        (status == "Queued" && Properties.Settings.Default.ExportQueued) ||
                        (status == "Error" && Properties.Settings.Default.ExportErrors);

                    if (!include) continue;

                    string url = row.Cells["URL"].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(url))
                        urls.Add(url);
                }

                if (urls.Count == 0)
                {
                    Notifications.Show("Nothing to export", "There are no URL's to export");
                    return;
                }

                string filename = $"export-{DateTime.Now:yyyy-MM-dd_HH.mm.ss}.txt";
                string exportDir = Path.Combine(Environment.CurrentDirectory, "Exports");
                Directory.CreateDirectory(exportDir);
                string fullPath = Path.Combine(exportDir, filename);
                File.WriteAllLines(fullPath, urls);

                Notifications.Show(
                    "Exported Successfully",
                    $"Exported to {filename}",
                    ToastDuration.Short,
                    new Dictionary<string, string>
                    {
                        { "action", "export" },
                        { "path", fullPath }
                    }
                );
            };

            MiniMenuPanel exportMenu = new MiniMenuPanel(export);

            CheckBox ExportDone = new CheckBox { Text = "Done", Checked = Properties.Settings.Default.ExportDone, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            CheckBox ExportSleeping = new CheckBox { Text = "Sleeping", Checked = Properties.Settings.Default.ExportSleeping, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            CheckBox ExportDownloading = new CheckBox { Text = "Downloading", Checked = Properties.Settings.Default.ExportDownloading, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            CheckBox ExportError = new CheckBox { Text = "Error", Checked = Properties.Settings.Default.ExportErrors, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            CheckBox ExportQueued = new CheckBox { Text = "Queued", Checked = Properties.Settings.Default.ExportQueued, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };

            ExportDone.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ExportDone = ExportDone.Checked;
                Properties.Settings.Default.Save();
            };

            ExportSleeping.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ExportSleeping = ExportSleeping.Checked;
                Properties.Settings.Default.Save();
            };

            ExportDownloading.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ExportDownloading = ExportDownloading.Checked;
                Properties.Settings.Default.Save();
            };

            ExportQueued.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ExportQueued = ExportQueued.Checked;
                Properties.Settings.Default.Save();
            };

            ExportError.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.ExportErrors = ExportError.Checked;
                Properties.Settings.Default.Save();
            };

            exportMenu.AddRow(new Control[] { ExportDone });
            exportMenu.AddRow(new Control[] { ExportSleeping });
            exportMenu.AddRow(new Control[] { ExportDownloading });
            exportMenu.AddRow(new Control[] { ExportQueued });
            exportMenu.AddRow(new Control[] { ExportError });

            Control[] Down = new Control[] { export, clear, pauseResume, cancel, autoStart, start };


            urlpnl.SetMainControl(_urls);
            urlpnl.AddDownControls(Down);

            return urlpnl;
        }

        private void ConfigNav(MiniPanel reference)
        {
            Button AppC = new Button { Size = new Size(170, 35), Text = "Configuration", TextAlign = ContentAlignment.MiddleCenter };
            AppC.Click += (s, e) => { MiniPanelManager.Show(AppConfiguration); };

            Button AppP = new Button { Size = new Size(170, 35), Text = "Personalization", TextAlign = ContentAlignment.MiddleCenter };
            AppP.Click += (s, e) => { MiniPanelManager.Show(AppPersonalization); };

            Button AppD = new Button { Size = new Size(170, 35), Text = "Dependencies", TextAlign = ContentAlignment.MiddleCenter };
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
            Control[] SDownloads = { new Label { Text = "Simultaneous Downloads", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, SDNud};

            NumericUpDown AutoDeleteDone = new NumericUpDown { Minimum = -1, Maximum = 1000, Value = Properties.Settings.Default.EraseDone, Width = 80 };
            AutoDeleteDone.ValueChanged += (s, e) =>
            {
                Properties.Settings.Default.EraseDone = (int)AutoDeleteDone.Value;
                Properties.Settings.Default.Save();
            };
            Control[] AutoDelete = { new Label { Text = "Auto Delete Done Downloads After", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, AutoDeleteDone, 
                new Label { Text = "Seconds (-1 To never)", TextAlign = ContentAlignment.MiddleLeft, Size = new Size(160, 35) } };

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
        private MiniPanel AppPersonalizationBuild()
        {
            MiniPanel AppCustom = new MiniPanel(false, true);

            ConfigNav(AppCustom);

            var rows = Properties.Settings.Default.Properties
            .Cast<SettingsProperty>()
            .Where(p => p.PropertyType == typeof(Color) && p.Name.Contains("Color"))
            .OrderBy(p => p.Name)
            .Select(p => RowColor(
            FormatName(p.Name),
            () => (Color)Properties.Settings.Default[p.Name],
            c => Properties.Settings.Default[p.Name] = c
            ));

            Label Text = new Label { Text = "Main Font", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };

            Label Preview = new Label
            {
                Size = new Size(35, 35),
                Font = Properties.Settings.Default.MainFont,
                Text = "A",
                TextAlign = ContentAlignment.MiddleCenter
            };

            Button change = new Button { Text = "Change", AutoSize = true };

            change.Click += (s, e) =>
            {
                using (FontDialog fd = new FontDialog())
                {
                    fd.Font = Properties.Settings.Default.MainFont;

                    if (fd.ShowDialog() == DialogResult.OK)
                    {
                        Preview.Font = fd.Font;
                        Properties.Settings.Default.MainFont = fd.Font;
                        Properties.Settings.Default.Save();
                    }
                }
            };

            Control[] Font = { Text, Preview, change };

            AppCustom.AddRow(Font);

            foreach (var row in rows)
                AppCustom.AddRow(row);

            return AppCustom;
        }

        private void ArgsNav(MiniPanel reference)
        {
            Button gdl = new Button { Size = new Size(170, 35), Text = "Gallery-DL Arguments", TextAlign = ContentAlignment.MiddleCenter };
            Button ytdlp = new Button { Size = new Size(170, 35), Text = "YT-dlp Arguments", TextAlign = ContentAlignment.MiddleCenter };
            Button alias = new Button { Size = new Size(170, 35), Text = "Alias Override", TextAlign = ContentAlignment.MiddleCenter };
            Button ratelimit = new Button { Size = new Size(170, 35), Text = "Rate Limiter", TextAlign = ContentAlignment.MiddleCenter };
            Button ytsites = new Button { Size = new Size(170, 35), Text = "YT-dlp Sites", TextAlign = ContentAlignment.MiddleCenter };

            gdl.Click += (s, e) => MiniPanelManager.Show(Arguments_GDL);
            ytdlp.Click += (s, e) => MiniPanelManager.Show(Arguments_YTDL);
            alias.Click += (s, e) => MiniPanelManager.Show(Alias_Override);
            ratelimit.Click += (s, e) => MiniPanelManager.Show(Rate_Limiter);
            ytsites.Click += (s, e) => MiniPanelManager.Show(YtSites_Panel);

            reference.AddUpControls(new Control[] { gdl, ytdlp, alias, ratelimit, ytsites });
            reference.AddRow(new Control[] { new Label() });
        }

        private MiniPanel Args_GDL()
        {
            MiniPanel panel = new MiniPanel(false, true);
            ArgsNav(panel);

            foreach (var arg in Manager.GalleryDLArgs.Profile.All())
                panel.AddRow(ArgumentRow(arg, () => Manager.GalleryDLArgs.Save()));

            return panel;
        }

        private MiniPanel Args_YTDL()
        {
            MiniPanel panel = new MiniPanel(false, true);
            ArgsNav(panel);

            foreach (var arg in Manager.YTDLPArgs.Profile.All())
                panel.AddRow(ArgumentRow(arg, () => Manager.YTDLPArgs.Save()));

            return panel;
        }
        private MiniPanel Alias()
        {
            MiniPanel panel = new MiniPanel(true, true);
            ArgsNav(panel);

            DataGridView dgv = new DataGridView
            {
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

            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "From", HeaderText = "From" });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "To", HeaderText = "To" });

            // Cargar entradas existentes
            foreach (var a in Manager.Filter.Engine.Entries.Where(e => e.Type == "alias"))
                dgv.Rows.Add(a.From, a.To);

            void SaveAliases()
            {
                // Eliminar todas las entradas alias del engine y reemplazar
                var entries = Manager.Filter.Engine.Entries
                    .Where(e => e.Type != "alias")
                    .ToList();

                Manager.Filter.Engine.Clear();

                foreach (var e in entries)
                {
                    switch (e.Type)
                    {
                        case "ratelimit":
                            Manager.Filter.Engine.Register(
                                new Manager.Filter.SiteRateLimit(e.Site, int.Parse(e.Replace)), e);
                            break;
                    }
                }

                foreach (DataGridViewRow row in dgv.Rows)
                {
                    if (row.IsNewRow) continue;
                    string from = row.Cells["From"].Value?.ToString()?.Trim();
                    string to = row.Cells["To"].Value?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;

                    var entry = new Manager.Filter.Entry { Type = "alias", From = from, To = to };
                    Manager.Filter.Engine.Register(new Manager.Filter.SiteAlias(from, to), entry);
                }

                Manager.Filter.Saver.Save("filters.json");
            }

            dgv.CellEndEdit += (s, e) => SaveAliases();

            Button add = new Button { Size = new Size(100, 35), Text = "Add" };
            add.Click += (s, e) =>
            {
                dgv.Rows.Add("site", "alias");
                SaveAliases();
            };

            Button delete = new Button { Size = new Size(100, 35), Text = "Delete" };
            delete.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count > 0 && !dgv.SelectedRows[0].IsNewRow)
                {
                    dgv.Rows.Remove(dgv.SelectedRows[0]);
                    SaveAliases();
                }
            };

            panel.SetMainControl(dgv);
            panel.AddDownControls(new Control[] { delete, add });

            return panel;
        }

        private MiniPanel Rate()
        {
            MiniPanel panel = new MiniPanel(true, true);
            ArgsNav(panel);

            DataGridView dgv = new DataGridView
            {
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

            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Site", HeaderText = "Site" });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Limit", HeaderText = "Limit" });

            foreach (var r in Manager.Filter.Engine.Entries.Where(e => e.Type == "ratelimit"))
                dgv.Rows.Add(r.Site, r.Replace);

            void SaveRates()
            {
                var entries = Manager.Filter.Engine.Entries
                    .Where(e => e.Type != "ratelimit")
                    .ToList();

                Manager.Filter.Engine.Clear();

                foreach (var e in entries)
                {
                    switch (e.Type)
                    {
                        case "alias":
                            Manager.Filter.Engine.Register(
                                new Manager.Filter.SiteAlias(e.From, e.To), e);
                            break;
                    }
                }

                foreach (DataGridViewRow row in dgv.Rows)
                {
                    if (row.IsNewRow) continue;
                    string site = row.Cells["Site"].Value?.ToString()?.Trim();
                    string limit = row.Cells["Limit"].Value?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(site) || !int.TryParse(limit, out int lim)) continue;

                    var entry = new Manager.Filter.Entry { Type = "ratelimit", Site = site, Replace = limit };
                    Manager.Filter.Engine.Register(new Manager.Filter.SiteRateLimit(site, lim), entry);
                }

                Manager.Filter.Saver.Save("filters.json");
            }

            dgv.CellEndEdit += (s, e) => SaveRates();

            Button add = new Button { Size = new Size(100, 35), Text = "Add" };
            add.Click += (s, e) =>
            {
                dgv.Rows.Add("site", "2");
                SaveRates();
            };

            Button delete = new Button { Size = new Size(100, 35), Text = "Delete" };
            delete.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count > 0 && !dgv.SelectedRows[0].IsNewRow)
                {
                    dgv.Rows.Remove(dgv.SelectedRows[0]);
                    SaveRates();
                }
            };

            panel.SetMainControl(dgv);
            panel.AddDownControls(new Control[] { delete, add });

            return panel;
        }

        private MiniPanel YtSitesPanel()
        {
            MiniPanel panel = new MiniPanel(true, true);
            ArgsNav(panel);

            DataGridView dgv = new DataGridView
            {
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

            dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "Site", HeaderText = "Site (uses yt-dlp)" });

            // Cargar defaults + los registrados por filtros
            foreach (var site in Manager.Downloader.YtSites)
                dgv.Rows.Add(site);

            void SaveYtSites()
            {
                // Reconstruir: quitar todos los ytsite entries, re-registrar desde DGV
                var entries = Manager.Filter.Engine.Entries
                    .Where(e => e.Type != "ytsite")
                    .ToList();

                Manager.Filter.Engine.Clear();

                foreach (var e in entries)
                {
                    switch (e.Type)
                    {
                        case "alias":
                            Manager.Filter.Engine.Register(new Manager.Filter.SiteAlias(e.From, e.To), e);
                            break;
                        case "ratelimit":
                            Manager.Filter.Engine.Register(new Manager.Filter.SiteRateLimit(e.Site, int.Parse(e.Replace)), e);
                            break;
                    }
                }

                // Limpiar y reconstruir el HashSet desde el DGV
                foreach (var site in Manager.Downloader.YtSites.ToList())
                    Manager.Downloader.UnregisterYtSite(site);

                foreach (DataGridViewRow row in dgv.Rows)
                {
                    if (row.IsNewRow) continue;
                    string site = row.Cells["Site"].Value?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(site)) continue;

                    var entry = new Manager.Filter.Entry { Type = "ytsite", Site = site };
                    Manager.Filter.Engine.Register(new Manager.Filter.YtSite(site), entry);
                }

                Manager.Filter.Saver.Save("filters.json");
            }

            dgv.CellEndEdit += (s, e) => SaveYtSites();

            Button add = new Button { Size = new Size(100, 35), Text = "Add" };
            add.Click += (s, e) =>
            {
                dgv.Rows.Add("site");
                SaveYtSites();
            };

            Button delete = new Button { Size = new Size(100, 35), Text = "Delete" };
            delete.Click += (s, e) =>
            {
                if (dgv.SelectedRows.Count > 0 && !dgv.SelectedRows[0].IsNewRow)
                {
                    dgv.Rows.Remove(dgv.SelectedRows[0]);
                    SaveYtSites();
                }
            };

            panel.SetMainControl(dgv);
            panel.AddDownControls(new Control[] { delete, add });

            return panel;
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
        MiniPanel Arguments_GDL;
        MiniPanel Arguments_YTDL;
        MiniPanel Alias_Override;
        MiniPanel Rate_Limiter;
        MiniPanel YtSites_Panel;

        private async void Main_Load(object sender, EventArgs e)
        {
            await GetDependenciesVersions();
            DependenciesCheck();
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.Save();
            Filter.Saver.Save("filters.json");
        }
    }
}
