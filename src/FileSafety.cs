using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace CraftBridge
{
    public static class FileSafety
    {
        public static string Full(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new IOException("请选择一个文件夹。");
            return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        }
        public static bool Inside(string path, string root)
        { return Full(path).StartsWith(Full(root), StringComparison.OrdinalIgnoreCase); }
        public static void Relative(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length > 2000 || path.Contains('\\') || path.StartsWith("/") || path.EndsWith("/"))
                throw new InvalidDataException("无效的包内路径：" + path);
            foreach (string part in path.Split('/'))
            {
                if (part == "" || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ")
                    || part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
                    || part.Any(c => c < 32)
                    || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|CLOCK\$|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("路径包含不安全或 Windows 不支持的名称：" + path);
            }
        }
        public static string Join(string root, string relative)
        {
            Relative(relative);
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            if (!Inside(path, root)) throw new InvalidDataException("文件路径超出目标文件夹。");
            return path;
        }
        public static bool Exists(string path) { return File.Exists(path) || Directory.Exists(path); }
        public static void NoLinks(string path)
        {
            string cursor = System.IO.Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(cursor))
            {
                try
                {
                    if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("为防止写入其他位置，请选择不包含符号链接或目录联接的路径：" + cursor);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                string parent = System.IO.Path.GetDirectoryName(cursor);
                if (parent == cursor) break;
                cursor = parent;
            }
        }
        public static string Hash(string path, CancellationToken token)
        {
            NoLinks(path);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return CopyHash(stream, null, stream.Length, token, null);
        }
        public static string CopyHash(Stream input, Stream output, long expected, CancellationToken token, Action<int> progress)
        {
            byte[] buffer = new byte[128 * 1024];
            long total = 0;
            using (SHA256 hash = SHA256.Create())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    total = checked(total + read);
                    if (total > expected) throw new InvalidDataException("文件实际大小超过清单记录，已停止导入。");
                    hash.TransformBlock(buffer, 0, read, null, 0);
                    if (output != null) output.Write(buffer, 0, read);
                    if (progress != null) progress(read);
                }
                token.ThrowIfCancellationRequested();
                if (total != expected) throw new InvalidDataException("文件大小与清单不符。");
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        public static void CheckWorldLocks(IEnumerable<string> filePaths)
        {
            foreach (string path in filePaths.Where(x => System.IO.Path.GetFileName(x).Equals("session.lock", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) continue;
                NoLinks(path);
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    { stream.Lock(0, 1); stream.Unlock(0, 1); }
                }
                catch (IOException e) { throw new IOException("存档可能正在使用，请先退出对应世界或关闭游戏：" + System.IO.Path.GetDirectoryName(path), e); }
            }
        }
        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double n = bytes; int i = 0;
            while (n >= 1024 && i < units.Length - 1) { n /= 1024; i++; }
            return n.ToString(i == 0 ? "0" : "0.0") + " " + units[i];
        }
        public static void RequireSpace(string path, long required)
        {
            if (required < 0 || required > long.MaxValue - 16 * 1024 * 1024) throw new IOException("无效的磁盘空间需求。");
            string drive = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path));
            DriveInfo info = new DriveInfo(drive);
            if (info.IsReady && info.AvailableFreeSpace < required + 16 * 1024 * 1024)
                throw new IOException("目标磁盘空间不足，需要约 " + FormatSize(required + 16 * 1024 * 1024) + " 可用空间。");
        }
        public static void DeleteOwnedDirectory(string path, string ownerRoot)
        {
            if (!Inside(path, ownerRoot) || Full(path) == Full(ownerRoot)) throw new IOException("临时目录不在本次任务范围内。");
            if (!Directory.Exists(path)) return;
            NoLinks(path);
            foreach (string item in Directory.EnumerateFileSystemEntries(path))
            {
                NoLinks(item);
                if (Directory.Exists(item)) DeleteOwnedDirectory(item, ownerRoot);
                else File.Delete(item);
            }
            Directory.Delete(path);
        }
    }
}
