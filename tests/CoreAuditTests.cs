using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CraftBridge.Tests
{
    // Regression cases from the v0.1.1 independent core review. All paths are
    // disposable fixtures beneath the test executable, never real game data.
    public static class CoreAuditTests
    {
        static readonly CancellationToken None = CancellationToken.None;

        public static void Run(Action<string, Action> run)
        {
            run("Runtime version payload cannot disguise a locked world's files", RuntimeWorldDisguise);
            run("Runtime mappings cannot write into game worlds or migration backups", RuntimeInsideGameData);
            run("Runtime library mapping cannot bypass a custom game directory's world protection", RuntimeOverCustomGameRoot);
            run("Preview rejects replacing the received ZIP itself", ReceivedArchiveDestination);
            run("Rollback rejects an altered original backup and preserves installed content", ChangedBackupDuringRollback);
            run("Whole-world publication rejects undeclared files injected into staging", StagedWorldExtraFile);
            run("Whole-world publication rejects undeclared empty directories injected into staging", StagedWorldExtraDirectory);
        }

        static void StagedWorldExtraFile() { StagedWorldMutation(false); }
        static void StagedWorldExtraDirectory() { StagedWorldMutation(true); }
        static void StagedWorldMutation(bool directory)
        {
            using (Fixture f = new Fixture())
            {
                RawZip(f.Zip, new Dictionary<string, string> {
                    { "game/saves/Home/level.dat", "incoming world" },
                    { "game/saves/Home/region/r.0.0.mca", "incoming chunks" }
                });
                ImportPlan plan = PackageEngine.Preview(f.Zip, f.Game, "", null, false, None, null);
                bool injected = false;
                Throws<IOException>(() => PackageEngine.Import(plan, None, p => {
                    if (!injected && p.Message.StartsWith("正在导入完整世界", StringComparison.Ordinal))
                    {
                        string tasks = Path.Combine(f.Game, ".craftbridge", "tasks");
                        string staging = Directory.GetDirectories(tasks, "world-*", SearchOption.AllDirectories).Single();
                        Assert(FileSafety.Inside(staging, f.Root), "Staging mutation must stay inside the owned fixture.");
                        if (directory) Directory.CreateDirectory(Path.Combine(staging, "undeclared-directory"));
                        else f.Put(staging, "undeclared.txt", "not in the verified ZIP");
                        injected = true;
                    }
                }));
                Assert(injected && !Directory.Exists(Path.Combine(f.Game, "saves", "Home")), "An unverified world was published.");
                Assert(File.Exists(f.Zip), "Rejected import removed the received ZIP.");
                string work = Path.Combine(f.Game, ".craftbridge", "tasks");
                Assert(!Directory.Exists(work) || !Directory.EnumerateFileSystemEntries(work).Any(), "Failed world staging was not cleaned.");
            }
        }

        sealed class Fixture : IDisposable
        {
            readonly string owner;
            public readonly string Root, Game, Zip;
            public Fixture()
            {
                owner = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(CoreAuditTests).Assembly.Location), "test-work"));
                Root = Path.Combine(owner, "audit-" + Guid.NewGuid().ToString("N"));
                Game = Path.Combine(Root, "target-game");
                Zip = Path.Combine(Root, "incoming.zip");
                Directory.CreateDirectory(Root);
            }
            public string Put(string root, string relative, string text)
            {
                string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new UTF8Encoding(false));
                return path;
            }
            public void Dispose()
            {
                if (!FileSafety.Inside(Root, owner) || !Path.GetFileName(Root).StartsWith("audit-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to clean a path outside the owned audit fixture.");
                if (Directory.Exists(Root)) FileSafety.DeleteOwnedDirectory(Root, owner);
            }
        }

        static void RuntimeWorldDisguise()
        {
            using (Fixture f = new Fixture())
            {
                string runtime = Path.Combine(f.Root, ".minecraft");
                string game = Path.Combine(runtime, "versions", "friends");
                string level = f.Put(game, "saves/Home/level.dat", "valuable old world");
                string worldLock = f.Put(game, "saves/Home/session.lock", "held world lock");
                RawZip(f.Zip, new Dictionary<string, string> {
                    { "runtime/versions/friends/saves/Home/level.dat", "disguised replacement" }
                });
                using (FileStream held = new FileStream(worldLock, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    Throws<InvalidDataException>(() => PackageEngine.ReadManifest(f.Zip));
                    Rejected(() => PackageEngine.Preview(f.Zip, game, runtime, null, true, None, null));
                }
                Assert(File.ReadAllText(level) == "valuable old world", "A runtime entry modified an existing world.");
                Assert(!Directory.Exists(Path.Combine(game, ".craftbridge")), "Rejected package created migration state.");
            }
        }

        static void RuntimeInsideGameData()
        {
            string[] relativeRoots = { "saves/Home", "config", ".craftbridge/backups/previous-task", ".blockporter/backups/previous-task" };
            foreach (string relativeRoot in relativeRoots)
                using (Fixture f = new Fixture())
                {
                    string runtime = Path.Combine(f.Game, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
                    string original = f.Put(runtime, "assets/indexes/1.20.json", "valuable local bytes");
                    RawZip(f.Zip, new Dictionary<string, string> {
                        { "runtime/assets/indexes/1.20.json", "incoming asset index" }
                    });
                    Rejected(() => PackageEngine.Preview(f.Zip, f.Game, runtime, null, true, None, null));
                    Assert(File.ReadAllText(original) == "valuable local bytes", "Preview modified protected target data.");
                }
        }

        static void RuntimeOverCustomGameRoot()
        {
            using (Fixture f = new Fixture())
            {
                string runtime = Path.Combine(f.Root, "runtime-root");
                string game = Path.Combine(runtime, "libraries", "custom-game");
                string level = f.Put(game, "saves/Home/level.dat", "valuable custom world");
                RawZip(f.Zip, new Dictionary<string, string> {
                    { "runtime/libraries/custom-game/saves/Home/level.dat", "disguised replacement" }
                });
                Rejected(() => PackageEngine.Preview(f.Zip, game, runtime, null, true, None, null));
                Assert(File.ReadAllText(level) == "valuable custom world", "Runtime mapping bypassed game-world protection.");
            }
        }

        static void ReceivedArchiveDestination()
        {
            using (Fixture f = new Fixture())
            {
                string archive = Path.Combine(f.Game, "resourcepacks", "incoming.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(archive));
                RawZip(archive, new Dictionary<string, string> { { "game/resourcepacks/incoming.zip", "resource pack bytes" } });
                string before = FileSafety.Hash(archive, None);
                Rejected(() => PackageEngine.Preview(archive, f.Game, "", null, true, None, null));
                Assert(FileSafety.Hash(archive, None) == before, "Preview changed the received ZIP.");
                Assert(!Directory.Exists(Path.Combine(f.Game, ".craftbridge")), "Archive collision created migration state.");
            }
        }

        static void ChangedBackupDuringRollback()
        {
            using (Fixture f = new Fixture())
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                RawZip(f.Zip, new Dictionary<string, string> {
                    { "game/mods/a.jar", "new a must stay recoverable" },
                    { "game/mods/b.jar", new string('b', 300000) }
                });
                string target = f.Put(f.Game, "mods/a.jar", "valuable original a");
                ImportPlan plan = PackageEngine.Preview(f.Zip, f.Game, "", null, true, None, null);
                bool changed = false;
                string backup = null;
                IOException error = null;
                try { PackageEngine.Import(plan, cancellation.Token, p => {
                    if (!changed && p.Message.StartsWith("正在导入", StringComparison.Ordinal) && p.Message.Contains("game/mods/b.jar"))
                    {
                        string backupRoot = Path.Combine(f.Game, ".craftbridge", "backups");
                        backup = Directory.GetFiles(backupRoot, "*.backup", SearchOption.AllDirectories).Single();
                        Assert(FileSafety.Inside(backup, f.Root), "Refusing to change a backup outside the audit fixture.");
                        Assert(File.ReadAllText(backup) == "valuable original a", "The first replacement did not create its original backup.");
                        File.WriteAllText(backup, "backup altered by another process", new UTF8Encoding(false));
                        changed = true;
                        cancellation.Cancel();
                    }
                }); }
                catch (IOException e) { error = e; }
                Assert(error != null && error.InnerException is OperationCanceledException, "Changed-backup recovery lost its cancellation cause.");
                Assert(changed, "The fixture did not reach a committed first replacement.");
                Assert(File.ReadAllText(target) == "new a must stay recoverable", "Rollback installed unverified backup content.");
                Assert(File.ReadAllText(backup) == "backup altered by another process", "Changed backup was consumed instead of retained for inspection.");
                Assert(!File.Exists(Path.Combine(f.Game, "mods", "b.jar")), "The cancelled second file was installed.");
                Assert(File.Exists(f.Zip), "Rollback removed the received ZIP.");
                string journal = Directory.GetFiles(Path.Combine(f.Game, ".craftbridge", "backups"), "恢复记录.txt", SearchOption.AllDirectories).Single();
                string recovery = File.ReadAllText(journal);
                string[] statuses = recovery.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Where(x => x.StartsWith("状态：", StringComparison.Ordinal)).ToArray();
                Assert(statuses.Last() == "状态：需手动恢复", "Changed-backup recovery record has an incorrect final status.");
                string final = recovery.Substring(recovery.LastIndexOf("状态：", StringComparison.Ordinal));
                Assert(final.Contains("原因：" + error.InnerException.Message) && final.Contains(target) && final.Contains("替换前的备份已被更改"), "Changed-backup recovery entry lost its cause or problem path.");
                string tasks = Path.Combine(f.Game, ".craftbridge", "tasks");
                Assert(!Directory.Exists(tasks) || !Directory.EnumerateFileSystemEntries(tasks).Any(), "Rollback left a staged task directory behind.");
            }
        }

        static void RawZip(string path, IDictionary<string, string> payloads)
        {
            PackageManifest manifest = new PackageManifest {
                FormatVersion = 1, Application = "Blockporter", Edition = "java", CreatedUtc = DateTime.UtcNow.ToString("o"),
                Name = "Audit fixture", Purpose = "regression", RuntimeIncluded = payloads.Keys.Any(x => x.StartsWith("runtime/", StringComparison.Ordinal))
            };
            using (FileStream output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                foreach (var item in payloads.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(item.Value);
                    string hash;
                    using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    manifest.Files.Add(new ManifestFile { Path = item.Key,
                        Category = item.Key.StartsWith("runtime/", StringComparison.Ordinal) ? "runtime" : Catalog.CategoryOf(item.Key.Substring(5)),
                        Size = bytes.Length, Sha256 = hash, ModifiedUtc = DateTime.UtcNow.ToString("o") });
                    using (Stream stream = archive.CreateEntry("payload/" + item.Key).Open()) stream.Write(bytes, 0, bytes.Length);
                }
                using (Stream stream = archive.CreateEntry("manifest.json").Open())
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(Json.Serializer().Serialize(manifest));
            }
        }

        static void Rejected(Action action)
        {
            try { action(); }
            catch (IOException) { return; }
            catch (InvalidDataException) { return; }
            throw new Exception("Unsafe package or mapping was not rejected.");
        }
        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name + " was not thrown.");
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
