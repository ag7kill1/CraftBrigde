using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CraftBridge
{
    public static class PackageEngine
    {
        const int MaxFiles = 250000;
        const long MaxTotalBytes = 2L * 1024 * 1024 * 1024 * 1024;
        const int MaxManifestBytes = 64 * 1024 * 1024;

        public static PackageManifest Export(ScanResult scan, IEnumerable<SourceFile> selected, string outputZip,
            string purpose, bool runtimeComplete, CancellationToken token, Action<OperationProgress> progress)
        {
            List<SourceFile> files = selected.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) throw new IOException("请至少勾选一个需要迁移的文件。");
            if (files.Count > MaxFiles) throw new IOException("文件超过首版的 25 万个上限，请拆分迁移包。");
            if (files.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
                throw new IOException("选择中存在重复的包内路径。");
            foreach (SourceFile file in files)
            {
                token.ThrowIfCancellationRequested();
                ValidatePayloadPath(file.Path);
                FileSafety.NoLinks(file.FullPath);
                if (file.Path.StartsWith("game/", StringComparison.Ordinal))
                {
                    string expected = FileSafety.Join(scan.Instance.GamePath, file.Path.Substring(5));
                    if (!expected.Equals(System.IO.Path.GetFullPath(file.FullPath), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("源文件不在所选游戏目录内。");
                }
                else if (String.IsNullOrEmpty(scan.Instance.RuntimePath)
                    || !FileSafety.Join(scan.Instance.RuntimePath, file.Path.Substring(8)).Equals(System.IO.Path.GetFullPath(file.FullPath), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("运行文件不在所选运行目录内。");
            }
            ValidateWorldSelection(scan, files, token);
            List<string> locks = WorldLockPaths(scan.Instance.GamePath, files.Select(x => x.Path)).ToList();
            FileSafety.CheckWorldLocks(locks);
            outputZip = System.IO.Path.GetFullPath(outputZip);
            FileSafety.NoLinks(outputZip);
            if (FileSafety.Exists(outputZip)) throw new IOException("同名文件已经存在，请为迁移包选择新的文件名。");
            if (FileSafety.Inside(outputZip, scan.Instance.GamePath)
                || (!String.IsNullOrEmpty(scan.Instance.RuntimePath) && FileSafety.Inside(outputZip, scan.Instance.RuntimePath)))
                throw new IOException("请把 ZIP 保存到游戏目录以外，避免把迁移包再次打包进游戏数据。");
            string outDir = System.IO.Path.GetDirectoryName(outputZip);
            if (!Directory.Exists(outDir)) throw new IOException("ZIP 的保存文件夹不存在。");
            long total = files.Sum(x => x.Size);
            if (total > MaxTotalBytes) throw new IOException("所选文件超过首版的 2 TB 上限，请拆分迁移。");
            FileSafety.RequireSpace(outDir, total);
            HashSet<string> selectedPaths = new HashSet<string>(files.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            PackageManifest manifest = new PackageManifest {
                FormatVersion = 1, Application = Product.Name, Edition = "java", CreatedUtc = DateTime.UtcNow.ToString("o"),
                Name = scan.Instance.Name, Purpose = purpose, MinecraftVersion = scan.Instance.MinecraftVersion,
                VersionId = scan.Instance.VersionId, Loader = scan.Instance.Loader, LoaderVersion = scan.Instance.LoaderVersion,
                RuntimeIncluded = files.Any(x => x.Category == "runtime"),
                RuntimeComplete = files.Any(x => x.Category == "runtime") && runtimeComplete && scan.Files.Where(x => x.Category == "runtime").All(x => selectedPaths.Contains(x.Path)),
                Notes = scan.Warnings.Distinct().Take(200).ToList()
            };
            string temporary = outputZip + ".partial-" + Guid.NewGuid().ToString("N");
            long done = 0;
            try
            {
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive archive = new ZipArchive(output, ZipArchiveMode.Create, false, Encoding.UTF8))
                {
                    foreach (SourceFile file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        EnsureSourceUnchanged(file);
                        ZipArchiveEntry entry = archive.CreateEntry("payload/" + file.Path, CompressionLevel.Optimal);
                        DateTime stamp = file.ModifiedUtc;
                        if (stamp.Year >= 1980 && stamp.Year <= 2107) entry.LastWriteTime = new DateTimeOffset(stamp, TimeSpan.Zero);
                        string hash;
                        using (FileStream input = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (Stream entryStream = entry.Open())
                            hash = FileSafety.CopyHash(input, entryStream, file.Size, token, count => {
                                done += count; Report(progress, "正在打包  " + file.Path, done, total);
                            });
                        EnsureSourceUnchanged(file);
                        manifest.Files.Add(new ManifestFile { Path = file.Path, Category = file.Category, Size = file.Size,
                            Sha256 = hash, ModifiedUtc = file.ModifiedUtc.ToString("o") });
                    }
                    FileSafety.CheckWorldLocks(locks);
                    foreach (SourceFile file in files) EnsureSourceUnchanged(file);
                    ValidateWorldSelection(scan, files, token);
                    ZipArchiveEntry metaEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    using (Stream stream = metaEntry.Open())
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.Write(Json.Serializer().Serialize(manifest));
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, outputZip);
                Report(progress, "迁移包已生成", total, total);
                return manifest;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        static void EnsureSourceUnchanged(SourceFile file)
        {
            FileSafety.NoLinks(file.FullPath);
            FileInfo now = new FileInfo(file.FullPath);
            if (!now.Exists || now.Length != file.Size || now.LastWriteTimeUtc != file.ModifiedUtc)
                throw new IOException("扫描后文件发生变化，请关闭游戏并重新扫描：" + file.Path);
        }
        static void ValidateWorldSelection(ScanResult scan, List<SourceFile> selected, CancellationToken token)
        {
            HashSet<string> selectedPaths = new HashSet<string>(selected.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
            ILookup<string, SourceFile> scannedWorlds = scan.Files.Where(x => IsWorld(x.Path)).ToLookup(x => WorldUnit(x.Path), StringComparer.OrdinalIgnoreCase);
            foreach (string unit in selected.Where(x => IsWorld(x.Path)).Select(x => WorldUnit(x.Path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                List<SourceFile> all = scannedWorlds[unit].ToList();
                if (all.Any(x => !selectedPaths.Contains(x.Path)))
                    throw new IOException("世界存档需要完整选择，请勾选整个世界文件夹：" + unit.Substring("game/saves/".Length));
                string dir = FileSafety.Join(scan.Instance.GamePath, unit.Substring(5));
                ScanResult current = new ScanResult { Instance = scan.Instance };
                Scanner.AddPath(current, dir, unit, "worlds", token);
                if (current.Warnings.Count > 0 || current.Files.Count != all.Count)
                    throw new IOException("世界文件发生变化或包含链接，请关闭游戏并重新扫描：" + unit);
                Dictionary<string, SourceFile> currentFiles = current.Files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
                foreach (SourceFile item in all)
                {
                    SourceFile actual;
                    if (!currentFiles.TryGetValue(item.Path, out actual) || actual.Size != item.Size || actual.ModifiedUtc != item.ModifiedUtc)
                        throw new IOException("世界扫描后发生变化，请重新扫描：" + unit);
                }
            }
        }

        public static PackageManifest ReadManifest(string zip)
        { return ReadManifest(zip, CancellationToken.None); }

        public static PackageManifest ReadManifest(string zip, CancellationToken token)
        {
            FileSafety.NoLinks(zip);
            using (FileStream stream = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8))
            {
                Dictionary<string, ZipArchiveEntry> index;
                return ValidateArchive(archive, out index, token);
            }
        }

        public static ImportPlan Preview(string zip, string targetGamePath, string targetRuntimePath,
            ISet<string> selectedCategories, bool replaceDifferent, CancellationToken token, Action<OperationProgress> progress)
        {
            zip = System.IO.Path.GetFullPath(zip);
            string target = FileSafety.Full(targetGamePath);
            FileSafety.NoLinks(target);
            if (File.Exists(target)) throw new IOException("目标游戏目录是文件，请选择文件夹。");
            string runtime = String.IsNullOrWhiteSpace(targetRuntimePath) ? "" : FileSafety.Full(targetRuntimePath);
            if (runtime.Length > 0) FileSafety.NoLinks(runtime);
            FileSafety.NoLinks(zip);
            using (FileStream stream = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8))
            {
                Dictionary<string, ZipArchiveEntry> entries;
                PackageManifest manifest = ValidateArchive(archive, out entries, token);
                ImportPlan plan = new ImportPlan { ArchivePath = zip, TargetRoot = target, Manifest = manifest };
                Report(progress, "正在校验迁移包", 0, 1);
                plan.ArchiveHash = FileSafety.Hash(zip, token);
                List<ManifestFile> chosen = manifest.Files.Where(x => (selectedCategories == null || selectedCategories.Contains(x.Category))
                    && (!x.Path.StartsWith("runtime/", StringComparison.Ordinal) || runtime.Length > 0)).ToList();
                if (chosen.Count == 0) throw new IOException("没有选中可导入的文件。运行文件还需要选择目标 .minecraft 根目录。");
                HashSet<string> destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < chosen.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    ManifestFile file = chosen[i];
                    string dest = file.Path.StartsWith("game/", StringComparison.Ordinal) ? FileSafety.Join(target, file.Path.Substring(5)) : FileSafety.Join(runtime, file.Path.Substring(8));
                    if (!destinations.Add(dest)) throw new IOException("游戏目录和运行目录的映射发生重叠，请重新选择。");
                    FileSafety.NoLinks(dest);
                    // A conflicting world will be renamed as a whole before checking its final paths.
                    if (!IsWorld(file.Path)) CheckDestinationShape(dest);
                    PlannedFile item = new PlannedFile { File = file, Destination = dest, Action = FileAction.Add };
                    if (File.Exists(dest))
                    {
                        item.ExistingHash = FileSafety.Hash(dest, token);
                        item.Action = item.ExistingHash == file.Sha256 ? FileAction.Reuse : replaceDifferent ? FileAction.Replace : FileAction.Keep;
                    }
                    plan.Files.Add(item);
                    Report(progress, "正在对比  " + file.Path, i + 1, chosen.Count);
                }
                // A changed world is always installed under a new world name, never merged into an existing world.
                HashSet<string> reservedWorlds = new HashSet<string>(plan.Files.Where(x => IsWorld(x.File.Path))
                    .Select(x => FileSafety.Join(target, WorldUnit(x.File.Path).Substring(5))), StringComparer.OrdinalIgnoreCase);
                string worldStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                foreach (IGrouping<string, PlannedFile> group in plan.Files.Where(x => IsWorld(x.File.Path)).GroupBy(x => WorldUnit(x.File.Path), StringComparer.OrdinalIgnoreCase))
                {
                    string existingWorld = FileSafety.Join(target, group.Key.Substring(5));
                    if (!Directory.Exists(existingWorld) && !File.Exists(existingWorld)) continue;
                    FileSafety.CheckWorldLocks(new[] { System.IO.Path.Combine(existingWorld, "session.lock") });
                    bool allSame = group.All(x => x.Action == FileAction.Reuse);
                    if (allSame)
                    {
                        ScanResult current = new ScanResult { Instance = new InstanceInfo { GamePath = target } };
                        Scanner.AddPath(current, existingWorld, group.Key, "worlds", token);
                        allSame = current.Warnings.Count == 0 && current.Files.Count == group.Count();
                    }
                    if (allSame) continue;
                    string newWorld = existingWorld + " (迁移 " + worldStamp + ")";
                    int suffix = 2;
                    while (FileSafety.Exists(newWorld) || reservedWorlds.Contains(newWorld))
                        newWorld = existingWorld + " (迁移 " + worldStamp + "-" + suffix++ + ")";
                    reservedWorlds.Add(newWorld);
                    foreach (PlannedFile item in group)
                    {
                        string relative = item.File.Path.Substring(group.Key.Length + 1);
                        item.Destination = FileSafety.Join(newWorld, relative);
                        item.ExistingHash = null;
                        item.Action = FileAction.Add;
                    }
                    plan.Warnings.Add("同名世界保留两份，导入到：" + System.IO.Path.GetFileName(newWorld));
                }
                ValidateDestinations(plan);
                foreach (PlannedFile item in plan.Files) CheckDestinationShape(item.Destination);
                if (manifest.RuntimeIncluded && runtime.Length == 0) plan.Warnings.Add("此次仅导入游戏数据；运行文件已跳过，请在目标启动器准备相应版本和加载器。");
                if (!manifest.RuntimeIncluded || !manifest.RuntimeComplete) plan.Warnings.Add("请在目标启动器确认游戏版本 " + (manifest.MinecraftVersion ?? "未知") + "、加载器 " + (manifest.Loader ?? "原版／未知") + " " + (manifest.LoaderVersion ?? "") + "；本工具不自动安装加载器或 Java。");
                InstanceInfo targetInfo = Discovery.Inspect(target, runtime, "");
                if (!String.IsNullOrEmpty(targetInfo.MinecraftVersion) && !String.IsNullOrEmpty(manifest.MinecraftVersion) && targetInfo.MinecraftVersion != manifest.MinecraftVersion)
                    plan.Warnings.Add("目标游戏版本与包不同：目标 " + targetInfo.MinecraftVersion + "，迁移包 " + manifest.MinecraftVersion + "。建议选择匹配的新实例。");
                if (!String.IsNullOrEmpty(targetInfo.Loader) && !String.IsNullOrEmpty(manifest.Loader) && targetInfo.Loader != manifest.Loader)
                    plan.Warnings.Add("目标加载器与迁移包不同，请先准备匹配的实例。");
                if (!String.IsNullOrEmpty(targetInfo.LoaderVersion) && !String.IsNullOrEmpty(manifest.LoaderVersion)
                    && targetInfo.Loader == manifest.Loader
                    && NormalizeLoaderVersion(targetInfo.LoaderVersion, targetInfo.MinecraftVersion) != NormalizeLoaderVersion(manifest.LoaderVersion, manifest.MinecraftVersion))
                    plan.Warnings.Add("目标加载器版本与迁移包不同：目标 " + targetInfo.LoaderVersion + "，迁移包 " + manifest.LoaderVersion + "，请在启动器确认匹配。");
                if (targetInfo.IsPrism && runtime.Length > 0) throw new IOException("Prism 使用自己的运行环境管理。请清空运行目录，只向已准备好的 Prism 实例导入游戏数据。");
                foreach (string warning in manifest.Notes ?? new List<string>()) plan.Warnings.Add(warning);
                return plan;
            }
        }

        sealed class CommitRecord
        {
            public PlannedFile Item;
            public string Backup;
            public string Temporary;
            public bool Installed;
        }

        sealed class PlannedWorld
        {
            public string Unit, Destination, Staging;
            public List<PlannedFile> Files;
            public bool Reused, Installed;
        }

        static List<PlannedWorld> PlanWorlds(ImportPlan plan)
        {
            List<PlannedWorld> worlds = new List<PlannedWorld>();
            foreach (IGrouping<string, PlannedFile> group in plan.Files.Where(x => IsWorld(x.File.Path)).GroupBy(x => WorldUnit(x.File.Path), StringComparer.OrdinalIgnoreCase))
            {
                string destination = null;
                foreach (PlannedFile item in group)
                {
                    string relative = item.File.Path.Substring(group.Key.Length + 1).Replace('/', System.IO.Path.DirectorySeparatorChar);
                    string root = item.Destination.Substring(0, item.Destination.Length - relative.Length).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                    if (destination == null) destination = root;
                    if (!root.Equals(destination, StringComparison.OrdinalIgnoreCase)
                        || !FileSafety.Join(root, relative.Replace('\\', '/')).Equals(item.Destination, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("同一世界的目标目录不一致，请重新预览。");
                }
                bool reused = group.All(x => x.Action == FileAction.Reuse);
                if (!reused && group.Any(x => x.Action != FileAction.Add)) throw new IOException("世界存档必须整体导入或完整复用，请重新预览。");
                worlds.Add(new PlannedWorld { Unit = group.Key, Destination = destination, Files = group.ToList(), Reused = reused });
            }
            return worlds;
        }

        static void CheckWorldsUnchanged(IEnumerable<PlannedWorld> worlds, CancellationToken token)
        {
            foreach (PlannedWorld world in worlds)
            {
                token.ThrowIfCancellationRequested();
                FileSafety.NoLinks(world.Destination);
                if (!world.Reused)
                {
                    if (FileSafety.Exists(world.Destination)) throw new IOException("预览后出现了同名世界目录，请重新预览，避免混合存档：" + world.Destination);
                    continue;
                }
                FileSafety.CheckWorldLocks(new[] { System.IO.Path.Combine(world.Destination, "session.lock") });
                ScanResult current = new ScanResult { Instance = new InstanceInfo { GamePath = world.Destination } };
                Scanner.AddPath(current, world.Destination, world.Unit, "worlds", token);
                HashSet<string> expected = new HashSet<string>(world.Files.Select(x => x.File.Path), StringComparer.OrdinalIgnoreCase);
                if (current.Warnings.Count > 0 || current.Files.Count != expected.Count || current.Files.Any(x => !expected.Contains(x.Path)))
                    throw new IOException("世界内容在预览后发生变化，请重新预览：" + world.Destination);
            }
        }

        static void ValidateDestinations(ImportPlan plan)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string controlRoot = System.IO.Path.Combine(plan.TargetRoot, Product.WorkDirectoryName);
            foreach (PlannedFile item in plan.Files)
            {
                string dest = System.IO.Path.GetFullPath(item.Destination);
                if (!paths.Add(dest)) throw new IOException("游戏目录和运行目录的映射发生重叠，请重新选择。");
                if (FileSafety.Inside(dest, controlRoot) || dest.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                    .Any(Product.IsInternalName))
                    throw new IOException("目标路径进入迁移工具的工作或备份目录，请重新选择运行目录。");
                if (FileSafety.Inside(plan.ArchivePath, dest)) throw new IOException("目标文件与收到的 ZIP 路径冲突，请将 ZIP 移到游戏目录以外。");
                if (item.File.Path.StartsWith("runtime/", StringComparison.Ordinal))
                    foreach (DataCategory cat in Catalog.Categories)
                        foreach (string root in cat.Roots)
                            if (FileSafety.Inside(dest, System.IO.Path.Combine(plan.TargetRoot, root)))
                                throw new IOException("运行文件不能写入存档、模组或其他游戏数据，请重新选择运行目录：" + dest);
            }
            foreach (string path in paths)
            {
                string parent = System.IO.Path.GetDirectoryName(path);
                while (!String.IsNullOrEmpty(parent))
                {
                    if (paths.Contains(parent)) throw new IOException("映射后的文件与文件夹路径冲突，请重新选择游戏目录和运行目录。");
                    string next = System.IO.Path.GetDirectoryName(parent);
                    if (next == parent) break;
                    parent = next;
                }
            }
        }

        public static ImportResult Import(ImportPlan plan, CancellationToken token, Action<OperationProgress> progress)
        {
            if (plan == null || plan.Files.Count == 0) throw new IOException("请先生成迁移预览。");
            FileSafety.NoLinks(plan.TargetRoot);
            string taskId = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string controlRoot = System.IO.Path.Combine(plan.TargetRoot, Product.WorkDirectoryName);
            string work = System.IO.Path.Combine(controlRoot, "tasks", taskId);
            string backupRoot = System.IO.Path.Combine(controlRoot, "backups", taskId);
            string journal = System.IO.Path.Combine(backupRoot, "恢复记录.txt");
            List<CommitRecord> records = new List<CommitRecord>();
            List<PlannedWorld> worlds = PlanWorlds(plan);
            Dictionary<string, PlannedWorld> worldIndex = worlds.ToDictionary(x => x.Unit, StringComparer.OrdinalIgnoreCase);
            List<string> createdDirs = new List<string>();
            ImportResult result = new ImportResult { TargetRoot = plan.TargetRoot };
            bool rollbackFailed = false;
            Exception failure = null;
            long done = 0, total = Math.Max(1, plan.WriteBytes * 2);
            try
            {
                FileSafety.NoLinks(plan.ArchivePath);
                using (FileStream zipStream = new FileStream(plan.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read, false, Encoding.UTF8))
                {
                    Dictionary<string, ZipArchiveEntry> entries;
                    PackageManifest actual = ValidateArchive(archive, out entries, token);
                    if (FileSafety.Hash(plan.ArchivePath, token) != plan.ArchiveHash) throw new IOException("迁移包在预览后发生变化，请重新生成预览。");
                    if (Json.Serializer().Serialize(actual) != Json.Serializer().Serialize(plan.Manifest)) throw new IOException("迁移包清单与预览不符。");
                    ValidateDestinations(plan);
                    HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (PlannedFile item in plan.Files)
                    {
                        ValidatePayloadPath(item.File.Path);
                        if (!uniquePaths.Add(item.Destination)) throw new IOException("预览包含重复的目标路径。");
                        bool game = item.File.Path.StartsWith("game/", StringComparison.Ordinal);
                        if (game && !FileSafety.Inside(item.Destination, plan.TargetRoot)) throw new IOException("目标文件超出所选游戏目录。");
                        FileSafety.NoLinks(item.Destination);
                        CheckPreviewUnchanged(item, token);
                    }
                    CheckWorldsUnchanged(worlds, token);
                    FileSafety.CheckWorldLocks(WorldLockPaths(plan.TargetRoot, plan.Files.Select(x => x.File.Path)));
                    List<PlannedFile> writes = plan.Files.Where(x => x.Action == FileAction.Add || x.Action == FileAction.Replace).ToList();
                    if (writes.Count == 0)
                    {
                        result.Reused = plan.Files.Count(x => x.Action == FileAction.Reuse);
                        result.Kept = plan.Files.Count(x => x.Action == FileAction.Keep);
                        Report(progress, "导入完成，无需写入文件", total, total);
                        return result;
                    }
                    foreach (IGrouping<string, PlannedFile> drive in plan.Files.Where(x => x.Action == FileAction.Add || x.Action == FileAction.Replace).GroupBy(x => System.IO.Path.GetPathRoot(x.Destination), StringComparer.OrdinalIgnoreCase))
                        FileSafety.RequireSpace(drive.Key, drive.Sum(x => x.File.Size));
                    FileSafety.RequireSpace(plan.TargetRoot, plan.WriteBytes * 2);
                    EnsureDirectory(work, createdDirs);
                    for (int i = 0; i < worlds.Count; i++)
                        if (!worlds[i].Reused) worlds[i].Staging = System.IO.Path.Combine(work, "world-" + i.ToString("D7"));
                    // Every file that will be written is fully staged and hash-checked before touching game data.
                    for (int i = 0; i < writes.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        PlannedFile item = writes[i];
                        string staging = System.IO.Path.Combine(work, i.ToString("D7") + ".data");
                        if (IsWorld(item.File.Path))
                        {
                            PlannedWorld world = worldIndex[WorldUnit(item.File.Path)];
                            staging = FileSafety.Join(world.Staging, item.File.Path.Substring(world.Unit.Length + 1));
                            EnsureDirectory(System.IO.Path.GetDirectoryName(staging), createdDirs);
                        }
                        using (Stream input = entries["payload/" + item.File.Path].Open())
                        using (FileStream output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            string hash = FileSafety.CopyHash(input, output, item.File.Size, token, count => { done += count; Report(progress, "校验并暂存  " + item.File.Path, done, total); });
                            if (hash != item.File.Sha256) throw new InvalidDataException("文件校验失败，游戏数据尚未改动：" + item.File.Path);
                            // Worlds are published directly from staging; other files are flushed at their final sibling copy.
                            if (IsWorld(item.File.Path)) output.Flush(true);
                        }
                        SetFileTimestamp(staging, item.File.ModifiedUtc);
                        records.Add(new CommitRecord { Item = item, Temporary = staging });
                    }
                    token.ThrowIfCancellationRequested();
                    foreach (PlannedFile item in plan.Files) CheckPreviewUnchanged(item, token);
                    CheckWorldsUnchanged(worlds, token);
                    FileSafety.CheckWorldLocks(WorldLockPaths(plan.TargetRoot, plan.Files.Select(x => x.File.Path)));
                    EnsureDirectory(backupRoot, createdDirs);
                    File.WriteAllText(journal, "CraftBridge 迁移恢复记录\r\n创建时间：" + DateTime.Now.ToString("s") + "\r\n状态：写入中\r\n原始 ZIP 未改动。下列 .backup 文件为替换前原文件，请勿在确认前删除。\r\n", new UTF8Encoding(true));
                    for (int i = 0; i < records.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        CommitRecord record = records[i];
                        PlannedFile item = record.Item;
                        if (IsWorld(item.File.Path))
                        {
                            PlannedWorld world = worldIndex[WorldUnit(item.File.Path)];
                            if (world.Installed) continue;
                            Report(progress, "正在导入完整世界  " + world.Unit, done, total);
                            CheckWorldsUnchanged(new[] { world }, token);
                            VerifyWorldContents(world, world.Staging, token);
                            EnsureDirectory(System.IO.Path.GetDirectoryName(world.Destination), createdDirs);
                            File.AppendAllText(journal, "\r\n新增完整世界：" + world.Destination + "\r\n", Encoding.UTF8);
                            token.ThrowIfCancellationRequested();
                            FileSafety.NoLinks(world.Destination);
                            // Same-volume directory rename publishes a complete world and fails if the name was taken.
                            Directory.Move(world.Staging, world.Destination);
                            world.Installed = true;
                            done += world.Files.Sum(x => x.File.Size);
                            continue;
                        }
                        FileSafety.NoLinks(item.Destination);
                        CheckPreviewUnchanged(item, token);
                        EnsureDirectory(System.IO.Path.GetDirectoryName(item.Destination), createdDirs);
                        string sibling = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(item.Destination), ".craftbridge-" + taskId + "-" + i + ".tmp");
                        FileSafety.NoLinks(sibling);
                        try
                        {
                            using (FileStream input = new FileStream(record.Temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (FileStream output = new FileStream(sibling, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            {
                                string hash = FileSafety.CopyHash(input, output, item.File.Size, token, count => { done += count; Report(progress, "正在导入  " + item.File.Path, done, total); });
                                if (hash != item.File.Sha256) throw new IOException("暂存文件发生变化，已停止导入。");
                                output.Flush(true);
                            }
                            FileSafety.NoLinks(item.Destination);
                            CheckPreviewUnchanged(item, token);
                            if (item.Action == FileAction.Replace)
                            {
                                // File.Replace keeps old content on the same volume as its original location.
                                string destDrive = System.IO.Path.GetPathRoot(item.Destination);
                                string backupDrive = System.IO.Path.GetPathRoot(backupRoot);
                                string localBackup = destDrive.Equals(backupDrive, StringComparison.OrdinalIgnoreCase)
                                    ? backupRoot : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(item.Destination), ".craftbridge-backup-" + taskId);
                                EnsureDirectory(localBackup, createdDirs);
                                record.Backup = System.IO.Path.Combine(localBackup, i.ToString("D7") + ".backup");
                                File.AppendAllText(journal, "\r\n原位置：" + item.Destination + "\r\n备份：" + record.Backup + "\r\n", Encoding.UTF8);
                                File.Replace(sibling, item.Destination, record.Backup, true);
                            }
                            else
                            {
                                File.AppendAllText(journal, "\r\n新增：" + item.Destination + "\r\n", Encoding.UTF8);
                                File.Move(sibling, item.Destination);
                            }
                            record.Installed = true;
                            SetFileTimestamp(item.Destination, item.File.ModifiedUtc);
                        }
                        finally { if (File.Exists(sibling)) File.Delete(sibling); }
                    }
                    result.Added = plan.Files.Count(x => x.Action == FileAction.Add);
                    result.Replaced = plan.Files.Count(x => x.Action == FileAction.Replace);
                    result.Reused = plan.Files.Count(x => x.Action == FileAction.Reuse);
                    result.Kept = plan.Files.Count(x => x.Action == FileAction.Keep);
                    result.BackupPath = backupRoot;
                    File.AppendAllText(journal, "\r\n状态：导入成功\r\n新增 " + result.Added + "，替换 " + result.Replaced + "，复用 " + result.Reused + "，保留本地 " + result.Kept + "。\r\n", Encoding.UTF8);
                }
                Report(progress, "导入完成", total, total);
                return result;
            }
            catch (Exception original)
            {
                failure = original;
                List<string> errors = new List<string>();
                // Do not roll back individual chunks of a world that has since been opened or edited.
                foreach (PlannedWorld world in worlds.Where(x => x.Installed).Reverse())
                {
                    try
                    {
                        FileSafety.CheckWorldLocks(new[] { System.IO.Path.Combine(world.Destination, "session.lock") });
                        VerifyWorldContents(world, world.Destination, CancellationToken.None);
                        FileSafety.NoLinks(world.Staging);
                        Directory.Move(world.Destination, world.Staging);
                        world.Installed = false;
                    }
                    catch (Exception e) { errors.Add(world.Destination + "：" + e.Message); }
                }
                foreach (CommitRecord record in records.AsEnumerable().Reverse())
                {
                    if (!record.Installed || IsWorld(record.Item.File.Path)) continue;
                    try
                    {
                        FileSafety.NoLinks(record.Item.Destination);
                        if (!File.Exists(record.Item.Destination) || FileSafety.Hash(record.Item.Destination, CancellationToken.None) != record.Item.File.Sha256)
                            throw new IOException("此文件在导入期间被其他程序更改或移走；已保留现状和原文件备份，请手动核对。");
                        if (!String.IsNullOrEmpty(record.Backup))
                        {
                            FileSafety.NoLinks(record.Backup);
                            if (!File.Exists(record.Backup)) throw new IOException("替换前的备份已被移走，已保留当前文件，请手动核对。");
                            if (FileSafety.Hash(record.Backup, CancellationToken.None) != record.Item.ExistingHash)
                                throw new IOException("替换前的备份已被更改，已保留当前文件和备份，请手动核对。");
                            File.Replace(record.Backup, record.Item.Destination, null, true);
                        }
                        else if (File.Exists(record.Item.Destination)) File.Delete(record.Item.Destination);
                    }
                    catch (Exception e) { errors.Add(record.Item.Destination + "：" + e.Message); }
                }
                if (File.Exists(journal))
                {
                    try
                    {
                        FileSafety.NoLinks(journal);
                        File.AppendAllText(journal, "\r\n状态：" + (errors.Count == 0 ? "已回退" : "需手动恢复")
                            + "\r\n原因：" + original.Message + "\r\n" + String.Join("\r\n", errors) + "\r\n", Encoding.UTF8);
                    }
                    catch (Exception e) { errors.Add("恢复记录未能更新：" + e.Message); }
                }
                if (errors.Count > 0)
                {
                    rollbackFailed = true;
                    failure = new IOException("导入未完成，部分文件无法自动恢复。请保留备份并按恢复记录处理：" + backupRoot + "\r\n" + String.Join("\r\n", errors), original);
                    throw failure;
                }
                throw;
            }
            finally
            {
                string cleanupWarning = null;
                try { if (Directory.Exists(work)) FileSafety.DeleteOwnedDirectory(work, controlRoot); }
                catch (Exception e) { cleanupWarning = "临时目录未能完全清理：" + work + "。" + e.Message; result.Warnings.Add(cleanupWarning); }
                if (!rollbackFailed)
                    foreach (string dir in createdDirs.AsEnumerable().Reverse())
                        try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (cleanupWarning != null && failure != null) throw new IOException(failure.Message + "\r\n" + cleanupWarning, failure);
            }
        }

        static void SetFileTimestamp(string path, string modifiedUtc)
        {
            DateTime stamp;
            if (DateTime.TryParse(modifiedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out stamp))
                File.SetLastWriteTimeUtc(path, stamp.ToUniversalTime());
        }

        static void VerifyWorldContents(PlannedWorld world, string root, CancellationToken token)
        {
            FileSafety.NoLinks(root);
            Dictionary<string, ManifestFile> expected = new Dictionary<string, ManifestFile>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlannedFile item in world.Files)
            {
                string path = FileSafety.Join(root, item.File.Path.Substring(world.Unit.Length + 1));
                expected.Add(path, item.File);
                string directory = System.IO.Path.GetDirectoryName(path);
                while (FileSafety.Inside(directory, root))
                {
                    if (!directories.Add(directory)) break;
                    if (FileSafety.Full(directory) == FileSafety.Full(root)) break;
                    directory = System.IO.Path.GetDirectoryName(directory);
                }
            }
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            int count = 0;
            while (pending.Count > 0)
                foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    token.ThrowIfCancellationRequested();
                    FileSafety.NoLinks(path);
                    if (Directory.Exists(path))
                    {
                        if (!directories.Contains(path)) throw new IOException("世界目录出现了清单外文件夹：" + path);
                        pending.Push(path);
                    }
                    else if (!expected.ContainsKey(path)) throw new IOException("世界目录出现了清单外文件：" + path);
                    else count++;
                }
            if (count != expected.Count) throw new IOException("世界目录有文件被移走：" + root);
            foreach (KeyValuePair<string, ManifestFile> item in expected)
                if (FileSafety.Hash(item.Key, token) != item.Value.Sha256)
                    throw new IOException("世界文件已被其他程序修改：" + item.Key);
        }

        static void CheckPreviewUnchanged(PlannedFile item, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FileSafety.NoLinks(item.Destination);
            CheckDestinationShape(item.Destination);
            if (item.Action == FileAction.Add)
            {
                if (File.Exists(item.Destination)) throw new IOException("预览后目标出现了新文件，请重新预览：" + item.Destination);
            }
            else if (!File.Exists(item.Destination) || FileSafety.Hash(item.Destination, token) != item.ExistingHash)
                throw new IOException("目标文件在预览后发生变化，请关闭游戏并重新预览：" + item.Destination);
        }

        static void EnsureDirectory(string path, List<string> created)
        {
            FileSafety.NoLinks(path);
            if (Directory.Exists(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(parent) && parent != path && !Directory.Exists(parent)) EnsureDirectory(parent, created);
            Directory.CreateDirectory(path);
            created.Add(path);
        }

        static void CheckDestinationShape(string path)
        {
            if (Directory.Exists(path)) throw new IOException("目标存在同名文件夹，请重新选择或预览：" + path);
            string parent = System.IO.Path.GetDirectoryName(path);
            while (!String.IsNullOrEmpty(parent))
            {
                if (File.Exists(parent)) throw new IOException("目标路径中的文件夹位置已被同名文件占用：" + parent);
                string next = System.IO.Path.GetDirectoryName(parent);
                if (next == parent) break;
                parent = next;
            }
        }

        static PackageManifest ValidateArchive(ZipArchive archive, out Dictionary<string, ZipArchiveEntry> entries, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (archive.Entries.Count > MaxFiles + 1) throw new InvalidDataException("迁移包文件数量超过首版上限。");
            entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("迁移包包含符号链接。");
                if (entries.ContainsKey(entry.FullName)) throw new InvalidDataException("迁移包包含重名文件（含大小写冲突）。");
                entries.Add(entry.FullName, entry);
            }
            ZipArchiveEntry meta;
            if (!entries.TryGetValue("manifest.json", out meta)) throw new InvalidDataException("这不是 CraftBridge 迁移包：缺少 manifest.json。请选择本工具导出的 ZIP。");
            if (meta.Length <= 0 || meta.Length > MaxManifestBytes) throw new InvalidDataException("迁移包清单大小异常。");
            PackageManifest manifest;
            using (Stream input = meta.Open())
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] bytes = new byte[8192]; int n;
                while ((n = input.Read(bytes, 0, bytes.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (buffer.Length + n > MaxManifestBytes) throw new InvalidDataException("迁移包清单过大。");
                    buffer.Write(bytes, 0, n);
                }
                manifest = Json.Serializer().Deserialize<PackageManifest>(Encoding.UTF8.GetString(buffer.ToArray()));
            }
            if (manifest == null || manifest.FormatVersion != 1 || !Product.IsPackageApplication(manifest.Application) || manifest.Edition != "java")
                throw new InvalidDataException("不支持此迁移包格式或版本。");
            if (manifest.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > MaxFiles) throw new InvalidDataException("文件清单为空或过大。");
            if (entries.Count != manifest.Files.Count + 1) throw new InvalidDataException("迁移包包含清单之外的文件。");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (ManifestFile file in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                if (file == null) throw new InvalidDataException("文件清单包含空记录。");
                ValidatePayloadPath(file.Path);
                if (!seen.Add(file.Path)) throw new InvalidDataException("文件清单存在重复路径。");
                if (file.Size < 0 || file.Size > MaxTotalBytes || !Regex.IsMatch(file.Sha256 ?? "", "^[a-f0-9]{64}$")) throw new InvalidDataException("文件大小或校验值无效。");
                string expectedCategory = file.Path.StartsWith("runtime/", StringComparison.Ordinal) ? "runtime" : Catalog.CategoryOf(file.Path.Substring(5));
                if (file.Category != expectedCategory) throw new InvalidDataException("文件分类与路径不符：" + file.Path);
                ZipArchiveEntry payload;
                if (!entries.TryGetValue("payload/" + file.Path, out payload) || payload.Length != file.Size) throw new InvalidDataException("文件清单与 ZIP 内容不一致：" + file.Path);
                total = checked(total + file.Size);
                if (total > MaxTotalBytes) throw new InvalidDataException("迁移包解压体积超过上限。");
            }
            foreach (string path in seen)
            {
                token.ThrowIfCancellationRequested();
                string parent = path;
                while (parent.LastIndexOf('/') > 0)
                {
                    parent = parent.Substring(0, parent.LastIndexOf('/'));
                    if (seen.Contains(parent)) throw new InvalidDataException("迁移包中的文件与文件夹路径相互冲突。");
                }
            }
            return manifest;
        }
        static void ValidatePayloadPath(string path)
        {
            FileSafety.Relative(path);
            string[] parts = path.Split('/');
            if (parts.Length < 2 || (parts[0] != "game" && parts[0] != "runtime") || (parts[0] == "runtime" && parts.Length < 3)) throw new InvalidDataException("文件必须位于 game/ 或 runtime/ 内。");
            if (parts.Any(x => Product.IsInternalName(x) || Scanner.IsExcluded(x))) throw new InvalidDataException("迁移包包含账号、锁文件或工具内部文件。");
            if (parts[0] == "runtime" && !new[] { "versions", "libraries", "assets" }.Contains(parts[1], StringComparer.Ordinal)) throw new InvalidDataException("运行文件类型不受支持。");
            if (parts[0] == "runtime" && parts[1] == "versions" && (parts.Length != 4
                || (!parts[3].Equals(parts[2] + ".json", StringComparison.OrdinalIgnoreCase)
                    && !parts[3].Equals(parts[2] + ".jar", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("版本运行文件只能是 versions/<版本>/<版本>.json 或 .jar，不能包含隔离实例的游戏数据。");
            if (parts[0] == "game" && new[] { "versions", "libraries", "assets", "runtime", "logs", "crash-reports" }.Contains(parts[1], StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("游戏数据路径包含运行文件或日志，请使用对应分类。");
        }
        static bool IsWorld(string path) { return path.StartsWith("game/saves/", StringComparison.OrdinalIgnoreCase) && path.Split('/').Length >= 4; }
        static string NormalizeLoaderVersion(string version, string minecraft)
        {
            return !String.IsNullOrEmpty(minecraft) && version.StartsWith(minecraft + "-", StringComparison.OrdinalIgnoreCase)
                ? version.Substring(minecraft.Length + 1) : version;
        }
        static string WorldUnit(string path) { return String.Join("/", path.Split('/').Take(3)); }
        static IEnumerable<string> WorldLockPaths(string root, IEnumerable<string> paths)
        {
            return paths.Where(IsWorld).Select(WorldUnit).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => FileSafety.Join(root, x.Substring(5) + "/session.lock"));
        }
        static void Report(Action<OperationProgress> progress, string message, long done, long total)
        { if (progress != null) progress(new OperationProgress { Message = message, Completed = done, Total = total }); }
    }
}
