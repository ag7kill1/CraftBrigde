using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CraftBridge.Tests
{
    // Opt in with an existing disposable parent directory on another local volume.
    // Only newly created cross-volume-<guid> children are written and removed.
    public static class CrossVolumeTests
    {
        static readonly CancellationToken None = CancellationToken.None;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder name, int length);

        public static void Run(Action<string, Action> run, Action<string, string> skip)
        {
            string parent = Environment.GetEnvironmentVariable("CRAFTBRIDGE_TEST_SECONDARY_ROOT");
            string[] names = { "Cross-volume replacements keep backups beside runtime files", "Cross-volume cancellation restores both volumes and consumes restored backups" };
            if (String.IsNullOrWhiteSpace(parent))
            {
                foreach (string name in names) skip(name, "Set CRAFTBRIDGE_TEST_SECONDARY_ROOT to an existing directory on a second local volume.");
                return;
            }
            run(names[0], () => Exercise(parent, false));
            run(names[1], () => Exercise(parent, true));
        }

        static string Volume(string path)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path));
            Assert(new DriveInfo(root).DriveType == DriveType.Fixed, "Cross-volume fixtures require local fixed volumes.");
            StringBuilder name = new StringBuilder(128);
            if (!GetVolumeNameForVolumeMountPoint(root, name, name.Capacity))
                throw new IOException("Cannot identify fixture volume.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            return name.ToString();
        }

        static void Exercise(string secondaryParent, bool cancel)
        {
            string owner = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(CrossVolumeTests).Assembly.Location), "test-work"));
            secondaryParent = Path.GetFullPath(secondaryParent);
            FileSafety.NoLinks(secondaryParent);
            Assert(Directory.Exists(secondaryParent), "Secondary fixture parent must already exist.");
            Assert(!Volume(owner).Equals(Volume(secondaryParent), StringComparison.OrdinalIgnoreCase), "The two fixture roots must be on different actual volumes, not drive aliases.");
            string id = "cross-volume-" + Guid.NewGuid().ToString("N");
            string primary = Path.Combine(owner, id), secondary = Path.Combine(secondaryParent, id);
            Assert(!FileSafety.Exists(primary) && !FileSafety.Exists(secondary), "Fixture path already exists.");
            try
            {
                Directory.CreateDirectory(primary);
                Directory.CreateDirectory(secondary);
                string game = Path.Combine(primary, "game"), runtime = Path.Combine(secondary, "runtime");
                string zip = Path.Combine(primary, "received.zip");
                string gameFile = Put(game, "mods/a.jar", "old game");
                string runtimeFile = Put(runtime, "libraries/fixture/a.jar", "old runtime");
                WriteZip(zip);
                string zipHash = FileSafety.Hash(zip, None);
                ImportPlan plan = PackageEngine.Preview(zip, game, runtime, null, true, None, null);
                string runtimeBackup = null;
                bool interrupted = false;
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    Action<OperationProgress> progress = p => {
                        if (interrupted || !p.Message.StartsWith("正在导入  runtime/libraries/fixture/z.jar", StringComparison.Ordinal)) return;
                        runtimeBackup = Directory.GetFiles(runtime, "*.backup", SearchOption.AllDirectories).Single();
                        Assert(Path.GetDirectoryName(runtimeBackup).StartsWith(Path.Combine(runtime, "libraries", "fixture", ".craftbridge-backup-"), StringComparison.OrdinalIgnoreCase), "Runtime backup is not beside its original file.");
                        Assert(File.ReadAllText(runtimeBackup) == "old runtime", "Runtime backup lost old bytes.");
                        Assert(File.ReadAllText(gameFile) == "new game" && File.ReadAllText(runtimeFile) == "new runtime", "Cancellation must follow both committed replacements.");
                        interrupted = true;
                        if (cancel) cancellation.Cancel();
                    };
                    if (cancel)
                    {
                        bool caught = false;
                        try { PackageEngine.Import(plan, cancellation.Token, progress); }
                        catch (OperationCanceledException) { caught = true; }
                        Assert(caught, "Cross-volume import did not propagate cancellation.");
                    }
                    else PackageEngine.Import(plan, None, progress);
                }
                Assert(interrupted && runtimeBackup != null, "Fixture never reached the cross-volume commit boundary.");
                string journal = Directory.GetFiles(Path.Combine(game, ".craftbridge", "backups"), "恢复记录.txt", SearchOption.AllDirectories).Single();
                string log = File.ReadAllText(journal);
                Assert(log.Contains("原位置：" + runtimeFile + "\r\n备份：" + runtimeBackup), "Journal does not map the runtime original to its sidecar backup.");
                string finalStatus = log.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Last(x => x.StartsWith("状态：", StringComparison.Ordinal));
                Assert(finalStatus == (cancel ? "状态：已回退" : "状态：导入成功"), "Incorrect final journal status.");
                Assert(File.ReadAllText(gameFile) == (cancel ? "old game" : "new game"), "Game replacement/rollback failed.");
                Assert(File.ReadAllText(runtimeFile) == (cancel ? "old runtime" : "new runtime"), "Runtime replacement/rollback failed.");
                string gameBackupRoot = Path.GetDirectoryName(journal);
                string[] gameBackups = Directory.GetFiles(gameBackupRoot, "*.backup");
                Assert(cancel ? gameBackups.Length == 0 && !File.Exists(runtimeBackup)
                    : gameBackups.Length == 1 && File.ReadAllText(gameBackups[0]) == "old game" && File.Exists(runtimeBackup), "Backups were lost or not consumed by completed rollback.");
                Assert(cancel ? !File.Exists(Path.Combine(runtime, "libraries", "fixture", "z.jar"))
                    : File.ReadAllText(Path.Combine(runtime, "libraries", "fixture", "z.jar")) == new string('z', 300000), "Last runtime file has incorrect state.");
                string tasks = Path.Combine(game, ".craftbridge", "tasks");
                Assert(!Directory.Exists(tasks) || !Directory.EnumerateFileSystemEntries(tasks).Any(), "Task directory or world staging remains.");
                foreach (string root in new[] { primary, secondary })
                    Assert(!Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Sibling temporary copy remains.");
                Assert(FileSafety.Hash(zip, None) == zipHash, "Received ZIP bytes changed.");
            }
            finally
            {
                try { Cleanup(primary, owner, id); }
                finally { Cleanup(secondary, secondaryParent, id); }
            }
        }

        static void Cleanup(string child, string parent, string id)
        {
            Assert(FileSafety.Inside(child, parent) && Path.GetFileName(child) == id && id.StartsWith("cross-volume-", StringComparison.Ordinal), "Refusing unsafe fixture cleanup.");
            if (Directory.Exists(child)) FileSafety.DeleteOwnedDirectory(child, parent);
        }
        static string Put(string root, string relative, string content)
        {
            string path = FileSafety.Join(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
        static void WriteZip(string path)
        {
            PackageManifest manifest = new PackageManifest { FormatVersion = 1, Application = "CraftBridge", Edition = "java", CreatedUtc = DateTime.UtcNow.ToString("o"), RuntimeIncluded = true, Name = "Cross-volume fixture", Purpose = "regression" };
            var payloads = new[] {
                new { Path = "game/mods/a.jar", Content = "new game", Category = "mods" },
                new { Path = "runtime/libraries/fixture/a.jar", Content = "new runtime", Category = "runtime" },
                new { Path = "runtime/libraries/fixture/z.jar", Content = new string('z', 300000), Category = "runtime" }
            };
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var item in payloads)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(item.Content);
                    string hash;
                    using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    manifest.Files.Add(new ManifestFile { Path = item.Path, Category = item.Category, Size = bytes.Length, Sha256 = hash, ModifiedUtc = DateTime.UtcNow.ToString("o") });
                    using (Stream stream = archive.CreateEntry("payload/" + item.Path).Open()) stream.Write(bytes, 0, bytes.Length);
                }
                using (StreamWriter writer = new StreamWriter(archive.CreateEntry("manifest.json").Open(), new UTF8Encoding(false))) writer.Write(Json.Serializer().Serialize(manifest));
            }
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
