using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        TextBox snAudio;
        ComboBox snAction, snLength, snEnd;
        NumericUpDown snMixClip, snMixAudio;

        static readonly string[] SnActions =
        {
            "Pun sunetul nou în locul sunetului din clip",
            "Amestec sunetul nou cu sunetul din clip (se aud amândouă)",
            "Adaug sunetul nou ca a doua pistă de sunet (în player alegi pe care o asculți)"
        };

        static readonly string[] SnLength =
        {
            "Cât videoclipul",
            "Cât sunetul nou"
        };

        static readonly string[] SnEnd =
        {
            "Nu se stinge nimic",
            "Se stinge doar imaginea",
            "Se stinge tot, imaginea și sunetul"
        };

        /// <summary>Partea „Un sunet nou” din fila Poze și sunet peste clip: sunetul, ce fac cu el, volumele, finalul, Pune sunetul.</summary>
        void BuildSoundSection(Builder g)
        {
            snAudio = g.Path(null, "&Sunetul nou", PathKind.OpenFile, Dialogs.AudioFilter);

            // ce fac cu el apare după ce e ales sunetul
            Panel boxAudio;
            var h = g.Gate(out boxAudio);
            snAction = h.Combo(null, "Ce fac cu sunetul &nou", SnActions, 0);
            snMixClip = h.Number(null, "Volumul sunetului din clip (la sută, 0 înseamnă că nu se aude)", 0, 200, 30, 5);
            snMixAudio = h.Number(null, "Volumul sunetului nou (la sută)", 0, 200, 100, 5);
            snLength = h.Combo(null, "Cât ține rezultatul", SnLength, 0, false,
                "Cât videoclipul: sunetul prea lung se taie, iar dacă e prea scurt, la final e liniște. Cât sunetul nou: dacă videoclipul e mai lung, se taie, iar dacă e mai scurt, după el rămâne ecran negru.");
            snEnd = h.Combo(null, "La final se stinge în 2 secunde", SnEnd, 0);
            h.Buttons(Builder.B("Pune s&unetul", (s, e) => Run(SoundJob())));

            // volumele au rost doar la amestec
            Action sync = () =>
            {
                bool mix = snAction.SelectedIndex == 1;
                Builder.ShowRow(snMixClip, mix);
                Builder.ShowRow(snMixAudio, mix);
            };
            snAction.SelectedIndexChanged += (s, e) => sync();
            sync();
            ShowWhenFile(snAudio, boxAudio);
        }

        Job SoundJob()
        {
            if (!Need(ovVideo.Text, "videoclipul")) return null;
            if (!Need(snAudio.Text, "sunetul nou")) return null;
            var input = ovVideo.Text.Trim();
            if (IsAudioOnly(input)) { Announce("Sunetul nou se pune doar peste un videoclip. Ai ales un fișier care are doar sunet."); return null; }
            var audio = snAudio.Text.Trim();
            int act = Math.Max(0, snAction.SelectedIndex);
            int endMode = Math.Max(0, snEnd.SelectedIndex);
            double fd = 2;
            var ext = Path.GetExtension(input).ToLowerInvariant();
            const string folder = "Sunet peste videoclip";   // folderul rămâne cu acest nume

            var streams = Probe(input);
            bool clipAudio = streams.Any(x => x.Type == "audio");
            bool clipVideo = streams.Any(x => x.Type == "video");

            // cât țin videoclipul și sunetul nou; din ele iese cât ține rezultatul
            double vd = Runner.ProbeDuration(input), ad = Runner.ProbeDuration(audio);
            bool bySound = snLength.SelectedIndex == 1 && ad > 0;
            bool tail = bySound && vd > 0 && ad > vd + 0.05;   // ecran negru după videoclip
            double outDur = bySound ? ad : vd;
            double clipEnd = tail ? vd : outDur;               // unde se termină imaginea clipului
            if (endMode > 0 && vd <= 0) { ShowError("Nu pot afla cât durează videoclipul, așa că nu pot face stingerea. Verifică dacă FFprobe e configurat în Setări."); return null; }
            if (endMode > 0 && outDur > 0) fd = Math.Min(fd, outDur / 2);

            bool reVideo = clipVideo && (tail || endMode > 0);     // imaginea trebuie refăcută
            bool fadeAudio = endMode == 2;
            string origExt = ext;
            if (reVideo && ext != ".mp4" && ext != ".mov" && ext != ".mkv" && ext != ".m4v") ext = ".mp4";

            var j = new Job(Tool.Ffmpeg, SnActions[act] + ": " + Path.GetFileName(input));
            if (outDur > 0) j.DurationSeconds = outDur; else j.ProbeForDuration = input;
            j.A("-hide_banner", Settings.Overwrite ? "-y" : "-n", "-progress", "pipe:1", "-nostats", "-i", input, "-i", audio);

            var filters = new List<string>();
            string afadeTail = fadeAudio ? ",afade=t=out:st=" + Sec(Math.Max(0, outDur - fd)) + ":d=" + Sec(fd) : "";

            // imaginea: stingere la sfârșitul clipului, apoi negru cât mai e din sunet
            if (reVideo)
            {
                var v = new List<string>();
                if (endMode > 0) v.Add("fade=t=out:st=" + Sec(Math.Max(0, clipEnd - fd)) + ":d=" + Sec(fd));
                if (tail) v.Add("tpad=stop_mode=add:stop_duration=" + Sec(ad - vd) + ":color=black");
                v.Add("format=yuv420p");
                filters.Add("[0:v:0]" + string.Join(",", v) + "[vout]");
            }

            string vc = Sec((double)snMixClip.Value / 100), va = Sec((double)snMixAudio.Value / 100);
            var audioMaps = new List<string>();
            bool audioFiltered;
            if (act == 1)
            {
                // amestec: se aud amândouă, fiecare cu volumul lui
                if (clipAudio)
                    filters.Add("[0:a:0]volume=" + vc + "[a0];[1:a:0]volume=" + va + "[a1];[a0][a1]amix=inputs=2:duration=" + (tail ? "longest" : "first") + ":dropout_transition=0:normalize=0" + afadeTail + "[aout]");
                else
                    filters.Add("[1:a:0]volume=" + va + afadeTail + "[aout]");
                audioMaps.Add("[aout]");
                audioFiltered = true;
            }
            else if (act == 0)
            {
                if (fadeAudio) { filters.Add("[1:a:0]anull" + afadeTail + "[aout]"); audioMaps.Add("[aout]"); }
                else audioMaps.Add("1:a:0");
                audioFiltered = fadeAudio;
            }
            else
            {
                // a doua pistă: dacă se stinge totul, se sting amândouă pistele
                if (fadeAudio)
                {
                    if (clipAudio) { filters.Add("[0:a:0]anull" + afadeTail + "[ac]"); audioMaps.Add("[ac]"); }
                    filters.Add("[1:a:0]anull" + afadeTail + "[an]"); audioMaps.Add("[an]");
                }
                else { if (clipAudio) audioMaps.Add("0:a"); audioMaps.Add("1:a:0"); }
                audioFiltered = fadeAudio;
            }

            if (filters.Count > 0) j.A("-filter_complex", string.Join(";", filters));

            // ce intră în rezultat
            if (reVideo) j.A("-map", "[vout]");
            else if (clipVideo) j.A("-map", "0:v:0");
            foreach (var m in audioMaps) j.A("-map", m);
            if (act != 1 && ext == origExt) j.A("-map", "0:s?");   // subtitrările rămân doar dacă formatul nu se schimbă

            if (reVideo) j.A("-c:v", "libx264", "-preset", "medium", "-crf", "20");
            else j.A("-c:v", "copy");
            j.A("-c:s", "copy");

            // sunetul se face în formatul potrivit clipului, ca să meargă orice fișier audio (wav, flac, mp3...)
            {
                if (ext == ".webm") j.A("-c:a", "libopus", "-b:a", "192k");
                else if (ext == ".avi") j.A("-c:a", "libmp3lame", "-b:a", "256k");
                else if (ext == ".mkv" && !audioFiltered && act != 1) j.A("-c:a", "copy");
                else j.A("-c:a", "aac", "-b:a", act == 1 ? "192k" : "256k");
            }

            // lungimea rezultatului: cât videoclipul sau cât sunetul nou
            if (outDur > 0) j.A("-t", Sec(outDur));
            else j.A("-shortest");

            if (act == 1 && ext != ".mp4" && ext != ".mov" && ext != ".mkv" && ext != ".m4v") ext = clipVideo ? ".mp4" : ".m4a";
            if (ext != ".mkv" && ext != ".webm" && ext != ".avi") j.A("-movflags", "+faststart");
            string tag = act == 0 ? "sunet_inlocuit" : act == 1 ? "amestec" : "pista_adaugata";
            j.A(AutoOut(input, folder, tag, act == 1 || reVideo ? ext : Path.GetExtension(input)));
            return j;
        }
    }
}
