using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MediaStudio
{
    /// <summary>
    /// Construiește formulare accesibile: fiecare control primește o etichetă vizibilă (cu tastă de acces),
    /// un AccessibleName identic și o ordine Tab egală cu ordinea vizuală.
    /// Controalele cu cheie sunt reținute pentru presetări.
    /// </summary>
    class Builder
    {
        public readonly TableLayoutPanel Table;
        public readonly Dictionary<string, Control> Fields;
        int tab;

        public Builder(Control parent, Dictionary<string, Control> fields = null)
        {
            Fields = fields ?? new Dictionary<string, Control>();
            Table = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Padding = new Padding(8, 6, 8, 6),
                TabIndex = 0
            };
            Table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            parent.Controls.Add(Table);
            Table.BringToFront();
        }

        public static string Plain(string label)
        {
            return label.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").TrimEnd(':', ' ');
        }

        Label AddLabel(string text)
        {
            var l = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3), TabIndex = tab++ };
            Table.Controls.Add(l, 0, Table.RowCount);
            return l;
        }
        void AddControl(Control c, string label)
        {
            c.AccessibleName = Plain(label);
            c.TabIndex = tab++;
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            c.Margin = new Padding(3, 3, 3, 3);
            Table.Controls.Add(c, 1, Table.RowCount);
            Table.RowCount++;
        }
        void AddWide(Control c)
        {
            c.TabIndex = tab++;
            c.Margin = new Padding(3, 4, 3, 4);
            Table.Controls.Add(c, 0, Table.RowCount);
            Table.SetColumnSpan(c, 2);
            Table.RowCount++;
        }
        void Keep(string key, Control c) { if (!string.IsNullOrEmpty(key)) Fields[key] = c; }

        public TextBox Text(string key, string label, string value = "", bool multiline = false, string description = null)
        {
            AddLabel(label);
            var t = new TextBox { Text = value, Width = 420 };
            if (multiline) { t.Multiline = true; t.Height = 70; t.ScrollBars = ScrollBars.Vertical; t.AcceptsReturn = true; }
            if (description != null) t.AccessibleDescription = description;
            AddControl(t, label);
            Keep(key, t);
            return t;
        }

        public ComboBox Combo(string key, string label, IEnumerable<string> items, int selected = 0, bool editable = false, string description = null)
        {
            AddLabel(label);
            var c = new ComboBox { DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, Width = 420, MaxDropDownItems = 20 };
            c.Items.AddRange(items.Cast<object>().ToArray());
            if (c.Items.Count > 0) c.SelectedIndex = Math.Max(0, Math.Min(selected, c.Items.Count - 1));
            if (description != null) c.AccessibleDescription = description;
            AddControl(c, label);
            Keep(key, c);
            return c;
        }

        public NumericUpDown Number(string key, string label, decimal min, decimal max, decimal value, decimal step = 1, int decimals = 0, string description = null)
        {
            AddLabel(label);
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Increment = step, DecimalPlaces = decimals, Width = 140, Anchor = AnchorStyles.Left };
            if (description != null) n.AccessibleDescription = description;
            n.AccessibleName = Plain(label);
            n.TabIndex = tab++;
            n.Margin = new Padding(3);
            Table.Controls.Add(n, 1, Table.RowCount);
            Table.RowCount++;
            Keep(key, n);
            return n;
        }

        public CheckBox Check(string key, string text, bool value = false, string description = null)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true };
            c.AccessibleName = Plain(text);
            if (description != null) c.AccessibleDescription = description;
            AddWide(c);
            Keep(key, c);
            return c;
        }

        public TrackBar Slider(string key, string label, int min, int max, int value, Func<int, string> describe)
        {
            AddLabel(label);
            var t = new TrackBar { Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), TickStyle = TickStyle.None, Width = 300, SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 10) };
            AddControl(t, label);
            Action upd = () => { t.AccessibleDescription = describe(t.Value); };
            t.ValueChanged += (s, e) => upd();
            upd();
            Keep(key, t);
            return t;
        }

        /// <summary>Câmp de cale + buton Răsfoiește (dialog standard Windows).</summary>
        public TextBox Path(string key, string label, PathKind kind, string filter = null, string value = "")
        {
            AddLabel(label);
            var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Anchor = AnchorStyles.Left | AnchorStyles.Right };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var t = new TextBox { Text = value, Anchor = AnchorStyles.Left | AnchorStyles.Right, AccessibleName = Plain(label), TabIndex = 0, Width = 330 };
            var b = new Button { Text = "Răsfoiește…", AutoSize = true, AccessibleName = "Răsfoiește: " + Plain(label), TabIndex = 1 };
            b.Click += (s, e) =>
            {
                string r = Dialogs.Pick(kind, Plain(label), filter, t.Text, b.FindForm());
                if (r != null) { t.Text = r; t.Focus(); }
            };
            panel.Controls.Add(t, 0, 0);
            panel.Controls.Add(b, 1, 0);
            panel.TabIndex = tab++;
            panel.Margin = new Padding(3);
            Table.Controls.Add(panel, 1, Table.RowCount);
            Table.RowCount++;
            Keep(key, t);
            return t;
        }

        public ListBox List(string key, string label, int height = 110)
        {
            AddLabel(label);
            var l = new ListBox { Height = height, Width = 420, HorizontalScrollbar = true, IntegralHeight = false };
            AddControl(l, label);
            Keep(key, l);
            return l;
        }

        /// <summary>Rând de butoane.</summary>
        public Button[] Buttons(params Tuple<string, EventHandler>[] items)
        {
            var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 6, 0, 6) };
            var res = new List<Button>();
            int i = 0;
            foreach (var it in items)
            {
                var b = new Button { Text = it.Item1, AutoSize = true, AccessibleName = Plain(it.Item1), TabIndex = i++, Padding = new Padding(6, 2, 6, 2), Margin = new Padding(3) };
                if (it.Item2 != null) b.Click += it.Item2;
                flow.Controls.Add(b);
                res.Add(b);
            }
            AddWide(flow);
            return res.ToArray();
        }
        public static Tuple<string, EventHandler> B(string text, EventHandler h) { return Tuple.Create(text, h); }

        /// <summary>Secțiune care se poate ascunde: o casetă „Arată …” și un grup cu nume (NVDA îl anunță la intrare).</summary>
        public Builder Section(string title, bool collapsible, bool startOpen = false)
        {
            CheckBox toggle = null;
            if (collapsible) toggle = Check(null, "Arată " + title.ToLowerInvariant(), startOpen);
            var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left | AnchorStyles.Right, Padding = new Padding(4, 2, 4, 4) };
            g.AccessibleName = title;
            AddWide(g);
            var inner = new Builder(g, Fields);
            inner.Table.Dock = DockStyle.Fill;
            if (toggle != null)
            {
                g.Visible = startOpen;
                toggle.CheckedChanged += (s, e) => { g.Visible = toggle.Checked; };
            }
            return inner;
        }

        /// <summary>Text explicativ scurt, citibil cu săgețile (casetă doar-citire).</summary>
        public TextBox Note(string text)
        {
            var t = new TextBox { Text = text, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Control, TabStop = false, Width = 560, AccessibleName = "Notă" };
            using (var gr = t.CreateGraphics())
            {
                var sz = gr.MeasureString(text, t.Font, 560);
                t.Height = (int)sz.Height + 6;
            }
            AddWide(t);
            t.TabStop = false;
            return t;
        }
    }

    enum PathKind { OpenFile, OpenFiles, SaveFile, Folder }

    static class FieldIO
    {
        public static string Value(Control c)
        {
            if (c is CheckBox) return ((CheckBox)c).Checked ? "1" : "0";
            if (c is NumericUpDown) return ((NumericUpDown)c).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (c is TrackBar) return ((TrackBar)c).Value.ToString();
            if (c is ComboBox) return ((ComboBox)c).Text;
            if (c is ListBox) return string.Join("\n", ((ListBox)c).Items.Cast<object>().Select(o => o.ToString()));
            return c.Text;
        }
        public static void SetValue(Control c, string v)
        {
            if (v == null) return;
            if (c is CheckBox) ((CheckBox)c).Checked = v == "1";
            else if (c is NumericUpDown)
            {
                decimal d; var n = (NumericUpDown)c;
                if (decimal.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out d)) n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, d));
            }
            else if (c is TrackBar) { int i; var t = (TrackBar)c; if (int.TryParse(v, out i)) t.Value = Math.Max(t.Minimum, Math.Min(t.Maximum, i)); }
            else if (c is ComboBox)
            {
                var cb = (ComboBox)c;
                int idx = cb.FindStringExact(v);
                if (idx >= 0) cb.SelectedIndex = idx; else if (cb.DropDownStyle != ComboBoxStyle.DropDownList) cb.Text = v;
            }
            else if (c is ListBox)
            {
                var l = (ListBox)c; l.Items.Clear();
                foreach (var s in v.Split('\n')) if (s.Length > 0) l.Items.Add(s);
            }
            else c.Text = v;
        }
    }
}
