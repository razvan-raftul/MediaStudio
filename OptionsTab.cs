using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaStudio
{
    class ExtraOpt
    {
        public Tool Tool;
        public List<string> Args = new List<string>();
        public string FilterKind;   // "v"/"a" pentru filtre
        public string Filter;       // nume=parametri
        public override string ToString()
        {
            var who = Tool == Tool.Ytdlp ? "yt-dlp" : "FFmpeg";
            if (FilterKind != null) return who + ", filtru " + (FilterKind == "v" ? "video" : "audio") + ": " + Filter;
            return who + ": " + string.Join(" ", Args.Select(Runner.Quote));
        }
    }

    partial class MainForm
    {
        ComboBox opProgram, opSection, opValue;
        TextBox opSearch, opDesc;
        ListBox opList, opActive;
        List<OptionInfo> ytOptions, ffOptions;
        bool opLoading;

        void BuildOptionsTab(TabPage page)
        {
            var b = new Builder(page);
            b.Note("Aici găsești toate opțiunile pe care le au yt-dlp și FFmpeg, citite direct din programe. Opțiunile adăugate se aplică automat la următoarele descărcări (yt-dlp) sau conversii (FFmpeg), pe lângă alegerile din celelalte secțiuni.");
            opProgram = b.Combo(null, "&Program", new[] { "yt-dlp (descărcare)", "FFmpeg (conversie și editare)" });
            opSection = b.Combo(null, "&Categorie", new[] { "Toate" });
            opSearch = b.Text(null, "Ca&ută opțiune", "", false, "Caută în nume și în descriere. Lista se filtrează pe măsură ce scrii.");
            opList = b.List(null, "&Opțiuni găsite", 160);
            opDesc = b.Text(null, "&Descriere", "", true);
            opDesc.ReadOnly = true; opDesc.Height = 90;
            opValue = b.Combo(null, "&Valoare", new string[0], 0, true, "Pentru opțiunile care cer o valoare. Dacă există valori posibile, le găsești în listă.");
            b.Buttons(
                Builder.B("&Adaugă opțiunea", (s, e) => AddOption()),
                Builder.B("Reîncarcă lista din programe", (s, e) => { ytOptions = null; ffOptions = null; LoadOptions(); }));
            opActive = b.List(null, "Opțiuni ad&ăugate", 100);
            b.Buttons(
                Builder.B("&Elimină opțiunea selectată", (s, e) => RemoveSelected(opActive)),
                Builder.B("Elimină toate opțiunile adăugate", (s, e) => { opActive.Items.Clear(); Announce("Nu mai e nicio opțiune adăugată."); }));

            opProgram.SelectedIndexChanged += (s, e) => LoadOptions();
            opSection.SelectedIndexChanged += (s, e) => FilterOptions();
            opSearch.TextChanged += (s, e) => FilterOptions();
            opList.SelectedIndexChanged += (s, e) => ShowOption();
            page.Enter += (s, e) => { if ((opProgram.SelectedIndex == 0 ? ytOptions : ffOptions) == null) LoadOptions(); };
        }

        List<OptionInfo> CurrentOptions() { return opProgram.SelectedIndex == 0 ? ytOptions : ffOptions; }

        void LoadOptions()
        {
            var tool = opProgram.SelectedIndex == 0 ? Tool.Ytdlp : Tool.Ffmpeg;
            if (CurrentOptions() != null) { FillSections(); return; }
            if (!System.IO.File.Exists(Runner.ExePath(tool))) { opList.Items.Clear(); opDesc.Text = Tools.MissingText(tool); Announce(Tools.MissingText(tool)); return; }
            if (opLoading) return;
            opLoading = true;
            Announce("Citesc opțiunile din " + (tool == Tool.Ytdlp ? "yt-dlp" : "FFmpeg") + "…");
            Task.Run(() =>
            {
                List<OptionInfo> res;
                if (tool == Tool.Ytdlp) res = HelpParser.ParseYtdlp(Runner.Capture(Tool.Ytdlp, "--help", 30000));
                else
                {
                    res = HelpParser.ParseFfmpeg(Runner.Capture(Tool.Ffmpeg, "-hide_banner -h full", 60000));
                    res.AddRange(HelpParser.ParseFilters(Runner.Capture(Tool.Ffmpeg, "-hide_banner -filters", 30000)));
                }
                return res;
            }).ContinueWith(t =>
            {
                BeginInvoke((Action)(() =>
                {
                    opLoading = false;
                    var res = t.IsFaulted ? new List<OptionInfo>() : t.Result;
                    if (tool == Tool.Ytdlp) ytOptions = res; else ffOptions = res;
                    FillSections();
                    Announce(res.Count + " opțiuni încărcate pentru " + (tool == Tool.Ytdlp ? "yt-dlp" : "FFmpeg") + ".");
                }));
            });
        }

        void FillSections()
        {
            var opts = CurrentOptions() ?? new List<OptionInfo>();
            opSection.BeginUpdate();
            opSection.Items.Clear();
            opSection.Items.Add("Toate");
            foreach (var s in opts.Select(o => o.Section).Distinct()) opSection.Items.Add(s);
            opSection.SelectedIndex = 0;
            opSection.EndUpdate();
            FilterOptions();
        }

        void FilterOptions()
        {
            var opts = CurrentOptions();
            if (opts == null) return;
            var q = opSearch.Text.Trim().ToLowerInvariant();
            var sec = opSection.SelectedIndex <= 0 ? null : opSection.Text;
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = opts.Where(o => (sec == null || o.Section == sec) &&
                                       words.All(w => o.Key.ToLowerInvariant().Contains(w) || o.Description.ToLowerInvariant().Contains(w) || o.Section.ToLowerInvariant().Contains(w)))
                           .Take(2000).ToList();
            opList.BeginUpdate();
            opList.Items.Clear();
            foreach (var o in hits) opList.Items.Add(o);
            opList.EndUpdate();
            if (opSearch.Focused || opSection.Focused) Announce(hits.Count == 2000 ? "Peste 2000 de opțiuni; restrânge căutarea." : hits.Count + " opțiuni.", false);
        }

        void ShowOption()
        {
            var o = opList.SelectedItem as OptionInfo;
            opValue.Items.Clear(); opValue.Text = "";
            if (o == null) { opDesc.Text = ""; return; }
            var lines = new List<string>();
            lines.Add(o.Names + (string.IsNullOrEmpty(o.Arg) ? "" : " " + o.Arg));
            lines.Add("Categorie: " + o.Section);
            lines.Add(o.Description);
            if (!o.NeedsValue) lines.Add("Nu cere valoare.");
            if (o.Values.Count > 0) { lines.Add("Valori posibile: " + o.Values.Count + ", le găsești în lista Valoare."); foreach (var v in o.Values) opValue.Items.Add(v); }
            opDesc.Text = string.Join("\r\n", lines);
            if (o.FilterKind != null)
            {
                opDesc.AppendText("\r\nSe citesc parametrii filtrului…");
                var name = o.Key;
                Task.Run(() => Runner.Capture(Tool.Ffmpeg, "-hide_banner -h filter=" + name, 15000)).ContinueWith(t =>
                {
                    if (t.IsFaulted) return;
                    BeginInvoke((Action)(() =>
                    {
                        if (opList.SelectedItem != o) return;
                        opDesc.Text = string.Join("\r\n", lines) + "\r\nParametrii se scriu ca nume=valoare, separați prin două puncte.\r\n" + (t.Result ?? "").Replace("\n", "\r\n");
                    }));
                });
            }
        }

        void AddOption()
        {
            var o = opList.SelectedItem as OptionInfo;
            if (o == null) { Announce("Alege mai întâi o opțiune din lista Opțiuni găsite."); return; }
            var val = FirstWord(opValue.Text);
            if (opValue.Text.Contains(" — ")) val = opValue.Text.Substring(0, opValue.Text.IndexOf(" — ")).Trim(); else val = opValue.Text.Trim();
            var e = new ExtraOpt { Tool = o.Tool };
            if (o.FilterKind != null)
            {
                e.FilterKind = o.FilterKind;
                e.Filter = o.Key + (val.Length > 0 ? "=" + val : "");
            }
            else
            {
                if (o.NeedsValue && val.Length == 0 && o.Type != "<flags>") { Announce("Opțiunea " + o.Key + " cere o valoare. Scrie-o în câmpul Valoare."); opValue.Focus(); return; }
                e.Args.Add(o.Key);
                if (val.Length > 0) e.Args.Add(val);
            }
            opActive.Items.Add(e);
            Announce("Adăugat: " + e + ". În total " + opActive.Items.Count + ".");
        }

        IEnumerable<string> ExtraArgs(Tool t)
        {
            if (opActive == null) yield break;
            foreach (var e in opActive.Items.OfType<ExtraOpt>().Where(x => x.Tool == t && x.FilterKind == null))
                foreach (var a in e.Args) yield return a;
        }

        void AddExtraFilters(List<string> vf, List<string> af)
        {
            if (opActive == null) return;
            foreach (var e in opActive.Items.OfType<ExtraOpt>().Where(x => x.FilterKind != null))
                (e.FilterKind == "v" ? vf : af).Add(e.Filter);
        }
    }
}
