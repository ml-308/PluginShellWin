#nullable disable
#pragma warning disable SYSLIB0014 // HttpWebRequest：与 .NET Framework 版共用同一份实现

// ============================================================================
//  UpdatePrompt.WinForms.cs — 「发现新版本」提示窗（WinForms）
//  与 UpdateCheck.cs 配套；启动后延迟检查，发现新版本时弹窗提示。
// ============================================================================
using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace UpdateKit
{
    internal static class UpdatePrompt
    {
        /// <summary>启动后自动检查（最多每 24 小时一次；--check-update 时立即检查）。</summary>
        public static void ScheduleAutoCheck(Control owner)
        {
            if (owner == null || !UpdateCheck.AutoEnabled) return;
            if (!UpdateCheck.ForceNow && !UpdateCheck.ShouldAutoCheck()) return;
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
            timer.Interval = 2200;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                Run(owner, !UpdateCheck.ForceNow);
            };
            timer.Start();
        }

        /// <summary>菜单「检查更新」用：立刻检查，已是最新也给反馈。</summary>
        public static void CheckNow(Control owner)
        {
            if (owner == null) return;
            Run(owner, false);
        }

        static void Run(Control owner, bool quietWhenUpToDate)
        {
            UpdateCheck.CheckAsync(delegate(UpdateInfo info)
            {
                if (owner == null) return;
                try
                {
                    if (owner.IsDisposed || !owner.IsHandleCreated) return;
                    owner.BeginInvoke(new MethodInvoker(delegate { Handle(owner, info, quietWhenUpToDate); }));
                }
                catch (Exception) { }
            });
        }

        static void Handle(IWin32Window owner, UpdateInfo info, bool quietWhenUpToDate)
        {
            try
            {
                if (!info.Ok)
                {
                    if (!quietWhenUpToDate)
                        MessageBox.Show(owner, "暂时无法检查更新。\n\n" + (info.Error ?? "未知错误") +
                            "\n\n可以稍后在菜单里重试，或直接访问：\n" + UpdateCheck.ReleasesUrl,
                            UpdateCheck.AppName + " - 检查更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!info.HasUpdate)
                {
                    if (!quietWhenUpToDate)
                        MessageBox.Show(owner, "当前已是最新版本 v" + info.Current + "。",
                            UpdateCheck.AppName + " - 检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (quietWhenUpToDate && string.Equals(info.Latest, UpdateCheck.SkippedVersion(), StringComparison.Ordinal))
                    return;

                using (UpdateDialog dlg = new UpdateDialog(info))
                {
                    dlg.ShowDialog(owner);
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>发现新版本时的提示窗：立即下载 / 稍后提醒 / 跳过此版本。</summary>
    internal sealed class UpdateDialog : Form
    {
        readonly UpdateInfo _info;
        readonly TextBox _notes;
        readonly Button _download;

        static readonly Color Ink = Color.FromArgb(26, 26, 30);
        static readonly Color Muted = Color.FromArgb(120, 122, 130);
        static readonly Color Accent = Color.FromArgb(10, 132, 255);

        public UpdateDialog(UpdateInfo info)
        {
            _info = info;

            Text = UpdateCheck.AppName + " - 发现新版本";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 388);
            BackColor = Color.White;
            Font = UiFont(9f);
            AutoScaleMode = AutoScaleMode.Dpi;

            Label title = new Label();
            title.Text = "发现新版本 v" + info.Latest;
            title.Font = UiFont(13f, FontStyle.Bold);
            title.ForeColor = Ink;
            title.SetBounds(22, 20, 500, 32);
            Controls.Add(title);

            Label sub = new Label();
            string name = string.IsNullOrEmpty(info.ReleaseName) ? "" : Trim(info.ReleaseName, 60);
            sub.Text = "当前版本 v" + info.Current + "  →  最新版本 v" + info.Latest +
                       (name.Length > 0 ? "\r\n" + name : "");
            sub.ForeColor = Muted;
            sub.SetBounds(22, 56, 500, 40);
            Controls.Add(sub);

            string published = FormatDate(info.PublishedAt);
            if (published.Length > 0)
            {
                Label date = new Label();
                date.Text = "发布于 " + published + "  ·  " + UpdateCheck.Owner + "/" + UpdateCheck.RepoSlug;
                date.ForeColor = Muted;
                date.Font = UiFont(8.5f);
                date.SetBounds(22, 98, 520, 20);
                Controls.Add(date);
            }

            Label notesLabel = new Label();
            notesLabel.Text = "更新内容";
            notesLabel.ForeColor = Ink;
            notesLabel.Font = UiFont(9f, FontStyle.Bold);
            notesLabel.SetBounds(22, 126, 120, 20);
            Controls.Add(notesLabel);

            _notes = new TextBox();
            _notes.Multiline = true;
            _notes.ReadOnly = true;
            _notes.ScrollBars = ScrollBars.Vertical;
            _notes.BorderStyle = BorderStyle.FixedSingle;
            _notes.BackColor = Color.FromArgb(247, 248, 250);
            _notes.ForeColor = Ink;
            _notes.Font = UiFont(9f);
            _notes.SetBounds(22, 150, 516, 152);
            _notes.Text = NotesText(info.Notes);
            _notes.SelectionStart = 0;
            _notes.SelectionLength = 0;
            Controls.Add(_notes);

            LinkLabel link = new LinkLabel();
            link.Text = "查看 Release 页面";
            link.LinkColor = Accent;
            link.ActiveLinkColor = Accent;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.SetBounds(22, 310, 200, 20);
            link.LinkClicked += delegate { UpdateCheck.OpenReleases(); };
            Controls.Add(link);

            // 右对齐排布：立即下载 | 稍后提醒 | 跳过此版本（从右往左算，保证不重叠）
            int rightEdge = ClientSize.Width - 22;
            _download = MakeButton("立即下载", rightEdge - 86, 344, 86, true);
            _download.Click += delegate { UpdateCheck.OpenReleases(); Close(); };
            Controls.Add(_download);

            Button later = MakeButton("稍后提醒", rightEdge - 86 - 10 - 100, 344, 100, false);
            later.Click += delegate { Close(); };
            Controls.Add(later);

            Button skip = MakeButton("跳过此版本", rightEdge - 86 - 10 - 100 - 10 - 116, 344, 116, false);
            skip.Click += delegate { UpdateCheck.SkipVersion(_info.Latest); Close(); };
            Controls.Add(skip);

            AcceptButton = _download;
            CancelButton = later;
        }

        static Button MakeButton(string text, int x, int y, int width, bool accent)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = UiFont(9f, FontStyle.Bold);
            b.SetBounds(x, y, width, 32);
            if (accent)
            {
                b.BackColor = Accent;
                b.ForeColor = Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
            }
            return b;
        }

        static Font UiFont(float size)
        {
            return UiFont(size, FontStyle.Regular);
        }

        static Font UiFont(float size, FontStyle style)
        {
            try { return new Font("Microsoft YaHei UI", size, style); }
            catch (Exception) { return new Font(FontFamily.GenericSansSerif, size, style); }
        }

        static string NotesText(string notes)
        {
            if (string.IsNullOrEmpty(notes)) return "（本版本未提供更新说明，详见 Release 页面。）";
            string text = notes.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (text.Length > 2400) text = text.Substring(0, 2400) + "\n…";
            return text.Replace("\n", "\r\n");
        }

        static string FormatDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "";
            DateTime parsed;
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                return parsed.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return "";
        }

        static string Trim(string text, int max)
        {
            if (text == null) return "";
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}
