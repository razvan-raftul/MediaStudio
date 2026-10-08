using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaStudio
{
    /// <summary>
    /// Actualizare automată la pornire: citește version.txt din release-ul „latest” de pe GitHub;
    /// dacă e mai nouă, descarcă MediaStudio-Setup.exe, îl rulează fără ferestre și închide aplicația.
    /// Instalarea pornește apoi singură Media Studio.
    /// </summary>
    static class Updater
    {
        const string Base = "https://github.com/razvan-raftul/MediaStudio/releases/download/latest/";

        static void Say(MainForm f, string m) { try { f.BeginInvoke((Action)(() => f.Announce(m))); } catch { } }

        public static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        public static void CheckAsync(MainForm form, bool manual = false)
        {
            Task.Run(() =>
            {
                try
                {
                    string text;
                    using (var w = new WebClient()) { w.Headers[HttpRequestHeader.UserAgent] = "MediaStudio"; text = w.DownloadString(Base + "version.txt"); }
                    Version remote;
                    if (!Version.TryParse(text.Trim(), out remote)) { if (manual) Say(form, "Nu am putut citi versiunea de pe internet."); return; }
                    var cur = Current;
                    var curCmp = new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build));
                    var remCmp = new Version(remote.Major, remote.Minor, Math.Max(0, remote.Build));
                    if (remCmp <= curCmp) { if (manual) Say(form, "Ai deja ultima versiune, " + curCmp + "."); return; }

                    form.BeginInvoke((Action)(() => form.Announce("Am găsit o versiune nouă, " + remCmp + ". O descarc și o instalez, aplicația se redeschide singură.")));
                    var setup = Path.Combine(Path.GetTempPath(), "MediaStudio-Setup-" + remCmp + ".exe");
                    using (var w = new WebClient()) { w.Headers[HttpRequestHeader.UserAgent] = "MediaStudio"; w.DownloadFile(Base + "MediaStudio-Setup.exe", setup); }
                    if (new FileInfo(setup).Length < 100000) return;

                    form.BeginInvoke((Action)(() =>
                    {
                        if (form.IsBusy) { form.Announce("Versiunea nouă se instalează data viitoare când deschizi aplicația, pentru că acum rulează o operațiune."); return; }
                        try
                        {
                            Process.Start(new ProcessStartInfo(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /AUTOUPDATE") { UseShellExecute = true });
                            form.CloseForUpdate();
                        }
                        catch (Exception ex) { form.Announce("Nu am putut porni actualizarea: " + ex.Message); }
                    }));
                }
                catch { if (manual) Say(form, "Nu am putut verifica actualizările. Verifică legătura la internet."); }
            });
        }
    }
}
