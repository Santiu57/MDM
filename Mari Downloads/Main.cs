using Microsoft.Toolkit.Uwp.Notifications;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using WK.Libraries.SharpClipboardNS;
using static Mari_Downloads.Manager;

namespace Mari_Downloads
{
    public partial class Main : Form
    {
        public Main()
        {
            //Notification Actions
            ToastNotificationManagerCompat.History.Clear();
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                ToastArguments args = ToastArguments.Parse(toastArgs.Argument);

                Application.OpenForms?[0]?.BeginInvoke(new Action(() =>
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
                    if (args.Contains("action") && args["action"] == "update")
                    {
                        MiniPanelManager.Show(AppDependencies);
                    }
                }));
            };

            InitializeComponent();

            Media.Check();
            MiniPanelManager.SetHost(this);

            if (string.IsNullOrEmpty(Properties.Settings.Default.DownloadPath))
                Properties.Settings.Default.DownloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "gallery-dl");
            if (string.IsNullOrEmpty(Properties.Settings.Default.YTOutput))
                Properties.Settings.Default.YTOutput = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "gallery-dl", "youtube");
            if (Properties.Settings.Default.MainFont.Size <= 6)
                Properties.Settings.Default.MainFont = new Font(Properties.Settings.Default.MainFont.Name, 7);

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
            RegexFilters_Panel = RegexFilters();

            BaseComponents();
            SetWindowConfig();

            AppCustomization.FontChange(this, Properties.Settings.Default.MainFont);
            AppCustomization.ColorComponents(this, Properties.Settings.Default.MainBackColor, Properties.Settings.Default.MainForeColor);

            AppCustomization.EnsureAutoSize(this);

            _downloadChannel = Channel.CreateUnbounded<(Manager.Url, DataGridViewRow)>();

            _maxDownloads = Properties.Settings.Default.SimultaneousDownloads;
            _semaphore = new SemaphoreSlim(_maxDownloads);
            StartWorkers();

            MiniPanelManager.PreloadAll();

            MiniPanelManager.Show(UrlPnl);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
    IntPtr hwnd,
    int attr,
    ref int attrValue,
    int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            int dark = 1;

            DwmSetWindowAttribute(
                Handle,
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref dark,
                sizeof(int));
        }

        private void SetWindowConfig()
        {
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw,
                true);

            FormBorderStyle = FormBorderStyle.Sizable;

            MinimumSize = new Size(400, 300);

            BackColor = Color.FromArgb(40, 40, 40);

            Padding = new Padding(1);

            Text = "(ᓀ‸ᓂ)";
            Icon = new Icon(Path.Combine(Application.StartupPath, "media", "icon.ico"));
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

                BeginInvoke(() =>
                {
                    if (RepetedUrl(url))
                        return;

                    url.status.Change(Manager.Status.StatusType.Pending);

                    int index = _urls.Rows.Add(url.status.GetDisplay(), url.site, url.url);
                    var row = _urls.Rows[index];
                    ColorFuncs.ApplyRowColor(row, url.status.GetDisplay());
                });
            });
        }

        private void UpdateRowStatus(DataGridViewRow row, string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => UpdateRowStatus(row, status));
                return;
            }

            if (!IsRowAlive(row) || row.Cells["Status"].Value == "Done") return;

            row.Cells["Status"].Value = status;
            ColorFuncs.ApplyRowColor(row, status);

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
                                BeginInvoke(() =>
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
            if (_statusLabel == null) return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => _statusLabel.Text = text));
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
            _workers.RemoveAll(t => t.IsCompleted);

            int target = 20;
            int missing = target - _workers.Count;

            for (int i = 0; i < missing; i++)
                _workers.Add(Task.Run(WorkerLoop));
        }

        private async Task WorkerLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var job = await _downloadChannel.Reader.ReadAsync(_cts.Token);

                    _pauseEvent.Wait(_cts.Token);

                    SemaphoreSlim currentSemaphore;
                    lock (_semaphoreLock)
                        currentSemaphore = _semaphore;

                    await currentSemaphore.WaitAsync(_cts.Token);

                    var siteSemaphore = Manager.SiteRateLimiter.Get(job.url.site);

                    await siteSemaphore.WaitAsync(_cts.Token);

                    try
                    {
                        UpdateRowStatus(job.row, "Downloading");
                        UrlsStatusUpdate();

                        var outputBuilder = new StringBuilder();

                        var (exit, output, command) =
                            await Manager.Downloader.Run(job.url, (cmd, line) =>
                            {
                                lock (outputBuilder)
                                {
                                    outputBuilder.AppendLine(line);

                                    if (IsRowAlive(job.row))
                                    {
                                        string currentOutput =
                                            $"COMMAND:{Environment.NewLine}{cmd}" +
                                            $"{Environment.NewLine}{Environment.NewLine}" +
                                            $"OUTPUT:{Environment.NewLine}{outputBuilder}";

                                        BeginInvoke(() =>
                                        {
                                            job.row.Tag = currentOutput;

                                            if (_openOutputs.TryGetValue(job.row, out var form))
                                                form.SetText(currentOutput);
                                        });
                                    }
                                }
                            });

                        if (_cts.IsCancellationRequested)
                        {
                            UpdateRowStatus(job.row, "Sleeping");
                            continue;
                        }

                        if (exit != 0)
                        {
                            UpdateRowStatus(job.row, "Error");
                        }
                        else
                        {
                            UpdateRowStatus(job.row, "Done");
                            Log.Save(job.url);
                            SesionUrls++;
                            _sesionUrls.Text = $"{SesionUrls}";
                        }

                        UrlsStatusUpdate();
                    }
                    catch
                    {
                        UpdateRowStatus(job.row, "Error");
                    }
                    finally
                    {
                        siteSemaphore.Release();
                        currentSemaphore.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        }

        private async Task StopDownloadsAsync()
        {
            _cts.Cancel();
            JobManager.Kill();

            try { await Task.WhenAll(_workers); }
            catch { }

            _workers.Clear();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            _pauseEvent.Set();
        }

        public void PauseDownloads() => _pauseEvent.Reset();
        public void ResumeDownloads() => _pauseEvent.Set();

        private void Start()
        {
            foreach (DataGridViewRow row in _urls.Rows)
                DownloadRow(row);
        }

        private void DownloadRow(DataGridViewRow row)
        {
            if (row.IsNewRow) return;

            string status = row.Cells["Status"].Value?.ToString();

            if (status == "Sleeping")
            {
                string url = row.Cells["Url"].Value?.ToString();
                var u = new Manager.Url(url);

                row.Cells["Site"].Value = u.ExtractSiteName();

                UpdateRowStatus(row, "Queued");
                _downloadChannel.Writer.TryWrite((u, row));
            }

            UrlsStatusUpdate();
        }

        private void UrlsStatusUpdate()
        {
            if (InvokeRequired)
            {
                BeginInvoke(UrlsStatusUpdate);
                return;
            }

            int[] count = UrlCount();
            StatusChange($"Total: {count[0]} | Sleeping: {count[1]} | Downloading: {count[2]} | Done: {count[3]} | Queued: {count[4]} | Errors: {count[5]}");
        }

        private void Scrollbar(int ease)
        {
            if (_urls.Rows.Count == 0)
                return;

            int current = _urls.FirstDisplayedScrollingRowIndex;
            int lastRow = _urls.Rows.Count - 1;
            int target = lastRow + ease;

            if (current++ == lastRow)
            {
                _urls.FirstDisplayedScrollingRowIndex = lastRow;
                return;
            }
            else if (current == 0)
            {
                _urls.FirstDisplayedScrollingRowIndex = 0;
                return;
            }
            else
            {
                target = current + ease;
            }

            if (target < 0) target = 0;
            if (target > lastRow) target = lastRow;
            if (target > 1) _urls.FirstDisplayedScrollingRowIndex = target;
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
            string galleryInstalled = null, galleryLatest = null;
            string ytdlpInstalled = null, ytdlpLatest = null;

            try
            {
                galleryInstalled = GetInstalledVersion("gallery-dl", "--version");
                galleryLatest = Packages.GetLatestPipVersion("gallery-dl");
                ytdlpInstalled = GetInstalledVersion("yt-dlp", "--version");
                ytdlpLatest = Packages.GetLatestPipVersion("yt-dlp");
            }
            catch (Exception e)
            {
                ScrollableMessageBox.Show(e.Message, "Error");
            }

            return new[] { galleryInstalled, galleryLatest, ytdlpInstalled, ytdlpLatest };
        }

        private string GetInstalledVersion(string exe, string args)
        {
            var info = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = info };
            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            var match = Regex.Match(output, @"\d+\.\d+\.\d+");
            return match.Success ? match.Value : null;
        }

        private void DependenciesCheck()
        {
            var GDLstate = FindControl<Label>(AppDependencies, l => l.Name == "GDLState");
            var YTstate = FindControl<Label>(AppDependencies, l => l.Name == "YTState");

            ApplyDependencyState(GDLstate, _galleryInstalled, _galleryLatest, "gallery-dl");
            ApplyDependencyState(YTstate, _ytdlpInstalled, _ytdlpLatest, "YT-dlp");
        }

        /// <summary>
        /// Aplica el estado visual (instalado / desactualizado / no encontrado) a un label
        /// y muestra notificación si hay update disponible.
        /// </summary>
        private void ApplyDependencyState(Label stateLabel, string installed, string latest, string packageName)
        {
            if (installed != null && latest != null)
            {
                if (new Version(installed) < new Version(latest))
                {
                    Notifications.Show(
                        "Update Available",
                        $"{packageName} update available{Environment.NewLine}Installed: {installed}{Environment.NewLine}Latest: {latest}",
                        Notifications.Type.NotifType.Dependencies,
                        ToastDuration.Long,
                        new Dictionary<string, string> { { "action", "update" } }
                    );
                    stateLabel.Text = $"{installed}: Outdated.{Environment.NewLine}{latest}: Latest.";
                    stateLabel.ForeColor = Color.Red;
                }
                else
                {
                    stateLabel.Text = $"{installed}: Latest ✓";
                    stateLabel.ForeColor = Color.Green;
                }
            }
            else
            {
                stateLabel.Text = "Couldn't obtain";
                stateLabel.ForeColor = Color.Yellow;
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

        private string FormatName(string name)
            => Regex.Replace(name, "(\\B[A-Z])", " $1");

        // ─── Base ────────────────────────────────────────────────────────────────────

        private void BaseComponents()
        {
            ToolStrip tools = new ToolStrip { Dock = DockStyle.Left };

            ToolStripButton UrlsShow = new ToolStripButton { Image = Media.Get("Icon") };
            UrlsShow.Click += (_, _) => MiniPanelManager.Show(UrlPnl);

            ToolStripButton Config = new ToolStripButton { Image = Media.Get("Config"), Alignment = ToolStripItemAlignment.Right };
            Config.Click += (_, _) => MiniPanelManager.Show(MiniPanelManager.ConfigLast ?? AppConfiguration);

            ToolStripButton Args = new ToolStripButton { Image = Media.Get("Icon") };
            Args.Click += (_, _) => MiniPanelManager.Show(MiniPanelManager.ArgsLast ??  Arguments_GDL);

            ToolStripButton Filters = new ToolStripButton { Image = Media.Get("Icon") };
            Filters.Click += (_, _) => MiniPanelManager.Show(MiniPanelManager.FiltersLast ?? Alias_Override);

            tools.Items.Add(UrlsShow);
            tools.Items.Add(Args);
            tools.Items.Add(Config);
            tools.Items.Add(Filters);

            _statusStrip = new StatusStrip
            {
                Name = "StatusStrip",
                Font = Properties.Settings.Default.MainFont,
                LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow
            };

            _statusLabel = new ToolStripStatusLabel { Name = "Status", Text = "Sleeping...", AutoSize = true };

            _sesionUrls = new ToolStripStatusLabel
            {
                Alignment = ToolStripItemAlignment.Right,
                Text = "No downloads so far",
                AutoSize = true
            };

            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Items.Add(_sesionUrls);

            SharpClipboard clipboard = new SharpClipboard();
            clipboard.ObservableFormats.All = false;
            clipboard.ObservableFormats.Texts = true;

            clipboard.ClipboardChanged += (_, e) =>
            {
                if (e.Content is not string text) return;

                Task.Run(() =>
                {
                    var urls = Manager.Url.UrlExtractor(text).Distinct().ToList();
                    if (urls.Count == 0) return;

                    BeginInvoke(() =>
                    {
                        foreach (var url in urls)
                            AddUrl(url);
                    });
                });
            };

            this.Controls.Add(tools);
            this.Controls.Add(_statusStrip);
        }

        // ─── Helpers reutilizables ────────────────────────────────────────────────────

        private static void SaveSettings() => Properties.Settings.Default.Save();

        private static Button MakeButton(
            string text,
            EventHandler? click = null,
            Size? size = null,
            Image backgroundImage = null,
            string name = null)
        {
            var btn = new Button
            {
                Name = name ?? string.Empty,
                Text = text,
                Size = size ?? new Size(170, 35),
                MinimumSize = size ?? new Size(170, 35),
                TextAlign = ContentAlignment.MiddleCenter
            };

            if (backgroundImage != null)
            {
                btn.BackgroundImage = backgroundImage;
                btn.BackgroundImageLayout = ImageLayout.Stretch;
            }

            if (click != null)
                btn.Click += click;

            return btn;
        }

        private static Label MakeLabel(
            string text,
            Size? size = null,
            bool auto = false,
            ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            return new Label
            {
                Text = text,
                Size = size ?? Size.Empty,
                AutoSize = auto,
                TextAlign = align
            };
        }

        private static NumericUpDown MakeNud(
            decimal min,
            decimal max,
            decimal value,
            EventHandler? onChange = null,
            int width = 80)
        {
            var nud = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = width };
            if (onChange != null) nud.ValueChanged += onChange;
            return nud;
        }

        /// <summary>
        /// Crea un DataGridView con configuración estándar.
        /// </summary>
        private static DataGridView MakeDgv(
            bool allowAddRows = false,
            bool multiSelect = false,
            params (string name, string header, bool autoFill)[] columns)
        {
            var dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = multiSelect,
                AllowUserToAddRows = allowAddRows
            };

            foreach (var (name, header, autoFill) in columns)
            {
                dgv.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = name,
                    HeaderText = header,
                    AutoSizeMode = autoFill
                        ? DataGridViewAutoSizeColumnMode.AllCells
                        : DataGridViewAutoSizeColumnMode.Fill
                });
            }

            return dgv;
        }

        /// <summary>
        /// CheckBox enlazado a Settings.
        /// </summary>
        private static CheckBox MakeSettingsCheckBox(
            string text,
            Func<bool> getter,
            Action<bool> setter)
        {
            var cb = new CheckBox
            {
                Text = text,
                Checked = getter(),
                TextAlign = ContentAlignment.MiddleLeft,
                CheckAlign = ContentAlignment.MiddleLeft,
                AutoSize = true
            };

            cb.CheckedChanged += (_, _) => { setter(cb.Checked); SaveSettings(); };
            return cb;
        }

        /// <summary>
        /// Construye un MiniMenuPanel con CheckBoxes de Settings.
        /// </summary>
        private static MiniMenuPanel MakeSettingsMenu(
            Button anchor,
            params (string text, Func<bool> getter, Action<bool> setter)[] items)
        {
            var menu = new MiniMenuPanel(anchor);

            foreach (var item in items)
                menu.AddRow([MakeSettingsCheckBox(item.text, item.getter, item.setter)]);

            return menu;
        }

        /// <summary>
        /// Construye panel genérico de filtros.
        /// </summary>
        private MiniPanel MakeFilterPanel(
            DataGridView dgv,
            Action onSave,
            object[] defaultAddRow)
        {
            var panel = new MiniPanel("Filter", true, true);

            FiltersNav(panel);

            dgv.CellEndEdit += (_, _) => onSave();

            var add = MakeButton("Add", (_, _) =>
            {
                dgv.Rows.Add(defaultAddRow);
                onSave();
            }, new Size(100, 35));

            var delete = MakeButton("Delete", (_, _) =>
            {
                if (dgv.SelectedRows.Count <= 0) return;
                var row = dgv.SelectedRows[0];
                if (row.IsNewRow) return;
                dgv.Rows.Remove(row);
                onSave();
            }, new Size(100, 35));

            panel.SetMainControl(dgv);
            panel.AddDownControls([delete, add]);

            return panel;
        }

        // ─── Helpers de filtros ───────────────────────────────────────────────────────

        private void RebuildFilterEngine(string excludeType, Action registerExcluded)
        {
            var keep = Manager.Filter.Engine.Entries
                .Where(e => e.Type != excludeType)
                .ToList();

            Manager.Filter.Engine.Clear();

            foreach (var e in keep)
                ReRegisterEntry(e);

            registerExcluded();

            Manager.Filter.Saver.Save("filters.json");
        }

        private static void ReRegisterEntry(Manager.Filter.Entry e)
        {
            switch (e.Type)
            {
                case "alias":
                    Manager.Filter.Engine.Register(new Manager.Filter.SiteAlias(e.From, e.To), e);
                    break;
                case "ratelimit":
                    Manager.Filter.Engine.Register(new Manager.Filter.SiteRateLimit(e.Site, e.Limit), e);
                    break;
                case "ytsite":
                    Manager.Filter.Engine.Register(new Manager.Filter.YtSite(e.Site), e);
                    break;
                case "regex":
                    Manager.Filter.Engine.Register(new Manager.Filter.RegexUrlRewrite(e.From, e.Replace, e.Site), e);
                    break;
            }
        }

        // ─── Menús reutilizables ──────────────────────────────────────────────────────

        private MiniMenuPanel MakeClearMenu(Button anchor)
        {
            var s = Properties.Settings.Default;
            return MakeSettingsMenu(anchor,
                ("Done", () => s.ClearDone, v => s.ClearDone = v),
                ("Sleeping", () => s.ClearSleeping, v => s.ClearSleeping = v),
                ("Downloading", () => s.ClearDownloading, v => s.ClearDownloading = v),
                ("Queued", () => s.ClearQueued, v => s.ClearQueued = v),
                ("Error", () => s.ClearErrors, v => s.ClearErrors = v));
        }

        private MiniMenuPanel MakeExportMenu(Button anchor)
        {
            var s = Properties.Settings.Default;
            return MakeSettingsMenu(anchor,
                ("Done", () => s.ExportDone, v => s.ExportDone = v),
                ("Sleeping", () => s.ExportSleeping, v => s.ExportSleeping = v),
                ("Downloading", () => s.ExportDownloading, v => s.ExportDownloading = v),
                ("Queued", () => s.ExportQueued, v => s.ExportQueued = v),
                ("Error", () => s.ExportErrors, v => s.ExportErrors = v));
        }

        private MiniMenuPanel MakeRetryMenu(Button anchor)
        {
            var s = Properties.Settings.Default;
            return MakeSettingsMenu(anchor,
                ("Done", () => s.RetryDone, v => s.RetryDone = v),
                ("Error", () => s.RetryErrors, v => s.RetryErrors = v));
        }

        // ─── Panels ──────────────────────────────────────────────────────────────────

        private MiniPanel UrlsPanel()
        {
            var urlpnl = new MiniPanel("Urls", true);

            _urls = MakeDgv(false, false,
                ("Status", "Status", true),
                ("Site", "Site", true),
                ("Url", "Url", false));

            _urls.Name = "Urls";
            _urls.Columns["Status"].ReadOnly = true;
            _urls.Columns["Site"].ReadOnly = true;

            _urls.RowsAdded += (_, _) =>
            {
                UrlsStatusUpdate();
                Scrollbar(1);
                if (Properties.Settings.Default.AutoStart) Start();
            };

            _urls.RowsRemoved += (_, _) =>
            {
                UrlsStatusUpdate();
                Scrollbar(-1);
            };

            _urls.MouseClick += (_, e) =>
            {
                bool right = e.Button == MouseButtons.Right;
                var hit = _urls.HitTest(e.X, e.Y);

                if (hit.RowIndex < 0 || hit.ColumnIndex < 0) return;

                var row = _urls.Rows[hit.RowIndex];
                var col = _urls.Columns[hit.ColumnIndex].Name;
                string status = row.Cells["Status"].Value?.ToString();

                if (right)
                {
                    _urls.CancelEdit();
                    if (!IsRowAlive(row)) return;

                    bool safeToDelete = status == "Done" || status == "Error";

                    if (!safeToDelete)
                    {
                        string warning = status switch
                        {
                            "Downloading" =>
                                $"This URL is currently downloading.{Environment.NewLine}" +
                                $"Removing it will not stop the process, but the row will be gone.{Environment.NewLine}" +
                                $"Remove anyway?",
                            "Queued" =>
                                $"This URL is queued and will start soon.{Environment.NewLine}" +
                                $"Removing it will not cancel the download if it has already started.{Environment.NewLine}" +
                                $"Remove anyway?",
                            "Sleeping" =>
                                $"This URL hasn't started yet.{Environment.NewLine}Remove it?",
                            _ => "Remove this URL?"
                        };

                        var result = ScrollableMessageBox.Show(warning, "Remove URL", MessageBoxButtons.YesNo);
                        if (result != DialogResult.Yes) return;
                    }

                    _urls.Rows.RemoveAt(hit.RowIndex);
                }

                if (!right && col == "Status")
                {
                    if (!_openOutputs.TryGetValue(row, out var form) || form.IsDisposed)
                    {
                        form = new ScrollableMessageBox.OutputForm();
                        _openOutputs[row] = form;
                        form.FormClosed += (_, _) => _openOutputs.Remove(row);
                        form.Show();
                    }

                    form.SetText(row.Tag as string ?? "Nothing here yet.");
                }
            };

            _urls.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                var col = _urls.Columns[e.ColumnIndex];

                switch (col.Name)
                {
                    case "Url":
                        string url = _urls.Rows[e.RowIndex].Cells["Url"].Value?.ToString();
                        if (!string.IsNullOrWhiteSpace(url))
                            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        break;

                    case "Site":
                        string status = _urls.Rows[e.RowIndex].Cells["Status"].Value?.ToString();
                        var row = _urls.Rows[e.RowIndex];
                        if (status == "Error")
                        {
                            UpdateRowStatus(row, "Sleeping");
                            DownloadRow(row);
                        }
                        if (status == "Sleeping")
                            DownloadRow(row);
                        break;
                }
            };

            // ── Controles inferiores ──────────────────────────────────────────────────

            var autoStart = new CheckBox
            {
                Text = "Auto Start",
                TextAlign = ContentAlignment.MiddleCenter,
                Checked = Properties.Settings.Default.AutoStart,
                CheckAlign = ContentAlignment.MiddleLeft,
                Size = new Size(130, 35)
            };
            autoStart.CheckedChanged += (_, _) =>
            {
                Properties.Settings.Default.AutoStart = autoStart.Checked;
                SaveSettings();
            };

            var start = MakeButton("Start Downloads", (_, _) => Start());

            var clear = MakeButton(null, size: new Size(35, 35),
                backgroundImage: Media.Get("Clear"), name: "Clear");
            clear.Click += (_, _) =>
            {
                var s = Properties.Settings.Default;
                for (int i = _urls.Rows.Count - 1; i >= 0; i--)
                {
                    var row = _urls.Rows[i];
                    if (row.IsNewRow) continue;

                    string status = row.Cells["Status"].Value?.ToString();

                    bool remove =
                        (status == "Done" && s.ClearDone) ||
                        (status == "Sleeping" && s.ClearSleeping) ||
                        (status == "Downloading" && s.ClearDownloading) ||
                        (status == "Queued" && s.ClearQueued) ||
                        (status == "Error" && s.ClearErrors);

                    if (remove) _urls.Rows.RemoveAt(i);
                }
                UrlsStatusUpdate();
            };

            // Los menús usan MakeSettingsMenu via MakeClearMenu/MakeExportMenu/MakeRetryMenu
            _ = MakeClearMenu(clear);

            var pauseResume = MakeButton("Pause", size: new Size(100, 35));
            pauseResume.Click += (_, _) =>
            {
                if (_pauseEvent.IsSet) { PauseDownloads(); pauseResume.Text = "Resume"; }
                else { ResumeDownloads(); pauseResume.Text = "Pause"; }
            };

            var cancel = MakeButton("Cancel", size: new Size(100, 35));
            cancel.Click += async (_, _) =>
            {
                var result = ScrollableMessageBox.Show(
                    $"Cancel all queued and downloading Urls?{Environment.NewLine}" +
                    $"Downloading and Queued URLs will be reset to Sleeping.{Environment.NewLine}" +
                    $"Processing will be stopped.",
                    "Cancel Downloads",
                    MessageBoxButtons.YesNo);

                if (result != DialogResult.Yes) return;

                bool wasPaused = !_pauseEvent.IsSet;
                if (!wasPaused) { PauseDownloads(); pauseResume.Text = "Resume"; }

                await StopDownloadsAsync();

                foreach (DataGridViewRow row in _urls.Rows)
                {
                    if (row.IsNewRow) continue;
                    string status = row.Cells["Status"].Value?.ToString();
                    if (status != "Error" && status != "Done" && status != "Sleeping")
                        UpdateRowStatus(row, "Sleeping");
                }

                StartWorkers();
                UrlsStatusUpdate();
            };

            var export = MakeButton("Export", size: new Size(100, 35));
            export.Click += (_, _) =>
            {
                var s = Properties.Settings.Default;
                var urls = new List<string>();

                foreach (DataGridViewRow row in _urls.Rows)
                {
                    if (row.IsNewRow) continue;

                    string status = row.Cells["Status"].Value?.ToString();

                    bool include =
                        (status == "Done" && s.ExportDone) ||
                        (status == "Sleeping" && s.ExportSleeping) ||
                        (status == "Downloading" && s.ExportDownloading) ||
                        (status == "Queued" && s.ExportQueued) ||
                        (status == "Error" && s.ExportErrors);

                    if (!include) continue;

                    string url = row.Cells["Url"].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(url)) urls.Add(url);
                }

                if (urls.Count == 0)
                {
                    Notifications.Show("Nothing to export", "There are no URL's to export", Notifications.Type.NotifType.Export);
                    return;
                }

                string filename = $"{DateTime.Now:yyyy-MM-dd_HH.mm.ss}_Urls.{urls.Count}.txt";
                string exportDir = Path.Combine(Application.StartupPath, "Exports");
                Directory.CreateDirectory(exportDir);
                string fullPath = Path.Combine(exportDir, filename);
                File.WriteAllLinesAsync(fullPath, urls);

                Notifications.Show(
                    "Exported Successfully",
                    $"Exported to {filename}",
                    Notifications.Type.NotifType.Export,
                    ToastDuration.Short,
                    new Dictionary<string, string> { { "action", "export" }, { "path", fullPath } }
                );
            };
            _ = MakeExportMenu(export);

            var retry = MakeButton("Retry", size: new Size(90, 35));
            retry.Click += (_, _) =>
            {
                var s = Properties.Settings.Default;
                foreach (DataGridViewRow row in _urls.Rows)
                {
                    if (row.IsNewRow) continue;
                    string status = row.Cells["Status"].Value?.ToString();

                    bool shouldRetry =
                        (status == "Done" && s.RetryDone) ||
                        (status == "Error" && s.RetryErrors);

                    if (shouldRetry) UpdateRowStatus(row, "Sleeping");
                }
                Start();
            };
            _ = MakeRetryMenu(retry);

            urlpnl.SetMainControl(_urls);
            urlpnl.AddDownControls([retry, export, clear, pauseResume, cancel, autoStart, start]);

            return urlpnl;
        }

        private void ConfigNav(MiniPanel reference)
        {
            reference.AddUpControls([
                MakeButton("Configuration",   (_, _) => MiniPanelManager.Show(AppConfiguration)),
                MakeButton("Personalization", (_, _) => MiniPanelManager.Show(AppPersonalization)),
                MakeButton("Dependencies",    (_, _) => MiniPanelManager.Show(AppDependencies)),
                MakeButton("Notifications",   (_, _) => MiniPanelManager.Show(NotificationsConfig))
            ]);
        }

        private MiniPanel AppConfig()
        {
            var panel = new MiniPanel("Config", false, true);
            ConfigNav(panel);

            panel.AddRow([
                MakeLabel("Simultaneous Downloads", auto: true),
                MakeNud(1, 20, Properties.Settings.Default.SimultaneousDownloads, (s, _) =>
                    ChangeSimultaneousDownloads((int)((NumericUpDown)s).Value))
            ]);

            panel.AddRow([
                MakeLabel("Auto Delete Done Downloads After", auto: true),
                MakeNud(-1, 1000, Properties.Settings.Default.EraseDone, (s, _) =>
                {
                    Properties.Settings.Default.EraseDone = (int)((NumericUpDown)s).Value;
                    SaveSettings();
                }),
                MakeLabel("Seconds (-1 To never)", size: new Size(160, 35))
            ]);

            panel.AddRow([
                MakeSettingsCheckBox(
                    "Automatic Startup",
                    () => Properties.Settings.Default.AutoStartup,
                    value =>
                    {
                        Properties.Settings.Default.AutoStartup = value;
                        Startup.SetStartup(value);
                    })
            ]);

            return panel;
        }

        private MiniPanel AppDependenciesBuild()
        {
            var panel = new MiniPanel("Config", false, true);
            ConfigNav(panel);

            panel.AddRow(BuildDependencyRows("Gallery-Dl", "gallery-dl", "GDLState",
                () => _galleryInstalled, () => _galleryLatest));

            panel.AddRow(BuildDependencyRows("YT-dlp", "yt-dlp", "YTState",
                () => _ytdlpInstalled, () => _ytdlpLatest));

            return panel;
        }

        /// <summary>
        /// Construye el bloque de controles para una dependencia (label, estado, botones install/update).
        /// Devuelve un arreglo listo para AddRow.
        /// </summary>
        private Control[] BuildDependencyRows(
            string displayName,
            string packageName,
            string stateLabelName,
            Func<string> getInstalled,
            Func<string> getLatest)
        {
            // Fila de título
            var titleLabel = MakeLabel($"{displayName}:", size: new Size(100, 35), align: ContentAlignment.MiddleCenter);

            var stateLabel = new Label
            {
                Name = stateLabelName,
                Size = new Size(150, 35),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Obtaining...",
                ForeColor = Properties.Settings.Default.MainForeColor,
                Tag = "NoAutoColor"
            };

            var install = MakeButton($"Install {displayName}", size: new Size(140, 35));
            install.Click += async (_, _) =>
            {
                stateLabel.Text = "Installing...";
                if (await Task.Run(() => Packages.InstallPackage(packageName)))
                {
                    await GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show($"{displayName} Installed", $"{displayName} Installed Successfully",
                        Notifications.Type.NotifType.Dependencies);
                }
            };

            var update = MakeButton($"Update {displayName}", size: new Size(140, 35));
            update.Click += async (_, _) =>
            {
                stateLabel.Text = "Updating...";
                stateLabel.ForeColor = Properties.Settings.Default.MainForeColor;
                if (await Task.Run(() => Packages.UpdatePackage(packageName)))
                {
                    await GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show($"{displayName} Updated", $"{displayName} Updated Successfully",
                        Notifications.Type.NotifType.Dependencies);
                }
            };

            // Nota: AddRow sólo acepta un array, así que el llamador debe
            // llamar AddRow varias veces. Aquí devolvemos todos los controles
            // aplanados y el caller los agrega con múltiples AddRow.
            // Por simplicidad integramos todo en el método padre usando sobrecarga interna.
            return [titleLabel, stateLabel, install, update];
        }

        // Versión refactorizada real de AppDependenciesBuild que llama AddRow por separado
        // para respetar el layout original de filas.
        private MiniPanel AppDependenciesBuildImpl()
        {
            var panel = new MiniPanel("Config", false, true);
            ConfigNav(panel);

            AddDependencySection(panel, "Gallery-Dl", "gallery-dl", "GDLState");
            AddDependencySection(panel, "YT-dlp", "yt-dlp", "YTState");

            return panel;
        }

        private void AddDependencySection(MiniPanel panel, string displayName, string packageName, string stateLabelName)
        {
            var stateLabel = new Label
            {
                Name = stateLabelName,
                Size = new Size(150, 35),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Obtaining...",
                ForeColor = Properties.Settings.Default.MainForeColor,
                Tag = "NoAutoColor"
            };

            var install = MakeButton($"Install {displayName}", size: new Size(140, 35));
            install.Click += async (_, _) =>
            {
                stateLabel.Text = "Installing...";
                if (await Task.Run(() => Packages.InstallPackage(packageName)))
                {
                    await GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show($"{displayName} Installed", $"{displayName} Installed Successfully",
                        Notifications.Type.NotifType.Dependencies);
                }
            };

            var update = MakeButton($"Update {displayName}", size: new Size(140, 35));
            update.Click += async (_, _) =>
            {
                stateLabel.Text = "Updating...";
                stateLabel.ForeColor = Properties.Settings.Default.MainForeColor;
                if (await Task.Run(() => Packages.UpdatePackage(packageName)))
                {
                    await GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show($"{displayName} Updated", $"{displayName} Updated Successfully",
                        Notifications.Type.NotifType.Dependencies);
                }
            };

            panel.AddRow([MakeLabel($"{displayName}:", size: new Size(100, 35), align: ContentAlignment.MiddleCenter)]);
            panel.AddRow([MakeLabel("Current Version:", size: new Size(140, 35), align: ContentAlignment.MiddleCenter), stateLabel]);
            panel.AddRow([install, update]);
        }

        private MiniPanel NotificationsConfigBuilder()
        {
            var panel = new MiniPanel("Config", false, true);
            ConfigNav(panel);
            // Aquí irán los controles de configuración de notificaciones cuando se implementen
            return panel;
        }

        private MiniPanel AppPersonalizationBuild()
        {
            var panel = new MiniPanel("Config", false, true);
            ConfigNav(panel);

            var rows = Properties.Settings.Default.Properties
                .Cast<SettingsProperty>()
                .Where(p => p.PropertyType == typeof(Color) && p.Name.Contains("Color"))
                .OrderBy(p => p.Name)
                .Select(p => RowFormat.Color(
                    FormatName(p.Name),
                    () => (Color)Properties.Settings.Default[p.Name],
                    c => Properties.Settings.Default[p.Name] = c));

            var preview = new Label
            {
                Size = new Size(35, 35),
                Font = Properties.Settings.Default.MainFont,
                Text = "A",
                TextAlign = ContentAlignment.MiddleCenter
            };

            var change = new Button { Text = "Change", AutoSize = true };
            change.Click += (_, _) =>
            {
                using var fd = new FontDialog { Font = Properties.Settings.Default.MainFont };

                if (fd.ShowDialog() == DialogResult.OK)
                {
                    var font = fd.Font.Size <= 6 ? new Font(fd.Font.Name, 7) : fd.Font;
                    Properties.Settings.Default.MainFont = font;
                    SaveSettings();
                    AppCustomization.FontChange(this, font);
                }
            };

            panel.AddRow([MakeLabel("Main Font", auto: true), preview, change]);

            foreach (var row in rows)
                panel.AddRow(row);

            return panel;
        }

        private void ArgsNav(MiniPanel reference)
        {
            reference.AddUpControls([
                MakeButton("Gallery-DL Arguments", (_, _) => MiniPanelManager.Show(Arguments_GDL)),
                MakeButton("YT-dlp Arguments",     (_, _) => MiniPanelManager.Show(Arguments_YTDL))
            ]);
        }

        private MiniPanel Args_GDL()
        {
            var panel = new MiniPanel("Arg", false, true);
            ArgsNav(panel);
            foreach (var arg in GalleryDLArgs.Profile.All())
                panel.AddRow(RowFormat.Argument(arg, () => GalleryDLArgs.Save()));
            return panel;
        }

        private MiniPanel Args_YTDL()
        {
            var panel = new MiniPanel("Arg", false, true);
            ArgsNav(panel);
            foreach (var arg in YTDLPArgs.Profile.All())
                panel.AddRow(RowFormat.Argument(arg, () => YTDLPArgs.Save()));
            return panel;
        }

        private void FiltersNav(MiniPanel reference)
        {
            reference.AddUpControls([
                MakeButton("Alias Override", (_, _) => MiniPanelManager.Show(Alias_Override)),
                MakeButton("Rate Limiter",   (_, _) => MiniPanelManager.Show(Rate_Limiter)),
                MakeButton("YT-dlp Sites",   (_, _) => MiniPanelManager.Show(YtSites_Panel)),
                MakeButton("Regex Filters",  (_, _) => MiniPanelManager.Show(RegexFilters_Panel))
            ]);
        }

        // ─── Panels de filtros ────────────────────────────────────────────────────────

        private MiniPanel Alias()
        {
            var dgv = MakeDgv(false, false, ("From", "From", false), ("To", "To", false));

            foreach (var a in Manager.Filter.Engine.Entries.Where(e => e.Type == "alias"))
                dgv.Rows.Add(a.From, a.To);

            void Save() => RebuildFilterEngine("alias", () =>
            {
                foreach (DataGridViewRow row in dgv.Rows)
                {
                    if (row.IsNewRow) continue;
                    string from = row.Cells["From"].Value?.ToString()?.Trim();
                    string to = row.Cells["To"].Value?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;

                    var entry = new Manager.Filter.Entry { Type = "alias", From = from, To = to };
                    Manager.Filter.Engine.Register(new Manager.Filter.SiteAlias(from, to), entry);
                }
            });

            return MakeFilterPanel(dgv, Save, ["site", "alias"]);
        }

        private MiniPanel Rate()
        {
            var dgv = MakeDgv(false, false, ("Site", "Site", false), ("Limit", "Limit", false));

            foreach (var r in Manager.Filter.Engine.Entries.Where(e => e.Type == "ratelimit"))
                dgv.Rows.Add(r.Site, r.Limit);

            void Save() => RebuildFilterEngine("ratelimit", () =>
            {
                foreach (DataGridViewRow row in dgv.Rows)
                {
                    if (row.IsNewRow) continue;
                    string site = row.Cells["Site"].Value?.ToString()?.Trim();
                    string limit = row.Cells["Limit"].Value?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(site) || !int.TryParse(limit, out int lim)) continue;

                    var entry = new Manager.Filter.Entry { Type = "ratelimit", Site = site, Limit = lim };
                    Manager.Filter.Engine.Register(new Manager.Filter.SiteRateLimit(site, lim), entry);
                }
            });

            return MakeFilterPanel(dgv, Save, ["site", "2"]);
        }

        private MiniPanel YtSitesPanel()
        {
            var dgv = MakeDgv(false, false, ("Site", "Site (uses yt-dlp)", false));

            foreach (var site in Manager.Downloader.YtSites)
                dgv.Rows.Add(site);

            void Save() => RebuildFilterEngine("ytsite", () =>
            {
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
            });

            return MakeFilterPanel(dgv, Save, ["site"]);
        }

        private MiniPanel RegexFilters()
        {
            var dgv = MakeDgv(false, false,
                ("Site", "Site (optional)", false),
                ("Pattern", "Regex Pattern", false),
                ("Replace", "Replace", false));

            foreach (var r in Manager.Filter.Engine.Entries.Where(e => e.Type == "regex"))
                dgv.Rows.Add(r.Site, r.From, r.Replace);

            void Save() => RebuildFilterEngine("regex", () =>
            {
                foreach (DataGridViewRow row in dgv.Rows)
                {
                    if (row.IsNewRow)
                        continue;

                    string site = row.Cells["Site"].Value?.ToString()?.Trim();

                    string pattern = row.Cells["Pattern"].Value?
                        .ToString()?
                        .Trim();

                    // Permitir replace vacío
                    string replace = row.Cells["Replace"].Value?
                        .ToString() ?? "";

                    // Solo el pattern es obligatorio
                    if (string.IsNullOrWhiteSpace(pattern))
                        continue;

                    var entry = new Manager.Filter.Entry
                    {
                        Type = "regex",
                        Site = string.IsNullOrWhiteSpace(site)
                            ? null
                            : site,

                        From = pattern,
                        Replace = replace
                    };

                    Manager.Filter.Engine.Register(
                        new Manager.Filter.RegexUrlRewrite(
                            pattern,
                            replace,
                            entry.Site),

                        entry);
                }
            });

            return MakeFilterPanel(
                dgv,
                Save,
                ["", "^https://", "https://"]);
        }

        // ─── FindControl ──────────────────────────────────────────────────────────────

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

        // ─── Campos ───────────────────────────────────────────────────────────────────

        private List<Task> _workers = new();
        private SemaphoreSlim _semaphore;
        private readonly object _semaphoreLock = new();
        private int _maxDownloads;
        private CancellationTokenSource _cts = new();
        private ManualResetEventSlim _pauseEvent = new(true);
        private Channel<(Manager.Url url, DataGridViewRow row)> _downloadChannel;

        private DataGridView _urls;
        private int SesionUrls = 0;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private ToolStripStatusLabel _sesionUrls;

        private string _galleryInstalled;
        private string _galleryLatest;
        private string _ytdlpInstalled;
        private string _ytdlpLatest;

        public MiniPanel UrlPnl;
        public MiniPanel AppConfiguration;
        public MiniPanel AppPersonalization;
        public MiniPanel AppDependencies;
        public MiniPanel NotificationsConfig;
        public MiniPanel Arguments_GDL;
        public MiniPanel Arguments_YTDL;
        public MiniPanel Alias_Override;
        public MiniPanel Rate_Limiter;
        public MiniPanel YtSites_Panel;
        public MiniPanel RegexFilters_Panel;

        private readonly Dictionary<DataGridViewRow, ScrollableMessageBox.OutputForm> _openOutputs = new();

        // ─── Eventos del Form ─────────────────────────────────────────────────────────

        private async void Main_Load(object sender, EventArgs e)
        {
            await GetDependenciesVersions();
            DependenciesCheck();
            Properties.Settings.Default.AutoStartup = Startup.IsStartupEnabled();
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.Save();
            Filter.Saver.Save("filters.json");

            if (UrlCount()[0] - UrlCount()[3] > 0)
            {
                if (ScrollableMessageBox.Show(
                    $"There are still active downloads, Sleeping Urls or Errors.{Environment.NewLine}" +
                    $"Are you sure you want to exit?{Environment.NewLine}" +
                    $"All processes will be terminated.",
                    "Active Downloads",
                    MessageBoxButtons.YesNo) == DialogResult.No)
                {
                    e.Cancel = true;
                }
            }
        }
    }
}