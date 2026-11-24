using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MeteoSrazky
{
    public static class DiskCache
    {
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(12);
        private static readonly TimeSpan OfflineRetentionTtl = TimeSpan.FromHours(72);
        private const int MaxCacheFiles = 350;

        public static string GetCacheDirectoryPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "MeteoBarbucha", "cache");
        }

        private static string GetFilePath(string key)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    sb.Append(b.ToString("x2"));
                return Path.Combine(GetCacheDirectoryPath(), sb.ToString() + ".json.gz");
            }
        }

        public static string TryGet(string key)
        {
            return TryGet(key, DefaultTtl);
        }

        public static string TryGet(string key, TimeSpan ttl)
        {
            try
            {
                string path = GetFilePath(key);
                if (!File.Exists(path))
                    return null;

                var info = new FileInfo(path);
                if (DateTime.UtcNow - info.LastWriteTimeUtc > ttl)
                {
                    // Keep file on disk up to OfflineRetentionTtl so offline fallback can still rescue data if network is down!
                    if (DateTime.UtcNow - info.LastWriteTimeUtc > OfflineRetentionTtl)
                        File.Delete(path);
                    return null;
                }

                return ReadGzipFile(path);
            }
            catch
            {
                return null;
            }
        }

        public static string TryGetStale(string key, out DateTime cachedAtLocal)
        {
            cachedAtLocal = DateTime.MinValue;
            try
            {
                string path = GetFilePath(key);
                if (!File.Exists(path))
                    return null;

                var info = new FileInfo(path);
                cachedAtLocal = info.LastWriteTime;
                return ReadGzipFile(path);
            }
            catch
            {
                return null;
            }
        }

        private static string ReadGzipFile(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            using (var sr = new StreamReader(gz, Encoding.UTF8))
            {
                return sr.ReadToEnd();
            }
        }

        public static void Put(string key, string json)
        {
            try
            {
                string dir = GetCacheDirectoryPath();
                Directory.CreateDirectory(dir);
                string path = GetFilePath(key);

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var gz = new GZipStream(fs, CompressionMode.Compress))
                using (var sw = new StreamWriter(gz, Encoding.UTF8))
                {
                    sw.Write(json);
                }

                PruneOldEntries(dir);
            }
            catch
            {
                // Ignore non-critical disk cache write errors
            }
        }

        public static void GetCacheStats(out int fileCount, out long totalBytes)
        {
            fileCount = 0;
            totalBytes = 0;
            try
            {
                string dir = GetCacheDirectoryPath();
                if (!Directory.Exists(dir))
                    return;

                var files = new DirectoryInfo(dir).GetFiles("*.json.gz");
                fileCount = files.Length;
                for (int i = 0; i < files.Length; i++)
                    totalBytes += files[i].Length;
            }
            catch
            {
            }
        }

        public static int ClearAll()
        {
            int count = 0;
            try
            {
                string dir = GetCacheDirectoryPath();
                if (!Directory.Exists(dir))
                    return 0;

                var files = new DirectoryInfo(dir).GetFiles("*.json.gz");
                foreach (var f in files)
                {
                    try
                    {
                        f.Delete();
                        count++;
                    }
                    catch { }
                }
            }
            catch
            {
            }
            return count;
        }

        private static void PruneOldEntries(string dir)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles("*.json.gz");
                DateTime cutoff = DateTime.UtcNow - OfflineRetentionTtl;
                foreach (var f in files)
                {
                    if (f.LastWriteTimeUtc < cutoff)
                        f.Delete();
                }

                files = new DirectoryInfo(dir).GetFiles("*.json.gz");
                if (files.Length > MaxCacheFiles)
                {
                    Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                    for (int i = 0; i < files.Length - MaxCacheFiles; i++)
                        files[i].Delete();
                }
            }
            catch
            {
            }
        }
    }
}
