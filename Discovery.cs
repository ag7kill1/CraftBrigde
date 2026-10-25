using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CraftBridge
{
    public static class Discovery
    {
        public static List<InstanceInfo> Find(string selectedRoot, CancellationToken token)
        {
            selectedRoot = DirectoryPath(selectedRoot);
            if (!Directory.Exists(selectedRoot)) throw new DirectoryNotFoundException("游戏目录不存在，请重新选择。");
            FileSafety.NoLinks(selectedRoot);
            List<InstanceInfo> found = new List<InstanceInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Queue<Tuple<string, int>> pending = new Queue<Tuple<string, int>>();
            pending.Enqueue(Tuple.Create(selectedRoot, 0));
            int visited = 0;
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                Tuple<string, int> node = pending.Dequeue();
                string dir = node.Item1;
                if (!visitedPaths.Add(FileSafety.Full(dir))) continue;
                if (++visited > 1500) throw new IOException("扫描范围过大，请直接选择启动器的游戏目录或实例目录。");
                if (!Directory.Exists(dir)) continue;
                FileSafety.NoLinks(dir);
                bool isRuntime = Directory.Exists(System.IO.Path.Combine(dir, "versions"));
                if (isRuntime)
                {
                    FileSafety.NoLinks(System.IO.Path.Combine(dir, "versions"));
                    string[] versions = ChildDirectories(System.IO.Path.Combine(dir, "versions"), false);
                    int added = 0;
                    foreach (string version in versions)
                    {
                        token.ThrowIfCancellationRequested();
                        if ((File.GetAttributes(version) & FileAttributes.ReparsePoint) != 0) continue;
                        string id = System.IO.Path.GetFileName(version);
                        if (!File.Exists(System.IO.Path.Combine(version, id + ".json"))) continue;
                        FileSafety.NoLinks(System.IO.Path.Combine(version, id + ".json"));
                        string game = HasData(version) ? version : dir;
                        Add(found, seen, Inspect(game, dir, id));
                        added++;
                    }
                    if (added == 0) Add(found, seen, Inspect(dir, dir, ""));
                }
                string dirName = System.IO.Path.GetFileName(dir);
                bool prismRoot = File.Exists(System.IO.Path.Combine(dir, "mmc-pack.json"));
                bool prismGame = File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dir) ?? dir, "mmc-pack.json"));
                bool versionFolder = File.Exists(System.IO.Path.Combine(dir, dirName + ".json"))
                    && String.Equals(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(dir)), "versions", StringComparison.OrdinalIgnoreCase);
                if (!isRuntime && (HasData(dir) || prismRoot || prismGame || versionFolder))
                    Add(found, seen, Inspect(dir, "", ""));
                if (node.Item2 >= 4) continue;
                foreach (string child in ChildDirectories(dir, node.Item2 == 0))
                {
                    string name = System.IO.Path.GetFileName(child);
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    if (isRuntime && name.Equals("versions", StringComparison.OrdinalIgnoreCase)) continue;
                    if (node.Item2 == 0 || new[] { ".minecraft", "minecraft", "instances", "profiles", "versions", "PrismLauncher", "MultiMC" }.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (Catalog.Categories.Any(c => c.Roots.Contains(name, StringComparer.OrdinalIgnoreCase))
                            || new[] { "libraries", "assets", "runtime", "logs", ".git", "node_modules" }.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                        pending.Enqueue(Tuple.Create(child, node.Item2 + 1));
                    }
                    else if (System.IO.Path.GetFileName(dir).Equals("instances", StringComparison.OrdinalIgnoreCase)
                        || System.IO.Path.GetFileName(dir).Equals("profiles", StringComparison.OrdinalIgnoreCase))
                        pending.Enqueue(Tuple.Create(child, node.Item2 + 1));
                }
            }
            if (found.Count == 0 && Directory.Exists(selectedRoot)) Add(found, seen, Inspect(selectedRoot, "", ""));
            return found.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        static string DirectoryPath(string path)
        {
            string full = System.IO.Path.GetFullPath(path);
            string root = System.IO.Path.GetPathRoot(full);
            return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
        }
        static string[] ChildDirectories(string path, bool required)
        {
            try { return Directory.GetDirectories(path); }
            catch (UnauthorizedAccessException) { if (required) throw; return new string[0]; }
            catch (DirectoryNotFoundException) { if (required) throw; return new string[0]; }
        }
        static bool HasData(string path)
        { return Catalog.Categories.Any(c => c.Roots.Any(r => FileSafety.Exists(System.IO.Path.Combine(path, r)))); }
        static void Add(List<InstanceInfo> found, HashSet<string> seen, InstanceInfo instance)
        { if (seen.Add(instance.GamePath + "|" + instance.VersionId)) found.Add(instance); }

        public static IEnumerable<string> KnownRoots()
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] paths = {
                System.IO.Path.Combine(roaming, ".minecraft"),
                System.IO.Path.Combine(roaming, "PrismLauncher", "instances"),
                System.IO.Path.Combine(roaming, "MultiMC", "instances"),
                System.IO.Path.Combine(roaming, "com.modrinth.theseus", "profiles"),
                System.IO.Path.Combine(local, "ModrinthApp", "profiles"),
                System.IO.Path.Combine(user, "curseforge", "minecraft", "Instances"),
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".minecraft")
            };
            return paths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static InstanceInfo Inspect(string gamePath, string runtimePath, string versionId)
        {
            gamePath = DirectoryPath(gamePath);
            FileSafety.NoLinks(gamePath);
            if (File.Exists(System.IO.Path.Combine(gamePath, "mmc-pack.json")))
            {
                string[] prismGames = { System.IO.Path.Combine(gamePath, ".minecraft"), System.IO.Path.Combine(gamePath, "minecraft") };
                string actual = prismGames.FirstOrDefault(x => Directory.Exists(x) && HasData(x))
                    ?? prismGames.FirstOrDefault(Directory.Exists);
                if (actual != null) { FileSafety.NoLinks(actual); gamePath = actual; }
            }
            InstanceInfo info = new InstanceInfo { GamePath = gamePath, Name = System.IO.Path.GetFileName(gamePath), VersionId = versionId ?? "", Loader = "", LoaderVersion = "", MinecraftVersion = "", RuntimePath = runtimePath ?? "" };
            string parent = System.IO.Path.GetDirectoryName(gamePath);
            string prism = System.IO.Path.Combine(parent ?? gamePath, "mmc-pack.json");
            if (!File.Exists(prism)) prism = System.IO.Path.Combine(gamePath, "mmc-pack.json");
            if (File.Exists(prism))
            {
                FileSafety.NoLinks(prism);
                info.IsPrism = true;
                info.Name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(prism));
                Dictionary<string, object> data = Json.ObjectFile(prism);
                foreach (object value in Json.Array(data, "components"))
                {
                    Dictionary<string, object> comp = value as Dictionary<string, object>;
                    string uid = Json.String(comp, "uid");
                    if (uid == "net.minecraft") info.MinecraftVersion = Json.String(comp, "version");
                    if (uid == "net.fabricmc.fabric-loader") { info.Loader = "Fabric"; info.LoaderVersion = Json.String(comp, "version"); }
                    if (uid == "net.minecraftforge") { info.Loader = "Forge"; info.LoaderVersion = Json.String(comp, "version"); }
                    if (uid == "net.neoforged") { info.Loader = "NeoForge"; info.LoaderVersion = Json.String(comp, "version"); }
                    if (uid == "org.quiltmc.quilt-loader") { info.Loader = "Quilt"; info.LoaderVersion = Json.String(comp, "version"); }
                }
                return info;
            }
            if (String.IsNullOrEmpty(info.RuntimePath))
            {
                string cursor = gamePath;
                for (int depth = 0; depth < 5 && !String.IsNullOrEmpty(cursor); depth++, cursor = System.IO.Path.GetDirectoryName(cursor))
                    if (Directory.Exists(System.IO.Path.Combine(cursor, "versions"))) { info.RuntimePath = cursor; break; }
            }
            if (String.IsNullOrEmpty(info.VersionId) && !String.IsNullOrEmpty(info.RuntimePath))
            {
                string possibleId = System.IO.Path.GetFileName(gamePath);
                if (File.Exists(System.IO.Path.Combine(info.RuntimePath, "versions", possibleId, possibleId + ".json"))) info.VersionId = possibleId;
            }
            if (!String.IsNullOrEmpty(info.VersionId) && !String.IsNullOrEmpty(info.RuntimePath))
            {
                info.RuntimePath = DirectoryPath(info.RuntimePath);
                FileSafety.NoLinks(info.RuntimePath);
                info.Name = info.VersionId + (FileSafety.Full(info.GamePath) == FileSafety.Full(info.RuntimePath) ? "（共享游戏目录）" : "（独立目录）");
                string cursor = info.VersionId;
                HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (!String.IsNullOrEmpty(cursor))
                {
                    FileSafety.Relative(cursor);
                    if (cursor.Contains('/') || !visited.Add(cursor) || visited.Count > 32)
                        throw new InvalidDataException("版本描述存在循环继承、过深继承或无效编号。");
                    string jsonPath = FileSafety.Join(info.RuntimePath, "versions/" + cursor + "/" + cursor + ".json");
                    if (!File.Exists(jsonPath)) break;
                    FileSafety.NoLinks(jsonPath);
                    Dictionary<string, object> data = Json.ObjectFile(jsonPath);
                    // Walk child first, just as the effective library merge does. A parent's
                    // older loader must not replace the nearest child's selected loader.
                    if (String.IsNullOrEmpty(info.Loader))
                    foreach (object value in Json.Array(data, "libraries"))
                    {
                        string library = Json.String(value as Dictionary<string, object>, "name");
                        string[] parts = library.Split(':');
                        if (parts.Length < 3) continue;
                        if (parts[0] == "net.fabricmc" && parts[1] == "fabric-loader") { info.Loader = "Fabric"; info.LoaderVersion = parts[2]; }
                        else if (parts[0] == "net.minecraftforge" && parts[1] == "forge") { info.Loader = "Forge"; info.LoaderVersion = parts[2]; }
                        else if (parts[0] == "net.neoforged" && parts[1] == "neoforge") { info.Loader = "NeoForge"; info.LoaderVersion = parts[2]; }
                        else if (parts[0] == "org.quiltmc" && parts[1] == "quilt-loader") { info.Loader = "Quilt"; info.LoaderVersion = parts[2]; }
                    }
                    string inherited = Json.String(data, "inheritsFrom");
                    if (String.IsNullOrEmpty(inherited)) info.MinecraftVersion = Json.String(data, "id");
                    cursor = inherited;
                }
            }
            return info;
        }
    }

    public static class Scanner
    {
        static readonly string[] PrivateNames = { "launcher_accounts.json", "launcher_accounts_microsoft_store.json", "accounts.json", "accounts.dat", "authlib-injector.json", "launcher_profiles.json", "launcher_profiles_microsoft_store.json" };
        public static bool IsExcluded(string name)
        { return name.Equals("session.lock", StringComparison.OrdinalIgnoreCase) || PrivateNames.Contains(name, StringComparer.OrdinalIgnoreCase); }
        public static ScanResult Scan(InstanceInfo instance, CancellationToken token)
        {
            FileSafety.NoLinks(instance.GamePath);
            ScanResult result = new ScanResult { Instance = instance };
            foreach (DataCategory cat in Catalog.Categories)
                foreach (string root in cat.Roots)
                    AddPath(result, System.IO.Path.Combine(instance.GamePath, root), "game/" + root, cat.Id, token);
            if (instance.IsPrism) result.Warnings.Add("Prism 实例可迁移游戏数据；导入 Prism 前请先在启动器中创建并运行相同版本和加载器的实例。");
            return result;
        }
        public static void AddCustom(ScanResult result, string fullPath, CancellationToken token)
        {
            if (!FileSafety.Inside(fullPath, result.Instance.GamePath) || FileSafety.Full(fullPath) == FileSafety.Full(result.Instance.GamePath))
                throw new IOException("其他文件必须位于当前游戏目录内，请选择其中的文件夹。");
            string relative = System.IO.Path.GetFullPath(fullPath).Substring(FileSafety.Full(result.Instance.GamePath).Length).Replace('\\', '/');
            if (new[] { "versions", "libraries", "assets", "runtime", "logs", "crash-reports" }.Contains(relative.Split('/')[0], StringComparer.OrdinalIgnoreCase)
                || relative.Split('/').Any(Product.IsInternalName))
                throw new IOException("这个目录包含运行文件、缓存或迁移备份；运行文件请使用单独的勾选项。");
            AddPath(result, fullPath, "game/" + relative, Catalog.CategoryOf(relative), token);
            result.Files = result.Files.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
        }
        public static void AddPath(ScanResult result, string fullPath, string relative, string category, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!FileSafety.Exists(fullPath)) return;
            if (Product.IsInternalName(System.IO.Path.GetFileName(fullPath)))
            { result.Warnings.Add("跳过迁移工具的工作或备份目录：" + relative); return; }
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            { result.Warnings.Add("跳过符号链接／联接：" + relative); return; }
            FileSafety.Relative(relative);
            if (File.Exists(fullPath))
            {
                if (IsExcluded(System.IO.Path.GetFileName(fullPath))) return;
                FileSafety.NoLinks(fullPath);
                FileInfo file = new FileInfo(fullPath);
                result.Files.Add(new SourceFile { FullPath = fullPath, Path = relative, Category = category, Size = file.Length, ModifiedUtc = file.LastWriteTimeUtc });
            }
            else
                foreach (string child in Directory.EnumerateFileSystemEntries(fullPath))
                    AddPath(result, child, relative + "/" + System.IO.Path.GetFileName(child), category, token);
        }
    }
}
