using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CraftBridge
{
    public static class Product
    {
        public const string Name = "CraftBridge";
        public const string WorkDirectoryName = ".craftbridge";
        public static bool IsPackageApplication(string name)
        { return name == Name || name == "Blockporter"; }
        public static bool IsInternalName(string name)
        {
            return name.StartsWith(WorkDirectoryName, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(".blockporter", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class DataCategory
    {
        public string Id, Name, Description;
        public string[] Roots;
        public DataCategory(string id, string name, string description, params string[] roots)
        { Id = id; Name = name; Description = description; Roots = roots; }
    }

    public static class Catalog
    {
        public static readonly DataCategory[] Categories = {
            new DataCategory("worlds", "世界存档", "建筑、玩家数据、维度及世界内的数据包", "saves"),
            new DataCategory("mods", "模组", "整套模组文件，包含禁用的模组", "mods"),
            new DataCategory("config", "配置与整合包脚本", "模组配置、默认配置、KubeJS 与脚本", "config", "defaultconfigs", "kubejs", "scripts", "patchouli_books"),
            new DataCategory("resources", "资源包与光影", "材质、音效与光影包", "resourcepacks", "shaderpacks"),
            new DataCategory("maps", "小地图与路径点", "常见地图模组的本地数据", "journeymap", "XaeroWaypoints", "XaeroWorldMap"),
            new DataCategory("personal", "个人设置", "按键、画质、服务器列表与快捷栏", "options.txt", "optionsof.txt", "optionsshaders.txt", "servers.dat", "hotbar.nbt"),
            new DataCategory("screenshots", "截图", "游戏内保存的截图", "screenshots")
        };
        public static string CategoryOf(string path)
        {
            string first = path.Split('/')[0];
            foreach (DataCategory cat in Categories)
                if (cat.Roots.Any(x => x.Equals(first, StringComparison.OrdinalIgnoreCase))) return cat.Id;
            return "custom";
        }
        public static string CategoryName(string id)
        {
            if (id == "runtime") return "所选版本的运行文件";
            if (id == "custom") return "其他文件（手动添加）";
            DataCategory cat = Categories.FirstOrDefault(x => x.Id == id);
            return cat == null ? id : cat.Name;
        }
    }

    public sealed class InstanceInfo
    {
        public string Name { get; set; }
        public string GamePath { get; set; }
        public string RuntimePath { get; set; }
        public string VersionId { get; set; }
        public string MinecraftVersion { get; set; }
        public string Loader { get; set; }
        public string LoaderVersion { get; set; }
        public bool IsPrism { get; set; }
        public override string ToString()
        {
            return Name + "  ·  " + (String.IsNullOrEmpty(MinecraftVersion) ? "版本待确认" : MinecraftVersion)
                + (String.IsNullOrEmpty(Loader) ? "" : " / " + Loader);
        }
    }

    public sealed class SourceFile
    {
        public string FullPath;
        public string Path;
        public string Category;
        public long Size;
        public DateTime ModifiedUtc;
    }

    public sealed class ScanResult
    {
        public InstanceInfo Instance;
        public List<SourceFile> Files = new List<SourceFile>();
        public List<string> Warnings = new List<string>();
    }

    // Only these fields enter the ZIP: machine paths and launcher accounts are never serialized.
    public sealed class PackageManifest
    {
        public int FormatVersion { get; set; }
        public string Application { get; set; }
        public string Edition { get; set; }
        public string CreatedUtc { get; set; }
        public string Name { get; set; }
        public string Purpose { get; set; }
        public string MinecraftVersion { get; set; }
        public string VersionId { get; set; }
        public string Loader { get; set; }
        public string LoaderVersion { get; set; }
        public bool RuntimeIncluded { get; set; }
        public bool RuntimeComplete { get; set; }
        public List<ManifestFile> Files { get; set; }
        public List<string> Notes { get; set; }
        public PackageManifest() { Files = new List<ManifestFile>(); Notes = new List<string>(); }
    }

    public sealed class ManifestFile
    {
        public string Path { get; set; }
        public string Category { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
        public string ModifiedUtc { get; set; }
    }

    public sealed class OperationProgress
    {
        public string Message;
        public long Completed, Total;
        public int Percent { get { return Total <= 0 ? 0 : (int)Math.Min(100, Completed * 100.0 / Total); } }
    }

    public enum FileAction { Add, Reuse, Replace, Keep }
    public sealed class PlannedFile
    {
        public ManifestFile File;
        public string Destination;
        public FileAction Action;
        public string ExistingHash;
    }
    public sealed class ImportPlan
    {
        public string ArchivePath;
        public string ArchiveHash;
        public string TargetRoot;
        public PackageManifest Manifest;
        public List<PlannedFile> Files = new List<PlannedFile>();
        public List<string> Warnings = new List<string>();
        public long WriteBytes { get { return Files.Where(x => x.Action == FileAction.Add || x.Action == FileAction.Replace).Sum(x => x.File.Size); } }
    }
    public sealed class ImportResult
    {
        public int Added, Replaced, Reused, Kept;
        public string BackupPath;
        public string TargetRoot;
        public List<string> Warnings = new List<string>();
    }

    public static class Json
    {
        public static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024, RecursionLimit = 100 }; }
        public static Dictionary<string, object> ObjectFile(string path)
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("配置文件过大：" + path);
            return Serializer().Deserialize<Dictionary<string, object>>(System.IO.File.ReadAllText(path));
        }
        public static string String(Dictionary<string, object> obj, string key)
        { object val; return obj != null && obj.TryGetValue(key, out val) && val != null ? val.ToString() : ""; }
        public static Dictionary<string, object> Map(Dictionary<string, object> obj, string key)
        { object val; return obj != null && obj.TryGetValue(key, out val) ? val as Dictionary<string, object> : null; }
        public static object[] Array(Dictionary<string, object> obj, string key)
        {
            object val;
            if (obj == null || !obj.TryGetValue(key, out val) || val is string || val is System.Collections.IDictionary) return new object[0];
            System.Collections.IEnumerable values = val as System.Collections.IEnumerable;
            return values == null ? new object[0] : values.Cast<object>().ToArray();
        }
    }
}
