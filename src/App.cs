using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CraftBridge
{
    public sealed class AppForm : Form
    {
        readonly Panel exportPage = new Panel(), importPage = new Panel(), pageHost = new Panel();
        readonly Button exportNav, importNav, cancelButton, revealButton;
        readonly Label statusLabel = Ui.Label("准备就绪，开始一次新的迁移。", 8.5F, false);
        readonly ProgressBar progress = new ProgressBar();
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 18000, InitialDelay = 500, ReshowDelay = 200 };
        readonly TextBox sourceRoot = Ui.TextBox();
        readonly ComboBox sourceInstances = Ui.Combo(), purpose = Ui.Combo();
        readonly Label sourceInfo = Ui.Label("支持 PCL、HMCL、Prism 与官方启动器。", 8.5F, false);
        readonly Label sourcePath = Ui.Label("选择启动器文件夹或实际游戏目录", 8.5F, false);
        readonly TreeView fileTree = new TreeView();
        readonly EmptyState exportEmpty = new EmptyState();
        readonly Label selectedSummary = Ui.Label("尚未选择文件", 11F, true);
        readonly TextBox exportNotice = Ui.Notice();
        readonly Button exportButton, runtimeButton, customButton;
        ScanResult scan;
        bool treeUpdating, runtimeComplete;
        readonly List<TreeNode> leafNodes = new List<TreeNode>();
        readonly List<Action> pageResizers = new List<Action>();
        readonly TextBox archivePath = Ui.TextBox(), targetGame = Ui.TextBox(), targetRuntime = Ui.TextBox();
        readonly ComboBox targetLauncher = Ui.Combo(), conflict = Ui.Combo();
        readonly Label targetHint = Ui.Label("", 8.3F, false), packageInfo = Ui.Label("选择 CraftBridge 迁移包，旧版包也能打开。", 8.5F, false);
        readonly CheckedListBox importCategories = new CheckedListBox();
        readonly DataGridView previewGrid = new DataGridView();
        readonly EmptyState importEmpty = new EmptyState { Heading = "预览后，再放心导入", Detail = "选择迁移包和目标目录，查看需要补充的文件。" };
        readonly TextBox importNotice = Ui.Notice();
        readonly Label importSummary = Ui.Label("准备好后，先预览差异。", 9F, false);
        readonly Button previewButton, importButton;
        readonly Panel runtimeRow = new Panel();
        PackageManifest manifest;
        ImportPlan plan;
        bool settingImport;
        CancellationTokenSource cancellation;
        bool busy, closeAfterJob, layoutReady;
        int jobGeneration;
        string lastResultPath;

        public AppForm(string initialSource, string initialArchive)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            Text = "CraftBridge"; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 850); MinimumSize = new Size(1000, 720);
            Font = new Font("Microsoft YaHei UI", 9F); BackColor = Ui.Background;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = Ui.Sidebar };
            sidebar.Controls.Add(new BridgeMark { Location = new Point(23, 30), BackColor = Ui.Sidebar });
            Label brand = Ui.Label("CraftBridge", 15F, true); brand.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            brand.AutoSize = false; brand.Size = new Size(164, 32); brand.Location = new Point(22, 90); sidebar.Controls.Add(brand);
            Label tagline = Ui.Label("把世界，轻松带走。", 8.7F, false); tagline.ForeColor = Ui.Muted; tagline.Location = new Point(23, 128); sidebar.Controls.Add(tagline);
            Label section = Ui.Label("迁移", 8F, true); section.ForeColor = Ui.Muted; section.Location = new Point(24, 192); sidebar.Controls.Add(section);
            exportNav = NavButton("导出迁移包", 219, Glyph.Export); importNav = NavButton("导入迁移包", 269, Glyph.Import);
            sidebar.Controls.Add(exportNav); sidebar.Controls.Add(importNav);
            exportNav.Click += delegate { ShowPage(false); }; importNav.Click += delegate { ShowPage(true); };
            Button help = NavButton("使用指南", 338, Glyph.Help); sidebar.Controls.Add(help); help.Click += delegate { ShowHelp(); };
            Label footer = Ui.Label("本地处理 · 由你分享\r\nv0.2.0  /  Java Edition", 8F, false); footer.ForeColor = Ui.Muted;
            footer.AutoSize = false; footer.Size = new Size(160, 51); footer.Location = new Point(24, sidebar.Height - 74); footer.Anchor = AnchorStyles.Left | AnchorStyles.Bottom; sidebar.Controls.Add(footer);
            Panel right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(30, 25, 30, 14), BackColor = Ui.Background };
            TableLayoutPanel shell = Ui.Table(1); shell.RowCount = 2; shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
            pageHost.Dock = DockStyle.Fill; pageHost.Margin = Padding.Empty; shell.Controls.Add(pageHost, 0, 0);
            TableLayoutPanel status = Ui.Table(3); status.RowCount = 2; status.RowStyles.Clear();
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 9)); status.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            progress.Dock = DockStyle.Fill; progress.Margin = new Padding(0, 3, 0, 3); progress.Visible = false; status.Controls.Add(progress, 0, 0); status.SetColumnSpan(progress, 3);
            statusLabel.Dock = DockStyle.Fill; statusLabel.AutoEllipsis = true; statusLabel.TextAlign = ContentAlignment.MiddleLeft; statusLabel.Margin = Padding.Empty; statusLabel.ForeColor = Ui.Muted; status.Controls.Add(statusLabel, 0, 1);
            revealButton = Ui.Button("打开结果位置", false); revealButton.Dock = DockStyle.Fill; revealButton.Visible = false;
            cancelButton = Ui.Button("取消", false); cancelButton.Dock = DockStyle.Fill; cancelButton.Visible = false;
            status.Controls.Add(revealButton, 1, 1); status.Controls.Add(cancelButton, 2, 1); shell.Controls.Add(status, 0, 1); right.Controls.Add(shell);
            Controls.Add(right); Controls.Add(sidebar);
            exportButton = Ui.Button("导出迁移包", true); runtimeButton = Ui.Button("添加运行文件", false); customButton = Ui.Button("添加文件夹", false);
            previewButton = Ui.Button("预览差异", false); importButton = Ui.Button("开始导入", true);
            BuildExportPage(); BuildImportPage(); pageHost.Controls.Add(exportPage); pageHost.Controls.Add(importPage); ShowPage(false);
            cancelButton.Click += delegate { RequestCancellation(); }; revealButton.Click += delegate { RevealResult(); }; FormClosing += ClosingRequested;
            Shown += delegate {
                layoutReady = true;
                FitToWorkingArea();
                foreach (Action resize in pageResizers) resize();
                if (!String.IsNullOrEmpty(initialArchive)) { ShowPage(true); archivePath.Text = initialArchive; LoadArchive(); }
                else if (!String.IsNullOrEmpty(initialSource)) { sourceRoot.Text = initialSource; DiscoverSource(initialSource); }
                else AutoDiscover();
            };
            // Font assignment can update the form's DPI baseline before its children exist.
            // Reapply the design baseline after constructing all controls so the whole layout scales together.
            AutoScaleDimensions = new SizeF(96F, 96F);
            ResumeLayout(true);
        }
        Button NavButton(string text, int top, Glyph glyph)
        {
            RoundedButton button = (RoundedButton)Ui.Button(text, false); button.Navigation = true; button.Glyph = glyph;
            button.Location = new Point(13, top); button.Size = new Size(174, 43); return button;
        }
        void ShowPage(bool importing)
        {
            exportPage.Visible = !importing; importPage.Visible = importing; (importing ? importPage : exportPage).BringToFront();
            Ui.NavSelected(exportNav, !importing); Ui.NavSelected(importNav, importing);
        }
        TableLayoutPanel Page(Panel panel, string title, string subtitle)
        {
            panel.Dock = DockStyle.Fill; panel.Margin = Padding.Empty; panel.BackColor = Ui.Background; panel.AutoScroll = true;
            TableLayoutPanel table = Ui.Table(1); table.Dock = DockStyle.Top; table.Height = panel == exportPage ? 710 : 750;
            Label eyebrow = Ui.Label("MINECRAFT JAVA EDITION", 7.8F, true); eyebrow.ForeColor = Ui.Muted; eyebrow.Font = new Font("Segoe UI", 8F, FontStyle.Bold); eyebrow.Margin = new Padding(0, 0, 0, 7); Ui.Row(table, eyebrow, SizeType.AutoSize, 0);
            Label heading = Ui.Label(title, 23F, true); heading.Dock = DockStyle.Fill; heading.Margin = new Padding(0, 0, 0, 5); Ui.Row(table, heading, SizeType.AutoSize, 0);
            Label sub = Ui.Label(subtitle, 9F, false); sub.ForeColor = Ui.Muted; sub.Dock = DockStyle.Fill; sub.Margin = new Padding(0, 0, 0, 20); Ui.Row(table, sub, SizeType.AutoSize, 0);
            Action resize = delegate { if (layoutReady) table.Height = Math.Max(panel.ClientSize.Height, (int)Math.Ceiling((panel == exportPage ? 710 : 750) * CurrentAutoScaleDimensions.Height / 96.0)); };
            panel.SizeChanged += delegate { resize(); }; panel.VisibleChanged += delegate { resize(); }; pageResizers.Add(resize); panel.Controls.Add(table); return table;
        }
        void FitToWorkingArea()
        {
            Rectangle working = Screen.FromControl(this).WorkingArea;
            int width = Math.Max(650, working.Width - 28), height = Math.Max(480, working.Height - 28);
            MinimumSize = new Size(Math.Min(MinimumSize.Width, width), Math.Min(MinimumSize.Height, height));
            Size = new Size(Math.Min(Width, width), Math.Min(Height, height));
            Location = new Point(working.Left + Math.Max(0, (working.Width - Width) / 2), working.Top + Math.Max(0, (working.Height - Height) / 2));
        }
        void BuildExportPage()
        {
            TableLayoutPanel page = Page(exportPage, "带走熟悉的世界。", "选好内容，打包分享。下次冒险，无缝继续。");
            Card source = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) };
            TableLayoutPanel sourceLayout = Ui.Table(1); Ui.Row(sourceLayout, Ui.Label("游戏实例", 10.5F, true), SizeType.Absolute, 28);
            TableLayoutPanel browseRow = Ui.Table(3); browseRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); browseRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85)); browseRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            sourceRoot.AccessibleName = "源游戏或启动器文件夹"; browseRow.Controls.Add(Ui.Input(sourceRoot), 0, 0);
            Button browse = Ui.Button("浏览…", false), find = Ui.Button("扫描", false); browse.Dock = find.Dock = DockStyle.Fill; browse.Margin = find.Margin = new Padding(6, 0, 0, 6);
            browseRow.Controls.Add(browse, 1, 0); browseRow.Controls.Add(find, 2, 0); Ui.Row(sourceLayout, browseRow, SizeType.Absolute, 42);
            sourceInstances.AccessibleName = "源游戏实例"; Ui.Row(sourceLayout, Ui.Input(sourceInstances), SizeType.Absolute, 40);
            sourceInfo.Dock = sourcePath.Dock = DockStyle.Fill; sourceInfo.AutoSize = sourcePath.AutoSize = false; sourceInfo.AutoEllipsis = sourcePath.AutoEllipsis = true;
            sourceInfo.ForeColor = sourcePath.ForeColor = Ui.Muted; sourceInfo.Margin = sourcePath.Margin = Padding.Empty;
            Ui.Row(sourceLayout, sourceInfo, SizeType.Absolute, 23); Ui.Row(sourceLayout, sourcePath, SizeType.Absolute, 22);
            source.Controls.Add(sourceLayout); Ui.Row(page, source, SizeType.Absolute, 205);
            Card data = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 15) };
            TableLayoutPanel dataLayout = Ui.Table(1), header = Ui.Table(2); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            Label dataTitle = Ui.Label("迁移内容", 10.5F, true); dataTitle.Margin = new Padding(0, 7, 0, 0); header.Controls.Add(dataTitle, 0, 0);
            purpose.Items.AddRange(new object[] { "联机整合包", "自己搬家", "仅搬世界", "自定义选择" }); purpose.SelectedIndex = 0; purpose.AccessibleName = "迁移用途"; header.Controls.Add(Ui.Input(purpose), 1, 0); Ui.Row(dataLayout, header, SizeType.Absolute, 42);
            TableLayoutPanel toolbar = Ui.Table(5); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            Button all = Ui.Button("全选", false), none = Ui.Button("清空", false); all.Dock = none.Dock = customButton.Dock = runtimeButton.Dock = DockStyle.Fill;
            all.Margin = new Padding(0, 0, 5, 7); none.Margin = new Padding(0, 0, 5, 7); customButton.Margin = runtimeButton.Margin = new Padding(6, 0, 0, 7);
            toolbar.Controls.Add(all, 0, 0); toolbar.Controls.Add(none, 1, 0); toolbar.Controls.Add(customButton, 3, 0); toolbar.Controls.Add(runtimeButton, 4, 0); Ui.Row(dataLayout, toolbar, SizeType.Absolute, 39);
            Panel treeHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 6, 0, 0) };
            fileTree.Dock = DockStyle.Fill; fileTree.CheckBoxes = true; fileTree.HideSelection = false; fileTree.ShowNodeToolTips = true; fileTree.ShowLines = false;
            fileTree.BorderStyle = BorderStyle.None; fileTree.BackColor = Color.White; fileTree.ForeColor = Ui.Ink; fileTree.ItemHeight = 27; fileTree.Indent = 22; fileTree.AccessibleName = "需要导出的文件夹与文件";
            treeHost.Controls.Add(fileTree); exportEmpty.AccessibleName = "扫描实例后选择迁移内容"; treeHost.Controls.Add(exportEmpty); exportEmpty.BringToFront(); Ui.Row(dataLayout, treeHost, SizeType.Percent, 100);
            exportNotice.Text = "与朋友联机，通常只需模组和配置。世界存档由房主保管。"; Ui.Row(dataLayout, exportNotice, SizeType.Absolute, 48);
            data.Controls.Add(dataLayout); Ui.Row(page, data, SizeType.Percent, 100);
            TableLayoutPanel actions = Ui.Table(2); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            selectedSummary.Dock = DockStyle.Fill; selectedSummary.AutoSize = false; selectedSummary.TextAlign = ContentAlignment.MiddleLeft; selectedSummary.Margin = Padding.Empty; actions.Controls.Add(selectedSummary, 0, 0);
            exportButton.Dock = DockStyle.Fill; actions.Controls.Add(exportButton, 1, 0); Ui.Row(page, actions, SizeType.Absolute, 43);
            browse.Click += delegate { string path = PickFolder("选择启动器文件夹或游戏目录", sourceRoot.Text, false); if (path != null) { sourceRoot.Text = path; DiscoverSource(path); } };
            find.Click += delegate { DiscoverSource(sourceRoot.Text); }; sourceRoot.TextChanged += delegate { if (!busy) ResetSourceInstances(); };
            sourceRoot.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DiscoverSource(sourceRoot.Text); } };
            sourceInstances.SelectedIndexChanged += delegate { if (!busy) ScanSelectedSource(); }; purpose.SelectedIndexChanged += delegate { if (!treeUpdating) ApplyPreset(); };
            fileTree.AfterCheck += TreeChecked; fileTree.AfterSelect += delegate(object sender, TreeViewEventArgs e) { if (e.Node.Tag is TreeMeta) tips.SetToolTip(fileTree, e.Node.ToolTipText); };
            all.Click += delegate { SetAll(true); }; none.Click += delegate { SetAll(false); }; customButton.Click += delegate { AddCustom(); }; runtimeButton.Click += delegate { AddRuntime(); }; exportButton.Click += delegate { ExportArchive(); };
            tips.SetToolTip(runtimeButton, "可选：按版本收集资源和运行库。Java 与加载器仍由目标启动器管理。"); UpdateExportSummary();
        }
        void BuildImportPage()
        {
            TableLayoutPanel page = Page(importPage, "让冒险，在这里继续。", "打开迁移包，预览差异，把需要的内容安放到新位置。");
            Card destination = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14) };
            TableLayoutPanel form = Ui.Table(1); Ui.Row(form, Ui.Label("迁移包与目标", 10.5F, true), SizeType.Absolute, 28);
            Ui.Row(form, FieldRow("迁移包", archivePath, "打开…", delegate { string selected = PickArchive(); if (selected != null) { archivePath.Text = selected; LoadArchive(); } }, "读取", delegate { LoadArchive(); }), SizeType.Absolute, 42);
            packageInfo.Dock = DockStyle.Fill; packageInfo.AutoSize = false; packageInfo.AutoEllipsis = true; packageInfo.ForeColor = Ui.Muted; packageInfo.Margin = new Padding(94, 0, 0, 0); Ui.Row(form, packageInfo, SizeType.Absolute, 25);
            TableLayoutPanel launcher = Ui.Table(4); launcher.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94)); launcher.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); launcher.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82)); launcher.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            Label launcherName = Ui.Label("目标启动器", 8.7F, false); launcherName.Margin = new Padding(0, 8, 0, 0); launcher.Controls.Add(launcherName, 0, 0);
            targetLauncher.Items.AddRange(new object[] { "PCL", "HMCL", "Prism（已有实例）", "官方启动器", "自定义目录" }); targetLauncher.SelectedIndex = 0; targetLauncher.AccessibleName = "目标启动器"; launcher.Controls.Add(Ui.Input(targetLauncher), 1, 0);
            Label conflictName = Ui.Label("冲突处理", 8.7F, false); conflictName.Margin = new Padding(14, 8, 0, 0); launcher.Controls.Add(conflictName, 2, 0);
            conflict.Items.AddRange(new object[] { "保留本地文件", "备份后替换" }); conflict.SelectedIndex = 0; conflict.AccessibleName = "同名文件冲突处理"; launcher.Controls.Add(Ui.Input(conflict), 3, 0); Ui.Row(form, launcher, SizeType.Absolute, 42);
            Ui.Row(form, FieldRow("游戏目录", targetGame, "浏览…", delegate { BrowseTarget(); }, "识别实例", delegate { DetectTarget(); }), SizeType.Absolute, 42);
            targetHint.Dock = DockStyle.Fill; targetHint.AutoSize = false; targetHint.AutoEllipsis = true; targetHint.ForeColor = Ui.Muted; targetHint.Margin = new Padding(94, 0, 0, 0); Ui.Row(form, targetHint, SizeType.Absolute, 28);
            runtimeRow.Dock = DockStyle.Fill; runtimeRow.Margin = Padding.Empty;
            runtimeRow.Controls.Add(FieldRow("运行目录", targetRuntime, "浏览…", delegate { string path = PickFolder("选择包含 versions、libraries、assets 的运行根目录", targetRuntime.Text, true); if (path != null) targetRuntime.Text = path; }, null, null)); Ui.Row(form, runtimeRow, SizeType.Absolute, 0);
            destination.Controls.Add(form); Ui.Row(page, destination, SizeType.Absolute, 268);
            Card comparison = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 15) };
            TableLayoutPanel body = Ui.Table(1); Ui.Row(body, Ui.Label("内容与差异", 10.5F, true), SizeType.Absolute, 29);
            importCategories.Dock = DockStyle.Fill; importCategories.CheckOnClick = true; importCategories.IntegralHeight = false; importCategories.MultiColumn = true;
            importCategories.ColumnWidth = 226; importCategories.BorderStyle = BorderStyle.None; importCategories.BackColor = Color.White; importCategories.ForeColor = Ui.Ink;
            importCategories.Font = new Font(Font.FontFamily, 8.5F); importCategories.AccessibleName = "需要导入的数据类别"; Ui.Row(body, importCategories, SizeType.Absolute, 48);
            Panel gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = Padding.Empty };
            previewGrid.Dock = DockStyle.Fill; previewGrid.ReadOnly = true; previewGrid.AllowUserToAddRows = false; previewGrid.AllowUserToDeleteRows = false; previewGrid.AllowUserToResizeRows = false;
            previewGrid.BackgroundColor = Color.White; previewGrid.BorderStyle = BorderStyle.None; previewGrid.RowHeadersVisible = false; previewGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            previewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; previewGrid.MultiSelect = false; previewGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            previewGrid.EnableHeadersVisualStyles = false; previewGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; previewGrid.ColumnHeadersDefaultCellStyle.BackColor = Ui.Soft; previewGrid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Muted;
            previewGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 8.3F, FontStyle.Bold); previewGrid.DefaultCellStyle.Font = new Font(Font.FontFamily, 8.3F);
            previewGrid.DefaultCellStyle.SelectionBackColor = Ui.AccentSoft; previewGrid.DefaultCellStyle.SelectionForeColor = Ui.Ink; previewGrid.GridColor = Ui.Soft;
            previewGrid.RowTemplate.Height = 29; previewGrid.ColumnHeadersHeight = 33; previewGrid.VirtualMode = true;
            string[] names = { "处理", "分类", "文件", "大小", "目标位置" }; float[] widths = { 10, 14, 32, 9, 35 };
            for (int i = 0; i < names.Length; i++) previewGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = names[i], FillWeight = widths[i], SortMode = DataGridViewColumnSortMode.NotSortable });
            previewGrid.CellValueNeeded += PreviewCell; previewGrid.AccessibleName = "迁移差异预览";
            gridHost.Controls.Add(previewGrid); importEmpty.AccessibleName = "选择迁移包并预览文件差异"; gridHost.Controls.Add(importEmpty); importEmpty.BringToFront(); Ui.Row(body, gridHost, SizeType.Percent, 100);
            importNotice.Text = "相同内容直接复用，同名世界保留副本。你的原有文件会妥善保留。"; Ui.Row(body, importNotice, SizeType.Absolute, 53);
            comparison.Controls.Add(body); Ui.Row(page, comparison, SizeType.Percent, 100);
            TableLayoutPanel actions = Ui.Table(3); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            importSummary.Dock = DockStyle.Fill; importSummary.AutoSize = false; importSummary.AutoEllipsis = true; importSummary.TextAlign = ContentAlignment.MiddleLeft; importSummary.Margin = Padding.Empty; importSummary.ForeColor = Ui.Muted; actions.Controls.Add(importSummary, 0, 0);
            previewButton.Dock = importButton.Dock = DockStyle.Fill; actions.Controls.Add(previewButton, 1, 0); actions.Controls.Add(importButton, 2, 0); importButton.Enabled = false; Ui.Row(page, actions, SizeType.Absolute, 43);
            archivePath.AccessibleName = "迁移包 ZIP 路径"; targetGame.AccessibleName = "目标游戏目录"; targetRuntime.AccessibleName = "目标共享运行目录";
            archivePath.TextChanged += delegate { if (!settingImport) { manifest = null; importCategories.Items.Clear(); packageInfo.Text = "选择迁移包后，点击读取。"; UpdateTargetHint(); InvalidatePlan(); } };
            archivePath.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadArchive(); } };
            targetGame.TextChanged += delegate { targetRuntime.Clear(); InvalidatePlan(); }; targetRuntime.TextChanged += delegate { InvalidatePlan(); }; conflict.SelectedIndexChanged += delegate { InvalidatePlan(); };
            targetLauncher.SelectedIndexChanged += delegate { UpdateTargetHint(); InvalidatePlan(); };
            importCategories.ItemCheck += delegate(object sender, ItemCheckEventArgs e) { CategoryChoice choice = importCategories.Items[e.Index] as CategoryChoice; if (IsPrismTarget() && choice != null && choice.Id == "runtime") e.NewValue = CheckState.Unchecked; if (!settingImport) InvalidatePlan(); };
            previewButton.Click += delegate { PreviewImport(); }; importButton.Click += delegate { ExecuteImport(); }; UpdateTargetHint();
        }
        TableLayoutPanel FieldRow(string label, TextBox input, string text, Action action, string secondText, Action secondAction)
        {
            TableLayoutPanel row = Ui.Table(secondText == null ? 3 : 4); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); if (secondText != null) row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            Label name = Ui.Label(label, 8.7F, false); name.Margin = new Padding(0, 8, 0, 0); row.Controls.Add(name, 0, 0); row.Controls.Add(Ui.Input(input), 1, 0);
            Button button = Ui.Button(text, false); button.Dock = DockStyle.Fill; button.Margin = new Padding(6, 0, 0, 6); row.Controls.Add(button, 2, 0); button.Click += delegate { action(); };
            if (secondText != null) { Button second = Ui.Button(secondText, false); second.Dock = DockStyle.Fill; second.Margin = new Padding(6, 0, 0, 6); row.Controls.Add(second, 3, 0); second.Click += delegate { secondAction(); }; } return row;
        }
        sealed class FoundInstances
        {
            public List<InstanceInfo> Instances = new List<InstanceInfo>();
            public List<string> Warnings = new List<string>();
        }
        void AutoDiscover()
        {
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report)
            {
                FoundInstances found = new FoundInstances();
                foreach (string root in Discovery.KnownRoots())
                {
                    token.ThrowIfCancellationRequested();
                    try { found.Instances.AddRange(Discovery.Find(root, token)); }
                    catch (IOException e) { found.Warnings.Add(e.Message); }
                    catch (UnauthorizedAccessException e) { found.Warnings.Add(e.Message); }
                }
                found.Instances = found.Instances.GroupBy(x => x.GamePath + "|" + x.VersionId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
                return found;
            }, delegate(FoundInstances found)
            {
                PresentInstances(found.Instances);
                if (found.Instances.Count == 0) SetStatus("没有发现常见位置的实例，点击“浏览…”选择 PCL / HMCL 所在文件夹。", false);
                if (found.Warnings.Count > 0) exportNotice.Text = String.Join("\r\n", found.Warnings.Take(8));
            }, "正在查找常见位置的游戏实例…");
        }
        void DiscoverSource(string root)
        {
            if (busy) return;
            ResetSourceInstances();
            if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) { Error("请选择存在的启动器文件夹或游戏目录。"); return; }
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report) { return Discovery.Find(root, token); },
                delegate(List<InstanceInfo> found) { PresentInstances(found); }, "正在识别游戏实例…");
        }
        void PresentInstances(List<InstanceInfo> found)
        {
            ResetSourceInstances();
            foreach (InstanceInfo item in found) sourceInstances.Items.Add(item);
            if (sourceInstances.Items.Count > 0)
            {
                int best = 0, score = -1;
                for (int i = 0; i < found.Count; i++)
                {
                    int candidateScore = InstanceDataScore(found[i]);
                    if (candidateScore > score) { score = candidateScore; best = i; }
                }
                sourceInstances.SelectedIndex = best;
            }
            else UpdateExportSummary();
        }
        void ClearSourceScan()
        {
            scan = null; runtimeComplete = false;
            treeUpdating = true;
            try { fileTree.Nodes.Clear(); leafNodes.Clear(); }
            finally { treeUpdating = false; }
            exportNotice.Text = "请先完成当前实例的扫描，再选择需要导出的内容。";
            UpdateExportSummary();
        }
        void ResetSourceInstances()
        {
            ClearSourceScan();
            sourceInstances.Items.Clear();
            sourcePath.Text = "尚未选择游戏实例"; tips.SetToolTip(sourcePath, "");
            sourceInfo.Text = "选择启动器所在文件夹，或直接选择游戏目录。";
        }
        static int InstanceDataScore(InstanceInfo instance)
        {
            int score = 0;
            foreach (DataCategory category in Catalog.Categories)
                foreach (string relative in category.Roots)
                {
                    string path = System.IO.Path.Combine(instance.GamePath, relative);
                    try
                    {
                        if (File.Exists(path) || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()))
                            score += category.Id == "mods" ? 10 : category.Id == "worlds" ? 5 : 1;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            return score;
        }
        void ScanSelectedSource()
        {
            InstanceInfo selected = sourceInstances.SelectedItem as InstanceInfo;
            if (selected == null || busy) return;
            // Invalidate the old instance before starting work; cancellation or failure must not revive it.
            ClearSourceScan();
            sourcePath.Text = selected.GamePath; tips.SetToolTip(sourcePath, selected.GamePath);
            sourceInfo.Text = "Minecraft " + Ui.SafeDisplay(selected.MinecraftVersion) + "   ·   " + (String.IsNullOrEmpty(selected.Loader) ? "原版／加载器待确认" : selected.Loader + " " + selected.LoaderVersion);
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report) { return Scanner.Scan(selected, token); },
                delegate(ScanResult result)
                {
                    scan = result; runtimeComplete = false; BuildFileTree(); ApplyPreset(); ShowScanWarnings();
                    SetStatus(result.Files.Count == 0 ? "这个目录还没有识别到游戏数据，可以选择其他实例，或手动添加目录内的文件夹。" : "已读取 " + result.Files.Count + " 个文件，按用途推荐的内容已勾选。", false);
                }, "正在读取文件目录与大小…");
        }
        sealed class TreeMeta
        {
            public string Name, Category;
            public SourceFile File;
            public long Bytes;
            public int Count;
        }
        void BuildFileTree()
        {
            treeUpdating = true; fileTree.BeginUpdate(); fileTree.Nodes.Clear(); leafNodes.Clear();
            try
            {
                if (scan == null) return;
                foreach (IGrouping<string, SourceFile> group in scan.Files.GroupBy(x => x.Category).OrderBy(x => CategoryOrder(x.Key)))
                {
                    TreeNode category = new TreeNode(Catalog.CategoryName(group.Key)) { Tag = new TreeMeta { Name = Catalog.CategoryName(group.Key), Category = group.Key }, NodeFont = new Font(Font.FontFamily, 9F, FontStyle.Bold) };
                    fileTree.Nodes.Add(category);
                    Dictionary<string, TreeNode> directories = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
                    foreach (SourceFile file in group.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
                    {
                        string display = file.Path.Substring(file.Path.IndexOf('/') + 1); string[] parts = display.Split('/'); TreeNode parent = category; string key = "";
                        for (int i = 0; i < parts.Length - 1; i++)
                        {
                            key += "/" + parts[i]; TreeNode node;
                            if (!directories.TryGetValue(key, out node))
                            {
                                node = new TreeNode(parts[i]) { Tag = new TreeMeta { Name = parts[i], Category = group.Key } };
                                directories.Add(key, node); parent.Nodes.Add(node);
                            }
                            parent = node;
                        }
                        TreeNode leaf = new TreeNode(parts[parts.Length - 1] + "  ·  " + FileSafety.FormatSize(file.Size))
                        { Tag = new TreeMeta { Name = parts[parts.Length - 1], Category = group.Key, File = file, Count = 1, Bytes = file.Size }, ToolTipText = file.Path + "\r\n" + FileSafety.FormatSize(file.Size) };
                        parent.Nodes.Add(leaf); leafNodes.Add(leaf);
                    }
                    ComputeTotals(category); category.Expand();
                    if (category.Nodes.Count == 1 && category.Nodes[0].Nodes.Count < 24) category.Nodes[0].Expand();
                }
            }
            finally { fileTree.EndUpdate(); treeUpdating = false; UpdateExportSummary(); }
        }
        int CategoryOrder(string id)
        {
            for (int i = 0; i < Catalog.Categories.Length; i++) if (Catalog.Categories[i].Id == id) return i;
            return id == "custom" ? 50 : 60;
        }
        void ComputeTotals(TreeNode node)
        {
            TreeMeta meta = node.Tag as TreeMeta; if (meta == null || meta.File != null) return;
            meta.Bytes = 0; meta.Count = 0;
            foreach (TreeNode child in node.Nodes) { ComputeTotals(child); TreeMeta item = child.Tag as TreeMeta; if (item != null) { meta.Bytes += item.Bytes; meta.Count += item.Count; } }
            node.Text = meta.Name + "  ·  " + meta.Count + " 文件 / " + FileSafety.FormatSize(meta.Bytes);
            node.ToolTipText = node.Text;
        }
        void ApplyPreset()
        {
            if (scan == null || purpose.SelectedIndex == 3) return;
            int mode = purpose.SelectedIndex;
            treeUpdating = true;
            foreach (TreeNode leaf in leafNodes)
            {
                TreeMeta meta = (TreeMeta)leaf.Tag;
                leaf.Checked = mode == 1 ? meta.Category != "runtime" : mode == 2 ? meta.Category == "worlds" : new[] { "mods", "config", "resources" }.Contains(meta.Category);
            }
            RefreshFolderChecks(); treeUpdating = false; UpdateExportSummary();
        }
        void SetAll(bool selected)
        {
            treeUpdating = true;
            foreach (TreeNode node in leafNodes) node.Checked = selected;
            RefreshFolderChecks(); purpose.SelectedIndex = 3; treeUpdating = false; UpdateExportSummary();
        }
        void TreeChecked(object sender, TreeViewEventArgs e)
        {
            if (treeUpdating) return;
            treeUpdating = true;
            SetDescendants(e.Node, e.Node.Checked); RefreshFolderChecks(); purpose.SelectedIndex = 3;
            treeUpdating = false; UpdateExportSummary();
        }
        void SetDescendants(TreeNode node, bool value)
        { foreach (TreeNode child in node.Nodes) { child.Checked = value; SetDescendants(child, value); } }
        void RefreshFolderChecks()
        { foreach (TreeNode root in fileTree.Nodes) RefreshFolderCheck(root); }
        int RefreshFolderCheck(TreeNode node)
        {
            if (node.Nodes.Count == 0) return node.Checked ? 1 : 0;
            int selected = 0; foreach (TreeNode child in node.Nodes) selected += RefreshFolderCheck(child);
            node.Checked = selected > 0;
            TreeMeta meta = node.Tag as TreeMeta;
            if (meta != null)
            {
                node.Text = meta.Name + "  ·  " + selected + " / " + meta.Count + " 文件 · " + FileSafety.FormatSize(meta.Bytes);
            }
            return selected;
        }
        List<SourceFile> SelectedFiles()
        { return leafNodes.Where(x => x.Checked).Select(x => ((TreeMeta)x.Tag).File).ToList(); }
        void UpdateExportSummary()
        {
            List<SourceFile> files = SelectedFiles(); long bytes = files.Sum(x => x.Size);
            selectedSummary.Text = files.Count.ToString("N0") + " 个文件  ·  " + FileSafety.FormatSize(bytes);
            exportEmpty.Visible = fileTree.Nodes.Count == 0;
            exportButton.Enabled = !busy && files.Count > 0; customButton.Enabled = !busy && scan != null;
            runtimeButton.Enabled = !busy && scan != null && !scan.Instance.IsPrism && !String.IsNullOrEmpty(scan.Instance.VersionId) && !scan.Files.Any(x => x.Category == "runtime");
        }
        void ShowScanWarnings()
        {
            if (scan == null) return;
            List<string> notes = scan.Warnings.Distinct().ToList();
            exportNotice.Text = notes.Count == 0 ? "世界请按完整文件夹勾选。账号文件与 session.lock 自动排除；配置和自定义文件夹请自行检查分享内容。"
                : String.Join("\r\n", notes.Take(14)) + (notes.Count > 14 ? "\r\n另有 " + (notes.Count - 14) + " 项提示。" : "");
        }
        void AddCustom()
        {
            if (scan == null || busy) return;
            string folder = PickFolder("选择当前游戏目录内的其他数据文件夹", scan.Instance.GamePath, false); if (folder == null) return;
            HashSet<string> previous = new HashSet<string>(SelectedFiles().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            HashSet<string> existing = new HashSet<string>(scan.Files.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            ScanResult candidate = CopyScan(scan);
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report) { Scanner.AddCustom(candidate, folder, token); return candidate; },
                delegate(ScanResult result)
                {
                    scan = result; BuildFileTree(); RestoreAddedSelection(previous, existing); ShowScanWarnings();
                }, "正在读取其他文件夹…");
        }
        void RestoreAddedSelection(HashSet<string> previous, HashSet<string> existing)
        {
            treeUpdating = true; fileTree.BeginUpdate();
            try
            {
                foreach (TreeNode leaf in leafNodes)
                {
                    string path = ((TreeMeta)leaf.Tag).File.Path;
                    leaf.Checked = previous.Contains(path) || !existing.Contains(path);
                }
                RefreshFolderChecks(); purpose.SelectedIndex = 3;
            }
            finally { treeUpdating = false; fileTree.EndUpdate(); UpdateExportSummary(); }
        }
        void AddRuntime()
        {
            if (scan == null || busy) return;
            HashSet<string> previous = new HashSet<string>(SelectedFiles().Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            ScanResult candidate = CopyScan(scan);
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report) { return Tuple.Create(candidate, RuntimeCollector.Collect(candidate, token)); },
                delegate(Tuple<ScanResult, bool> collected)
                {
                    scan = collected.Item1; runtimeComplete = collected.Item2; BuildFileTree(); treeUpdating = true;
                    foreach (TreeNode leaf in leafNodes) { TreeMeta meta = (TreeMeta)leaf.Tag; leaf.Checked = previous.Contains(meta.File.Path) || meta.Category == "runtime"; }
                    RefreshFolderChecks(); purpose.SelectedIndex = 3; treeUpdating = false; UpdateExportSummary(); ShowScanWarnings();
                    SetStatus("已添加本地可找到的版本运行文件。请查看提示，部分加载器仍需由目标启动器安装。", false);
                }, "正在按所选版本收集资源与运行库…");
        }
        static ScanResult CopyScan(ScanResult value)
        {
            return new ScanResult { Instance = value.Instance, Files = new List<SourceFile>(value.Files), Warnings = new List<string>(value.Warnings) };
        }
        void ExportArchive()
        {
            if (scan == null || busy) return;
            List<SourceFile> files = SelectedFiles(); if (files.Count == 0) { Error("请先勾选需要打包的内容。"); return; }
            string output;
            using (SaveFileDialog dialog = new SaveFileDialog { Title = "保存迁移包", Filter = "CraftBridge 迁移包 (*.zip)|*.zip", DefaultExt = "zip", AddExtension = true,
                FileName = SafeFileName(scan.Instance.Name) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip", OverwritePrompt = false })
            { if (dialog.ShowDialog(this) != DialogResult.OK) return; output = dialog.FileName; }
            if (File.Exists(output)) { Error("这个 ZIP 已经存在。请使用新的文件名，以保留已有迁移包。"); return; }
            string selectedPurpose = purpose.SelectedItem == null ? "自定义选择" : purpose.SelectedItem.ToString();
            HashSet<string> paths = new HashSet<string>(files.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            bool selectedRuntimeComplete = runtimeComplete && scan.Files.Where(x => x.Category == "runtime").All(x => paths.Contains(x.Path));
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report) { return PackageEngine.Export(scan, files, output, selectedPurpose, selectedRuntimeComplete, token, report); },
                delegate(PackageManifest exported)
                {
                    SetResult(output); SetStatus("导出完成 · " + exported.Files.Count + " 个文件 · ZIP " + FileSafety.FormatSize(new FileInfo(output).Length) + "。现在可以通过网盘、微信或其他方式发送。", false);
                    MessageBox.Show(this, "迁移包已准备好。\r\n\r\n" + output + "\r\n\r\n朋友收到后，在“导入迁移包”中打开这个 ZIP 即可。", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }, "正在校验并打包所选文件…");
        }

        sealed class CategoryChoice
        {
            public string Id; public string Text;
            public override string ToString() { return Text; }
        }
        void LoadArchive()
        {
            if (busy) return;
            manifest = null; importCategories.Items.Clear(); UpdateTargetHint();
            packageInfo.Text = "正在重新读取迁移包，完成后可生成差异预览。";
            InvalidatePlan();
            string zip = archivePath.Text.Trim(); if (!File.Exists(zip)) { Error("请先选择存在的 ZIP 迁移包。"); return; }
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report) { return PackageEngine.ReadManifest(zip, token); },
                delegate(PackageManifest loaded)
                {
                    manifest = loaded; settingImport = true; importCategories.Items.Clear();
                    foreach (IGrouping<string, ManifestFile> group in loaded.Files.GroupBy(x => x.Category).OrderBy(x => CategoryOrder(x.Key)))
                    {
                        CategoryChoice choice = new CategoryChoice { Id = group.Key, Text = Catalog.CategoryName(group.Key) + " · " + FileSafety.FormatSize(group.Sum(x => x.Size)) };
                        importCategories.Items.Add(choice, group.Key != "runtime" || !IsPrismTarget());
                    }
                    settingImport = false;
                    packageInfo.Text = Ui.SafeDisplay(loaded.Name) + "  ·  Minecraft " + Ui.SafeDisplay(loaded.MinecraftVersion)
                        + (String.IsNullOrEmpty(loaded.Loader) ? "" : " / " + loaded.Loader + " " + loaded.LoaderVersion)
                        + "  ·  " + loaded.Files.Count + " 文件 / " + FileSafety.FormatSize(loaded.Files.Sum(x => x.Size));
                    importNotice.Text = ManifestNotes(loaded); UpdateTargetHint();
                    SetStatus("已读取迁移包。选择目标位置和内容，然后预览文件差异。", false);
                }, "正在读取迁移包清单…");
        }
        string ManifestNotes(PackageManifest loaded)
        {
            List<string> notes = new List<string>();
            if (!loaded.RuntimeIncluded) notes.Add("这个包包含游戏数据；目标启动器需准备相同 Minecraft 版本及加载器。");
            else if (!loaded.RuntimeComplete) notes.Add("包内运行文件可能不完整，请在目标启动器准备对应加载器并检查依赖。");
            if (loaded.Notes != null) notes.AddRange(loaded.Notes);
            notes.Add("相同内容复用；不同内容按所选策略处理；原有额外文件保留。完成后仅清理本次临时文件，ZIP 保留。");
            return String.Join("\r\n", notes.Distinct().Take(12));
        }
        bool IsPrismTarget() { return targetLauncher.SelectedItem != null && targetLauncher.SelectedItem.ToString().StartsWith("Prism", StringComparison.Ordinal); }
        void UpdateTargetHint()
        {
            if (IsPrismTarget())
            {
                targetHint.Text = "先在 Prism 创建并运行相同版本／加载器的实例，再选择实例内的 minecraft 或 .minecraft 目录。";
                settingImport = true;
                for (int i = 0; i < importCategories.Items.Count; i++) if (((CategoryChoice)importCategories.Items[i]).Id == "runtime") importCategories.SetItemChecked(i, false);
                settingImport = false; targetRuntime.Enabled = false; runtimeRow.Enabled = false;
            }
            else
            {
                string selected = targetLauncher.SelectedItem == null ? "" : targetLauncher.SelectedItem.ToString();
                targetHint.Text = selected == "官方启动器" ? "选择安装配置的实际游戏目录；导入后在启动器中选用对应版本／加载器，并设置该游戏目录。"
                    : "选择该版本实际使用的游戏目录；版本隔离时通常是 .minecraft\\versions\\版本名，可用“识别实例”。";
                targetRuntime.Enabled = true; runtimeRow.Enabled = true;
            }
            tips.SetToolTip(targetHint, targetHint.Text);
                        bool showRuntime = manifest != null && manifest.RuntimeIncluded;
            runtimeRow.Visible = showRuntime;
            TableLayoutPanel runtimeForm = runtimeRow.Parent as TableLayoutPanel;
            if (runtimeForm != null)
            {
                float scale = layoutReady ? CurrentAutoScaleDimensions.Height / 96F : 1F;
                runtimeForm.RowStyles[runtimeForm.GetRow(runtimeRow)].Height = showRuntime ? 42 * scale : 0;
                Card card = runtimeForm.Parent as Card;
                TableLayoutPanel page = card == null ? null : card.Parent as TableLayoutPanel;
                if (page != null) page.RowStyles[page.GetRow(card)].Height = (showRuntime ? 310 : 268) * scale;
            }
        }
        void InvalidatePlan()
        {
            plan = null; previewGrid.RowCount = 0; importButton.Enabled = false; importEmpty.Visible = true;
            importSummary.Text = "先预览差异，再导入文件。";
        }
        HashSet<string> SelectedCategories()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CategoryChoice item in importCategories.CheckedItems) if (item.Id != "runtime" || !IsPrismTarget()) result.Add(item.Id);
            return result;
        }
        void BrowseTarget()
        {
            string folder = PickFolder("选择目标游戏目录（可以新建文件夹）", targetGame.Text, true);
            if (folder == null) return; targetGame.Text = folder; targetRuntime.Clear();
            try
            {
                InstanceInfo target = Discovery.Inspect(folder, "", "");
                targetRuntime.Text = target.RuntimePath ?? "";
            }
            catch (Exception e) { SetStatus("已选择目标目录；版本信息待确认：" + e.Message, false); }
        }
        void DetectTarget()
        {
            if (busy) return;
            string root = targetGame.Text.Trim();
            if (!Directory.Exists(root)) { Error("请先浏览并选择现有的目标启动器或游戏文件夹，再识别其中的实例。"); return; }
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report) { return Discovery.Find(root, token); },
                delegate(List<InstanceInfo> found)
                {
                    if (found.Count == 0) { Error("此目录未找到实例。你仍可手动填写目标游戏目录。"); return; }
                    InstanceInfo selected;
                    using (InstancePicker picker = new InstancePicker(found))
                    { if (picker.ShowDialog(this) != DialogResult.OK) return; selected = picker.SelectedInstance; }
                    if (selected == null) return;
                    targetGame.Text = selected.GamePath;
                    targetRuntime.Text = selected.RuntimePath ?? "";
                    if (selected.IsPrism) targetLauncher.SelectedIndex = 2;
                    SetStatus("目标实例：" + selected + "。请预览文件差异。", false);
                }, "正在识别目标游戏实例…");
        }
        void PreviewImport()
        {
            if (busy) return;
            if (manifest == null) { Error("请先读取由 CraftBridge 导出的 ZIP 迁移包。"); return; }
            HashSet<string> categories = SelectedCategories();
            if (categories.Count == 0) { Error("请至少勾选一类需要导入的内容。"); return; }
            if (String.IsNullOrWhiteSpace(targetGame.Text)) { Error("请先选择目标游戏目录。"); return; }
            string zip = archivePath.Text.Trim(), game = targetGame.Text.Trim();
            string runtime = categories.Contains("runtime") && !IsPrismTarget() ? targetRuntime.Text.Trim() : "";
            if (categories.Contains("runtime") && String.IsNullOrWhiteSpace(runtime))
            { Error("包中勾选了运行文件。请填写共享运行目录（包含 versions、libraries、assets 的 .minecraft 根目录），或取消勾选运行文件。"); return; }
            bool replace = conflict.SelectedIndex == 1;
            InvalidatePlan();
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report) { return PackageEngine.Preview(zip, game, runtime, categories, replace, token, report); },
                delegate(ImportPlan preview)
                {
                    plan = preview; importEmpty.Visible = false; previewGrid.RowCount = preview.Files.Count; previewGrid.Invalidate();
                    int added = preview.Files.Count(x => x.Action == FileAction.Add), reused = preview.Files.Count(x => x.Action == FileAction.Reuse);
                    int replaced = preview.Files.Count(x => x.Action == FileAction.Replace), kept = preview.Files.Count(x => x.Action == FileAction.Keep);
                    importSummary.Text = "写入 " + FileSafety.FormatSize(preview.WriteBytes) + " · 新增 " + added + " / 复用 " + reused + " / 替换 " + replaced + " / 保留 " + kept;
                    importNotice.Text = (preview.Warnings.Count > 0 ? String.Join("\r\n", preview.Warnings.Distinct().Take(12)) + "\r\n" : "") + ManifestNotes(preview.Manifest);
                    importButton.Enabled = preview.Files.Count > 0; SetStatus("预览已准备好。请确认目标位置与处理方式，再开始导入。", false);
                }, "正在校验包并对比本地文件…");
        }
        void PreviewCell(object sender, DataGridViewCellValueEventArgs e)
        {
            if (plan == null || e.RowIndex < 0 || e.RowIndex >= plan.Files.Count) return;
            PlannedFile item = plan.Files[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case 0: e.Value = ActionText(item.Action); break;
                case 1: e.Value = Catalog.CategoryName(item.File.Category); break;
                case 2: e.Value = item.File.Path; break;
                case 3: e.Value = FileSafety.FormatSize(item.File.Size); break;
                case 4: e.Value = item.Destination; break;
            }
        }
        static string ActionText(FileAction action)
        { return action == FileAction.Add ? "新增" : action == FileAction.Reuse ? "复用" : action == FileAction.Replace ? "备份替换" : "保留本地"; }
        void ExecuteImport()
        {
            if (busy || plan == null) return;
            ImportPlan approved = plan;
            InvalidatePlan();
            RunJob(delegate(CancellationToken token, Action<OperationProgress> report) { return PackageEngine.Import(approved, token, report); },
                delegate(ImportResult result)
                {
                    SetResult(result.TargetRoot); InvalidatePlan();
                    string text = "新增 " + result.Added + " · 复用 " + result.Reused + " · 替换 " + result.Replaced + " · 保留本地 " + result.Kept;
                    SetStatus("导入完成 · " + text + "。ZIP 已保留。", false);
                    string message = "迁移完成。\r\n\r\n" + text + "\r\n\r\n目标目录：" + result.TargetRoot;
                    if (!String.IsNullOrEmpty(result.BackupPath)) message += "\r\n\r\n迁移记录与备份位置：" + result.BackupPath;
                    if (result.Warnings.Count > 0) message += "\r\n\r\n" + String.Join("\r\n", result.Warnings.Take(8));
                    message += "\r\n\r\n请在目标启动器选用对应版本与加载器，并确认它使用上述游戏目录。";
                    importNotice.Text = message; MessageBox.Show(this, message, "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }, "正在校验、导入并准备恢复记录…");
        }

        async void RunJob<T>(Func<CancellationToken, Action<OperationProgress>, T> work, Action<T> success, string message)
        {
            if (busy) return;
            busy = true; cancellation = new CancellationTokenSource(); CancellationToken token = cancellation.Token;
            int generation = ++jobGeneration;
            exportPage.Enabled = importPage.Enabled = false; exportNav.Enabled = importNav.Enabled = false;
            progress.Visible = true; cancelButton.Visible = true; cancelButton.Enabled = true; progress.Style = ProgressBarStyle.Marquee; progress.MarqueeAnimationSpeed = 25;
            SetStatus(message, false); T result = default(T); bool completed = false;
            Stopwatch watch = Stopwatch.StartNew();
            Action<OperationProgress> report = delegate(OperationProgress update)
            {
                if (watch.ElapsedMilliseconds < 120 && update.Percent < 100) return;
                watch.Restart();
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        if (!busy || IsDisposed || generation != jobGeneration) return;
                        if (update.Total > 0) { progress.Style = ProgressBarStyle.Continuous; progress.Value = Math.Max(0, Math.Min(100, update.Percent)); }
                        SetStatus(update.Message + (update.Total > 0 ? "  ·  " + update.Percent + "%" : ""), false);
                    });
                }
                catch (InvalidOperationException) { }
            };
            try { result = await Task.Run(delegate { token.ThrowIfCancellationRequested(); return work(token, report); }, token); completed = true; }
            catch (OperationCanceledException) { SetStatus("操作已取消。原始 ZIP 保留，临时文件按任务状态清理。", false); }
            catch (Exception e)
            {
                SetStatus("操作未完成：" + e.Message, true);
                MessageBox.Show(this, e.Message, "这一步暂时没有完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                busy = false; cancellation.Dispose(); cancellation = null;
                exportPage.Enabled = importPage.Enabled = true; exportNav.Enabled = importNav.Enabled = true; cancelButton.Visible = false;
                progress.Style = ProgressBarStyle.Continuous; progress.Value = completed ? 100 : 0; progress.Visible = false;
                UpdateExportSummary(); importButton.Enabled = plan != null;
            }
            if (closeAfterJob) { Close(); return; }
            if (completed) success(result);
        }
        void RequestCancellation()
        {
            if (cancellation == null) return;
            cancellation.Cancel(); cancelButton.Enabled = false; SetStatus("正在停止任务；如果正在写入，会先完成必要的恢复处理…", false);
        }
        void ClosingRequested(object sender, FormClosingEventArgs e)
        {
            if (!busy) return;
            e.Cancel = true; closeAfterJob = true; RequestCancellation();
        }
        void SetStatus(string text, bool error)
        { statusLabel.Text = text; statusLabel.ForeColor = error ? Color.FromArgb(166, 57, 42) : Ui.Muted; tips.SetToolTip(statusLabel, text); }
        void SetResult(string path) { lastResultPath = path; revealButton.Visible = true; }
        void RevealResult()
        {
            if (String.IsNullOrEmpty(lastResultPath)) return;
            try
            {
                string full = System.IO.Path.GetFullPath(lastResultPath);
                Process.Start(new ProcessStartInfo("explorer.exe", File.Exists(full) ? "/select,\"" + full + "\"" : "\"" + full + "\"") { UseShellExecute = true });
            }
            catch (Exception e) { Error("暂时无法打开结果位置：" + e.Message); }
        }
        void Error(string text) { MessageBox.Show(this, text, "CraftBridge", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        static string SafeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Replace("（共享游戏目录）", "").Replace("（独立目录）", "").Trim();
            return String.IsNullOrEmpty(name) ? "Minecraft-迁移包" : name;
        }
        string PickFolder(string title, string initial, bool create)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = title, ShowNewFolderButton = create })
            {
                if (Directory.Exists(initial)) dialog.SelectedPath = initial;
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }
        string PickArchive()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "打开 CraftBridge 迁移包", Filter = "ZIP 迁移包 (*.zip)|*.zip", CheckFileExists = true })
            { return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null; }
        }
        void ShowHelp()
        {
            using (Form help = new Form { Text = "使用说明 · CraftBridge", StartPosition = FormStartPosition.CenterParent, Size = new Size(760, 620), MinimumSize = new Size(650, 500), Font = Font, BackColor = Ui.Background })
            {
                TextBox text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                    BackColor = Color.White, ForeColor = Ui.Ink, BorderStyle = BorderStyle.None, Font = new Font(Font.FontFamily, 10F) };
                help.Padding = new Padding(23);
                text.Text = "把熟悉的游戏，带到另一台电脑\r\n\r\n"
                    + "1. A 机：选择启动器文件夹或游戏目录，选中实际玩的实例。按用途勾选文件夹与文件，导出 ZIP。\r\n"
                    + "2. 通过网盘、微信、邮件或其他方式自行发送 ZIP。程序完全本地运行，不需要云端服务。\r\n"
                    + "3. B 机：在导入页读取 ZIP，选择目标启动器及游戏目录，预览文件差异，再执行导入。\r\n\r\n"
                    + "首版支持范围\r\n"
                    + "• Java 版、Windows；存档、模组、配置、脚本、资源包、光影、地图模组数据、个人设置、截图与手动添加的目录。\r\n"
                    + "• PCL / HMCL：选择实际游戏目录；启用版本隔离时，通常是 .minecraft\\versions\\版本名。\r\n"
                    + "• 官方启动器：创建对应版本和加载器的安装配置，并将“游戏目录”指向导入位置。\r\n"
                    + "• Prism：先在 Prism 创建并运行对应版本及加载器的实例，再选择实例内 minecraft 或 .minecraft 目录导入数据。\r\n"
                    + "• 运行文件为可选项，按所选版本描述收集本地文件；Java 环境、Prism 元数据、部分 Forge / NeoForge 生成文件仍由目标启动器准备。\r\n"
                    + "• 服务端整体迁移、基岩版、联网直传和导出前的目标清单精简尚未包含在此版本。\r\n\r\n"
                    + "文件处理\r\n"
                    + "• 内容相同的文件复用；不同文件可以保留，或备份后替换。B 机原有的额外文件保留。\r\n"
                    + "• 同名世界存在差异时保存为新世界副本，避免把两个世界的数据混在一起。世界需要完整勾选。\r\n"
                    + "• 程序只清理本次任务的临时文件。收到的 ZIP 和替换前的备份保留。\r\n"
                    + "• 迁移前退出正在使用的世界。完成后检查启动器使用的版本、加载器和游戏目录；导入文件本身并不等于已经验证联机兼容性。\r\n"
                    + "• 启动器账号文件和 session.lock 自动排除。模组配置、自定义目录可能包含私人信息，分享前请查看勾选内容。\r\n\r\n"
                    + "迁移包是普通 ZIP，内有公开的清单和原始文件；可直接解压取回数据。支持 CraftBridge 和旧版 Blockporter 导出的迁移包。";
                help.Controls.Add(text); help.ShowDialog(this);
            }
        }

        sealed class InstancePicker : Form
        {
            readonly ListBox list = new ListBox();
            public InstanceInfo SelectedInstance { get { return list.SelectedItem as InstanceInfo; } }
            public InstancePicker(List<InstanceInfo> instances)
            {
                Text = "选择目标游戏实例"; StartPosition = FormStartPosition.CenterParent; Size = new Size(790, 430); MinimumSize = new Size(620, 350);
                Font = new Font("Microsoft YaHei UI", 9F); BackColor = Ui.Background; Padding = new Padding(19);
                TableLayoutPanel body = Ui.Table(1);
                Ui.Row(body, Ui.Label("请选择将要接收数据的实例", 12F, true), SizeType.Absolute, 34);
                list.Dock = DockStyle.Fill; list.HorizontalScrollbar = true; list.IntegralHeight = false;
                foreach (InstanceInfo instance in instances) list.Items.Add(instance); if (list.Items.Count > 0) list.SelectedIndex = 0;
                Ui.Row(body, list, SizeType.Percent, 100);
                Label path = Ui.Label("", 8.5F, false); path.Dock = DockStyle.Fill; path.AutoEllipsis = true; path.ForeColor = Ui.Muted; path.Margin = new Padding(0, 9, 0, 0);
                list.SelectedIndexChanged += delegate { path.Text = SelectedInstance == null ? "" : SelectedInstance.GamePath; }; if (SelectedInstance != null) path.Text = SelectedInstance.GamePath;
                Ui.Row(body, path, SizeType.Absolute, 44);
                Button choose = Ui.Button("选择此实例", true); choose.Dock = DockStyle.Right; choose.Width = 160; choose.DialogResult = DialogResult.OK;
                Ui.Row(body, choose, SizeType.Absolute, 38); Controls.Add(body); AcceptButton = choose;
            }
        }
    }
}
