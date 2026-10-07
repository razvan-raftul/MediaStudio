using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace MediaStudio
{
    partial class MainForm : Form
    {
        readonly TabControl tabs = new TabControl();
        TextBox statusBox, logBox;
        ProgressBar progress;
        Button cancelBtn;
        CheckBox detailsToggle;
        readonly Runner runner = new Runner();
        readonly Queue<Job> queue = new Queue<Job>();
        int jobsTotal, jobsDone, jobsFailed;
        int lastAnnounced = -1;
        string lastCommand = "";
        readonly StringBuilder pendingLog = new StringBuilder();
        readonly Timer logTimer = new Timer { Interval = 250 };

        public MainForm()
        {
            Text = "Media Studio – FFmpeg și yt-dlp";
            Width = 900; Height = 760;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            KeyPreview = true;

            Tools.Detect();
            BuildBottom();
            tabs.Dock = DockStyle.Fill;
            tabs.AccessibleName = "Secțiuni";
            tabs.TabIndex = 0;
            Controls.Add(tabs);
            tabs.BringToFront();

            AddTab("&Descărcare", BuildDownloadTab);
            AddTab("&Conversie", BuildConvertTab);
            AddTab("&Editare", BuildEditTab);
            AddTab("&Piste și subtitrări", BuildTracksTab);
            AddTab("&Metadate", BuildMetadataTab);
            AddTab("&Toate opțiunile", BuildOptionsTab);
            AddTab("&Setări", BuildSettingsTab);

            runner.Line += (l, err) => { lock (pendingLog) pendingLog.Append(l).Append("\r\n"); };
            runner.Progress += p => BeginInvoke((Action)(() => OnProgress(p)));
            runner.Exited += code => BeginInvoke((Action)(() => OnExited(code)));
            logTimer.Tick += (s, e) => FlushLog();
            logTimer.Start();

            if (Settings.LastTab >= 0 && Settings.LastTab < tabs.TabPages.Count) tabs.SelectedIndex = Settings.LastTab;
            Shown += (s, e) => { tabs.Focus(); ReportMissingTools(true); };
            FormClosing += (s, e) =>
            {
                if (runner.Running && MessageBox.Show(this, "O operațiune încă rulează. O opresc și închid aplicația?", "Media Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
                runner.Cancel();
                Settings.LastTab = tabs.SelectedIndex;
                Settings.Save();
            };
        }

        void AddTab(string title, Action<TabPage> build)
        {
            var p = new TabPage(title.Replace("&", "")) { AutoScroll = true, UseVisualStyleBackColor = true };
            p.AccessibleName = title.Replace("&", "");
            tabs.TabPages.Add(p);
            build(p);
        }

        // ------------------------------------------------------------------ zona de stare
        void BuildBottom()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 4, 8, 8), TabIndex = 1 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int ti = 0;
            // mereu vizibile: Stare, Oprește și caseta care arată detaliile
            panel.Controls.Add(new Label { Text = "St&are:", AutoSize = true, Anchor = AnchorStyles.Left, TabIndex = ti++ }, 0, 0);
            statusBox = new TextBox { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "Stare", Text = "Pregătit.", TabIndex = ti++ };
            panel.Controls.Add(statusBox, 1, 0);
            var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, TabIndex = ti++, Margin = new Padding(0, 4, 0, 4) };
            cancelBtn = new Button { Text = "&Oprește operațiunea", AutoSize = true, Enabled = false, AccessibleName = "Oprește operațiunea", TabIndex = 0 };
            cancelBtn.Click += (s, e) => { queue.Clear(); runner.Cancel(); Announce("Opresc operațiunea…"); };
            detailsToggle = new CheckBox { Text = "Arată progresul și &jurnalul", AutoSize = true, AccessibleName = "Arată progresul și jurnalul", TabIndex = 1, Margin = new Padding(12, 6, 3, 3) };
            flow.Controls.AddRange(new Control[] { cancelBtn, detailsToggle });
            panel.Controls.Add(flow, 0, 1); panel.SetColumnSpan(flow, 2);

            // restrânse: Progres, Copiază jurnalul, Jurnal
            var details = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0), TabIndex = ti++, Visible = false };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int di = 0;
            details.Controls.Add(new Label { Text = "Progres:", AutoSize = true, Anchor = AnchorStyles.Left, TabIndex = di++ }, 0, 0);
            progress = new ProgressBar { Anchor = AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "Progres", Maximum = 1000, TabIndex = di++, Height = 18 };
            details.Controls.Add(progress, 1, 0);
            var copyLog = new Button { Text = "Copiază jurnalul", AutoSize = true, AccessibleName = "Copiază jurnalul", TabIndex = di++, Margin = new Padding(3, 4, 3, 4) };
            copyLog.Click += (s, e) => { FlushLog(); if (logBox.TextLength > 0) { Clipboard.SetText(logBox.Text); Announce("Jurnalul a fost copiat."); } else Announce("Jurnalul e gol."); };
            details.Controls.Add(copyLog, 0, 1); details.SetColumnSpan(copyLog, 2);
            details.Controls.Add(new Label { Text = "Ju&rnal:", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, TabIndex = di++ }, 0, 2);
            logBox = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Height = 150, Anchor = AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "Jurnal", TabIndex = di++, Font = new Font("Consolas", 9.5f) };
            details.Controls.Add(logBox, 1, 2);
            panel.Controls.Add(details, 0, 2); panel.SetColumnSpan(details, 2);
            detailsToggle.CheckedChanged += (s, e) => { details.Visible = detailsToggle.Checked; };
            Controls.Add(panel);
        }

        void ShowDetails() { if (!detailsToggle.Checked) detailsToggle.Checked = true; }

        /// <summary>Afișează mesajul în câmpul Stare și îl trimite către NVDA (notificare UI Automation).</summary>
        public void Announce(string msg, bool important = true)
        {
            statusBox.Text = msg;
            try
            {
                statusBox.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.ActionCompleted,
                    important ? AutomationNotificationProcessing.ImportantMostRecent : AutomationNotificationProcessing.MostRecent, msg);
            }
            catch { }
        }

        void Log(string text)
        {
            lock (pendingLog) pendingLog.Append(text).Append("\r\n");
        }

        void FlushLog()
        {
            string add;
            lock (pendingLog) { if (pendingLog.Length == 0) return; add = pendingLog.ToString(); pendingLog.Clear(); }
            if (logBox.TextLength > 400000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 200000);
            bool focused = logBox.Focused;
            if (!focused) { logBox.AppendText(add); }
            else
            {
                // nu mut cursorul utilizatorului în timp ce citește jurnalul
                int sel = logBox.SelectionStart, len = logBox.SelectionLength;
                logBox.AppendText(add);
                logBox.Select(sel, len);
            }
        }

        // ------------------------------------------------------------------ rularea operațiunilor
        public bool Run(IEnumerable<Job> jobs)
        {
            var list = jobs.Where(j => j != null).ToList();
            if (list.Count == 0) { Announce("Nu e nimic de făcut. Verifică fișierele sau adresele."); return false; }
            if (runner.Running) { Announce("O operațiune rulează deja. Așteaptă să se termine sau oprește-o."); return false; }
            foreach (var t in list.Select(j => j.Tool).Distinct())
                if (!File.Exists(Runner.ExePath(t))) { ShowError(Tools.MissingText(t)); return false; }
            queue.Clear();
            foreach (var j in list) queue.Enqueue(j);
            jobsTotal = list.Count; jobsDone = 0; jobsFailed = 0;
            cancelBtn.Enabled = true;
            StartNext();
            return true;
        }
        public bool Run(Job job) { return Run(new[] { job }); }

        void StartNext()
        {
            if (queue.Count == 0) { Finish(); return; }
            var j = queue.Dequeue();
            lastCommand = Runner.CommandLine(j);
            lastAnnounced = -1;
            progress.Value = 0;
            Log("");
            Log("=== " + j.Title + " ===");
            Log("> " + lastCommand);
            string pos = jobsTotal > 1 ? " (" + (jobsDone + jobsFailed + 1) + " din " + jobsTotal + ")" : "";
            Announce("Pornit: " + j.Title + pos);
            try { runner.Start(j); }
            catch (Exception ex) { Log("EROARE: " + ex.Message); jobsFailed++; ShowError(ex.Message); StartNext(); }
        }

        void OnProgress(double p)
        {
            progress.Value = Math.Max(0, Math.Min(1000, (int)(p * 10)));
            int step = Settings.AnnounceStep;
            int bucket = (int)(p / step) * step;
            if (Settings.AnnounceProgress && bucket > lastAnnounced && bucket < 100)
            {
                lastAnnounced = bucket;
                Announce(bucket + "%", false);
            }
        }

        void OnExited(int code)
        {
            FlushLog();
            var j = runner.Current;
            if (j != null && j.TempFile != null) { try { File.Delete(j.TempFile); } catch { } }
            if (code == 0)
            {
                jobsDone++;
                progress.Value = 1000;
                Log("Gata: " + (j == null ? "" : j.Title));
            }
            else
            {
                jobsFailed++;
                var errs = runner.ErrorLines.Count > 0 ? runner.ErrorLines : runner.TailLines;
                string msg = string.Join("\r\n", errs.Skip(Math.Max(0, errs.Count - 6)));
                Log("Eșuat (cod " + code + "): " + (j == null ? "" : j.Title));
                if (queue.Count == 0 || true) ShowError((j == null ? "" : j.Title + "\r\n\r\n") + (msg.Length > 0 ? msg : "Programul s-a oprit cu codul " + code + ". Detaliile sunt în jurnal."));
            }
            if (j != null && j.After != null) { try { j.After(j, code); } catch (Exception ex) { Log(ex.Message); } }
            StartNext();
        }

        void Finish()
        {
            cancelBtn.Enabled = false;
            string msg;
            if (jobsFailed == 0) msg = jobsTotal > 1 ? "Gata. Toate cele " + jobsTotal + " operațiuni s-au terminat cu succes." : "Gata. Operațiunea s-a terminat cu succes.";
            else if (jobsDone == 0) msg = jobsTotal > 1 ? "Toate operațiunile au eșuat. Detaliile sunt în jurnal." : "Operațiunea a eșuat. Detaliile sunt în jurnal.";
            else msg = "Terminat: " + jobsDone + " reușite, " + jobsFailed + " eșuate. Detaliile sunt în jurnal.";
            Announce(msg);
            Log(msg);
            if (Settings.DoneSound) { if (jobsFailed == 0) SystemSounds.Asterisk.Play(); else SystemSounds.Hand.Play(); }
        }

        public void ShowError(string msg)
        {
            Announce("Eroare: " + msg.Split('\n')[0].Trim());
            if (Settings.ErrorDialogs)
                MessageBox.Show(this, msg + "\r\n\r\nPoți copia acest mesaj cu Control+C.", "Media Studio - eroare", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        void ReportMissingTools(bool startup)
        {
            var missing = new List<string>();
            if (!File.Exists(Settings.YtdlpPath)) missing.Add("yt-dlp");
            if (!File.Exists(Settings.FfmpegPath)) missing.Add("FFmpeg");
            if (!File.Exists(Settings.FfprobePath)) missing.Add("FFprobe");
            if (missing.Count == 0) { if (!startup) Announce("Toate programele sunt configurate."); return; }
            string m = "Lipsesc: " + string.Join(", ", missing) + ". Le poți descărca automat din tabul Setări, cu butonul „Descarcă automat ce lipsește”.";
            Announce(m);
            if (startup && MessageBox.Show(this, m + "\r\n\r\nVrei să le descarc acum? Sunt programele oficiale, gratuite, de pe GitHub (în jur de 150 MB).", "Media Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                DownloadMissing();
        }

        // ------------------------------------------------------------------ scurtături
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.Alt) == 0)
            {
                var k = keyData & Keys.KeyCode;
                if (k >= Keys.D1 && k <= Keys.D9 && (keyData & Keys.Shift) == 0)
                {
                    int i = k - Keys.D1;
                    if (i < tabs.TabPages.Count) { tabs.SelectedIndex = i; tabs.Focus(); return true; }
                }
                if (k == Keys.L && (keyData & Keys.Shift) == 0) { ShowDetails(); FlushLog(); logBox.Focus(); return true; }
                if (k == Keys.T && (keyData & Keys.Shift) == 0) { statusBox.Focus(); return true; }
            }
            if (keyData == Keys.F1) { ShowHelp(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void ShowHelp()
        {
            MessageBox.Show(this,
                "Scurtături:\r\n" +
                "Control+1 până la Control+7: mergi la secțiunea 1-7 (Descărcare, Conversie, Editare, Piste și subtitrări, Metadate, Toate opțiunile, Setări).\r\n" +
                "Control+Tab și Control+Shift+Tab: secțiunea următoare sau anterioară.\r\n" +
                "Control+L: jurnalul. Control+T: câmpul Stare.\r\n" +
                "Alt cu litera subliniată: mergi direct la un câmp sau buton.\r\n" +
                "F1: acest ajutor.\r\n\r\n" +
                "Starea și progresul sunt anunțate automat. Erorile apar într-o fereastră din care textul se poate copia cu Control+C.",
                "Ajutor Media Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------ utilitare comune
        public static string Sel(ComboBox c) { return c.Text.Trim(); }
        public static string FirstWord(string s) { s = (s ?? "").Trim(); int i = s.IndexOfAny(new[] { ' ', '—' }); return i > 0 ? s.Substring(0, i) : s; }

        public static string OutPath(string input, string folder, string suffix, string ext)
        {
            var dir = string.IsNullOrWhiteSpace(folder) ? Path.GetDirectoryName(input) : folder;
            var name = Path.GetFileNameWithoutExtension(input) + suffix;
            var p = Path.Combine(dir, name + "." + ext.TrimStart('.'));
            if (string.Equals(p, input, StringComparison.OrdinalIgnoreCase)) p = Path.Combine(dir, name + " (nou)." + ext.TrimStart('.'));
            return p;
        }

        /// <summary>Panou de presetări pentru un tab: alege, încarcă, salvează, șterge.</summary>
        void PresetBar(Builder b, string tabKey, Func<Dictionary<string, Control>> fields)
        {
            var dir = Path.Combine(Settings.PresetDir, tabKey);
            var combo = b.Combo(null, "Pre&setare", new string[0]);
            Action refresh = () =>
            {
                combo.Items.Clear();
                if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.ini").OrderBy(x => x)) combo.Items.Add(Path.GetFileNameWithoutExtension(f));
                if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            };
            refresh();
            b.Buttons(
                Builder.B("Încarcă presetarea", (s, e) =>
                {
                    if (combo.SelectedItem == null) { Announce("Nu există presetări salvate în această secțiune."); return; }
                    var file = Path.Combine(dir, combo.SelectedItem + ".ini");
                    var vals = Ini.Read(File.ReadAllText(file, Encoding.UTF8));
                    foreach (var kv in fields()) { string v; if (vals.TryGetValue(kv.Key, out v)) FieldIO.SetValue(kv.Value, v); }
                    Announce("Presetarea „" + combo.SelectedItem + "” a fost încărcată.");
                }),
                Builder.B("Salvează ca presetare…", (s, e) =>
                {
                    var name = Prompt.Ask(this, "Salvează presetarea", "Numele presetării:", combo.Text);
                    if (string.IsNullOrWhiteSpace(name)) return;
                    foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
                    Directory.CreateDirectory(dir);
                    var d = new Dictionary<string, string>();
                    foreach (var kv in fields()) d[kv.Key] = FieldIO.Value(kv.Value);
                    File.WriteAllText(Path.Combine(dir, name + ".ini"), Ini.Write(d), Encoding.UTF8);
                    refresh(); combo.SelectedItem = name;
                    Announce("Presetarea „" + name + "” a fost salvată.");
                }),
                Builder.B("Șterge presetarea", (s, e) =>
                {
                    if (combo.SelectedItem == null) return;
                    var name = combo.SelectedItem.ToString();
                    if (MessageBox.Show(this, "Ștergi presetarea „" + name + "”?", "Media Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    File.Delete(Path.Combine(dir, name + ".ini"));
                    refresh();
                    Announce("Presetarea „" + name + "” a fost ștearsă.");
                }));
        }
    }

    /// <summary>Fereastră mică pentru a cere un text.</summary>
    static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string label, string value)
        {
            using (var f = new Form { Text = title, Width = 460, Height = 160, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, Font = new Font("Segoe UI", 9.5f) })
            {
                var l = new Label { Text = label, Left = 12, Top = 14, AutoSize = true, TabIndex = 0 };
                var t = new TextBox { Left = 12, Top = 38, Width = 420, Text = value ?? "", AccessibleName = label.TrimEnd(':'), TabIndex = 1 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 252, Top = 74, Width = 85, TabIndex = 2 };
                var cancel = new Button { Text = "Renunță", DialogResult = DialogResult.Cancel, Left = 347, Top = 74, Width = 85, TabIndex = 3 };
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text.Trim() : null;
            }
        }
    }
}
