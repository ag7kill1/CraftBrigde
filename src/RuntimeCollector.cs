using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace CraftBridge
{
    public static class RuntimeCollector
    {
        public static bool Collect(ScanResult scan, CancellationToken token)
        {
            InstanceInfo instance = scan.Instance;
            if (instance.IsPrism || String.IsNullOrEmpty(instance.VersionId) || String.IsNullOrEmpty(instance.RuntimePath))
                throw new IOException("当前实例没有可读取的标准版本描述。可先迁移模组和存档，并在目标启动器准备对应版本及加载器。");
            FileSafety.NoLinks(instance.RuntimePath);
            ScanResult destination = scan;
            scan = new ScanResult { Instance = instance };
            HashSet<string> requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Dictionary<string, object>> metadata = new List<Dictionary<string, object>>();
            string id = instance.VersionId;
            bool complete = true;
            while (!String.IsNullOrEmpty(id))
            {
                token.ThrowIfCancellationRequested();
                FileSafety.Relative(id);
                if (id.Contains('/') || !versions.Add(id) || versions.Count > 32) throw new InvalidDataException("版本描述存在循环继承、过深继承或无效版本编号。");
                string relative = "versions/" + id + "/" + id + ".json";
                requested.Add(relative);
                string path = FileSafety.Join(instance.RuntimePath, relative);
                if (!File.Exists(path)) { complete = false; scan.Warnings.Add("缺少版本描述：" + relative); break; }
                FileSafety.NoLinks(path);
                Dictionary<string, object> data = Json.ObjectFile(path);
                metadata.Add(data);
                string jarId = Json.String(data, "jar");
                bool explicitJar = !String.IsNullOrEmpty(jarId);
                if (String.IsNullOrEmpty(jarId)) jarId = id;
                FileSafety.Relative(jarId);
                if (jarId.Contains('/')) throw new InvalidDataException("版本 jar 编号无效。");
                string jar = "versions/" + jarId + "/" + jarId + ".jar";
                if (explicitJar || File.Exists(FileSafety.Join(instance.RuntimePath, jar)) || String.IsNullOrEmpty(Json.String(data, "inheritsFrom"))) requested.Add(jar);
                id = Json.String(data, "inheritsFrom");
            }
            HashSet<string> effectiveLibraries = new HashSet<string>(StringComparer.Ordinal);
            bool foundAssetIndex = false;
            bool foundLogConfig = false;
            // Metadata is child first: a child library/asset override supersedes its parent.
            foreach (Dictionary<string, object> data in metadata)
            {
                token.ThrowIfCancellationRequested();
                foreach (object value in Json.Array(data, "libraries"))
                {
                    token.ThrowIfCancellationRequested();
                    Dictionary<string, object> lib = value as Dictionary<string, object>;
                    if (lib == null || !AllowedOnWindows(lib)) continue;
                    string name = Json.String(lib, "name");
                    string key = LibraryKey(name);
                    if (String.IsNullOrEmpty(key))
                    { complete = false; scan.Warnings.Add("无法解析运行库坐标，需由目标启动器检查运行环境。"); continue; }
                    if (!effectiveLibraries.Add(key)) continue;
                    Dictionary<string, object> downloads = Json.Map(lib, "downloads");
                    Dictionary<string, object> artifact = Json.Map(downloads, "artifact");
                    Dictionary<string, object> classifiers = Json.Map(downloads, "classifiers");
                    Dictionary<string, object> natives = Json.Map(lib, "natives");
                    string libPath = Json.String(artifact, "path");
                    if (String.IsNullOrEmpty(libPath) && (artifact != null || (classifiers == null && natives == null)))
                        libPath = MavenPath(name);
                    if (!String.IsNullOrEmpty(libPath)) requested.Add("libraries/" + libPath);
                    string classifier = Json.String(natives, "windows").Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                    if (!String.IsNullOrEmpty(classifier))
                    {
                        object native;
                        if (classifiers != null && classifiers.TryGetValue(classifier, out native))
                        {
                            string nativePath = Json.String(native as Dictionary<string, object>, "path");
                            if (String.IsNullOrEmpty(nativePath)) nativePath = NativePath(name, classifier);
                            if (!String.IsNullOrEmpty(nativePath)) requested.Add("libraries/" + nativePath);
                        }
                        else if (downloads == null) requested.Add("libraries/" + NativePath(name, classifier));
                        else { complete = false; scan.Warnings.Add("缺少 Windows 原生运行库描述：" + name + " / " + classifier); }
                    }
                }
                Dictionary<string, object> assetIndex = Json.Map(data, "assetIndex");
                string assetId = Json.String(assetIndex, "id");
                if (!foundAssetIndex && !String.IsNullOrEmpty(assetId))
                {
                    foundAssetIndex = true;
                    FileSafety.Relative(assetId);
                    if (assetId.Contains('/')) throw new InvalidDataException("资源索引编号无效。");
                    string index = "assets/indexes/" + assetId + ".json";
                    requested.Add(index);
                    string indexPath = FileSafety.Join(instance.RuntimePath, index);
                    if (File.Exists(indexPath))
                    {
                        FileSafety.NoLinks(indexPath);
                        Dictionary<string, object> assetData = Json.ObjectFile(indexPath);
                        Dictionary<string, object> objects = Json.Map(assetData, "objects");
                        if (objects != null)
                            foreach (KeyValuePair<string, object> obj in objects)
                            {
                                token.ThrowIfCancellationRequested();
                                string hash = Json.String(obj.Value as Dictionary<string, object>, "hash");
                                if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{40}$")) throw new InvalidDataException("资源索引包含无效校验值。");
                                requested.Add("assets/objects/" + hash.Substring(0, 2) + "/" + hash);
                            }
                        else { complete = false; scan.Warnings.Add("资源索引没有有效的对象清单，需由目标启动器检查资源。"); }
                        if (Enabled(assetData, "virtual") || Enabled(assetData, "map_to_resources"))
                        {
                            complete = false;
                            scan.Warnings.Add("此旧版资源需要启动器生成虚拟资源目录，请在目标启动器检查运行环境。");
                        }
                    }
                    else { complete = false; scan.Warnings.Add("本地缺少资源索引：" + index); }
                }
                Dictionary<string, object> logFile = Json.Map(Json.Map(Json.Map(data, "logging"), "client"), "file");
                string logId = Json.String(logFile, "id");
                if (!foundLogConfig && !String.IsNullOrEmpty(logId))
                { foundLogConfig = true; requested.Add("assets/log_configs/" + logId); }
            }
            if (!foundAssetIndex)
            { complete = false; scan.Warnings.Add("版本描述未提供可识别的资源索引，无法确认运行资源完整性；请由目标启动器检查。"); }
            foreach (string relative in requested.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                string path = FileSafety.Join(instance.RuntimePath, relative);
                if (File.Exists(path))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    { complete = false; scan.Warnings.Add("跳过链接形式的运行文件：" + relative); }
                    else Scanner.AddPath(scan, path, "runtime/" + relative, "runtime", token);
                }
                else { complete = false; scan.Warnings.Add("本地缺少运行文件：" + relative); }
            }
            if (instance.Loader == "Forge" || instance.Loader == "NeoForge")
            {
                complete = false;
                scan.Warnings.Add("Forge / NeoForge 可能依赖安装时生成的额外文件；目标启动器应先安装相同版本加载器，本工具不执行安装脚本。");
            }
            string mainClass = metadata.Select(x => Json.String(x, "mainClass")).FirstOrDefault(x => !String.IsNullOrEmpty(x)) ?? "";
            if (String.IsNullOrEmpty(instance.Loader) && mainClass != "net.minecraft.client.main.Main")
            {
                complete = false;
                scan.Warnings.Add("当前版本包含未识别或旧式启动入口，无法确认额外运行依赖；目标启动器应先准备相同环境。");
            }
            token.ThrowIfCancellationRequested();
            destination.Files.RemoveAll(x => x.Category == "runtime");
            destination.Files.AddRange(scan.Files);
            destination.Files = destination.Files.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
            destination.Warnings = destination.Warnings.Concat(scan.Warnings).Distinct().ToList();
            return complete;
        }
        static bool Enabled(Dictionary<string, object> map, string key)
        { object value; return map.TryGetValue(key, out value) && value is bool && (bool)value; }
        static string LibraryKey(string coordinate)
        {
            string[] ext = coordinate.Split('@');
            string[] parts = ext[0].Split(':');
            if (parts.Length < 3 || parts.Length > 4 || parts.Any(String.IsNullOrWhiteSpace)) return "";
            return parts[0] + ":" + parts[1] + ":" + (parts.Length == 4 ? parts[3] : "") + "@" + (ext.Length > 1 ? ext[1] : "jar");
        }
        static string NativePath(string coordinate, string classifier)
        {
            string[] ext = coordinate.Split('@');
            string[] parts = ext[0].Split(':');
            if (parts.Length < 3) return "";
            return MavenPath(parts[0] + ":" + parts[1] + ":" + parts[2] + ":" + classifier + (ext.Length > 1 ? "@" + ext[1] : ""));
        }
        static bool AllowedOnWindows(Dictionary<string, object> lib)
        {
            object[] rules = Json.Array(lib, "rules");
            if (rules.Length == 0) return true;
            bool allowed = false;
            foreach (object value in rules)
            {
                Dictionary<string, object> rule = value as Dictionary<string, object>;
                Dictionary<string, object> os = Json.Map(rule, "os");
                string name = Json.String(os, "name");
                string arch = Json.String(os, "arch");
                if (!String.IsNullOrEmpty(name) && name != "windows") continue;
                if (!String.IsNullOrEmpty(arch) && arch != "x86_64" && arch != "amd64" && arch != "x86") continue;
                if ((arch == "x86" && Environment.Is64BitOperatingSystem) || ((arch == "x86_64" || arch == "amd64") && !Environment.Is64BitOperatingSystem)) continue;
                if (Json.Map(rule, "features") != null) continue;
                string version = Json.String(os, "version");
                if (!String.IsNullOrEmpty(version) && !Regex.IsMatch(Environment.OSVersion.Version.ToString(), version, RegexOptions.None, TimeSpan.FromMilliseconds(100))) continue;
                allowed = Json.String(rule, "action") == "allow";
            }
            return allowed;
        }
        static string MavenPath(string coordinate)
        {
            string[] ext = coordinate.Split('@');
            string[] parts = ext[0].Split(':');
            if (parts.Length < 3) return "";
            return parts[0].Replace('.', '/') + "/" + parts[1] + "/" + parts[2] + "/" + parts[1] + "-" + parts[2]
                + (parts.Length >= 4 ? "-" + parts[3] : "") + "." + (ext.Length > 1 ? ext[1] : "jar");
        }
    }
}
