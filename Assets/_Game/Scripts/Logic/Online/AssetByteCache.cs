using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>Content-addressed verified bytes. Failed writes never replace a working revision.</summary>
    public sealed class AssetByteCache
    {
        public const int MaxBytes = 1048576;
        private readonly string _root;
        public AssetByteCache(string root) { _root = root ?? throw new ArgumentNullException(nameof(root)); Directory.CreateDirectory(root); }
        public static string Digest(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private string PathFor(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{64}$")) throw new ArgumentException("Invalid asset checksum.");
            return Path.Combine(_root, id + ".png");
        }
        public byte[] Read(string id, int size)
        {
            var path = PathFor(id);
            if (size < 1 || size > MaxBytes || !File.Exists(path) || new FileInfo(path).Length != size) return null;
            var bytes = File.ReadAllBytes(path);
            return Digest(bytes) == id ? bytes : null;
        }
        public void Save(string id, int size, byte[] bytes)
        {
            var path = PathFor(id);
            if (bytes == null || bytes.Length != size || size < 1 || size > MaxBytes || Digest(bytes) != id)
                throw new InvalidDataException("Asset checksum or size mismatch.");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path))
                {
                    if (Read(id, size) != null) return;
                    File.Replace(temporary, path, null);
                }
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        /// <summary>Retain active/previous media; bound abandoned downloads without removing referenced bytes.</summary>
        public void Prune(System.Collections.Generic.ISet<string> retained, long budget = 67108864)
        {
            var files = new DirectoryInfo(_root).GetFiles("*.png");
            Array.Sort(files, (a,b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            long total = 0; foreach(var file in files) total += file.Length;
            foreach(var file in files) if(total > budget && !retained.Contains(Path.GetFileNameWithoutExtension(file.Name))) { total -= file.Length; file.Delete(); }
        }
    }
}
