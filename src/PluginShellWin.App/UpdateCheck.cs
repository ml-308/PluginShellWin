#nullable disable
#pragma warning disable SYSLIB0014 // HttpWebRequest：与 .NET Framework 版共用同一份实现

// ============================================================================
//  UpdateCheck.cs — 「发现新版本」检查核心（无第三方依赖）
//
//  设计：
//   * 启动后后台线程查 GitHub Releases 最新版，最多每 24 小时一次；
//   * 只有比当前版本新才提示，用户可「跳过此版本」不再打扰；
//   * 断网 / 代理 / 限流都只是「检查失败」，绝不阻塞或影响主程序；
//   * 命令行开关（诊断与自动化用）：
//       --check-update         启动时立即检查并给出结果（忽略 24h 与跳过记录）
//       --no-update-check      本次启动不检查
//       --update-feed <url>    覆盖检查地址（本地联调 / 镜像）
//       --update-probe <path>  无人值守检查，把结果写成 JSON 后退出
// ============================================================================
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace UpdateKit
{
    internal sealed class UpdateInfo
    {
        public bool Ok;
        public string Error;
        public string Current;
        public string Latest;
        public string ReleaseName;
        public string ReleaseUrl;
        public string Notes;
        public string PublishedAt;
        public bool HasUpdate;

        public string Headline()
        {
            if (!Ok) return "检查更新失败";
            return HasUpdate ? "发现新版本 v" + Latest : "已是最新版本 v" + Current;
        }
    }

    internal static class UpdateCheck
    {
        // ===== 应用身份：每个仓库只改这几行 =====
        public const string AppSlug = "PluginShellWin";
        public const string AppName = "PluginShellWin";
        public const string Owner = "ml-308";
        public const string RepoSlug = "PluginShellWin";
        public const string CurrentVersion = "1.2.0";

        public const string AppUrl = "https://github.com/" + Owner + "/" + RepoSlug;
        public const string ReleasesUrl = AppUrl + "/releases";
        public const string LatestApiUrl = "https://api.github.com/repos/" + Owner + "/" + RepoSlug + "/releases/latest";

        // ===== 运行期开关 =====
        public static bool AutoEnabled = true;
        public static bool ForceNow = false;
        public static string FeedOverride = null;

        static string FeedUrl
        {
            get { return string.IsNullOrEmpty(FeedOverride) ? LatestApiUrl : FeedOverride; }
        }

        // ===== 状态文件（%LOCALAPPDATA%\<AppSlug>\update-state.txt）=====
        static string StateDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppSlug); }
        }

        static string StateFile
        {
            get { return Path.Combine(StateDir, "update-state.txt"); }
        }

        static string ReadState(string key)
        {
            try
            {
                if (!File.Exists(StateFile)) return "";
                string[] lines = File.ReadAllLines(StateFile, Encoding.UTF8);
                string prefix = key + "=";
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] != null && lines[i].StartsWith(prefix, StringComparison.Ordinal))
                        return lines[i].Substring(prefix.Length).Trim();
                }
            }
            catch (Exception) { }
            return "";
        }

        static void WriteState(string key, string value)
        {
            try
            {
                Directory.CreateDirectory(StateDir);
                string[] keys = new string[] { "last_check_utc", "skipped_version" };
                string[] values = new string[keys.Length];
                for (int i = 0; i < keys.Length; i++) values[i] = ReadState(keys[i]);
                for (int i = 0; i < keys.Length; i++)
                    if (keys[i] == key) values[i] = value;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < keys.Length; i++) sb.Append(keys[i] + "=" + values[i] + "\r\n");
                File.WriteAllText(StateFile, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        public static string SkippedVersion()
        {
            return ReadState("skipped_version");
        }

        public static void SkipVersion(string version)
        {
            if (!string.IsNullOrEmpty(version)) WriteState("skipped_version", version);
        }

        public static void MarkChecked()
        {
            WriteState("last_check_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        }

        /// <summary>距上次检查是否已超过 24 小时（失败也会记录，避免断网时反复重试）。</summary>
        public static bool ShouldAutoCheck()
        {
            string last = ReadState("last_check_utc");
            if (last.Length == 0) return true;
            DateTime parsed;
            if (!DateTime.TryParse(last, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return true;
            return DateTime.UtcNow - parsed >= TimeSpan.FromHours(24);
        }

        /// <summary>同步检查（阻塞，仅后台线程调用）。</summary>
        public static UpdateInfo Check(int timeoutMs)
        {
            UpdateInfo info = new UpdateInfo();
            info.Current = CurrentVersion;
            try
            {
                try
                {
                    ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
                }
                catch (Exception) { }

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(FeedUrl);
                req.UserAgent = AppSlug + "/" + CurrentVersion + " update-check";
                req.Accept = "application/vnd.github+json";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                req.Proxy = null; // 直连更快；系统代理由 .NET 默认回退

                using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
                using (Stream stream = res.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string body = reader.ReadToEnd();
                    info.Latest = Normalize(JsonString(body, "tag_name"));
                    info.ReleaseName = JsonString(body, "name");
                    info.ReleaseUrl = JsonString(body, "html_url");
                    info.PublishedAt = JsonString(body, "published_at");
                    info.Notes = JsonString(body, "body");
                }

                if (info.Latest.Length == 0)
                {
                    info.Error = "响应中没有版本号";
                }
                else
                {
                    info.Ok = true;
                    info.HasUpdate = CompareVersion(info.Latest, Normalize(CurrentVersion)) > 0;
                }
            }
            catch (WebException we)
            {
                info.Error = NetworkErrorText(we);
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }
            try { MarkChecked(); }
            catch (Exception) { }
            return info;
        }

        /// <summary>后台线程检查，回调也在后台线程（由调用方切回 UI 线程）。</summary>
        public static void CheckAsync(Action<UpdateInfo> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = Check(10000);
                if (done != null)
                {
                    try { done(info); }
                    catch (Exception) { }
                }
            });
        }

        public static void OpenReleases()
        {
            try { System.Diagnostics.Process.Start(ReleasesUrl); }
            catch (Exception) { }
        }

        // ===== 命令行 =====
        /// <summary>解析共用开关；--update-probe 由 TryProbe 单独处理。</summary>
        public static void Configure(string[] args)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] == null ? "" : args[i];
                string low = a.ToLowerInvariant();
                if (low == "--no-update-check") AutoEnabled = false;
                else if (low == "--check-update") ForceNow = true;
                else if (low.StartsWith("--update-feed=", StringComparison.Ordinal))
                    FeedOverride = a.Substring("--update-feed=".Length);
                else if (low == "--update-feed" && i + 1 < args.Length)
                    FeedOverride = args[++i];
            }
        }

        /// <summary>没有任何 UI 的更新检查；返回退出码，未要求 probe 时返回 null。</summary>
        public static int? TryProbe(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                string low = args[i] == null ? "" : args[i].ToLowerInvariant();
                if (low == "--update-probe")
                {
                    string path = (i + 1 < args.Length && args[i + 1].Length > 0)
                        ? args[i + 1]
                        : Path.Combine(Path.GetTempPath(), AppSlug + "-update-probe.json");
                    return RunProbe(path);
                }
            }
            return null;
        }

        public static int RunProbe(string path)
        {
            Configure(new string[] { });
            UpdateInfo info = Check(15000);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"app\": \"" + EscapeJson(AppSlug) + "\",\r\n");
            sb.Append("  \"feed\": \"" + EscapeJson(FeedUrl) + "\",\r\n");
            sb.Append("  \"current\": \"" + EscapeJson(CurrentVersion) + "\",\r\n");
            sb.Append("  \"latest\": \"" + EscapeJson(info.Latest) + "\",\r\n");
            sb.Append("  \"hasUpdate\": " + (info.HasUpdate ? "true" : "false") + ",\r\n");
            sb.Append("  \"ok\": " + (info.Ok ? "true" : "false") + ",\r\n");
            sb.Append("  \"url\": \"" + EscapeJson(info.ReleaseUrl) + "\",\r\n");
            sb.Append("  \"error\": \"" + EscapeJson(info.Error) + "\",\r\n");
            sb.Append("  \"checkedAt\": \"" + EscapeJson(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)) + "\"\r\n");
            sb.Append("}\r\n");
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
            return info.Ok ? 0 : 1;
        }

        static string EscapeJson(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ===== 版本号 =====
        public static string Normalize(string version)
        {
            if (version == null) return "";
            string v = version.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
            int space = v.IndexOf(' ');
            if (space > 0) v = v.Substring(0, space);
            return v;
        }

        /// <summary>返回 &gt;0 表示 a 比 b 新。</summary>
        public static int CompareVersion(string a, string b)
        {
            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < pa.Length ? Segment(pa[i]) : 0;
                int y = i < pb.Length ? Segment(pb[i]) : 0;
                if (x != y) return x > y ? 1 : -1;
            }
            return 0;
        }

        static int Segment(string text)
        {
            int value = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9') break;
                value = value * 10 + (c - '0');
                if (value > 100000) break;
            }
            return value;
        }

        // ===== 极简 JSON 取值（只取顶层字符串字段，够用且无依赖）=====
        static string JsonString(string body, string key)
        {
            if (string.IsNullOrEmpty(body)) return "";
            string needle = "\"" + key + "\"";
            int i = body.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return "";
            int k = i + needle.Length;
            while (k < body.Length && IsJsonSpace(body[k])) k++;
            if (k >= body.Length || body[k] != ':') return "";
            k++;
            while (k < body.Length && IsJsonSpace(body[k])) k++;
            if (k >= body.Length || body[k] != '"') return "";
            k++;
            StringBuilder sb = new StringBuilder();
            while (k < body.Length)
            {
                char c = body[k++];
                if (c == '\\' && k < body.Length)
                {
                    char e = body[k++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (k + 4 <= body.Length)
                            {
                                string hex = body.Substring(k, 4);
                                k += 4;
                                int code;
                                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                    sb.Append((char)code);
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else if (c == '"') break;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static bool IsJsonSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }

        static string NetworkErrorText(WebException we)
        {
            if (we == null) return "未知错误";
            if (we.Status == WebExceptionStatus.Timeout) return "连接超时，请检查网络";
            if (we.Status == WebExceptionStatus.NameResolutionFailure) return "无法解析 api.github.com，请检查网络与代理";
            if (we.Status == WebExceptionStatus.ProxyNameResolutionFailure) return "无法解析代理服务器";
            if (we.Status == WebExceptionStatus.ConnectFailure) return "无法连接到 api.github.com";
            if (we.Status == WebExceptionStatus.TrustFailure) return "TLS 证书校验失败";
            if (we.Response is HttpWebResponse)
            {
                HttpWebResponse res = (HttpWebResponse)we.Response;
                if ((int)res.StatusCode == 403) return "GitHub 接口限流（403），请稍后再试";
                if ((int)res.StatusCode == 404) return "仓库没有可用的 Release（404）";
                return "HTTP " + (int)res.StatusCode + " " + res.StatusDescription;
            }
            return we.Message;
        }
    }
}
