using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Web.Script.Serialization;

class ModInfo {
  public string Id = "", Name = "", Version = "", Loader = "unknown", File = "";
  public bool Locked, Unknown;
  public List<string> Depends = new List<string>();
  public override string ToString() { return Name; }
}
class Candidate { public string Url, File, Version, Project; }
class BufferedTableLayoutPanel : TableLayoutPanel {
  public BufferedTableLayoutPanel() { DoubleBuffered = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
}
class GlassProgressBar : ProgressBar {
  public GlassProgressBar() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Height = 9; }
  protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Color.FromArgb(34, 43, 68)); if (Maximum > Minimum && Value > Minimum) { int width = (int)((float)(Value - Minimum) / (Maximum - Minimum) * Width); using (var brush = new SolidBrush(Color.FromArgb(135, 166, 255))) e.Graphics.FillRectangle(brush, 0, 0, width, Height); } }
}

class MainForm : Form {
  TextBox source = new TextBox(), target = new TextBox(); ComboBox gameVersion = new ComboBox(); CheckBox allowPatch = new CheckBox { Text = "允许同分支补丁版（可能不兼容）" };
  ComboBox loader = new ComboBox(); DataGridView grid = new DataGridView();
  Button scan = new Button { Text = "读取模组" }, migrate = new Button { Text = "开始迁移" };
  GlassProgressBar progress = new GlassProgressBar(); TextBox log = new TextBox();
  List<ModInfo> mods = new List<ModInfo>(); StringBuilder logBuffer = new StringBuilder(); JavaScriptSerializer json = new JavaScriptSerializer();
  HttpClient http = new HttpClient();

  public MainForm() {
    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); UpdateStyles();
    Text = "MC Mod Migrator"; Width = 1180; Height = 820; MinimumSize = new Size(960, 680);
    BackColor = Color.FromArgb(15, 20, 34); ForeColor = Color.FromArgb(242, 245, 255); Font = new Font("Microsoft YaHei UI", 11);
    string background = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Background.jpg"); if (File.Exists(background)) { using (var original = Image.FromFile(background)) BackgroundImage = new Bitmap(original); BackgroundImageLayout = ImageLayout.Stretch; }
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MC-Mod-Migrator/1.0"); http.Timeout = TimeSpan.FromSeconds(25); json.MaxJsonLength = Int32.MaxValue;
    gameVersion.DropDownStyle = ComboBoxStyle.DropDown; gameVersion.MaxDropDownItems = 12; gameVersion.Items.AddRange(new object[] { "26.2", "26.1", "1.21.8", "1.21.7", "1.21.6", "1.21.5", "1.21.4", "1.21.3", "1.21.2", "1.21.1", "1.20.6", "1.20.5", "1.20.4", "1.20.3", "1.20.2", "1.20.1", "1.19.4", "1.19.3", "1.19.2", "1.18.2", "1.17.1", "1.16.5", "1.15.2", "1.14.4", "1.13.2", "1.12.2", "1.11.2", "1.10.2", "1.9.4", "1.8.9", "1.7.10" });
    loader.Items.AddRange(new object[] { "fabric", "forge", "neoforge", "quilt" }); loader.SelectedIndex = -1; loader.DropDownStyle = ComboBoxStyle.DropDownList; loader.MaxDropDownItems = 4;
    var root = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(46, 30, 46, 30), ColumnCount = 1, RowCount = 6, BackColor = Color.FromArgb(180, 10, 16, 30) };
    root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 210)); Controls.Add(root);
    var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(8, 0, 0, 0) };
    heading.Controls.Add(new Label { Text = "MC Mod Migrator", ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 27, FontStyle.Bold), AutoSize = true });
    heading.Controls.Add(new Label { Text = "跨版本迁移 · 自动匹配 · 保留配置与快捷键", ForeColor = Color.FromArgb(220, 226, 242), Font = new Font("Microsoft YaHei UI", 11), AutoSize = true }); root.Controls.Add(heading, 0, 0);
    root.Controls.Add(PathRow("来源 mods 文件夹", source, delegate { Pick(source); }), 0, 1);
    var options = new FlowLayoutPanel { AutoSize = false, Height = 52, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 6), Padding = new Padding(16, 7, 16, 7), BackColor = Color.FromArgb(215, 26, 33, 55) }; Round(options, 14);
    options.Controls.Add(new Label { Text = "目标版本", AutoSize = true, Padding = new Padding(0, 9, 6, 0), ForeColor = Color.White }); gameVersion.Width = 130; gameVersion.Height = 34; StyleSelect(gameVersion); options.Controls.Add(gameVersion);
    options.Controls.Add(new Label { Text = "（也可以自己输入版本号）", AutoSize = true, Padding = new Padding(4, 9, 6, 0), ForeColor = Color.FromArgb(220, 226, 242), Font = new Font("Microsoft YaHei UI", 9) });
    options.Controls.Add(new Label { Text = "加载器", AutoSize = true, Padding = new Padding(18, 9, 6, 0), ForeColor = Color.White }); loader.Width = 120; loader.Height = 34; StyleSelect(loader); options.Controls.Add(loader);
    allowPatch.AutoSize = true; allowPatch.ForeColor = Color.FromArgb(220, 226, 242); allowPatch.Padding = new Padding(14, 6, 0, 0); allowPatch.Font = new Font("Microsoft YaHei UI", 9); options.Controls.Add(allowPatch);
    scan.BackColor = Color.FromArgb(120, 152, 255); scan.ForeColor = Color.White; scan.FlatStyle = FlatStyle.Flat; scan.FlatAppearance.BorderSize = 0; scan.Height = 38; Round(scan, 11); scan.Click += delegate { Scan(); }; options.Controls.Add(scan); root.Controls.Add(options, 0, 2);
    root.Controls.Add(Grid(), 0, 3);
    var targetRow = PathRow("目标 mods 文件夹", target, delegate { Pick(target); }); targetRow.Margin = new Padding(0, 12, 0, 8); targetRow.Controls.Add(migrate); migrate.BackColor = Color.FromArgb(255, 162, 107); migrate.ForeColor = Color.FromArgb(25, 20, 32); migrate.FlatStyle = FlatStyle.Flat; migrate.FlatAppearance.BorderSize = 0; migrate.Height = 42; Round(migrate, 11); migrate.Enabled = false; migrate.Click += async delegate { await Migrate(); }; root.Controls.Add(targetRow, 0, 4);
    var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 10)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); progress.Dock = DockStyle.Fill; bottom.Controls.Add(progress, 0, 0);
    log.Dock = DockStyle.Fill; log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.BorderStyle = BorderStyle.None; log.Font = new Font("Consolas", 10); log.BackColor = Color.FromArgb(25, 31, 51); log.ForeColor = Color.FromArgb(225, 231, 245); bottom.Controls.Add(log, 0, 1); root.Controls.Add(bottom, 0, 5);
    Opacity = 0; var fade = new Timer { Interval = 15 }; fade.Tick += delegate { Opacity = Math.Min(1, Opacity + 0.065); if (Opacity >= 1) fade.Stop(); }; Shown += async delegate { fade.Start(); await LoadReleaseVersions(); };
  }
  Control PathRow(string label, TextBox box, Action browse) {
    var row = new FlowLayoutPanel { AutoSize = false, Height = 64, Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.FromArgb(42, 50, 75), Padding = new Padding(18, 12, 18, 12) }; Round(row, 16); var l = new Label { Text = label, Width = 170, Height = 38, AutoSize = false, Padding = new Padding(0, 8, 0, 0), ForeColor = Color.FromArgb(235, 239, 250), Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold) }; box.Width = 650; box.Height = 38; box.Font = new Font("Microsoft YaHei UI", 11); box.BorderStyle = BorderStyle.FixedSingle; box.BackColor = Color.FromArgb(68, 82, 123); box.ForeColor = Color.White; box.Margin = new Padding(3, 0, 8, 0); box.TextChanged += delegate { migrate.Enabled = Directory.Exists(source.Text) && Directory.Exists(target.Text) && mods.Count > 0; }; var b = new Button { Text = "浏览文件夹", Width = 112, Height = 38, BackColor = Color.FromArgb(74, 91, 137), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; b.FlatAppearance.BorderSize = 0; Round(b, 11); b.Click += delegate { browse(); }; row.Controls.Add(l); row.Controls.Add(box); row.Controls.Add(b); return row;
  }
  Control Grid() {
    grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.FromArgb(24, 30, 48); grid.BorderStyle = BorderStyle.None; grid.CellBorderStyle = DataGridViewCellBorderStyle.None; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; grid.AutoGenerateColumns = false; grid.AllowUserToAddRows = false; grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersHeight = 46; grid.RowTemplate.Height = 42; grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(45, 56, 88); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White; grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold); grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 0, 0); grid.DefaultCellStyle.BackColor = Color.FromArgb(24, 30, 48); grid.DefaultCellStyle.ForeColor = Color.FromArgb(238, 242, 252); grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(62, 82, 135); grid.DefaultCellStyle.Padding = new Padding(8, 0, 4, 0); grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(29, 36, 58); grid.GridColor = Color.FromArgb(55, 65, 95);
    grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Move", HeaderText = "迁移", Width = 55 }); grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "模组", Width = 240 }); grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "内部 ID", Width = 190 }); grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "版本", Width = 130 }); grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "加载器", Width = 100 }); grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "文件", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }); return grid;
  }
  void Pick(TextBox box) { using (var dialog = new FolderBrowserDialog { Description = "选择 Minecraft mods 文件夹", ShowNewFolderButton = true }) { if (dialog.ShowDialog(this) == DialogResult.OK) { box.Text = dialog.SelectedPath; if (box == source) Scan(); } } }
  void Write(string message) { string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine; logBuffer.Append(line); log.AppendText(line); }
  void Scan() {
    if (!Directory.Exists(source.Text)) { MessageBox.Show(this, "请选择有效的来源 mods 文件夹。", "MC Mod Migrator"); return; }
    mods.Clear(); grid.Rows.Clear(); log.Clear(); logBuffer.Clear();
    foreach (string file in Directory.GetFiles(source.Text, "*.jar")) { try { mods.Add(ReadJar(file)); } catch (Exception ex) { mods.Add(new ModInfo { Name = Path.GetFileName(file), File = Path.GetFileName(file), Unknown = true }); Write("无法读取 " + Path.GetFileName(file) + "：" + ex.Message); } }
    foreach (ModInfo mod in mods.OrderBy(m => m.Name)) { int row = grid.Rows.Add(!mod.Locked && !mod.Unknown, mod.Name, mod.Id, mod.Version, mod.Loader, mod.File + (mod.Locked ? "  [核心已锁定]" : mod.Unknown ? "  [未识别]" : "")); grid.Rows[row].Tag = mod; grid.Rows[row].Cells[0].ReadOnly = mod.Locked || mod.Unknown; }
    Write("已读取 " + mods.Count + " 个 JAR。核心加载器与未识别文件不会迁移。"); migrate.Enabled = Directory.Exists(target.Text) && mods.Count > 0;
  }
  ModInfo ReadJar(string file) {
    var mod = new ModInfo { File = Path.GetFileName(file) }; using (var zip = ZipFile.OpenRead(file)) {
      var fabric = zip.GetEntry("fabric.mod.json"); if (fabric != null) { var d = Obj(ReadEntry(fabric)); mod.Id = Str(d, "id"); mod.Name = Str(d, "name"); mod.Version = Str(d, "version"); mod.Loader = "fabric"; var deps = Get(d, "depends") as Dictionary<string, object>; if (deps != null) mod.Depends.AddRange(deps.Keys.Where(x => x != "minecraft" && x != "fabricloader" && x != "java")); }
      else { var toml = zip.GetEntry("META-INF/neoforge.mods.toml") ?? zip.GetEntry("META-INF/mods.toml"); if (toml != null) { string t = ReadEntry(toml); mod.Id = Match(t, "modId\\s*=\\s*[\\\"']([^\\\"']+)"); mod.Name = Match(t, "displayName\\s*=\\s*[\\\"']([^\\\"']+)"); mod.Version = Match(t, "version\\s*=\\s*[\\\"']([^\\\"']+)"); mod.Loader = t.IndexOf("neoforge", StringComparison.OrdinalIgnoreCase) >= 0 ? "neoforge" : "forge"; foreach (Match m in Regex.Matches(t, "modId\\s*=\\s*[\\\"']([^\\\"']+)")) if (m.Groups[1].Value != mod.Id && m.Groups[1].Value != "minecraft" && m.Groups[1].Value != "forge") mod.Depends.Add(m.Groups[1].Value); } }
    }
    if (String.IsNullOrEmpty(mod.Id)) { mod.Name = Path.GetFileNameWithoutExtension(file); mod.Unknown = true; } if (String.IsNullOrEmpty(mod.Name)) mod.Name = mod.Id;
    string lower = mod.File.ToLowerInvariant(); mod.Locked = new[] { "fabric-loader", "fabric-api-", "forge-", "neoforge-", "quilt-loader" }.Any(x => lower.StartsWith(x)) || new[] { "fabricloader", "fabric-api", "forge", "neoforge", "quilt_loader", "minecraft", "java" }.Contains(mod.Id); return mod;
  }
  async Task Migrate() {
    if (!Directory.Exists(target.Text)) { MessageBox.Show(this, "请选择有效的目标 mods 文件夹。", "MC Mod Migrator"); return; }
    if (String.IsNullOrWhiteSpace(gameVersion.Text)) { MessageBox.Show(this, "请选择或输入目标 Minecraft 版本号。", "MC Mod Migrator"); gameVersion.Focus(); return; }
    if (String.IsNullOrWhiteSpace(loader.Text)) { MessageBox.Show(this, "请选择目标模组加载器。", "MC Mod Migrator"); loader.Focus(); return; }
    var selected = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells[0].Value != null && Convert.ToBoolean(r.Cells[0].Value)).Select(r => (ModInfo)r.Tag).ToList(); if (!selected.Any()) { MessageBox.Show(this, "请先读取来源模组，并至少勾选一个要迁移的模组。", "MC Mod Migrator"); return; }
    string originalButtonText = migrate.Text; migrate.Enabled = scan.Enabled = false; migrate.Text = "正在匹配…"; UseWaitCursor = true; progress.Value = 0; progress.Maximum = selected.Count * 2 + 1; var found = new Dictionary<string, Candidate>(); var failed = new HashSet<string>(); Write("开始迁移：正在匹配 " + selected.Count + " 个模组…"); await Task.Yield();
    try {
      foreach (var mod in selected) { migrate.Text = "查找：" + mod.Name; Write("查找 " + mod.Name + "..."); var candidate = await Find(mod); if (candidate == null) { failed.Add(mod.Id); Write("未找到 " + mod.Name + " 的 " + gameVersion.Text + " / " + loader.Text + " 版本。"); } else { found[mod.Id] = candidate; Write("匹配：" + mod.Name + " -> " + candidate.Project); } progress.Value++; }
      bool changed = true; while (changed) { changed = false; foreach (var mod in selected) if (!failed.Contains(mod.Id) && mod.Depends.Any(d => failed.Contains(d))) { failed.Add(mod.Id); changed = true; } }
      var moved = new List<ModInfo>(); foreach (var mod in selected) { if (failed.Contains(mod.Id)) { Write("跳过 " + mod.Name + "（缺失版本或依赖）"); progress.Value++; continue; } var c = found[mod.Id]; migrate.Text = "下载：" + mod.Name; Write("下载 " + mod.Name + "..."); string destination = Path.Combine(target.Text, c.File); if (!File.Exists(destination)) File.WriteAllBytes(destination, await http.GetByteArrayAsync(c.Url)); moved.Add(mod); Write("已迁移 " + mod.Name); progress.Value++; }
      CopyConfigs(moved); progress.Value++; Write("完成。已迁移 " + moved.Count + " 个模组。"); string report = SaveLog(); Write("完整日志已保存到：" + report); MessageBox.Show(this, "模组迁移完成。\n已迁移 " + moved.Count + " 个模组。\n日志已保存到程序根目录。", "MC Mod Migrator");
    } catch (Exception ex) { Write("迁移失败：" + ex.Message); SaveLog(); MessageBox.Show(this, "迁移没有完成：\n" + ex.Message + "\n日志已保存到程序根目录。", "MC Mod Migrator"); } finally { migrate.Text = originalButtonText; UseWaitCursor = false; migrate.Enabled = scan.Enabled = true; }
  }
  async Task<Candidate> Find(ModInfo mod) {
    string facets = Uri.EscapeDataString("[[\"categories:" + loader.Text + "\"]]"); var hits = new List<Dictionary<string, object>>();
    foreach (string term in new[] { mod.Id, mod.Name }.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct()) { string search = await http.GetStringAsync("https://api.modrinth.com/v2/search?query=" + Uri.EscapeDataString(term) + "&facets=" + facets + "&limit=12"); foreach (object item in Items(Get(Obj(search), "hits"))) { var hit = item as Dictionary<string, object>; if (hit != null && !hits.Any(x => Str(x, "project_id") == Str(hit, "project_id"))) hits.Add(hit); } }
    hits = hits.Where(x => IsExactProject(x, mod)).OrderByDescending(x => Clean(Str(x, "slug")) == Clean(mod.Id) || Clean(Str(x, "slug")) == Clean(mod.Name) || Clean(Str(x, "title")) == Clean(mod.Id) || Clean(Str(x, "title")) == Clean(mod.Name)).ToList();
    foreach (var hit in hits) { string projectId = Str(hit, "project_id"); string title = Str(hit, "title"); string endpoint = "https://api.modrinth.com/v2/project/" + projectId + "/version?loaders=" + Uri.EscapeDataString("[\"" + loader.Text + "\"]"); var versions = Items(json.DeserializeObject(await http.GetStringAsync(endpoint))); var version = versions.Select(x => x as Dictionary<string, object>).FirstOrDefault(x => x != null && Items(Get(x, "game_versions")).Select(v => Convert.ToString(v)).Any(GameVersionMatches)); if (version == null) continue; var files = Items(Get(version, "files")); if (files.Count == 0) continue; var file = files[0] as Dictionary<string, object>; return new Candidate { Url = Str(file, "url"), File = Str(file, "filename"), Version = Str(version, "version_number"), Project = title }; }
    return null;
  }
  void CopyConfigs(List<ModInfo> moved) {
    string from = Path.Combine(Directory.GetParent(source.Text).FullName, "config"), to = Path.Combine(Directory.GetParent(target.Text).FullName, "config"); if (!Directory.Exists(from)) { Write("未发现来源 config 文件夹。"); return; }
    int copied = 0; foreach (string file in Directory.GetFiles(from, "*.*", SearchOption.AllDirectories)) { string ext = Path.GetExtension(file).ToLowerInvariant(); if (!(new[] { ".json", ".toml", ".cfg", ".conf", ".properties", ".txt" }).Contains(ext)) continue; string relative = file.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar); string key = Regex.Replace(relative.ToLowerInvariant(), "[^a-z0-9]", ""); if (!moved.Any(m => Clean(m.Id).Length >= 4 && key.Contains(Clean(m.Id)))) continue; string outFile = Path.Combine(to, relative); Directory.CreateDirectory(Path.GetDirectoryName(outFile)); if (File.Exists(outFile) && !File.ReadAllBytes(outFile).SequenceEqual(File.ReadAllBytes(file))) File.Copy(outFile, NextBackup(outFile)); File.Copy(file, outFile, true); copied++; }
    Write("已迁移 " + copied + " 个模组配置/快捷键文件。");
  }
  string NextBackup(string file) { string p = file + ".migrator-backup"; int i = 1; while (File.Exists(p)) p = file + ".migrator-backup." + i++; return p; }
  string SaveLog() { try { string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"); Directory.CreateDirectory(folder); string file = Path.Combine(folder, "migration-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log"); File.WriteAllText(file, logBuffer.ToString(), new UTF8Encoding(true)); return file; } catch { return "日志保存失败"; } }
  async Task LoadReleaseVersions() {
    try {
      var manifest = Obj(await http.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
      var values = Items(Get(manifest, "versions")); if (values.Count == 0) return;
      var releases = values.Select(x => x as Dictionary<string, object>).Where(x => x != null && Str(x, "type") == "release").Select(x => Str(x, "id")).Where(x => x.Length > 0).ToList();
      string selected = gameVersion.Text; gameVersion.BeginUpdate(); gameVersion.Items.Clear(); gameVersion.Items.AddRange(releases.ToArray()); gameVersion.Text = selected; gameVersion.EndUpdate();
      Write("已加载 " + releases.Count + " 个 Minecraft 正式版；也可以直接输入版本号。");
    } catch { Write("无法获取版本清单，已保留常用版本；仍可手动输入。 "); }
  }
  void StyleSelect(ComboBox box) { box.FlatStyle = FlatStyle.Flat; box.BackColor = Color.FromArgb(68, 82, 123); box.ForeColor = Color.White; box.Font = new Font("Microsoft YaHei UI", 11); box.DrawMode = DrawMode.OwnerDrawFixed; box.DrawItem += delegate(object sender, DrawItemEventArgs e) { var combo = sender as ComboBox; if (e.Index < 0) return; bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected; using (var back = new SolidBrush(selected ? Color.FromArgb(106, 128, 190) : Color.FromArgb(43, 54, 87))) e.Graphics.FillRectangle(back, e.Bounds); using (var foreground = new SolidBrush(Color.White)) e.Graphics.DrawString(combo.Items[e.Index].ToString(), combo.Font, foreground, e.Bounds.X + 9, e.Bounds.Y + 4); e.DrawFocusRectangle(); }; }
  string ReadEntry(ZipArchiveEntry entry) { using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) return reader.ReadToEnd(); }
  Dictionary<string, object> Obj(string text) { return json.DeserializeObject(text) as Dictionary<string, object>; }
  List<object> Items(object value) { var array = value as object[]; if (array != null) return array.ToList(); var list = value as ArrayList; if (list != null) return list.Cast<object>().ToList(); var sequence = value as IEnumerable; return sequence == null ? new List<object>() : sequence.Cast<object>().ToList(); }
  bool IsExactProject(Dictionary<string, object> project, ModInfo mod) { string[] candidates = new[] { Clean(Str(project, "slug")), Clean(Str(project, "title")) }; string[] expected = new[] { Clean(mod.Id), Clean(mod.Name) }.Where(x => x.Length >= 4).ToArray(); return candidates.Any(value => expected.Contains(value)); }
  bool GameVersionMatches(string available) { string requested = gameVersion.Text.Trim(); if (available == requested) return true; return allowPatch.Checked && Regex.IsMatch(requested, "^\\d+\\.\\d+$") && available.StartsWith(requested + ".", StringComparison.Ordinal); }
  object Get(Dictionary<string, object> d, string key) { object value; return d != null && d.TryGetValue(key, out value) ? value : null; }
  string Str(Dictionary<string, object> d, string key) { object v = Get(d, key); return v == null ? "" : Convert.ToString(v); }
  string Match(string text, string pattern) { var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value : ""; }
  string Clean(string value) { return Regex.Replace((value ?? "").ToLowerInvariant(), "[^a-z0-9]", ""); }
  void Round(Control control, int radius) { Action apply = delegate { if (control.Width < radius * 2 || control.Height < radius * 2) return; using (var path = new GraphicsPath()) { int d = radius * 2; path.AddArc(0, 0, d, d, 180, 90); path.AddArc(control.Width - d, 0, d, d, 270, 90); path.AddArc(control.Width - d, control.Height - d, d, d, 0, 90); path.AddArc(0, control.Height - d, d, d, 90, 90); path.CloseFigure(); control.Region = new Region(path); } }; control.Resize += delegate { apply(); }; control.HandleCreated += delegate { apply(); }; apply(); }
  protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x02000000; return value; } }
  [STAThread] static void Main() { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm()); }
}
