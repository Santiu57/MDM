using Microsoft.Toolkit.Uwp.Notifications;
using System.Configuration;
using System.Data;
using System.Diagnostics;
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
            MiniPanelManager.Show(UrlPnl);

            AppCustomization.FontChange(this, Properties.Settings.Default.MainFont);
            AppCustomization.ColorComponents(this, Properties.Settings.Default.MainBackColor, Properties.Settings.Default.MainForeColor);

            // Ensure controls that don't explicitly set AutoSize will auto-size
            AppCustomization.EnsureAutoSize(this);

            _downloadChannel = Channel.CreateUnbounded<(Manager.Url, DataGridViewRow)>();

            _maxDownloads = Properties.Settings.Default.SimultaneousDownloads;
            _semaphore = new SemaphoreSlim(_maxDownloads);
            StartWorkers();
        }

        private void SetWindowConfig()
        {
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.ControlBox = true;
            this.ShowIcon = true;
            this.Text = "(ᓀ‸ᓂ)";
            this.Icon = new Icon("media/icon.ico");
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

                    var siteSemaphore =
                        Manager.SiteRateLimiter.Get(job.url.site);

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
                                            {
                                                form.SetText(currentOutput);
                                            }
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
                            SesionUrls++; _sesionUrls.Text = $"{SesionUrls}";
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

            try
            {
                await Task.WhenAll(_workers);
            }
            catch { }

            _workers.Clear();

            _cts.Dispose();
            _cts = new CancellationTokenSource();

            _pauseEvent.Set();
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
                DownloadRow(row);
            }
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

            if(current++ == lastRow)
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

            if (target < 0)
                target = 0;

            if (target > lastRow)
                target = lastRow;

            if (target > 1)
                _urls.FirstDisplayedScrollingRowIndex = target;
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
                galleryLatest = Packages.GetLatestPipVersion("gallery-dl");

                ytdlpInstalled = GetInstalledVersion("yt-dlp", "--version");
                ytdlpLatest = Packages.GetLatestPipVersion("yt-dlp");
            }
            catch (Exception e) 
            {
                ScrollableMessageBox.Show(e.Message, "Error");
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
                        $"gallery-dl update available{Environment.NewLine}Installed: {_galleryInstalled}{Environment.NewLine}Latest: {_galleryLatest}",
                        Notifications.Type.NotifType.Dependencies, ToastDuration.Long, 
                        new Dictionary<string, string>
                        {
                            { "action", "update" }
                        }
                    );
                    GDLstate.Text = $"{_galleryInstalled}: Outdated. {Environment.NewLine} {_galleryLatest}: Lastest.";
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
                        $"YT-dlp update available{Environment.NewLine}Installed: {_ytdlpInstalled}{Environment.NewLine}Latest: {_ytdlpLatest}",
                        Notifications.Type.NotifType.Dependencies
                    );
                    YTstate.Text = $"{_ytdlpInstalled}: Outdated. {Environment.NewLine} {_ytdlpLatest}: Lastest.";
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
                Image = Media.Get("Icon"),
            };
            UrlsShow.Click += (sender, e) => { MiniPanelManager.Show(UrlPnl); };

            ToolStripButton Config = new ToolStripButton
            {
                Image = Media.Get("Config"),
                Alignment = ToolStripItemAlignment.Right
            };
            Config.Click += (sender, e) => { MiniPanelManager.Show(AppConfiguration); };

            ToolStripButton Args = new ToolStripButton
            {
                Image = Media.Get("Icon"),
                Alignment = ToolStripItemAlignment.Left
            };
            Args.Click += (sender, e) => { MiniPanelManager.Show(Arguments_GDL); };

            ToolStripButton Filters = new ToolStripButton
            {
                Image = Media.Get("Icon"),
                Alignment = ToolStripItemAlignment.Left
            };
            Filters.Click += (sender, e) => { MiniPanelManager.Show(Alias_Override); };

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

            _statusLabel = new ToolStripStatusLabel
            {
                Name = "Status",
                Text = "Sleeping...",
                AutoSize = true
            };

            _sesionUrls = new ToolStripStatusLabel
            {
                Alignment = ToolStripItemAlignment.Right,
                Text = "0",
                AutoSize = false
            };

            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Items.Add(_sesionUrls);

            SharpClipboard clipboard = new SharpClipboard();
            clipboard.ObservableFormats.All = false;
            clipboard.ObservableFormats.Texts = true;

            clipboard.ClipboardChanged += (sender, e) =>
            {
                if (e.Content is not string text) return;

                Task.Run(() =>
                {
                    var urls = Manager.Url.UrlExtractor(text)
                        .Distinct()
                        .ToList();

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

        // Settings
        private static void SaveSettings()
        {
            Properties.Settings.Default.Save();
        }

        // Button
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

        // Label
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

        // NumericUpDown
        private static NumericUpDown MakeNud(
            decimal min,
            decimal max,
            decimal value,
            EventHandler? onChange = null,
            int width = 80)
        {
            var nud = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                Width = width
            };

            if (onChange != null)
                nud.ValueChanged += onChange;

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
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
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

            cb.CheckedChanged += (_, _) =>
            {
                setter(cb.Checked);
                SaveSettings();
            };

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
            {
                menu.AddRow(
                [
                    MakeSettingsCheckBox(
                item.text,
                item.getter,
                item.setter)
                ]);
            }

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
            var panel = new MiniPanel(true, true);

            FiltersNav(panel);

            dgv.CellEndEdit += (_, _) => onSave();

            var add = MakeButton(
                "Add",
                (_, _) =>
                {
                    dgv.Rows.Add(defaultAddRow);
                    onSave();
                },
                new Size(100, 35));

            var delete = MakeButton(
                "Delete",
                (_, _) =>
                {
                    if (dgv.SelectedRows.Count <= 0)
                        return;

                    var row = dgv.SelectedRows[0];

                    if (row.IsNewRow)
                        return;

                    dgv.Rows.Remove(row);

                    onSave();
                },
                new Size(100, 35));

            panel.SetMainControl(dgv);
            panel.AddDownControls([delete, add]);

            return panel;
        }

        // ─── Helpers de filtros ───────────────────────────────────────────────────────

        private void RebuildFilterEngine(
            string excludeType,
            Action registerExcluded)
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
                    Manager.Filter.Engine.Register(
                        new Manager.Filter.SiteAlias(e.From, e.To),
                        e);
                    break;

                case "ratelimit":
                    Manager.Filter.Engine.Register(
                        new Manager.Filter.SiteRateLimit(e.Site, e.Limit),
                        e);
                    break;

                case "ytsite":
                    Manager.Filter.Engine.Register(
                        new Manager.Filter.YtSite(e.Site),
                        e);
                    break;

                case "regex":
                    Manager.Filter.Engine.Register(
                        new Manager.Filter.RegexUrlRewrite(
                            e.From,
                            e.Replace,
                            e.Site),
                        e);
                    break;
            }
        }

        // ─── Menús reutilizables ──────────────────────────────────────────────────────

        private MiniMenuPanel MakeClearMenu(Button anchor)
        {
            var s = Properties.Settings.Default;

            return MakeSettingsMenu(anchor,
            (
                "Done",
                () => s.ClearDone,
                v => s.ClearDone = v
            ),
            (
                "Sleeping",
                () => s.ClearSleeping,
                v => s.ClearSleeping = v
            ),
            (
                "Downloading",
                () => s.ClearDownloading,
                v => s.ClearDownloading = v
            ),
            (
                "Queued",
                () => s.ClearQueued,
                v => s.ClearQueued = v
            ),
            (
                "Error",
                () => s.ClearErrors,
                v => s.ClearErrors = v
            ));
        }

        private MiniMenuPanel MakeExportMenu(Button anchor)
        {
            var s = Properties.Settings.Default;

            return MakeSettingsMenu(anchor,
            (
                "Done",
                () => s.ExportDone,
                v => s.ExportDone = v
            ),
            (
                "Sleeping",
                () => s.ExportSleeping,
                v => s.ExportSleeping = v
            ),
            (
                "Downloading",
                () => s.ExportDownloading,
                v => s.ExportDownloading = v
            ),
            (
                "Queued",
                () => s.ExportQueued,
                v => s.ExportQueued = v
            ),
            (
                "Error",
                () => s.ExportErrors,
                v => s.ExportErrors = v
            ));
        }

        private MiniMenuPanel MakeRetryMenu(Button anchor)
        {
            var s = Properties.Settings.Default;

            return MakeSettingsMenu(anchor,
            (
                "Done",
                () => s.RetryDone,
                v => s.RetryDone = v
            ),
            (
                "Error",
                () => s.RetryErrors,
                v => s.RetryErrors = v
            ));
        }

        private MiniPanel UrlsPanel()
        {
            MiniPanel urlpnl = new MiniPanel(true);

            //Urls DGV
            _urls = MakeDgv(
                false, false,
                ("Status", "Status", true),
                ("Site", "Site", true),
                ("Url", "Url", false)
            );

            _urls.Name = "Urls";

            _urls.Columns["Status"].ReadOnly = true;
            _urls.Columns["Site"].ReadOnly = true;

            _urls.RowsAdded += (s, e) =>
            {
                UrlsStatusUpdate();

                Scrollbar(1);

                if (Properties.Settings.Default.AutoStart)
                    Start();
            };

            _urls.RowsRemoved += (s, e) =>
            {
                UrlsStatusUpdate();
                Scrollbar(-1);
            };

            _urls.MouseClick += (s, e) =>
            {
                bool right = e.Button == MouseButtons.Right;

                var hit = _urls.HitTest(e.X, e.Y);

                if (hit.RowIndex < 0 || hit.ColumnIndex < 0)
                    return;

                var row = _urls.Rows[hit.RowIndex];
                var col = _urls.Columns[hit.ColumnIndex].Name;

                string status = row.Cells["Status"].Value?.ToString();

                // Click derecho sobre row → eliminar
                bool eliminar = right;

                if (eliminar)
                {
                    _urls.CancelEdit();

                    if (!IsRowAlive(row))
                        return;

                    bool safeToDelete =
                        status == "Done" ||
                        status == "Error";

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
                                $"This URL hasn't started yet.{Environment.NewLine}" +
                                $"Remove it?",

                            _ =>
                                "Remove this URL?"
                        };

                        var result = ScrollableMessageBox.Show(
                            warning,
                            "Remove URL",
                            MessageBoxButtons.YesNo
                        );

                        if (result != DialogResult.Yes)
                            return;
                    }

                    _urls.Rows.RemoveAt(hit.RowIndex);
                }

                // Click izquierdo en Status → mostrar output
                bool output =
                    !right &&
                    col == "Status";

                if (output)
                {
                    if (!_openOutputs.TryGetValue(row, out var form) || form.IsDisposed)
                    {
                        form = new ScrollableMessageBox.OutputForm();

                        _openOutputs[row] = form;

                        form.FormClosed += (_, __) =>
                        {
                            _openOutputs.Remove(row);
                        };

                        form.Show();
                    }

                    form.SetText(row.Tag as string ?? "Nothing here yet.");
                }
            };

            // Doble click izquierdo en celda Url → abrir en navegador, Iniciar Descarga individual
            _urls.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                var col = _urls.Columns[e.ColumnIndex];

                switch (col.Name)
                {
                    case "Url":
                        string url = _urls.Rows[e.RowIndex].Cells["Url"].Value?.ToString();
                        if (string.IsNullOrWhiteSpace(url)) return;

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
                        if(status == "Sleeping") 
                            DownloadRow(row);

                        break;
                }
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

            Button clear = new Button {Name = "Clear", BackgroundImage = Media.Get("Clear"), BackgroundImageLayout = ImageLayout.Stretch, Size = new Size(35, 35), MinimumSize = new Size(35, 35), };
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
            clearMenu.AddRow([ClearError]);

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
            cancel.Click += async (s, e) =>
            {
                var result = ScrollableMessageBox.Show(
                    "Cancel all queued and downloading Urls?{Environment.NewLine}Downloading and Queued URLs will be reset to Sleeping.{Environment.NewLine}Processing will be stopped.",
                    "Cancel Downloads",
                    MessageBoxButtons.YesNo);

                if (result != DialogResult.Yes)
                    return;

                bool wasPaused = !_pauseEvent.IsSet;

                if (!wasPaused)
                {
                    PauseDownloads();
                    pauseResume.Text = "Resume";
                }

                await StopDownloadsAsync(); 

                foreach (DataGridViewRow row in _urls.Rows)
                {
                    if (row.IsNewRow) continue;

                    string status = row.Cells["Status"].Value?.ToString();

                    if (status != "Error" &&
                        status != "Done" &&
                        status != "Sleeping")
                    {
                        UpdateRowStatus(row, "Sleeping");
                    }
                }

                StartWorkers();

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

                    string url = row.Cells["Url"].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(url))
                        urls.Add(url);
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

            exportMenu.AddRow([ExportDone]);
            exportMenu.AddRow([ExportSleeping]);
            exportMenu.AddRow([ExportDownloading]);
            exportMenu.AddRow([ExportQueued]);
            exportMenu.AddRow([ ExportError ]);

            Button retry = new Button { Size = new Size(90, 35), Text = "Retry" };

            retry.Click += (s, e) =>
            {
                foreach (DataGridViewRow row in _urls.Rows)
                {
                    if (row.IsNewRow) continue;

                    string status = row.Cells["Status"].Value?.ToString();

                    bool shouldRetry =
                        (status == "Done" && Properties.Settings.Default.RetryDone) ||
                        (status == "Error" && Properties.Settings.Default.RetryErrors);

                    if (!shouldRetry) continue;

                    UpdateRowStatus(row, "Sleeping");
                }

                Start();
            };

            MiniMenuPanel RetryMenu = new MiniMenuPanel(retry);

            CheckBox RetryDone = new CheckBox { Text = "Done", Checked = Properties.Settings.Default.RetryDone, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };
            CheckBox RetryError = new CheckBox { Text = "Error", Checked = Properties.Settings.Default.RetryErrors, TextAlign = ContentAlignment.MiddleLeft, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = true };

            RetryDone.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.RetryDone = RetryDone.Checked;
                Properties.Settings.Default.Save();
            };

            RetryError.CheckedChanged += (s, e) =>
            {
                Properties.Settings.Default.RetryErrors = RetryError.Checked;
                Properties.Settings.Default.Save();
            };

            RetryMenu.AddRow(new Control[] { RetryDone });
            RetryMenu.AddRow(new Control[] { RetryError });

            Control[] Down = [retry, export, clear, pauseResume, cancel, autoStart, start];


            urlpnl.SetMainControl(_urls);
            urlpnl.AddDownControls(Down);

            return urlpnl;
        }

        private void ConfigNav(MiniPanel reference)
        {
            reference.AddUpControls(
            [
        MakeButton("Configuration",
            (s,e) => MiniPanelManager.Show(AppConfiguration)),

        MakeButton("Personalization",
            (s,e) => MiniPanelManager.Show(AppPersonalization)),

        MakeButton("Dependencies",
            (s,e) => MiniPanelManager.Show(AppDependencies)),

        MakeButton("Notifications",
            (s,e) => MiniPanelManager.Show(NotificationsConfig))
            ]);
        }

        private MiniPanel AppConfig()
        {
            MiniPanel AppConfig = new MiniPanel(false,true);

            ConfigNav(AppConfig);

            var SDNud = MakeNud(
            1,
            20,
            Properties.Settings.Default.SimultaneousDownloads,
            (s, e) =>
            {
                var nud = (NumericUpDown)s;
                ChangeSimultaneousDownloads((int)nud.Value);
            });
            Control[] SDownloads =
            {
                MakeLabel("Simultaneous Downloads", auto: true),
                SDNud
            };

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
            Label GDLstate = new Label { Size = new Size(150, 35), TextAlign = ContentAlignment.MiddleCenter, Text = "Obtaining...", Name = "GDLState", ForeColor = Properties.Settings.Default.MainForeColor, Tag = "NoAutoColor" };

            Control[] GDL_info = [new Label { Text = "Current Version: ", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter }, GDLstate];

            Button GDL_install = new Button { Text = "Install Gallery-Dl", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter };
            GDL_install.Click += async (s, e) =>
            {
                GDLstate.Text = "Instaling...";
                if (await Task.Run(() => Packages.InstallPackage("gallery-dl")))
                {
                    await GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show("Gallery-dl Installed", "Gallery-dl Installed Succesfully", Notifications.Type.NotifType.Dependencies);
                }
            };
            Button GDL_Update = new() { Text = "Update Gallery-Dl", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter };
            GDL_Update.Click += async (s, e) =>
            {
                GDLstate.Text = "Instaling...";
                GDLstate.ForeColor = Properties.Settings.Default.MainForeColor;
                if (await Task.Run(() => Packages.UpdatePackage("gallery-dl")))
                {
                    await GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show("Gallery-dl Updated", "Gallery-dl Updated Succesfully", Notifications.Type.NotifType.Dependencies);
                }
            };
            Control[] GDL_Btns = { GDL_install,GDL_Update };

            Control[] YT_dlp = { new Label { Text = "YT-dlp:", Size = new Size(100, 35), TextAlign = ContentAlignment.MiddleCenter } };
            Label YTState = new Label { Size = new Size(150, 35), TextAlign = ContentAlignment.MiddleCenter, Text = "Obtaining...", Name = "YTState", ForeColor = Properties.Settings.Default.MainForeColor, Tag = "NoAutoColor" };

            Control[] YT_info = { new Label { Text = "Current Version: ", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter }, YTState };

            Button YT_install = new Button { Text = "Install YT-dlp", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter };
            YT_install.Click += async (s, e) =>
            {
                YTState.Text = "Installing...";
                YTState.ForeColor = Properties.Settings.Default.MainForeColor;
                if (await Task.Run(() => Packages.InstallPackage("yt-dlp")))
                {
                    await GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show("YT-dlp Installed", "YT-dlp Installed Succesfully", Notifications.Type.NotifType.Dependencies);
                }
            };
            Button YT_Update = new Button { Text = "Update YT-dlp", Size = new Size(140, 35), TextAlign = ContentAlignment.MiddleCenter };
            YT_Update.Click += async (s, e) =>
            {
                YTState.Text = "Updating...";
                if (await Task.Run(() => Packages.UpdatePackage("yt-dlp")))
                {
                    await GetDependenciesVersions();
                    DependenciesCheck();
                    Notifications.Show("YT-dlp Updated", "YT-dlp Updated Succesfully", Notifications.Type.NotifType.Dependencies);
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
            .Select(p => RowFormat.Color(
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
                        Font font = fd.Font;
                        if (font.Size <= 6)
                            font = new Font(font.Name, 7);
                        Properties.Settings.Default.MainFont = font;
                        Properties.Settings.Default.Save();
                        AppCustomization.FontChange(this, Properties.Settings.Default.MainFont);
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
            reference.AddUpControls(
            [
        MakeButton("Gallery-DL Arguments",
            (s,e) => MiniPanelManager.Show(Arguments_GDL)),

        MakeButton("YT-dlp Arguments",
            (s,e) => MiniPanelManager.Show(Arguments_YTDL))
            ]);
        }

        private MiniPanel Args_GDL()
        {
            MiniPanel panel = new MiniPanel(false, true);
            ArgsNav(panel);

            foreach (var arg in GalleryDLArgs.Profile.All())
                panel.AddRow(RowFormat.Argument(arg, () => GalleryDLArgs.Save()));

            return panel;
        }

        private MiniPanel Args_YTDL()
        {
            MiniPanel panel = new MiniPanel(false, true);
            ArgsNav(panel);

            foreach (var arg in YTDLPArgs.Profile.All())
                panel.AddRow(RowFormat.Argument(arg, () => YTDLPArgs.Save()));

            return panel;
        }

        private void FiltersNav(MiniPanel reference)
        {
            reference.AddUpControls(
            [
        MakeButton("Alias Override",
            (s,e) => MiniPanelManager.Show(Alias_Override)),

        MakeButton("Rate Limiter",
            (s,e) => MiniPanelManager.Show(Rate_Limiter)),

        MakeButton("YT-dlp Sites",
            (s,e) => MiniPanelManager.Show(YtSites_Panel)),

        MakeButton("Regex Filters",
            (s,e) => MiniPanelManager.Show(RegexFilters_Panel))
            ]);
        }

        private MiniPanel Alias() { var dgv = MakeDgv(false, false,("From", "From", false), ("To", "To", false)); foreach (var a in Manager.Filter.Engine.Entries.Where(e => e.Type == "alias")) dgv.Rows.Add(a.From, a.To); void Save() => RebuildFilterEngine("alias", () => { foreach (DataGridViewRow row in dgv.Rows) { if (row.IsNewRow) continue; string from = row.Cells["From"].Value?.ToString()?.Trim(); string to = row.Cells["To"].Value?.ToString()?.Trim(); if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue; var entry = new Manager.Filter.Entry { Type = "alias", From = from, To = to }; Manager.Filter.Engine.Register(new Manager.Filter.SiteAlias(from, to), entry); } }); return MakeFilterPanel(dgv, Save, new object[] { "site", "alias" }); }
        private MiniPanel Rate() { var dgv = MakeDgv(false, false, ("Site", "Site", false), ("Limit", "Limit", false)); foreach (var r in Manager.Filter.Engine.Entries.Where(e => e.Type == "ratelimit")) dgv.Rows.Add(r.Site, r.Limit); void Save() => RebuildFilterEngine("ratelimit", () => { foreach (DataGridViewRow row in dgv.Rows) { if (row.IsNewRow) continue; string site = row.Cells["Site"].Value?.ToString()?.Trim(); string limit = row.Cells["Limit"].Value?.ToString()?.Trim(); if (string.IsNullOrWhiteSpace(site) || !int.TryParse(limit, out int lim)) continue; var entry = new Manager.Filter.Entry { Type = "ratelimit", Site = site, Limit = lim }; Manager.Filter.Engine.Register(new Manager.Filter.SiteRateLimit(site, lim), entry); } }); return MakeFilterPanel(dgv, Save, new object[] { "site", "2" }); }
        private MiniPanel YtSitesPanel() { var dgv = MakeDgv(false, false, ("Site", "Site (uses yt-dlp)", false)); foreach (var site in Manager.Downloader.YtSites) dgv.Rows.Add(site); void Save() => RebuildFilterEngine("ytsite", () => { foreach (var site in Manager.Downloader.YtSites.ToList()) Manager.Downloader.UnregisterYtSite(site); foreach (DataGridViewRow row in dgv.Rows) { if (row.IsNewRow) continue; string site = row.Cells["Site"].Value?.ToString()?.Trim(); if (string.IsNullOrWhiteSpace(site)) continue; var entry = new Manager.Filter.Entry { Type = "ytsite", Site = site }; Manager.Filter.Engine.Register(new Manager.Filter.YtSite(site), entry); } }); return MakeFilterPanel(dgv, Save, new object[] { "site" }); }
        private MiniPanel RegexFilters() { var dgv = MakeDgv(false, false, ("Site", "Site (optional)", false), ("Pattern", "Regex Pattern", false), ("Replace", "Replace", false)); foreach (var r in Manager.Filter.Engine.Entries.Where(e => e.Type == "regex")) dgv.Rows.Add(r.Site, r.From, r.Replace); void Save() => RebuildFilterEngine("regex", () => { foreach (DataGridViewRow row in dgv.Rows) { if (row.IsNewRow) continue; string site = row.Cells["Site"].Value?.ToString()?.Trim(); string pattern = row.Cells["Pattern"].Value?.ToString()?.Trim(); string replace = row.Cells["Replace"].Value?.ToString(); if (string.IsNullOrWhiteSpace(pattern) || replace == null) continue; var entry = new Manager.Filter.Entry { Type = "regex", Site = string.IsNullOrWhiteSpace(site) ? null : site, From = pattern, Replace = replace }; Manager.Filter.Engine.Register(new Manager.Filter.RegexUrlRewrite(pattern, replace, entry.Site), entry); } }); return MakeFilterPanel(dgv, Save, new object[] { "", "^https://", "https://" }); }

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
        MiniPanel RegexFilters_Panel;

        private readonly Dictionary<DataGridViewRow, ScrollableMessageBox.OutputForm> _openOutputs = new();

        private async void Main_Load(object sender, EventArgs e)
        {
            await GetDependenciesVersions();
            DependenciesCheck();
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.Save();
            Filter.Saver.Save("filters.json");
            if (UrlCount()[0] - UrlCount()[3] > 0) {
                if (ScrollableMessageBox.Show($"There are still active downloads, Sleeping Urls or Errors. {Environment.NewLine} Are you sure you want to exit? {Environment.NewLine} All processes will be terminated.", "Active Downloads", MessageBoxButtons.YesNo) == DialogResult.No)
                    e.Cancel = true;
            }
        }
    }
}
