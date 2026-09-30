using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("PLrename")]
[assembly: System.Reflection.AssemblyProduct("PLrename")]
[assembly: System.Reflection.AssemblyDescription("Windows batch file renaming utility")]

namespace BatchNumberRenamer
{
    public sealed class FileDropBox : Panel
    {
        public bool DragActive { get; set; }
        public FileDropBox()
        {
            DoubleBuffered = true; TabStop = true; Cursor = Cursors.Hand;
            AccessibleName = "Add files or folders"; AccessibleDescription = "Drop files or folders here, or activate to choose files.";
            AccessibleRole = AccessibleRole.PushButton;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(DragActive ? Color.FromArgb(223, 236, 255) : Color.FromArgb(237, 243, 252));
            using (var pen = new Pen(DragActive ? Color.FromArgb(34, 98, 210) : Color.FromArgb(142, 165, 199), 1))
            {
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            }
            using (var titleFont = new Font(Font, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, DragActive ? "Release to add files" : "Drop files or folders here", titleFont,
                    new Rectangle(8, 12, Width - 16, 25), Color.FromArgb(34, 77, 139), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "or click to browse • Folders add their direct files", Font,
                new Rectangle(8, 38, Width - 16, 22), Color.FromArgb(80, 92, 110), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 10, Height - 10));
        }
        protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }
    }

    public sealed class Rename
    {
        public string From, To;
        public Rename(string from, string to) { From = from; To = to; }
    }

    public static class Engine
    {
        static readonly HashSet<string> sessionJournals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static string[] CleanupSessionJournals()
        {
            var failures = new List<string>();
            lock (sessionJournals)
            {
                foreach (string journal in sessionJournals.ToArray())
                {
                    bool removed = true;
                    foreach (string path in new[] { journal, journal + ".new" })
                        try { File.Delete(path); }
                        catch (Exception e) { removed = false; failures.Add(path + ": " + e.Message); }
                    if (removed) sessionJournals.Remove(journal);
                }
            }
            return failures.ToArray();
        }
        public static string Number(long value, int width)
        {
            if (value < 0) throw new Exception("A filename number cannot be negative.");
            return value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        }
        public static string Sequence(string path, string start, long step, int index, string prefix, string suffix)
        {
            if (!Regex.IsMatch(start, @"^[0-9]{1,18}$")) throw new Exception("Enter a starting number containing 1 to 18 digits.");
            long value = checked(long.Parse(start, CultureInfo.InvariantCulture) + checked(step * index));
            return prefix + Number(value, start.Length) + suffix + Path.GetExtension(path);
        }
        public static string Shift(string path, long offset)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            MatchCollection matches = Regex.Matches(stem, @"[0-9]+");
            if (matches.Count == 0) throw new Exception("No number in " + Path.GetFileName(path));
            Match m = matches[matches.Count - 1];
            long value;
            if (!long.TryParse(m.Value, out value)) throw new Exception("Number is too large in " + Path.GetFileName(path));
            return stem.Substring(0, m.Index) + Number(checked(value + offset), m.Length) + stem.Substring(m.Index + m.Length) + Path.GetExtension(path);
        }
        public static void Validate(IList<Rename> plan)
        {
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Rename r in plan)
            {
                if (!File.Exists(r.From)) throw new Exception("Source no longer exists: " + r.From);
                if (!sources.Add(r.From)) throw new Exception("Duplicate source: " + r.From);
                if ((File.GetAttributes(r.From) & FileAttributes.ReparsePoint) != 0) throw new Exception("Symbolic links are not supported: " + r.From);
            }
            foreach (Rename r in plan)
            {
                string name = Path.GetFileName(r.To);
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.EndsWith(" ") || name.Length > 255)
                    throw new Exception("Invalid Windows filename: " + name);
                if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                    throw new Exception("Reserved Windows filename: " + name);
                if (!string.Equals(Path.GetDirectoryName(r.From), Path.GetDirectoryName(r.To), StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Renaming must stay in the same folder.");
                if (r.To.Length >= 260) throw new Exception("The resulting path is too long: " + r.To);
                if (!destinations.Add(r.To)) throw new Exception("Two files would have the same name: " + name);
                if (Directory.Exists(r.To) || (File.Exists(r.To) && !sources.Contains(r.To)))
                    throw new Exception("A file or folder already uses: " + r.To);
            }
        }
        public static string Apply(IList<Rename> plan, string journalDirectory = null, Action<int, int> progress = null)
        {
            Validate(plan);
            string journalDir = journalDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchNumberRenamer");
            Directory.CreateDirectory(journalDir);
            string journal = Path.Combine(journalDir, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".tsv");
            lock (sessionJournals) sessionJournals.Add(journal);
            string[] current = plan.Select(r => r.From).ToArray();
            string[] temps = plan.Select(r => Path.Combine(Path.GetDirectoryName(r.From), ".rename-" + Guid.NewGuid().ToString("N"))).ToArray();
            Action save = delegate {
                string[] lines = new string[plan.Count + 1];
                lines[0] = "Original\tTarget\tCurrent\tTemporary";
                for (int i = 0; i < plan.Count; i++) lines[i + 1] = plan[i].From + "\t" + plan[i].To + "\t" + current[i] + "\t" + temps[i];
                File.WriteAllLines(journal + ".new", lines);
                if (File.Exists(journal)) File.Replace(journal + ".new", journal, null); else File.Move(journal + ".new", journal);
            };
            save();
            try
            {
                // Stage every source first so sequences and swaps cannot overwrite one another.
                for (int i = 0; i < plan.Count; i++) { File.Move(current[i], temps[i]); current[i] = temps[i]; save(); if (progress != null) progress(i + 1, plan.Count * 2); }
                for (int i = 0; i < plan.Count; i++) { File.Move(current[i], plan[i].To); current[i] = plan[i].To; save(); if (progress != null) progress(plan.Count + i + 1, plan.Count * 2); }
                return journal;
            }
            catch (Exception error)
            {
                var failures = new List<string>();
                // Restage completed destinations before restoring originals; this handles chains.
                for (int i = 0; i < plan.Count; i++)
                {
                    if (current[i] == plan[i].From || current[i] == temps[i]) continue;
                    try { File.Move(current[i], temps[i]); current[i] = temps[i]; } catch (Exception e) { failures.Add(e.Message); }
                }
                for (int i = 0; i < plan.Count; i++)
                {
                    if (current[i] == plan[i].From) continue;
                    try { File.Move(current[i], plan[i].From); current[i] = plan[i].From; } catch (Exception e) { failures.Add(e.Message); }
                }
                try { save(); } catch (Exception e) { failures.Add("Journal update: " + e.Message); }
                throw new Exception(error.Message + (failures.Count == 0 ? "\nThe batch was rolled back." : "\nSome files could not be restored:\n" + string.Join("\n", failures)) + "\nRecovery journal: " + journal);
            }
        }
    }

    public sealed class MainWindow : Form
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);
        readonly List<string> files = new List<string>();
        readonly ComboBox mode = new ComboBox();
        readonly ComboBox sort = new ComboBox();
        readonly TextBox start = new TextBox(), prefix = new TextBox(), suffix = new TextBox();
        readonly NumericUpDown step = new NumericUpDown();
        readonly DataGridView grid = new DataGridView();
        readonly Label status = new Label(), help = new Label(), startLabel = new Label(), stepLabel = new Label();
        readonly Button rename = new Button(), undo = new Button();
        readonly FileDropBox dropBox = new FileDropBox();
        readonly Image logo;
        readonly ProgressBar progressBar = new ProgressBar();
        TableLayoutPanel content, footerPanel;
        bool busy;
        List<Rename> plan = new List<Rename>(), last;
        long[] lastSizes; DateTime[] lastTimes;

        public MainWindow()
        {
            Text = "PLrename"; ClientSize = new Size(900, 774); MinimumSize = new Size(760, 664);
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(246, 248, 251);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi; AllowDrop = true;
            var assembly = typeof(MainWindow).Assembly;
            using (var stream = assembly.GetManifestResourceStream("BatchNumberRenamer.Logo.png"))
            if (stream != null)
            using (var image = Image.FromStream(stream))
            {
                // Adapt the white transparent mark to the light window without a backing box.
                var themed = new Bitmap(image.Width, image.Height);
                using (var graphics = Graphics.FromImage(themed))
                using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                {
                    attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new float[][] {
                        new float[] { -1, 0, 0, 0, 0 }, new float[] { 0, -1, 0, 0, 0 },
                        new float[] { 0, 0, -1, 0, 0 }, new float[] { 0, 0, 0, 1, 0 },
                        new float[] { 1, 1, 1, 0, 1 }
                    }));
                    graphics.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
                logo = themed;
            }
            Icon = Icon.ExtractAssociatedIcon(assembly.Location);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 9 };
            content = layout;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(layout);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Controls.Add(new PictureBox { Image = logo, BackColor = Color.Transparent, SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 10), AccessibleName = "PL Studio logo" }, 0, 0);
            header.Controls.Add(new Label { Text = "PLrename", Font = new Font("Segoe UI", 21, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
            layout.Controls.Add(header, 0, 0);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            toolbar.Controls.Add(Button("Add files…", AddFiles)); toolbar.Controls.Add(Button("Add folder…", AddFolder));
            toolbar.Controls.Add(Button("Remove selected", delegate { foreach (DataGridViewRow r in grid.SelectedRows) files.Remove((string)r.Tag); Preview(); }));
            toolbar.Controls.Add(Button("Clear", delegate { files.Clear(); Preview(); })); layout.Controls.Add(toolbar, 0, 1);
            dropBox.Dock = DockStyle.Fill; dropBox.Margin = new Padding(3, 3, 3, 9);
            dropBox.Click += delegate { AddFiles(); }; layout.Controls.Add(dropBox, 0, 2);
            var sortRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            sortRow.Controls.Add(new Label { Text = "Sort by", AutoSize = true, Margin = new Padding(3, 7, 12, 0) });
            sort.DropDownStyle = ComboBoxStyle.DropDownList; sort.Width = 290; sort.AccessibleName = "File sort order";
            sort.Items.AddRange(new[] { "Name (natural order)", "Modified date (oldest first)", "Modified date (newest first)" }); sort.SelectedIndex = 0;
            sortRow.Controls.Add(sort); layout.Controls.Add(sortRow, 0, 3);
            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2 };
            foreach (int width in new[] { 235, 150, 120, 160, 160 }) options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            options.Controls.Add(new Label { Text = "Numbering mode", AutoSize = true }, 0, 0);
            startLabel.Text = "Start (zeros set width)"; startLabel.AutoSize = true; options.Controls.Add(startLabel, 1, 0);
            stepLabel.Text = "Step"; stepLabel.AutoSize = true; options.Controls.Add(stepLabel, 2, 0);
            options.Controls.Add(new Label { Text = "Prefix", AutoSize = true }, 3, 0); options.Controls.Add(new Label { Text = "Suffix", AutoSize = true }, 4, 0);
            mode.DropDownStyle = ComboBoxStyle.DropDownList; mode.Items.AddRange(new[] { "Assign counting sequence", "Shift existing last number" }); mode.SelectedIndex = 0;
            start.Text = "0001"; step.Minimum = -1000000; step.Maximum = 1000000; step.Value = 1;
            Control[] inputs = { mode, start, step, prefix, suffix }; for (int i = 0; i < inputs.Length; i++) { inputs[i].Dock = DockStyle.Top; options.Controls.Add(inputs[i], i, 1); }
            layout.Controls.Add(options, 0, 4);
            help.Dock = DockStyle.Fill; help.ForeColor = Color.FromArgb(80, 92, 110); layout.Controls.Add(help, 0, 5);
            grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.FixedSingle;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.ReadOnly = true; grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add("old", "Current name"); grid.Columns.Add("new", "New name"); grid.Columns.Add("modified", "Modified date"); grid.Columns.Add("folder", "Folder");
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns[2].FillWeight = 90; grid.Columns[3].FillWeight = 70; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 235, 252); grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            layout.Controls.Add(grid, 0, 6);
            progressBar.Dock = DockStyle.Fill; progressBar.Margin = new Padding(3, 7, 3, 0); progressBar.AccessibleName = "Rename progress";
            layout.Controls.Add(progressBar, 0, 7);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 10, 0, 0) };
            footerPanel = footer;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            status.Dock = DockStyle.Fill; status.AutoEllipsis = true; footer.Controls.Add(status, 0, 0);
            undo.Text = "Undo last batch"; undo.Dock = DockStyle.Fill; undo.Enabled = false; undo.Click += delegate { Undo(); }; footer.Controls.Add(undo, 1, 0);
            rename.Text = "Rename files"; rename.Dock = DockStyle.Fill; rename.BackColor = Color.FromArgb(34, 98, 210); rename.ForeColor = Color.White; rename.FlatStyle = FlatStyle.Flat;
            rename.Click += delegate { Apply(); }; footer.Controls.Add(rename, 2, 0); layout.Controls.Add(footer, 0, 8);
            mode.SelectedIndexChanged += delegate { Preview(); }; start.TextChanged += delegate { Preview(); }; step.ValueChanged += delegate { Preview(); };
            prefix.TextChanged += delegate { Preview(); }; suffix.TextChanged += delegate { Preview(); };
            sort.SelectedIndexChanged += delegate { Preview(); };
            EnableFileDrop(this);
            Preview();
        }
        void EnableFileDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += OnFileDragEnter;
            control.DragOver += OnFileDragEnter;
            control.DragLeave += delegate { dropBox.DragActive = false; dropBox.Invalidate(); };
            control.DragDrop += OnFileDrop;
            foreach (Control child in control.Controls) EnableFileDrop(child);
        }
        void OnFileDragEnter(object sender, DragEventArgs e)
        {
            bool accepted = !busy && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop) && (e.AllowedEffect & DragDropEffects.Copy) != 0;
            e.Effect = accepted ? DragDropEffects.Copy : DragDropEffects.None;
            dropBox.DragActive = accepted; dropBox.Invalidate();
        }
        void OnFileDrop(object sender, DragEventArgs e)
        {
            dropBox.DragActive = false; dropBox.Invalidate();
            if (busy || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) LoadPaths(paths);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (logo != null) logo.Dispose(); if (Icon != null) Icon.Dispose(); }
            base.Dispose(disposing);
        }
        Button Button(string text, Action action)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, Padding = new Padding(6, 0, 6, 0) }; b.Click += delegate { action(); }; return b;
        }
        void AddFiles() { using (var d = new OpenFileDialog { Multiselect = true, Title = "Choose files to rename", Filter = "All files|*.*" }) if (d.ShowDialog(this) == DialogResult.OK) LoadPaths(d.FileNames); }
        void AddFolder() { using (var d = new FolderBrowserDialog { Description = "Choose a folder. Only its direct files are added.", ShowNewFolderButton = false }) if (d.ShowDialog(this) == DialogResult.OK) LoadPaths(new[] { d.SelectedPath }); }
        void LoadPaths(IEnumerable<string> paths)
        {
            try {
                foreach (string p in paths) {
                    var items = Directory.Exists(p) ? Directory.GetFiles(p) : new[] { p };
                    foreach (string f in items) if (File.Exists(f) && !files.Contains(f, StringComparer.OrdinalIgnoreCase)) files.Add(Path.GetFullPath(f));
                }
            } catch (Exception e) { MessageBox.Show(this, e.Message, "Could not load some files"); }
            Preview();
        }
        void Preview()
        {
            if (busy) return;
            bool sequence = mode.SelectedIndex == 0; start.Enabled = prefix.Enabled = suffix.Enabled = sequence;
            stepLabel.Text = sequence ? "Step" : "Offset";
            help.Text = sequence ? "Enter 009 for 009, 010, 011… • Extensions are kept • Numbering follows the order shown below" : "Changes the last number before the extension: photo_009.jpg → photo_010.jpg (offset +1)";
            grid.Rows.Clear(); plan.Clear(); string error = null;
            var modified = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try {
                foreach (string file in files) modified[file] = File.GetLastWriteTimeUtc(file);
                files.Sort(delegate(string a, string b) {
                    if (sort.SelectedIndex > 0) {
                        int dateOrder = modified[a].CompareTo(modified[b]);
                        if (dateOrder != 0) return sort.SelectedIndex == 2 ? -dateOrder : dateOrder;
                    }
                    int folderOrder = StrCmpLogicalW(Path.GetDirectoryName(a), Path.GetDirectoryName(b));
                    if (folderOrder != 0) return folderOrder;
                    int nameOrder = StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b));
                    return nameOrder != 0 ? nameOrder : StringComparer.OrdinalIgnoreCase.Compare(a, b);
                });
            } catch (Exception e) { error = "Could not read modified dates: " + e.Message; }
            for (int i = 0; i < files.Count; i++) {
                string name = "";
                try {
                    name = sequence ? Engine.Sequence(files[i], start.Text, (long)step.Value, i, prefix.Text, suffix.Text) : Engine.Shift(files[i], (long)step.Value);
                    // Check the raw name before combining it with a path.
                    if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new Exception("Prefix or suffix contains an invalid filename character.");
                    plan.Add(new Rename(files[i], Path.Combine(Path.GetDirectoryName(files[i]), name)));
                } catch (Exception e) { error = e.Message; name = "⚠ " + e.Message; }
                string date = modified.ContainsKey(files[i]) ? modified[files[i]].ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "Unavailable";
                int row = grid.Rows.Add(Path.GetFileName(files[i]), name, date, Path.GetDirectoryName(files[i])); grid.Rows[row].Tag = files[i];
            }
            if (error == null && files.Count > 0) try { Engine.Validate(plan); } catch (Exception e) { error = e.Message; }
            int changed = plan.Count(r => !string.Equals(r.From, r.To, StringComparison.Ordinal));
            status.Text = error ?? (files.Count == 0 ? "Add files or drop them here to begin." : files.Count + " files • " + changed + " names will change");
            status.ForeColor = error == null ? Color.FromArgb(65, 80, 95) : Color.Firebrick;
            rename.Enabled = error == null && changed > 0;
        }
        void Apply()
        {
            Preview(); if (!rename.Enabled) return;
            var batch = plan.ToList();
            if (MessageBox.Show(this, "Rename " + batch.Count + " files using the preview?", "Apply rename", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            RunBatch(batch, false);
        }
        void Undo()
        {
            if (busy || last == null) return;
            try {
                for (int i = 0; i < last.Count; i++) if (!File.Exists(last[i].To) || new FileInfo(last[i].To).Length != lastSizes[i] || File.GetLastWriteTimeUtc(last[i].To) != lastTimes[i]) throw new Exception("A renamed file has changed or moved. Undo was stopped: " + last[i].To);
                RunBatch(last.Select(r => new Rename(r.To, r.From)).ToList(), true);
            } catch (Exception e) { MessageBox.Show(this, e.Message, "Undo failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        void SetBusy(bool value)
        {
            if (value) busy = true;
            UseWaitCursor = value;
            foreach (Control control in content.Controls)
                if (control != progressBar && control != footerPanel) control.Enabled = !value;
            rename.Enabled = !value; undo.Enabled = !value && last != null;
            busy = value;
        }
        void RunBatch(List<Rename> batch, bool isUndo, string journalDirectory = null)
        {
            SetBusy(true); progressBar.Value = 0; status.ForeColor = Color.FromArgb(65, 80, 95); status.Text = "Preparing files…";
            var worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += delegate(object sender, DoWorkEventArgs e) {
                var sizes = batch.Select(r => new FileInfo(r.From).Length).ToArray();
                var times = batch.Select(r => File.GetLastWriteTimeUtc(r.From)).ToArray();
                Engine.Apply(batch, journalDirectory, delegate(int done, int total) { worker.ReportProgress((int)((long)done * 100 / total), new int[] { done, total }); });
                e.Result = new object[] { sizes, times };
            };
            worker.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e) {
                BeginInvoke((Action)delegate {
                progressBar.Value = e.ProgressPercentage;
                var counts = (int[])e.UserState; int count = counts[1] / 2;
                status.Text = counts[0] <= count ? "Preparing files… " + counts[0] + " / " + count : (isUndo ? "Restoring names… " : "Renaming files… ") + (counts[0] - count) + " / " + count;
                });
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e) {
                BeginInvoke((Action)delegate {
                if (e.Error != null) {
                    SetBusy(false); progressBar.Value = 0; Preview();
                    MessageBox.Show(this, e.Error.Message, isUndo ? "Undo failed" : "Rename failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                } else {
                    if (isUndo) last = null;
                    else { last = batch; var metadata = (object[])e.Result; lastSizes = (long[])metadata[0]; lastTimes = (DateTime[])metadata[1]; }
                    files.Clear(); files.AddRange(batch.Select(r => r.To)); SetBusy(false); Preview(); progressBar.Value = 100;
                    status.Text = isUndo ? "Original names restored." : "Renamed " + batch.Count + " files. Undo is available until you close the app.";
                }
                worker.Dispose();
                });
            };
            worker.RunWorkerAsync();
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { e.Cancel = true; status.Text = "Please wait for the current batch to finish."; }
            base.OnFormClosing(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            string[] failures = Engine.CleanupSessionJournals();
            if (failures.Length > 0)
                MessageBox.Show("Some session logs could not be deleted:\n" + string.Join("\n", failures), "PLrename log cleanup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            base.OnFormClosed(e);
        }
    }

    static class Program
    {
        [STAThread] static void Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainWindow());
        }
    }
}
