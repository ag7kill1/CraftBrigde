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
    // All files below are byte fixtures, not playable Minecraft worlds or mods.
    // Tests never inspect or write the user's actual game directories.
    public static class EngineTests
    {
        static readonly CancellationToken None = CancellationToken.None;

        public static void Run(Action<string, Action> run)
        {
            run("ZIP roundtrip preserves Unicode game data and excludes launcher accounts and locks", Roundtrip);
            run("CraftBridge exports its new brand and imports legacy Blockporter ZIPs", LegacyPackageCompatibility);
            run("Import category selection restricts the write scope", CategorySelection);
            run("Identical content is reused even when timestamps differ", IdenticalReuse);
            run("Default conflict handling keeps different local data", KeepLocal);
            run("Explicit replacement preserves old bytes in backup and keeps the received ZIP", BackupReplace);
            run("Import never deletes an extra target mod", ExtraTargetFiles);
            run("Different same-name worlds import to a separate complete world", WorldCopy);
            run("An identical complete world is reused", IdenticalWorld);
            run("A world created after preview is never merged with incoming chunks", WorldAppearedAfterPreview);
            run("A world created during staging aborts before game writes", WorldAppearedDuringStaging);
            run("An incoming world becomes visible only after all of its files are ready", AtomicWorldPublication);
            run("Renamed worlds reserve names already used by other worlds in the package", WorldRenameCollision);
            run("A different world with a file-directory conflict is imported separately", WorldShapeConflict);
            run("An extra file added to a reused world invalidates its preview", ReusedWorldChanged);
            run("A world name taken during commit is not merged or overwritten", WorldNameTakenDuringCommit);
            run("Cancellation after world publication rolls back the whole new world", CancelAfterWorldPublication);
            run("Rollback retains the entire newly imported world if any chunk was edited", EditedWorldRollback);
            run("A fully reused import creates no temporary or backup directories", ReuseCreatesNoWork);
            run("Export rejects a partially selected world", PartialWorld);
            run("Export rejects changed source files and removes partial ZIPs", ChangedSource);
            run("Export refuses an actively locked source world", SourceWorldLock);
            run("Preview-to-import target mutation is rejected before writes", ChangedTarget);
            run("Preview-to-import archive mutation is rejected before writes", ChangedArchive);
            run("Payload corruption is detected before any game data is replaced", CorruptPayload);
            run("Cancellation while staging removes temporary files without target writes", CancelStaging);
            run("Cancellation during commit rolls back already replaced data", CancelCommit);
            run("Cancellation records a completed rollback in the recovery journal", RollbackJournal);
            run("Failed temporary cleanup reports its exact path even on cancellation", CleanupFailure);
            run("Rollback preserves concurrent destination edits and retains the original backup", ConcurrentEditDuringRollback);
            run("Rollback retains installed content when an original backup disappears", MissingBackupDuringRollback);
            run("Preview preserves absolute drive-root game and runtime mappings without writing", DriveRootPreview);
            run("Unmapped runtime files are skipped while selected game data imports", RuntimeSkipped);
            run("Runtime files map to the explicitly selected runtime directory", RuntimeMapped);
            run("Standard version metadata can still map into an isolated game directory", IsolatedRuntimeMetadata);
            run("A file-directory destination collision is rejected in preview", DirectoryCollision);
            run("An existing ancestor file is rejected before preview completes", AncestorFileCollision);
            run("Traversal, rooted, reserved and internal paths in ZIP manifests are rejected", InvalidPaths);
            run("Duplicate ZIP names and case collisions are rejected", DuplicateEntries);
            run("Undeclared ZIP entries and mismatched categories are rejected", UndeclaredAndCategory);
        }

        sealed class Fixture : IDisposable
        {
            readonly string owner;
            public readonly string Root, Source, Target, Zip;

            public Fixture()
            {
                owner = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(EngineTests).Assembly.Location), "test-work"));
                Root = Path.Combine(owner, "engine-" + Guid.NewGuid().ToString("N"));
                Source = Path.Combine(Root, "source-game");
                Target = Path.Combine(Root, "target-game");
                Zip = Path.Combine(Root, "friend-pack.zip");
                Directory.CreateDirectory(Source);
            }

            public string Put(string root, string relative, string text)
            {
                string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new UTF8Encoding(false));
                return path;
            }

            public string PutSource(string relative, string text) { return Put(Source, relative, text); }
            public string PutTarget(string relative, string text) { return Put(Target, relative, text); }

            public ScanResult Scan()
            {
                return Scanner.Scan(new InstanceInfo {
                    Name = "Fixture 联机实例", GamePath = Source, RuntimePath = "", VersionId = "fixture-version",
                    MinecraftVersion = "1.20.1", Loader = "Fabric", LoaderVersion = "fixture-loader"
                }, None);
            }

            public PackageManifest Export(ScanResult scan)
            {
                return PackageEngine.Export(scan, scan.Files, Zip, "fixture-test", false, None, null);
            }

            public ImportPlan Preview(bool replace)
            {
                return PackageEngine.Preview(Zip, Target, "", null, replace, None, null);
            }

            public void Dispose()
            {
                string resolved = Path.GetFullPath(Root);
                string prefix = owner.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(resolved).StartsWith("engine-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to clean a test path outside its owned fixture root.");
                if (Directory.Exists(resolved)) FileSafety.DeleteOwnedDirectory(resolved, owner);
            }
        }

        static void Roundtrip()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/示例模组.jar", "fixture mod bytes");
                f.PutSource("config/示例.toml", "setting = true");
                f.PutSource("kubejs/server_scripts/配方.js", "fixture script bytes");
                f.PutSource("resourcepacks/测试资源.zip", "fixture resource bytes");
                f.PutSource("shaderpacks/测试光影.zip", "fixture shader bytes");
                f.PutSource("saves/朋友的世界/level.dat", "fixture level bytes");
                f.PutSource("saves/朋友的世界/region/r.0.0.mca", "fixture region bytes");
                f.PutSource("saves/朋友的世界/playerdata/fixture-player.dat", "fixture player bytes");
                f.PutSource("saves/朋友的世界/session.lock", "fixture lock bytes");
                f.PutSource("options.txt", "fixture controls");
                f.PutSource("screenshots/合照.png", "fixture image bytes");
                f.PutSource("launcher_accounts.json", "fixture secrets excluded");
                ScanResult scan = f.Scan();
                PackageManifest written = f.Export(scan);
                Assert(written.Application == "CraftBridge", "New packages still use the retired product name.");
                Assert(written.Files.Count == 10, "Expected ten non-lock game files.");
                PackageManifest read = PackageEngine.ReadManifest(f.Zip);
                Assert(read.MinecraftVersion == "1.20.1" && read.Loader == "Fabric", "Instance metadata did not survive serialization.");
                Assert(!Json.Serializer().Serialize(read).Contains(f.Source), "Manifest should not serialize the source machine path.");
                ImportResult result = PackageEngine.Import(f.Preview(false), None, null);
                Assert(result.Added == scan.Files.Count, "Roundtrip import did not add all selected files.");
                foreach (SourceFile file in scan.Files)
                {
                    string target = Path.Combine(f.Target, file.Path.Substring(5).Replace('/', Path.DirectorySeparatorChar));
                    Assert(File.Exists(target), "Missing roundtrip file: " + file.Path);
                    Assert(FileSafety.Hash(target, None) == FileSafety.Hash(file.FullPath, None), "Roundtrip bytes changed: " + file.Path);
                }
                Assert(!File.Exists(Path.Combine(f.Target, "launcher_accounts.json")), "Launcher account file leaked into target.");
                Assert(!File.Exists(Path.Combine(f.Target, "saves", "朋友的世界", "session.lock")), "World lock should not be copied.");
                Assert(File.Exists(f.Zip), "Received ZIP was deleted.");
                AssertNoTemporary(f);
            }
        }

        static void CategorySelection()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", "a");
                f.PutSource("config/a.toml", "a=true");
                f.PutSource("screenshots/a.png", "image");
                f.Export(f.Scan());
                ImportPlan plan = PackageEngine.Preview(f.Zip, f.Target, "", new HashSet<string> { "mods" }, false, None, null);
                Assert(plan.Files.Count == 1 && plan.Files[0].File.Category == "mods", "Category selection leaked other data.");
                PackageEngine.Import(plan, None, null);
                Assert(File.Exists(Path.Combine(f.Target, "mods", "a.jar")), "Selected mod was not imported.");
                Assert(!Directory.Exists(Path.Combine(f.Target, "config")) && !Directory.Exists(Path.Combine(f.Target, "screenshots")), "Unselected categories were written.");
                AssertNoTemporary(f);
            }
        }

        static void IdenticalReuse()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", "same content");
                string target = f.PutTarget("mods/a.jar", "same content");
                File.SetLastWriteTimeUtc(target, new DateTime(2002, 2, 2, 2, 2, 2, DateTimeKind.Utc));
                DateTime before = File.GetLastWriteTimeUtc(target);
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(true);
                Assert(plan.Files.Single().Action == FileAction.Reuse && plan.WriteBytes == 0, "Same content should be reused.");
                ImportResult result = PackageEngine.Import(plan, None, null);
                Assert(result.Reused == 1 && result.Added == 0 && result.Replaced == 0, "Incorrect reuse counts.");
                Assert(File.GetLastWriteTimeUtc(target) == before, "Reused file was unnecessarily rewritten.");
                AssertNoTemporary(f);
            }
        }

        static void KeepLocal()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("config/local.toml", "source setting");
                string target = f.PutTarget("config/local.toml", "my existing setting");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(false);
                Assert(plan.Files.Single().Action == FileAction.Keep, "Default conflict policy should keep local content.");
                ImportResult result = PackageEngine.Import(plan, None, null);
                Assert(File.ReadAllText(target) == "my existing setting" && result.Kept == 1, "Local content was overwritten by default.");
                AssertNoTemporary(f);
            }
        }

        static void BackupReplace()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("config/a.toml", "new content");
                string target = f.PutTarget("config/a.toml", "valuable old content");
                f.Export(f.Scan());
                string zipHash = FileSafety.Hash(f.Zip, None);
                ImportPlan plan = f.Preview(true);
                Assert(plan.Files.Single().Action == FileAction.Replace, "Explicit replace was not planned.");
                ImportResult result = PackageEngine.Import(plan, None, null);
                Assert(File.ReadAllText(target) == "new content" && result.Replaced == 1, "Replacement did not complete.");
                string[] backups = Directory.GetFiles(result.BackupPath, "*.backup", SearchOption.AllDirectories);
                Assert(backups.Length == 1 && File.ReadAllText(backups[0]) == "valuable old content", "Old bytes are missing from backup.");
                Assert(FileSafety.Hash(f.Zip, None) == zipHash, "Import modified or deleted the received ZIP.");
                AssertNoTemporary(f);
            }
        }

        static void ExtraTargetFiles()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", "a");
                string extra = f.PutTarget("mods/my-client-mod.jar", "keep my independent mod");
                f.Export(f.Scan());
                PackageEngine.Import(f.Preview(true), None, null);
                Assert(File.ReadAllText(extra) == "keep my independent mod", "Extra target mods must never be pruned.");
            }
        }

        static void WorldCopy()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("saves/家/level.dat", "incoming world");
                f.PutSource("saves/家/region/r.0.0.mca", "incoming chunks");
                string oldLevel = f.PutTarget("saves/家/level.dat", "existing world");
                string oldExtra = f.PutTarget("saves/家/region/r.9.9.mca", "existing extra chunks");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(true);
                Assert(plan.Files.All(x => x.Action == FileAction.Add), "World copy should not replace old files.");
                string copiedLevel = plan.Files.Single(x => x.File.Path.EndsWith("/level.dat", StringComparison.Ordinal)).Destination;
                Assert(copiedLevel != oldLevel && copiedLevel.Contains("迁移"), "Changed world did not receive a separate name.");
                PackageEngine.Import(plan, None, null);
                Assert(File.ReadAllText(oldLevel) == "existing world" && File.ReadAllText(oldExtra) == "existing extra chunks", "Original world was merged into or damaged.");
                Assert(File.ReadAllText(copiedLevel) == "incoming world", "Copied world data is missing.");
                string copiedWorld = Path.GetDirectoryName(copiedLevel);
                Assert(File.Exists(Path.Combine(copiedWorld, "region", "r.0.0.mca")), "World copy lost incoming chunk data.");
                Assert(!File.Exists(Path.Combine(copiedWorld, "region", "r.9.9.mca")), "Old extra chunks were merged into incoming world.");
                AssertNoTemporary(f);
            }
        }

        static void IdenticalWorld()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("saves/家/level.dat", "same world");
                f.PutSource("saves/家/region/r.0.0.mca", "same chunks");
                f.PutTarget("saves/家/level.dat", "same world");
                f.PutTarget("saves/家/region/r.0.0.mca", "same chunks");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(false);
                Assert(plan.Files.All(x => x.Action == FileAction.Reuse), "An identical complete world should be reused.");
                PackageEngine.Import(plan, None, null);
                Assert(Directory.GetDirectories(Path.Combine(f.Target, "saves")).Length == 1, "Identical world was duplicated unnecessarily.");
            }
        }

        static void WorldAppearedAfterPreview()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", "incoming mod");
                f.PutSource("saves/Home/level.dat", "incoming world");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(false);
                string extra = f.PutTarget("saves/Home/region/r.9.9.mca", "other world's chunks");
                Throws<IOException>(() => PackageEngine.Import(plan, None, null));
                Assert(File.ReadAllText(extra) == "other world's chunks", "Existing world chunks were changed.");
                Assert(!File.Exists(Path.Combine(f.Target, "saves/Home/level.dat")), "Two worlds were mixed after preview.");
                Assert(!File.Exists(Path.Combine(f.Target, "mods/a.jar")), "Changed world should be rejected before any game writes.");
                AssertNoTemporary(f);
            }
        }

        static void WorldAppearedDuringStaging()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", "incoming mod");
                f.PutSource("saves/Home/level.dat", "incoming world");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(false);
                bool changed = false;
                Throws<IOException>(() => PackageEngine.Import(plan, None, p => {
                    if (!changed && p.Message.StartsWith("校验并暂存", StringComparison.Ordinal))
                    { changed = true; f.PutTarget("saves/Home/region/r.9.9.mca", "other world"); }
                }));
                Assert(changed && !File.Exists(Path.Combine(f.Target, "mods/a.jar")), "Staging mutation did not abort before writes.");
                Assert(!File.Exists(Path.Combine(f.Target, "saves/Home/level.dat")), "Incoming world was merged during staging.");
                AssertNoTemporary(f);
            }
        }

        static void AtomicWorldPublication()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("saves/Home/level.dat", "incoming world");
                f.PutSource("saves/Home/region/r.0.0.mca", "incoming chunks");
                f.Export(f.Scan());
                bool observed = false;
                PackageEngine.Import(f.Preview(false), None, p => {
                    if (p.Message.StartsWith("正在导入", StringComparison.Ordinal) && p.Message.Contains("game/saves/"))
                    {
                        observed = true;
                        Assert(!Directory.Exists(Path.Combine(f.Target, "saves/Home")), "A partially written world became visible.");
                    }
                });
                Assert(observed, "World publication progress was not observed.");
                Assert(File.Exists(Path.Combine(f.Target, "saves/Home/level.dat")) && File.Exists(Path.Combine(f.Target, "saves/Home/region/r.0.0.mca")), "Published world is incomplete.");
                AssertNoTemporary(f);
            }
        }

        static void WorldRenameCollision()
        {
            using (Fixture f = new Fixture())
            {
                DateTime now = DateTime.Now;
                List<string> paths = new List<string> { "game/saves/Home/level.dat" };
                for (int i = -120; i <= 120; i++)
                    paths.Add("game/saves/Home (迁移 " + now.AddSeconds(i).ToString("yyyyMMdd-HHmmss") + ")/level.dat");
                RawZip(f.Zip, paths, null, false);
                f.PutTarget("saves/Home/level.dat", "different original world");
                ImportPlan plan = f.Preview(false);
                Assert(plan.Files.Select(x => x.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() == plan.Files.Count,
                    "Renamed world collided with another incoming world's directory.");
                Assert(!Directory.Exists(Path.Combine(f.Target, ".craftbridge")), "Preview must not write any data.");
            }
        }

        static void WorldShapeConflict()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("saves/Home/level.dat", "incoming world");
                f.PutTarget("saves/Home/level.dat/valuable.txt", "keep existing data");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(true);
                Assert(plan.Files.Single().Destination.Contains("迁移"), "Different world was not renamed before checking file shape.");
                PackageEngine.Import(plan, None, null);
                Assert(File.ReadAllText(Path.Combine(f.Target, "saves/Home/level.dat/valuable.txt")) == "keep existing data", "Original world changed.");
            }
        }

        static void ReusedWorldChanged()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("saves/Home/level.dat", "same world");
                f.PutTarget("saves/Home/level.dat", "same world");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(false);
                Assert(plan.Files.Single().Action == FileAction.Reuse, "Fixture must start with a reused world.");
                f.PutTarget("saves/Home/region/new.mca", "new chunks since preview");
                Throws<IOException>(() => PackageEngine.Import(plan, None, null));
                Assert(File.Exists(Path.Combine(f.Target, "saves/Home/region/new.mca")), "New local chunks were removed.");
            }
        }

        static void RollbackJournal()
        {
            using (Fixture f = new Fixture())
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                f.PutSource("mods/a.jar", "new a");
                f.PutSource("mods/b.jar", "new b");
                f.PutTarget("mods/a.jar", "original a");
                f.Export(f.Scan());
                Throws<OperationCanceledException>(() => PackageEngine.Import(f.Preview(true), cancel.Token, p => {
                    if (p.Message.StartsWith("正在导入", StringComparison.Ordinal) && p.Message.Contains("game/mods/b.jar")) cancel.Cancel();
                }));
                string journal = Directory.GetFiles(Path.Combine(f.Target, ".craftbridge/backups"), "恢复记录.txt", SearchOption.AllDirectories).Single();
                Assert(File.ReadAllText(journal).Contains("状态：已回退"), "Recovery record still says writing after a successful rollback.");
            }
        }

        static void WorldNameTakenDuringCommit()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("saves/Home/level.dat", "incoming world");
                f.Export(f.Scan());
                bool taken = false;
                Throws<IOException>(() => PackageEngine.Import(f.Preview(false), None, p => {
                    if (!taken && p.Message.StartsWith("正在导入完整世界", StringComparison.Ordinal))
                    { taken = true; f.PutTarget("saves/Home/region/other.mca", "other world"); }
                }));
                Assert(taken && !File.Exists(Path.Combine(f.Target, "saves/Home/level.dat")), "Late directory creation mixed two worlds.");
                Assert(File.ReadAllText(Path.Combine(f.Target, "saves/Home/region/other.mca")) == "other world", "Other world's data changed.");
                AssertNoTemporary(f);
            }
        }

        static void CancelAfterWorldPublication()
        {
            using (Fixture f = new Fixture())
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                f.PutSource("saves/Home/level.dat", "incoming world");
                f.PutSource("saves/Home/region/r.0.0.mca", "incoming chunks");
                f.PutTarget("saves/Home/level.dat", "original world");
                f.Export(RuntimeScan(f));
                ImportPlan plan = PackageEngine.Preview(f.Zip, f.Target, Path.Combine(f.Root, "target-runtime"), null, true, None, null);
                string newWorld = Path.GetDirectoryName(plan.Files.Single(x => x.File.Path.EndsWith("/level.dat", StringComparison.Ordinal)).Destination);
                bool observed = false;
                Throws<OperationCanceledException>(() => PackageEngine.Import(plan, cancel.Token, p => {
                    if (p.Message.StartsWith("正在导入  runtime/", StringComparison.Ordinal))
                    { observed = true; Assert(Directory.Exists(newWorld), "World should have been published before the runtime commit."); cancel.Cancel(); }
                }));
                Assert(observed && !Directory.Exists(newWorld), "Cancelled world was not rolled back as a complete directory.");
                Assert(File.ReadAllText(Path.Combine(f.Target, "saves/Home/level.dat")) == "original world", "Original same-name world was changed.");
                Assert(!File.Exists(Path.Combine(f.Target, "mods/a.jar")), "Earlier added mod was not rolled back.");
                AssertNoTemporary(f);
            }
        }

        static void EditedWorldRollback()
        {
            using (Fixture f = new Fixture())
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                f.PutSource("saves/Home/level.dat", "incoming world");
                f.PutSource("saves/Home/region/r.0.0.mca", "incoming chunks");
                f.Export(RuntimeScan(f));
                ImportPlan plan = PackageEngine.Preview(f.Zip, f.Target, Path.Combine(f.Root, "target-runtime"), null, false, None, null);
                bool edited = false;
                Throws<IOException>(() => PackageEngine.Import(plan, cancel.Token, p => {
                    if (!edited && p.Message.StartsWith("正在导入  runtime/", StringComparison.Ordinal))
                    { edited = true; f.PutTarget("saves/Home/level.dat", "later game edit"); cancel.Cancel(); }
                }));
                Assert(edited && File.ReadAllText(Path.Combine(f.Target, "saves/Home/level.dat")) == "later game edit", "Concurrent edit was overwritten.");
                Assert(File.ReadAllText(Path.Combine(f.Target, "saves/Home/region/r.0.0.mca")) == "incoming chunks", "Rollback removed unchanged chunks from the edited world.");
                string journal = Directory.GetFiles(Path.Combine(f.Target, ".craftbridge/backups"), "恢复记录.txt", SearchOption.AllDirectories).Single();
                Assert(File.ReadAllText(journal).Contains("状态：需手动恢复"), "Partial rollback was not recorded accurately.");
                AssertNoTemporary(f);
            }
        }

        static void ReuseCreatesNoWork()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", "same");
                f.PutTarget("mods/a.jar", "same");
                f.Export(f.Scan());
                ImportResult result = PackageEngine.Import(f.Preview(false), None, null);
                Assert(result.Reused == 1 && String.IsNullOrEmpty(result.BackupPath), "No-write import reported a backup.");
                Assert(!Directory.Exists(Path.Combine(f.Target, ".craftbridge")), "No-write import created unnecessary work directories.");
            }
        }

        static void CleanupFailure()
        {
            using (Fixture f = new Fixture())
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                f.PutSource("mods/a.jar", "a");
                f.PutSource("mods/b.jar", "b");
                f.Export(f.Scan());
                FileStream held = null;
                string locked = null;
                Exception failure = null;
                try
                {
                    PackageEngine.Import(f.Preview(false), cancel.Token, p => {
                        if (held == null && p.Message.StartsWith("校验并暂存", StringComparison.Ordinal) && p.Message.Contains("game/mods/b.jar"))
                        {
                            locked = Directory.GetFiles(Path.Combine(f.Target, ".craftbridge/tasks"), "0000000.data", SearchOption.AllDirectories).Single();
                            held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);
                            cancel.Cancel();
                        }
                    });
                }
                catch (Exception e) { failure = e; }
                finally { if (held != null) held.Dispose(); }
                Assert(locked != null, "Cleanup failure fixture did not acquire the staged file.");
                Assert(failure is IOException && failure.Message.Contains(Path.GetDirectoryName(locked)), "Cleanup failure path was silently lost during cancellation.");
                Assert(!File.Exists(Path.Combine(f.Target, "mods/a.jar")), "Cancelled staging wrote game data.");
            }
        }

        static void PartialWorld()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("saves/家/level.dat", "level");
                f.PutSource("saves/家/region/r.0.0.mca", "chunks");
                ScanResult scan = f.Scan();
                Throws<IOException>(() => PackageEngine.Export(scan, scan.Files.Take(1), f.Zip, "fixture", false, None, null));
                Assert(!File.Exists(f.Zip), "A partial world ZIP should not be published.");
                AssertNoTemporary(f);
            }
        }

        static void ChangedSource()
        {
            using (Fixture f = new Fixture())
            {
                string source = f.PutSource("mods/a.jar", "before");
                ScanResult scan = f.Scan();
                File.WriteAllText(source, "changed file with different size", new UTF8Encoding(false));
                Throws<IOException>(() => f.Export(scan));
                Assert(!File.Exists(f.Zip), "Changed source ZIP should not be published.");
                AssertNoTemporary(f);
            }
        }

        static void SourceWorldLock()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("saves/家/level.dat", "level");
                string lockPath = f.PutSource("saves/家/session.lock", "locked");
                ScanResult scan = f.Scan();
                using (FileStream held = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Throws<IOException>(() => f.Export(scan));
                Assert(!File.Exists(f.Zip), "Locked source world should not be exported.");
            }
        }

        static void ChangedTarget()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", "incoming");
                string target = f.PutTarget("mods/a.jar", "old");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(true);
                File.WriteAllText(target, "edited after preview", new UTF8Encoding(false));
                Throws<IOException>(() => PackageEngine.Import(plan, None, null));
                Assert(File.ReadAllText(target) == "edited after preview", "Target mutation was overwritten.");
                AssertNoTemporary(f);
            }
        }

        static void ChangedArchive()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", new string('a', 128));
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(false);
                ChangePayload(f.Zip, "payload/game/mods/a.jar", new string('b', 128));
                Throws<IOException>(() => PackageEngine.Import(plan, None, null));
                Assert(!File.Exists(Path.Combine(f.Target, "mods", "a.jar")), "Changed archive reached target data.");
                AssertNoTemporary(f);
            }
        }

        static void CorruptPayload()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", "replacement");
                f.PutSource("mods/z.jar", new string('a', 256));
                string target = f.PutTarget("mods/a.jar", "precious old data");
                f.Export(f.Scan());
                ChangePayload(f.Zip, "payload/game/mods/z.jar", new string('b', 256));
                ImportPlan plan = f.Preview(true);
                Throws<InvalidDataException>(() => PackageEngine.Import(plan, None, null));
                Assert(File.ReadAllText(target) == "precious old data", "Target was changed before all incoming payloads were verified.");
                Assert(!File.Exists(Path.Combine(f.Target, "mods", "z.jar")), "Corrupt payload was installed.");
                AssertNoTemporary(f);
            }
        }

        static void CancelStaging()
        {
            using (Fixture f = new Fixture())
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                f.PutSource("mods/a.jar", new string('a', 300000));
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(false);
                Throws<OperationCanceledException>(() => PackageEngine.Import(plan, cancel.Token, p => {
                    if (p.Message.StartsWith("校验并暂存", StringComparison.Ordinal)) cancel.Cancel();
                }));
                Assert(!File.Exists(Path.Combine(f.Target, "mods", "a.jar")), "Cancelled staging wrote game data.");
                Assert(File.Exists(f.Zip), "Cancellation deleted the received ZIP.");
                AssertNoTemporary(f);
            }
        }

        static void CancelCommit()
        {
            using (Fixture f = new Fixture())
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                f.PutSource("mods/a.jar", "new a");
                f.PutSource("mods/b.jar", new string('b', 300000));
                string target = f.PutTarget("mods/a.jar", "old a to restore");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(true);
                Throws<OperationCanceledException>(() => PackageEngine.Import(plan, cancel.Token, p => {
                    if (p.Message.StartsWith("正在导入", StringComparison.Ordinal) && p.Message.Contains("game/mods/b.jar")) cancel.Cancel();
                }));
                Assert(File.ReadAllText(target) == "old a to restore", "Already replaced file was not restored on cancellation.");
                Assert(!File.Exists(Path.Combine(f.Target, "mods", "b.jar")), "Cancelled second file was installed.");
                Assert(File.Exists(f.Zip), "Commit cancellation deleted the received ZIP.");
                AssertNoTemporary(f);
            }
        }

        static void ConcurrentEditDuringRollback()
        {
            using (Fixture f = new Fixture())
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                f.PutSource("mods/a.jar", "new a");
                f.PutSource("mods/b.jar", new string('b', 300000));
                string target = f.PutTarget("mods/a.jar", "old a to retain in backup");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(true);
                bool edited = false;
                Throws<IOException>(() => PackageEngine.Import(plan, cancel.Token, p => {
                    if (!edited && p.Message.StartsWith("正在导入", StringComparison.Ordinal) && p.Message.Contains("game/mods/b.jar"))
                    {
                        edited = true;
                        File.WriteAllText(target, "concurrent edit made after the first commit", new UTF8Encoding(false));
                        cancel.Cancel();
                    }
                }));
                Assert(edited, "The concurrent-edit fixture did not reach its intended commit phase.");
                Assert(File.ReadAllText(target) == "concurrent edit made after the first commit", "Rollback overwrote another process's intervening edit.");
                string backupRoot = Path.Combine(f.Target, ".craftbridge", "backups");
                string[] backups = Directory.GetFiles(backupRoot, "*.backup", SearchOption.AllDirectories);
                Assert(backups.Any(x => File.ReadAllText(x) == "old a to retain in backup"), "Original content should remain available for manual recovery.");
                Assert(!File.Exists(Path.Combine(f.Target, "mods", "b.jar")), "Cancelled second file was installed.");
                AssertNoTemporary(f);
            }
        }

        static void MissingBackupDuringRollback()
        {
            using (Fixture f = new Fixture())
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                f.PutSource("mods/a.jar", "new a should remain recoverable");
                f.PutSource("mods/b.jar", new string('b', 300000));
                string target = f.PutTarget("mods/a.jar", "old a in the original backup");
                f.Export(f.Scan());
                ImportPlan plan = f.Preview(true);
                bool removed = false;
                Throws<IOException>(() => PackageEngine.Import(plan, cancel.Token, p => {
                    if (!removed && p.Message.StartsWith("正在导入", StringComparison.Ordinal) && p.Message.Contains("game/mods/b.jar"))
                    {
                        string[] backups = Directory.GetFiles(Path.Combine(f.Target, ".craftbridge", "backups"), "*.backup", SearchOption.AllDirectories);
                        Assert(backups.Length == 1 && File.ReadAllText(backups[0]) == "old a in the original backup", "Fixture did not reach a committed replacement with its original backup.");
                        Assert(FileSafety.Inside(backups[0], f.Root), "Refusing to remove a backup outside the owned fixture.");
                        File.Delete(backups[0]);
                        removed = true;
                        cancel.Cancel();
                    }
                }));
                Assert(removed, "The missing-backup fixture did not reach its intended phase.");
                Assert(File.Exists(target) && File.ReadAllText(target) == "new a should remain recoverable", "Rollback deleted installed data after its original backup disappeared.");
                Assert(!File.Exists(Path.Combine(f.Target, "mods", "b.jar")), "Cancelled second file was installed.");
                Assert(File.Exists(f.Zip), "Manual recovery must preserve the received ZIP.");
                AssertNoTemporary(f);
            }
        }

        static void DriveRootPreview()
        {
            using (Fixture f = new Fixture())
            {
                // Preview only: no import operation is permitted against a real drive root.
                string drive = Path.GetPathRoot(f.Root);
                string unique = "__craftbridge_preview_fixture_" + Guid.NewGuid().ToString("N");
                string gameRelative = unique + "/game.txt";
                string runtimeRelative = "libraries/" + unique + "/library.jar";
                RawZip(f.Zip, new[] { "game/" + gameRelative, "runtime/" + runtimeRelative }, null, false);
                ImportPlan plan = PackageEngine.Preview(f.Zip, drive, drive, null, false, None, null);
                Assert(String.Equals(plan.TargetRoot, drive, StringComparison.OrdinalIgnoreCase), "Drive-root target must remain fully qualified (for example C:\\), not drive-relative C:.");
                Assert(plan.Files.Single(x => x.File.Path.StartsWith("game/", StringComparison.Ordinal)).Destination
                    .Equals(Path.Combine(drive, gameRelative.Replace('/', Path.DirectorySeparatorChar)), StringComparison.OrdinalIgnoreCase), "Game mapping used the process's drive-relative current directory.");
                Assert(plan.Files.Single(x => x.File.Path.StartsWith("runtime/", StringComparison.Ordinal)).Destination
                    .Equals(Path.Combine(drive, runtimeRelative.Replace('/', Path.DirectorySeparatorChar)), StringComparison.OrdinalIgnoreCase), "Runtime mapping used the process's drive-relative current directory.");
                Assert(!FileSafety.Exists(Path.Combine(drive, unique)), "Preview wrote the game fixture into a real drive root.");
                Assert(!FileSafety.Exists(Path.Combine(drive, "libraries", unique)), "Preview wrote the runtime fixture into a real drive root.");
            }
        }

        static ScanResult RuntimeScan(Fixture f)
        {
            f.PutSource("mods/a.jar", "fixture mod");
            ScanResult scan = f.Scan();
            string runtime = Path.Combine(f.Root, "source-runtime");
            string file = f.Put(runtime, "libraries/fixture/runtime/1/runtime-1.jar", "fixture library");
            scan.Instance.RuntimePath = runtime;
            FileInfo info = new FileInfo(file);
            scan.Files.Add(new SourceFile { FullPath = file, Path = "runtime/libraries/fixture/runtime/1/runtime-1.jar", Category = "runtime", Size = info.Length, ModifiedUtc = info.LastWriteTimeUtc });
            return scan;
        }

        static void RuntimeSkipped()
        {
            using (Fixture f = new Fixture())
            {
                f.Export(RuntimeScan(f));
                ImportPlan plan = f.Preview(false);
                Assert(plan.Files.Count == 1 && plan.Files[0].File.Category == "mods", "Unmapped runtime entries should be skipped.");
                Assert(plan.Warnings.Any(x => x.Contains("运行文件已跳过")), "Skipping runtime should be visible in preview.");
                PackageEngine.Import(plan, None, null);
                Assert(File.Exists(Path.Combine(f.Target, "mods", "a.jar")), "Selected game data was not imported.");
                Assert(!Directory.Exists(Path.Combine(f.Target, "libraries")), "Runtime files were written without an explicit mapping.");
            }
        }

        static void RuntimeMapped()
        {
            using (Fixture f = new Fixture())
            {
                f.Export(RuntimeScan(f));
                string runtime = Path.Combine(f.Root, "target-runtime");
                ImportPlan plan = PackageEngine.Preview(f.Zip, f.Target, runtime, null, false, None, null);
                Assert(plan.Files.Count == 2, "Explicitly mapped runtime entry was omitted.");
                PackageEngine.Import(plan, None, null);
                Assert(File.ReadAllText(Path.Combine(runtime, "libraries", "fixture", "runtime", "1", "runtime-1.jar")) == "fixture library", "Runtime files did not use their separate root.");
                Assert(!Directory.Exists(Path.Combine(f.Target, "libraries")), "Runtime files leaked into game directory.");
                AssertNoTemporary(f);
            }
        }

        static void IsolatedRuntimeMetadata()
        {
            using (Fixture f = new Fixture())
            {
                RawZip(f.Zip, new[] { "game/mods/a.jar", "runtime/versions/friends/friends.json", "runtime/versions/friends/friends.jar" }, null, false);
                string runtime = Path.Combine(f.Root, "isolated-runtime");
                string game = Path.Combine(runtime, "versions/friends");
                ImportPlan plan = PackageEngine.Preview(f.Zip, game, runtime, null, false, None, null);
                ImportResult result = PackageEngine.Import(plan, None, null);
                Assert(result.Added == 3 && File.Exists(Path.Combine(game, "friends.json")) && File.Exists(Path.Combine(game, "mods/a.jar")), "Legitimate isolated version mapping was rejected or changed.");
                AssertNoTemporary(f);
            }
        }

        static void DirectoryCollision()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/a.jar", "a");
                Directory.CreateDirectory(Path.Combine(f.Target, "mods", "a.jar"));
                f.Export(f.Scan());
                Throws<IOException>(() => f.Preview(true));
                Assert(!Directory.Exists(Path.Combine(f.Target, ".craftbridge")), "Preview should not mutate target directories.");
            }
        }

        static void AncestorFileCollision()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/subdir/a.jar", "incoming mod bytes");
                string ancestor = f.PutTarget("mods/subdir", "valuable existing file in the directory position");
                f.Export(f.Scan());
                Throws<IOException>(() => f.Preview(true));
                Assert(File.ReadAllText(ancestor) == "valuable existing file in the directory position", "Preview changed the conflicting ancestor file.");
                Assert(!Directory.Exists(Path.Combine(f.Target, ".craftbridge")), "A rejected preview created task directories.");
            }
        }

        static void LegacyPackageCompatibility()
        {
            using (Fixture f = new Fixture())
            {
                f.PutSource("mods/legacy.jar", "legacy package bytes");
                f.Export(f.Scan());
                using (FileStream stream = new FileStream(f.Zip, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Update, false, Encoding.UTF8))
                {
                    ZipArchiveEntry old = archive.GetEntry("manifest.json");
                    PackageManifest legacy;
                    using (Stream input = old.Open())
                    using (StreamReader reader = new StreamReader(input, Encoding.UTF8))
                        legacy = Json.Serializer().Deserialize<PackageManifest>(reader.ReadToEnd());
                    legacy.Application = "Blockporter";
                    old.Delete();
                    using (Stream output = archive.CreateEntry("manifest.json").Open())
                    using (StreamWriter writer = new StreamWriter(output, new UTF8Encoding(false)))
                        writer.Write(Json.Serializer().Serialize(legacy));
                }
                Assert(PackageEngine.ReadManifest(f.Zip).Application == "Blockporter", "Legacy identity should be accepted without rewriting the received ZIP.");
                string receivedHash = FileSafety.Hash(f.Zip, None);
                ImportResult result = PackageEngine.Import(f.Preview(false), None, null);
                Assert(result.Added == 1 && File.ReadAllText(Path.Combine(f.Target, "mods/legacy.jar")) == "legacy package bytes", "Legacy payload was not imported.");
                Assert(FileSafety.Hash(f.Zip, None) == receivedHash, "Legacy package was modified during migration.");
                Assert(!String.IsNullOrEmpty(result.BackupPath) && result.BackupPath.Contains(".craftbridge"), "New imports should record recovery state under the new product directory.");
            }
        }

        static void InvalidPaths()
        {
            string[] paths = {
                "game/../escape.txt", "game/C:/escape.txt", "game//escape.txt", "game/mods/CON.jar",
                "game/mods/file. ", "/game/mods/a.jar", "game/mods/..\\escape.jar",
                "game/.craftbridge/tasks/owned.txt", "game/.blockporter/backups/secret.txt",
                "game/config/.CraftBridge-backup-task/secret.txt", "game/config/.BLOCKPORTER-task.tmp",
                "game/config/launcher_accounts.json", "runtime/other/a.txt"
            };
            foreach (string path in paths)
                using (Fixture f = new Fixture())
                {
                    RawZip(f.Zip, new[] { path }, null, false);
                    Throws<InvalidDataException>(() => PackageEngine.ReadManifest(f.Zip));
                    Assert(!Directory.Exists(f.Target), "Reading an invalid package must not create a target.");
                }
        }

        static void DuplicateEntries()
        {
            using (Fixture f = new Fixture())
            {
                RawZip(f.Zip, new[] { "game/mods/a.jar", "game/mods/A.jar" }, null, false);
                Throws<InvalidDataException>(() => PackageEngine.ReadManifest(f.Zip));
            }
            using (Fixture f = new Fixture())
            {
                RawZip(f.Zip, new[] { "game/mods/a.jar", "game/mods/a.jar" }, null, false);
                Throws<InvalidDataException>(() => PackageEngine.ReadManifest(f.Zip));
            }
        }

        static void UndeclaredAndCategory()
        {
            using (Fixture f = new Fixture())
            {
                RawZip(f.Zip, new[] { "game/mods/a.jar" }, "unlisted.txt", false);
                Throws<InvalidDataException>(() => PackageEngine.ReadManifest(f.Zip));
            }
            using (Fixture f = new Fixture())
            {
                RawZip(f.Zip, new[] { "game/mods/a.jar" }, null, true);
                Throws<InvalidDataException>(() => PackageEngine.ReadManifest(f.Zip));
            }
        }

        static void RawZip(string path, IEnumerable<string> paths, string extraEntry, bool wrongCategory)
        {
            PackageManifest manifest = new PackageManifest { FormatVersion = 1, Application = "Blockporter", Edition = "java", CreatedUtc = DateTime.UtcNow.ToString("o"), Name = "Adversarial fixture", Purpose = "test" };
            byte[] content = Encoding.UTF8.GetBytes("fixture bytes");
            using (FileStream output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                foreach (string entryPath in paths)
                {
                    string category = entryPath.StartsWith("runtime/", StringComparison.Ordinal) ? "runtime"
                        : entryPath.StartsWith("game/", StringComparison.Ordinal) ? Catalog.CategoryOf(entryPath.Substring(5)) : "custom";
                    manifest.Files.Add(new ManifestFile { Path = entryPath, Category = wrongCategory ? "worlds" : category, Size = content.Length, Sha256 = HashBytes(content), ModifiedUtc = DateTime.UtcNow.ToString("o") });
                    using (Stream entry = archive.CreateEntry("payload/" + entryPath).Open()) entry.Write(content, 0, content.Length);
                }
                if (extraEntry != null)
                    using (Stream extra = archive.CreateEntry(extraEntry).Open()) extra.WriteByte(1);
                using (Stream entry = archive.CreateEntry("manifest.json").Open())
                using (StreamWriter writer = new StreamWriter(entry, new UTF8Encoding(false))) writer.Write(Json.Serializer().Serialize(manifest));
            }
        }

        static void ChangePayload(string zip, string entryName, string replacement)
        {
            using (FileStream stream = new FileStream(zip, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Update, false, Encoding.UTF8))
            {
                ZipArchiveEntry old = archive.GetEntry(entryName);
                Assert(old != null, "Fixture entry is missing.");
                old.Delete();
                using (Stream entry = archive.CreateEntry(entryName, CompressionLevel.Optimal).Open())
                using (StreamWriter writer = new StreamWriter(entry, new UTF8Encoding(false))) writer.Write(replacement);
            }
        }

        static string HashBytes(byte[] content)
        {
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
        }

        static void AssertNoTemporary(Fixture f)
        {
            string[] leftovers = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).Where(x =>
                Path.GetFileName(x).Contains(".partial-")
                || (Path.GetFileName(x).StartsWith(".craftbridge-", StringComparison.Ordinal) && x.EndsWith(".tmp", StringComparison.Ordinal))
                || (x.Contains(Path.DirectorySeparatorChar + "tasks" + Path.DirectorySeparatorChar) && x.EndsWith(".data", StringComparison.Ordinal))).ToArray();
            Assert(leftovers.Length == 0, "Temporary task files remain: " + String.Join(", ", leftovers));
        }

        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name + " was not thrown.");
        }

        static void Assert(bool condition, string message)
        { if (!condition) throw new Exception(message); }
    }
}
