using System;
using System.IO;
using System.Windows.Forms;

namespace MediaStudio
{
    partial class MainForm
    {
        /// <summary>
        /// Arată grupul doar când în câmp e un fișier care există (sau un link, dacă allowLink).
        /// Când fișierul apare, rulează onLoaded (de exemplu citește pistele), fără niciun anunț.
        /// </summary>
        void ShowWhenFile(TextBox path, Control box, Action onLoaded = null, bool allowLink = false)
        {
            string loaded = null;   // fișierul încărcat ultima dată; la alt fișier se încarcă din nou
            Action upd = () =>
            {
                var t = path.Text.Trim().Trim('"');
                bool link = allowLink && SepEsteLink(t);
                bool ok = t.Length > 0 && (link || File.Exists(t));
                box.Visible = ok;
                if (ok && !string.Equals(t, loaded, StringComparison.OrdinalIgnoreCase))
                {
                    if (onLoaded != null) { try { onLoaded(); } catch (Exception ex) { Log(ex.Message); } }
                }
                loaded = ok ? t : null;
            };
            path.TextChanged += (s, e) => upd();
            upd();
        }

        /// <summary>Ascunde grupul; dacă focusul era în el, îl mută pe controlul dat, ca să nu se piardă.</summary>
        static void HideKeepFocus(Control box, bool visible, Control fallback)
        {
            bool had = !visible && box.ContainsFocus;
            box.Visible = visible;
            if (had && fallback != null && fallback.CanFocus) fallback.Focus();
        }
    }
}
