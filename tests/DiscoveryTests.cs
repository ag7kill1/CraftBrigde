using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CraftBridge.Tests
{
    public static class DiscoveryTests
    {
        public static void Run(Action<string, Action> run)
        {
            run("Discovery distinguishes isolated and shared game directories", delegate {
                Fixture(delegate(string root) {
                    string runtime = Path.Combine(root, ".minecraft");
                    WriteVersion(runtime, "plain", "{\"id\":\"plain\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
                    WriteVersion(runtime, "modded", "{\"id\":\"modded\",\"inheritsFrom\":\"plain\",\"libraries\":[{\"name\":\"net.fabricmc:fabric-loader:0.16.0\"}]}");
                    Write(runtime, "saves/shared/level.dat", "shared");
                    string isolated = Path.Combine(runtime, "versions", "modded");
                    Directory.CreateDirectory(Path.Combine(isolated, "mods"));
                    List<InstanceInfo> found = Discovery.Find(root, CancellationToken.None);
                    InstanceInfo plain = found.Single(x => x.VersionId == "plain");
                    InstanceInfo modded = found.Single(x => x.VersionId == "modded");
                    Equal(FileSafety.Full(runtime), FileSafety.Full(plain.GamePath));
                    Equal(FileSafety.Full(isolated), FileSafety.Full(modded.GamePath));
                    Equal("plain", modded.MinecraftVersion);
                    Equal("Fabric", modded.Loader);
                    Equal("0.16.0", modded.LoaderVersion);
                });
            });
            run("Discovery resolves a selected isolated version directory", delegate {
                Fixture(delegate(string root) {
                    string runtime = Path.Combine(root, ".minecraft");
                    WriteVersion(runtime, "test", "{\"id\":\"test\"}");
                    string game = Path.Combine(runtime, "versions", "test");
                    InstanceInfo instance = Discovery.Find(game, CancellationToken.None).Single();
                    Equal("test", instance.VersionId);
                    Equal(FileSafety.Full(runtime), FileSafety.Full(instance.RuntimePath));
                });
            });
            run("Discovery resolves empty prepared Prism instance roots", delegate {
                Fixture(delegate(string root) {
                    string instance = Path.Combine(root, "instances", "Friends Pack");
                    Write(instance, "mmc-pack.json", "{\"formatVersion\":1,\"components\":[{\"uid\":\"net.minecraft\",\"version\":\"1.20.1\"},{\"uid\":\"net.fabricmc.fabric-loader\",\"version\":\"0.16.0\"}]}");
                    string game = Path.Combine(instance, ".minecraft");
                    Directory.CreateDirectory(game);
                    InstanceInfo info = Discovery.Find(instance, CancellationToken.None).Single();
                    Equal(FileSafety.Full(game), FileSafety.Full(info.GamePath));
                    Assert(info.IsPrism, "Prism flag missing");
                    Equal("1.20.1", info.MinecraftVersion);
                    Equal("Fabric", info.Loader);
                    Assert(Discovery.Find(root, CancellationToken.None).Any(x => FileSafety.Full(x.GamePath) == FileSafety.Full(game)), "Nested Prism instance missing");
                });
            });
            run("Discovery rejects cyclic version inheritance", delegate {
                Fixture(delegate(string root) {
                    WriteVersion(root, "a", "{\"id\":\"a\",\"inheritsFrom\":\"b\"}");
                    WriteVersion(root, "b", "{\"id\":\"b\",\"inheritsFrom\":\"a\"}");
                    Throws<InvalidDataException>(delegate { Discovery.Inspect(root, root, "a"); });
                });
            });
            run("Discovery keeps the nearest child's loader version across inheritance", delegate {
                Fixture(delegate(string root) {
                    WriteVersion(root, "base", "{\"id\":\"1.20.1\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
                    WriteVersion(root, "old-fabric", "{\"id\":\"old-fabric\",\"inheritsFrom\":\"base\",\"libraries\":[{\"name\":\"net.fabricmc:fabric-loader:0.15.0\"}]}");
                    WriteVersion(root, "new-fabric", "{\"id\":\"new-fabric\",\"inheritsFrom\":\"old-fabric\",\"libraries\":[{\"name\":\"net.fabricmc:fabric-loader:0.16.0\"}]}");
                    InstanceInfo info = Discovery.Inspect(root, root, "new-fabric");
                    Equal("Fabric", info.Loader);
                    Equal("0.16.0", info.LoaderVersion);
                    Equal("1.20.1", info.MinecraftVersion);
                });
            });
            run("Discovery honors cancellation", delegate {
                Fixture(delegate(string root) {
                    CancellationTokenSource cts = new CancellationTokenSource(); cts.Cancel();
                    Throws<OperationCanceledException>(delegate { Discovery.Find(root, cts.Token); });
                });
            });
            run("Scanner preserves known categories when custom folders are added", delegate {
                Fixture(delegate(string root) {
                    Write(root, "config/custom/mod.toml", "setting=true");
                    ScanResult scan = new ScanResult { Instance = Discovery.Inspect(root, "", "") };
                    Scanner.AddCustom(scan, Path.Combine(root, "config", "custom"), CancellationToken.None);
                    Equal("config", scan.Files.Single().Category);
                });
            });
            run("Scanner omits session locks, accounts and nested migration backups", delegate {
                Fixture(delegate(string root) {
                    Write(root, "config/visible.toml", "ok");
                    Write(root, "config/accounts.json", "private");
                    Write(root, "saves/world/session.lock", "lock");
                    Write(root, "saves/world/level.dat", "world");
                    Write(root, "config/.craftbridge/backups/secret.dat", "backup");
                    ScanResult scan = Scanner.Scan(Discovery.Inspect(root, "", ""), CancellationToken.None);
                    Assert(scan.Files.Count == 2, "Private or temporary data entered scan");
                    Assert(scan.Warnings.Any(x => x.Contains(".craftbridge")), "Skipped backup warning missing");
                });
            });
            run("Scanner skips cross-volume backups and unfinished migration files", delegate {
                Fixture(delegate(string root) {
                    Write(root, "config/visible.toml", "ok");
                    Write(root, "config/.craftbridge-backup-fixture/0000000.backup", "old private content");
                    Write(root, "mods/.craftbridge-fixture-0.tmp", "unfinished import");
                    ScanResult scan = Scanner.Scan(Discovery.Inspect(root, "", ""), CancellationToken.None);
                    Assert(scan.Files.Count == 1 && scan.Files[0].Path == "game/config/visible.toml", "Internal backups or temporary bytes entered the export selection");
                    Assert(scan.Warnings.Count == 2, "Skipped internal content should be visible");
                });
            });
            run("Scanner refuses manually added folders inside migration backups", delegate {
                Fixture(delegate(string root) {
                    foreach (string prefix in new[] { ".craftbridge", ".blockporter" })
                    {
                        Write(root, "config/" + prefix + "-backup-fixture/nested/old.toml", "private backup");
                        ScanResult scan = new ScanResult { Instance = Discovery.Inspect(root, "", "") };
                        Throws<IOException>(delegate { Scanner.AddCustom(scan, Path.Combine(root, "config", prefix + "-backup-fixture", "nested"), CancellationToken.None); });
                        Assert(scan.Files.Count == 0, "Manually selecting a backup descendant disclosed backup contents");
                    }
                });
            });
            run("Scanner excludes both current and legacy migration workspace names", delegate {
                Fixture(delegate(string root) {
                    Write(root, "config/visible.toml", "setting=true");
                    foreach (string prefix in new[] { ".craftbridge", ".blockporter" })
                    {
                        Write(root, "config/" + prefix + "/backups/private.txt", "private");
                        Write(root, "config/" + prefix + "-backup-fixture/0000000.backup", "private");
                        Write(root, "mods/" + prefix + "-fixture.tmp", "unfinished");
                    }
                    ScanResult scan = Scanner.Scan(Discovery.Inspect(root, "", ""), CancellationToken.None);
                    Assert(scan.Files.Count == 1 && scan.Files[0].Path == "game/config/visible.toml", "Current or legacy migration bytes entered the export selection.");
                    Assert(scan.Warnings.Count == 6, "Each current and legacy migration entry should be reported as excluded.");
                });
            });
            run("Runtime collector uses effective child libraries and asset index only", delegate {
                Fixture(delegate(string root) {
                    WriteVersion(root, "base", "{\"id\":\"base\",\"mainClass\":\"net.minecraft.client.main.Main\",\"assetIndex\":{\"id\":\"unused-parent-index\"},\"libraries\":[{\"name\":\"org.example:common:1.0\"},{\"name\":\"org.example:linux-only:1.0\",\"rules\":[{\"action\":\"allow\",\"os\":{\"name\":\"linux\"}}]}]}");
                    WriteVersion(root, "fabric", "{\"id\":\"fabric\",\"inheritsFrom\":\"base\",\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\",\"assetIndex\":{\"id\":\"selected\"},\"libraries\":[{\"name\":\"org.example:common:2.0\"},{\"name\":\"net.fabricmc:fabric-loader:0.16.0\"},{\"name\":\"org.example:native:1.0\",\"natives\":{\"windows\":\"natives-windows\"}}]}");
                    Write(root, "versions/base/base.jar", "jar");
                    Write(root, "libraries/org/example/common/2.0/common-2.0.jar", "library");
                    Write(root, "libraries/net/fabricmc/fabric-loader/0.16.0/fabric-loader-0.16.0.jar", "loader");
                    Write(root, "libraries/org/example/native/1.0/native-1.0-natives-windows.jar", "native");
                    string hash = new String('a', 40);
                    Write(root, "assets/indexes/selected.json", "{\"objects\":{\"sound.ogg\":{\"hash\":\"" + hash + "\",\"size\":5}}}");
                    Write(root, "assets/objects/aa/" + hash, "asset");
                    ScanResult scan = Scanner.Scan(Discovery.Inspect(root, root, "fabric"), CancellationToken.None);
                    Assert(RuntimeCollector.Collect(scan, CancellationToken.None), String.Join("; ", scan.Warnings));
                    Assert(scan.Files.Any(x => x.Path == "runtime/libraries/org/example/common/2.0/common-2.0.jar"), "Child library omitted");
                    Assert(!scan.Files.Any(x => x.Path.Contains("common/1.0") || x.Path.Contains("linux-only") || x.Path.Contains("unused-parent-index")), "Overridden or wrong-platform dependency collected");
                    Assert(scan.Files.Count(x => x.Path.StartsWith("runtime/")) == 8, "Unexpected runtime file count");
                });
            });
            run("Runtime collector does not claim complete when asset index is missing", delegate {
                Fixture(delegate(string root) {
                    WriteVersion(root, "base", "{\"id\":\"base\",\"mainClass\":\"net.minecraft.client.main.Main\",\"assetIndex\":{\"id\":\"missing\"}}");
                    Write(root, "versions/base/base.jar", "jar");
                    ScanResult scan = Scanner.Scan(Discovery.Inspect(root, root, "base"), CancellationToken.None);
                    Assert(!RuntimeCollector.Collect(scan, CancellationToken.None), "Missing resource index was reported complete");
                    Assert(scan.Warnings.Any(x => x.Contains("资源索引")), "Missing index warning absent");
                });
            });
            run("Runtime collector flags a missing explicit inherited client jar", delegate {
                Fixture(delegate(string root) {
                    WriteVersion(root, "base", "{\"id\":\"base\",\"mainClass\":\"net.minecraft.client.main.Main\",\"assetIndex\":{\"id\":\"selected\"}}");
                    WriteVersion(root, "child", "{\"id\":\"child\",\"inheritsFrom\":\"base\",\"jar\":\"alternate-client\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
                    Write(root, "versions/base/base.jar", "unused original client");
                    Write(root, "assets/indexes/selected.json", "{\"objects\":{}}");
                    ScanResult scan = Scanner.Scan(Discovery.Inspect(root, root, "child"), CancellationToken.None);
                    Assert(!RuntimeCollector.Collect(scan, CancellationToken.None), "Missing explicitly selected client jar was reported complete");
                    Assert(scan.Warnings.Any(x => x.Contains("alternate-client.jar")), "Missing client jar should be named in the warning");
                });
            });
            run("Runtime collector inherits a vanilla entry point from its parent", delegate {
                Fixture(delegate(string root) {
                    WriteVersion(root, "base", "{\"id\":\"base\",\"mainClass\":\"net.minecraft.client.main.Main\",\"assetIndex\":{\"id\":\"selected\"}}");
                    WriteVersion(root, "child", "{\"id\":\"child\",\"inheritsFrom\":\"base\"}");
                    Write(root, "versions/base/base.jar", "client");
                    Write(root, "assets/indexes/selected.json", "{\"objects\":{}}");
                    ScanResult scan = Scanner.Scan(Discovery.Inspect(root, root, "child"), CancellationToken.None);
                    Assert(RuntimeCollector.Collect(scan, CancellationToken.None), String.Join("; ", scan.Warnings));
                    Assert(!scan.Warnings.Any(x => x.Contains("未识别或旧式启动入口")), "A known inherited entry point was ignored");
                });
            });
            run("Runtime collector flags old virtual resources as requiring preparation", delegate {
                Fixture(delegate(string root) {
                    WriteVersion(root, "old", "{\"id\":\"old\",\"mainClass\":\"net.minecraft.client.main.Main\",\"assetIndex\":{\"id\":\"old\"}}");
                    Write(root, "versions/old/old.jar", "jar");
                    Write(root, "assets/indexes/old.json", "{\"virtual\":true,\"objects\":{}}");
                    ScanResult scan = Scanner.Scan(Discovery.Inspect(root, root, "old"), CancellationToken.None);
                    Assert(!RuntimeCollector.Collect(scan, CancellationToken.None), "Virtual-resource generation was ignored");
                });
            });
            run("Runtime collector refuses Prism runtime copying", delegate {
                Fixture(delegate(string root) {
                    ScanResult scan = new ScanResult { Instance = new InstanceInfo { GamePath = root, IsPrism = true } };
                    Throws<IOException>(delegate { RuntimeCollector.Collect(scan, CancellationToken.None); });
                });
            });
            run("Runtime collector leaves no partial selection after invalid metadata", delegate {
                Fixture(delegate(string root) {
                    WriteVersion(root, "bad", "{\"id\":\"bad\",\"mainClass\":\"net.minecraft.client.main.Main\",\"assetIndex\":{\"id\":\"selected\"},\"libraries\":[{\"name\":\"org.example:bad:1.0\",\"downloads\":{\"artifact\":{\"path\":\"../escape.jar\"}}}]}");
                    Write(root, "versions/bad/bad.jar", "jar");
                    Write(root, "assets/indexes/selected.json", "{\"objects\":{}}");
                    Write(root, "mods/keep.jar", "mod");
                    ScanResult scan = Scanner.Scan(Discovery.Inspect(root, root, "bad"), CancellationToken.None);
                    Throws<InvalidDataException>(delegate { RuntimeCollector.Collect(scan, CancellationToken.None); });
                    Assert(scan.Files.Count == 1 && scan.Files[0].Category == "mods", "A failed runtime scan changed the current selection");
                });
            });
            run("FileSafety rejects traversal and Win32 device filenames", delegate {
                string[] bad = { "../file", "/file", "a\\b", "a//b", "a/..", "C:/file", "a/CON.txt", "a/COM¹.dat", "a/LPT²", "a/CONIN$", "a/CONOUT$", "a/name. ", "a/name." };
                foreach (string path in bad) Throws<InvalidDataException>(delegate { FileSafety.Relative(path); });
                FileSafety.Relative("saves/我的世界/DIM-1/region/r.-1.0.mca");
            });
            run("FileSafety detects locked world sessions", delegate {
                Fixture(delegate(string root) {
                    string path = Write(root, "saves/world/session.lock", "lock");
                    using (FileStream held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        Throws<IOException>(delegate { FileSafety.CheckWorldLocks(new[] { path }); });
                    FileSafety.CheckWorldLocks(new[] { path });
                });
            });
            run("FileSafety never deletes its owning work root", delegate {
                Fixture(delegate(string root) {
                    Write(root, "task/stage/file.dat", "temporary");
                    Throws<IOException>(delegate { FileSafety.DeleteOwnedDirectory(root, root); });
                    FileSafety.DeleteOwnedDirectory(Path.Combine(root, "task"), root);
                    Assert(Directory.Exists(root) && !Directory.Exists(Path.Combine(root, "task")), "Owned cleanup scope incorrect");
                });
            });
        }

        static void Fixture(Action<string> action)
        {
            string owner = Path.Combine(Path.GetDirectoryName(typeof(DiscoveryTests).Assembly.Location), "test-data");
            Directory.CreateDirectory(owner);
            string root = Path.Combine(owner, "discovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { action(root); }
            finally { FileSafety.DeleteOwnedDirectory(root, owner); }
        }
        static string Write(string root, string relative, string content)
        {
            string path = FileSafety.Join(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
            return path;
        }
        static void WriteVersion(string root, string id, string json)
        { Write(root, "versions/" + id + "/" + id + ".json", json); }
        static void Assert(bool value, string message)
        { if (!value) throw new Exception(message); }
        static void Equal(string expected, string actual)
        { Assert(String.Equals(expected, actual, StringComparison.Ordinal), "Expected " + expected + ", actual " + actual); }
        static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
    }
}
