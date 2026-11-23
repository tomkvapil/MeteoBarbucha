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
        private const int MaxCacheFiles = 30;

        private static string GetCacheDir()
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
                return Path.Combine(GetCacheDir(), sb.ToString() + ".json.gz");
            }
        }

        public static string TryGet(string key)
        {
            try
            {
                string path = GetFilePath(key);
                if (!File.Exists(path))
                    return null;

                var info = new FileInfo(path);
                if (DateTime.UtcNow - info.LastWriteTimeUtc > DefaultTtl)
                {
                    File.Delete(path);
                    return null;
                }

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var sr = new StreamReader(gz, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        public static void Put(string key, string json)
        {
            try
            {
                string dir = GetCacheDir();
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

        private static void PruneOldEntries(string dir)
        {
            try
            {
                var files = new DirectoryInfo(dir).GetFiles("*.json.gz");
                DateTime cutoff = DateTime.UtcNow - DefaultTtl;
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
