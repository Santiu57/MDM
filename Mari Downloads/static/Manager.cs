using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mari_Downloads
{
    public static class Manager
    {
        public class Url
        {
            public string url { get; set; }
            public string site { get; set; }
            public Status status { get; set; }
            public Filter.Context Context { get; set; }
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

                string pattern = @"https?://[^\s<>""'\]\}]+";

                var matches = Regex.Matches(input, pattern, RegexOptions.IgnoreCase);

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                char[] trimChars = new[]
                {
                        '<', '>', '[', ']', '{', '}',
                        '"', '\'', '.', ',', ';', ':', '!', '?'
                    };

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

                public List<string> DynamicArguments { get; } = new();

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
            public class RegexUrlRewrite : IUrlFilter
            {
                private readonly Regex _pattern;
                private readonly string _replace;
                private readonly string? _site;

                public RegexUrlRewrite(string pattern, string replace, string? site = null)
                {
                    _pattern = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    _replace = replace;
                    _site = site;
                }

                public bool Match(Context ctx)
                {
                    if (_site != null && ctx.Site != _site)
                        return false;

                    return _pattern.IsMatch(ctx.Url);
                }

                public void Apply(Context ctx)
                {
                    ctx.Url = _pattern.Replace(ctx.Url, _replace);
                }
            }
            public class DynamicArgument : IUrlFilter
            {
                private readonly Regex _pattern;
                private readonly string _argument;
                private readonly string _site;

                public DynamicArgument(string pattern, string argument, string site)
                {
                    _pattern = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    _argument = argument;
                    _site = site;
                }

                public bool Match(Context ctx)
                {
                    if (_site != null && ctx.Site != _site)
                        return false;

                    return _pattern.IsMatch(ctx.Url);
                }

                public void Apply(Context ctx)
                {
                    var match = _pattern.Match(ctx.Url);

                    if (!match.Success)
                        return;

                    string argument = _argument;

                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        string value = Uri.UnescapeDataString(match.Groups[i].Value);
                        argument = argument.Replace($"{{{i}}}", value);
                    }

                    ctx.DynamicArguments.Add(argument);
                }
            }
            public static class Loader
            {
                public static void Load(string file)
                {
                    // Resolve relative paths to absolute paths in the application folder
                    var path = Path.IsPathRooted(file) ? file : Path.Combine(Application.StartupPath, file);

                    if (!File.Exists(path))
                        return;

                    var json = File.ReadAllText(path);

                    var filters = JsonSerializer.Deserialize<List<Entry>>(json);

                    if (filters == null)
                        return;

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
                                    new SiteRateLimit(f.Site, f.Limit),
                                    f
                                );
                                break;

                            case "regex":
                                Engine.Register(
                                    new RegexUrlRewrite(f.From, f.Replace, f.Site),
                                    f
                                );
                                break;

                            case "dynamic":
                                Engine.Register(
                                    new DynamicArgument(f.From, f.Replace, f.Site),
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

                        var path = Path.IsPathRooted(file) ? file : Path.Combine(Application.StartupPath, file);

                        File.WriteAllText(path, json);
                    }
                    catch (Exception ex)
                    {
                        ScrollableMessageBox.Show(
                            $"Error saving filters:{Environment.NewLine}{ex.Message}",
                            "Filters Error",
                            MessageBoxButtons.OK
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
                Path.Combine(Application.StartupPath, "log.json");

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

                try
                {
                    File.WriteAllText(file,
                    JsonSerializer.Serialize(logs, options));
                }
                catch
                {
                    Save(url);
                }
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

        public static class ProcessRunner
        {
            public static async Task<(int ExitCode, string Output, string Command)> Run(
                string exe,
                Url url,
                string arguments,
                Action<string, string>? onOutput = null)
            {
                var sb = new StringBuilder();

                string command = $"{exe} {arguments} {url.url}";

                var startInfo = new ProcessStartInfo()
                {
                    FileName = exe,
                    Arguments = $"{arguments} {url.url}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = new Process();
                process.StartInfo = startInfo;

                void HandleLine(string? line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        return;

                    lock (sb)
                    {
                        sb.AppendLine(line);
                    }

                    onOutput?.Invoke(command, line);
                }

                process.OutputDataReceived += (_, e) => HandleLine(e.Data);
                process.ErrorDataReceived += (_, e) => HandleLine(e.Data);

                process.Start();

                JobManager.AddProcess(process);

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                return (process.ExitCode, sb.ToString(), command);
            }
        }

        public static class Downloader
        {
            private static readonly HashSet<string> _ytSites =
                new(StringComparer.OrdinalIgnoreCase)
            {
                    "youtube",
                    "youtu",
                    "twitch",
                    "bilibili"
            };

            public static void RegisterYtSite(string site) { lock (_ytSites) _ytSites.Add(site); }
            public static void UnregisterYtSite(string site) { lock (_ytSites) _ytSites.Remove(site); }
            public static IReadOnlyCollection<string> YtSites { get { lock (_ytSites) return _ytSites.ToList(); } }

            public static async Task<(int ExitCode, string Output, string Command)> Run(
                Url url,
                Action<string, string>? onOutput = null)
            {
                bool isYt;

                lock (_ytSites)
                    isYt = _ytSites.Contains(url.site);

                string args = "";
                var ctx = Filter.Engine.Process(url.url, url.site);

                if (isYt)
                {
                    args = YTDLPArgs.Build();

                    if (ctx.DynamicArguments.Count > 0)
                        args += " " + string.Join(" ", ctx.DynamicArguments);

                    return await ProcessRunner.Run(
                        "yt-dlp",
                        url,
                        args,
                        onOutput);
                }

                args = GalleryDLArgs.Build();

                if (ctx.DynamicArguments.Count > 0)
                    args += " " + string.Join(" ", ctx.DynamicArguments);

                return await ProcessRunner.Run(
                    "gallery-dl",
                    url,
                    args,
                    onOutput);
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
            static string file = Path.Combine(Application.StartupPath, "args_gdl.json");
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
            static string file = Path.Combine(Application.StartupPath, "args_ytdlp.json");
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
                    "Post Processor Args",
                    "--postprocessor-args",
                    Properties.Settings.Default.YTPostProcessorArgs));

                Profile.Add(new Argument(
                    "Extractor Args",
                    "--extractor-args",
                    Properties.Settings.Default.YTExtractorArgs));

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
}
