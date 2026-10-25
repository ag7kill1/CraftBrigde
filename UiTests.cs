using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CraftBridge.Tests
{
    // In-process control-state checks only. No window is shown, and no real game data is inspected.
    public static class UiTests
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [STAThread]
        public static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int passed = 0, failed = 0;
            List<object> results = new List<object>();
            Action<string, Action> run = delegate(string name, Action test)
            {
                try { test(); passed++; Console.WriteLine("PASS  " + name); results.Add(new { name = name, passed = true, error = "" }); }
                catch (Exception error) { failed++; Console.WriteLine("FAIL  " + name + "\n" + error); results.Add(new { name = name, passed = false, error = error.ToString() }); }
            };
            run("Changing the source root invalidates the old instance and export selection", SourceRootChange);
            run("Switching source instances cannot expose the previous scan while the next scan runs or cancels", SourceInstanceChange);
            run("Changing the target game folder clears its stale runtime mapping and preview", TargetMappingChange);
            run("Adding a custom folder selects only newly added files and preserves earlier unchecked files", AddedSelection);
            run("Nested tree selection totals remain correct across bulk and individual changes", TreeSelectionTotals);
            string build = Path.Combine(Environment.CurrentDirectory, "build");
            Directory.CreateDirectory(build);
            File.WriteAllText(Path.Combine(build, "ui-test-results.json"), new JavaScriptSerializer().Serialize(new { passed = passed, failed = failed, tests = results }));
            Console.WriteLine("\n" + passed + " UI tests passed, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }

        static T Field<T>(AppForm form, string name)
        { return (T)typeof(AppForm).GetField(name, Private).GetValue(form); }
        static void SetField(AppForm form, string name, object value)
        { typeof(AppForm).GetField(name, Private).SetValue(form, value); }
        static object Call(AppForm form, string name, params object[] args)
        { return typeof(AppForm).GetMethod(name, Private).Invoke(form, args); }
        static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        static ScanResult FakeScan(string root)
        {
            ScanResult scan = new ScanResult { Instance = new InstanceInfo { Name = "UI fixture", GamePath = root } };
            scan.Files.Add(new SourceFile { Path = "game/mods/fixture.jar", FullPath = Path.Combine(root, "mods", "fixture.jar"), Category = "mods", Size = 7 });
            return scan;
        }
        static void PresentScan(AppForm form, ScanResult scan)
        {
            SetField(form, "scan", scan);
            Call(form, "BuildFileTree");
            Call(form, "ApplyPreset");
        }
        static void SourceRootChange()
        {
            using (AppForm form = new AppForm("", ""))
            {
                PresentScan(form, FakeScan("C:\\UI-fixture-A"));
                Assert(Field<Button>(form, "exportButton").Enabled, "Fixture should begin with an exportable selection.");
                SetField(form, "runtimeComplete", true);
                Field<TextBox>(form, "sourceRoot").Text = "C:\\UI-fixture-B";
                Assert(Field<ScanResult>(form, "scan") == null, "Editing the root left the previous scan available.");
                Assert(Field<TreeView>(form, "fileTree").Nodes.Count == 0, "Old source files remain visible.");
                Assert(!Field<Button>(form, "exportButton").Enabled && !Field<Button>(form, "customButton").Enabled, "Old source operations remain enabled.");
                Assert(!Field<bool>(form, "runtimeComplete"), "Runtime completeness leaked from another source.");
            }
        }
        static void SourceInstanceChange()
        {
            string owner = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "build", "ui-test-work"));
            string root = Path.Combine(owner, "ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using (AppForm form = new AppForm("", ""))
                {
                    ScanResult old = FakeScan(Path.Combine(root, "old"));
                    InstanceInfo next = new InstanceInfo { Name = "Empty next fixture", GamePath = root };
                    ComboBox instances = Field<ComboBox>(form, "sourceInstances");
                    SetField(form, "busy", true);
                    instances.Items.Add(old.Instance); instances.Items.Add(next); instances.SelectedIndex = 0;
                    SetField(form, "busy", false); PresentScan(form, old);
                    instances.SelectedIndex = 1;
                    try
                    {
                        Assert(!Object.ReferenceEquals(Field<ScanResult>(form, "scan"), old), "The previous scan survived switching instances.");
                        Assert(Field<TreeView>(form, "fileTree").Nodes.Count == 0, "Old files remain selectable during a new scan.");
                    }
                    finally
                    {
                        Call(form, "RequestCancellation");
                        Stopwatch timeout = Stopwatch.StartNew();
                        while (Field<bool>(form, "busy") && timeout.ElapsedMilliseconds < 10000)
                        { Application.DoEvents(); Thread.Sleep(1); }
                    }
                    Assert(!Field<bool>(form, "busy"), "Background scan did not finish or cancel.");
                    Assert(!Object.ReferenceEquals(Field<ScanResult>(form, "scan"), old), "Cancelling the new scan revived old data.");
                    Assert(!Field<Button>(form, "exportButton").Enabled, "The empty or cancelled scan allowed old files to export.");
                }
            }
            finally { FileSafety.DeleteOwnedDirectory(root, owner); }
        }
        static void TargetMappingChange()
        {
            using (AppForm form = new AppForm("", ""))
            {
                TextBox game = Field<TextBox>(form, "targetGame"), runtime = Field<TextBox>(form, "targetRuntime");
                game.Text = "C:\\UI-target-A"; runtime.Text = "C:\\UI-runtime-A";
                SetField(form, "plan", new ImportPlan()); Field<Button>(form, "importButton").Enabled = true;
                game.Text = "C:\\UI-target-B";
                Assert(runtime.Text == "", "The new target retained the previous instance's runtime directory.");
                Assert(Field<ImportPlan>(form, "plan") == null && !Field<Button>(form, "importButton").Enabled, "Changing target did not invalidate the approved preview.");
            }
        }
        static void AddedSelection()
        {
            using (AppForm form = new AppForm("", ""))
            {
                ScanResult scan = FakeScan("C:\\UI-fixture");
                scan.Files.Add(new SourceFile { Path = "game/extra/unchecked.txt", Category = "custom", Size = 4 });
                scan.Files.Add(new SourceFile { Path = "game/config/new/options.toml", Category = "config", Size = 8 });
                PresentScan(form, scan);
                HashSet<string> previous = new HashSet<string>(new[] { "game/mods/fixture.jar" }, StringComparer.OrdinalIgnoreCase);
                HashSet<string> existing = new HashSet<string>(new[] { "game/mods/fixture.jar", "game/extra/unchecked.txt" }, StringComparer.OrdinalIgnoreCase);
                Call(form, "RestoreAddedSelection", previous, existing);
                List<SourceFile> selected = (List<SourceFile>)Call(form, "SelectedFiles");
                Assert(selected.Count == 2 && selected.Any(x => x.Path == "game/config/new/options.toml"), "A newly added folder with a recognized category was not selected.");
                Assert(!selected.Any(x => x.Path == "game/extra/unchecked.txt"), "Adding another folder reselected a previously unchecked custom file.");
            }
        }
        static void TreeSelectionTotals()
        {
            using (AppForm form = new AppForm("", ""))
            {
                ScanResult scan = FakeScan("C:\\UI-fixture"); scan.Files.Clear();
                for (int i = 0; i < 3000; i++)
                    scan.Files.Add(new SourceFile { Path = "game/config/group" + (i % 25) + "/nested/file" + i + ".toml", Category = "config", Size = 2 });
                PresentScan(form, scan);
                Call(form, "SetAll", true);
                TreeView tree = Field<TreeView>(form, "fileTree");
                Assert(tree.Nodes[0].Text.Contains("3000 / 3000"), "Bulk selection count is inaccurate.");
                Field<List<TreeNode>>(form, "leafNodes")[0].Checked = false;
                Assert(tree.Nodes[0].Text.Contains("2999 / 3000"), "Individual leaf change did not update ancestor totals.");
                Assert(((List<SourceFile>)Call(form, "SelectedFiles")).Count == 2999, "Selected-file count disagrees with tree totals.");
                Call(form, "SetAll", false);
                Assert(!tree.Nodes[0].Checked && tree.Nodes[0].Text.Contains("0 / 3000"), "Clearing selection left an incorrect folder state.");
            }
        }
    }
}
