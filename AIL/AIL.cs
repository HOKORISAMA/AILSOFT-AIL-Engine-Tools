using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AIL
{
    internal static class FileSignatures
    {
        private static readonly Dictionary<uint, string> Extensions = new()
        {
            { 0x4D42, "bmp" },
            { 0x474E5089, "png" },
            { 0xE0FFD8FF, "jpg" },
            { 0x002B4949, "tif" },
            { 0x53504B47, "ogg" },
            { 0x46464952, "wav" },
            { 0x5367674F, "ogg" },
            { 0x4D524F46, "rm" },
            { 0xBA010000, "mpg" },
        };

        public static string Detect(uint signature) =>
            Extensions.TryGetValue(signature, out string ext) ? ext :
            Extensions.TryGetValue(signature & 0xFFFF, out ext) ? ext : "";

        // Added reverse lookup for packing
        public static uint? GetSignature(string extension) =>
            Extensions.FirstOrDefault(x => x.Value.Equals(extension.ToLower().TrimStart('.'))).Key;
    }

    internal class AilArchive
    {
        private class Entry
        {
            public string Name { get; set; }
            public uint Size { get; set; }
            public uint UnpackedSize { get; set; }
            public long Offset { get; set; }
            public bool IsPacked { get; set; }

            public void UpdateExtension(string extension) =>
                Name = Path.ChangeExtension(Name, extension);
        }

        // Added internal to match Entry class scope
        private class PackEntry
        {
            public int Index { get; set; }
            public string FilePath { get; set; }
            public bool IsPlaceholder { get; set; }
        }

        public void Pack(string[] files, string outputPath, bool compress = true)
        {
            if (files == null || files.Length == 0)
                throw new ArgumentException("No files provided for packing");

            // Parse file indices and create pack entries
            var entries = PreparePackEntries(files);
            int maxIndex = entries.Max(e => e.Index);

            using var fs = File.Create(outputPath);
            using var writer = new BinaryWriter(fs);

            // Write number of files (including placeholders)
            writer.Write((uint)(maxIndex + 1));

            // First pass: calculate and write sizes
            long headerSize = 4 + ((maxIndex + 1) * 4); // 4 bytes for count + 4 bytes per file size
            long currentOffset = headerSize;

            // Write size table with placeholders
            for (int i = 0; i <= maxIndex; i++)
            {
                var entry = entries.FirstOrDefault(e => e.Index == i);
                if (entry == null || entry.IsPlaceholder)
                {
                    writer.Write((uint)0); // Placeholder entry
                    continue;
                }

                byte[] fileData = File.ReadAllBytes(entry.FilePath);
                uint entrySize;

                if (compress && fileData.Length > 8)
                {
                    byte[] compressed = Lzss.Compress(fileData);
                    entrySize = (uint)(compressed.Length < fileData.Length ? 
                        compressed.Length + 6 : // Compressed: data + 6 byte header
                        fileData.Length + 4);   // Uncompressed: data + 4 byte header
                }
                else
                {
                    entrySize = (uint)(fileData.Length + 4); // Uncompressed: data + 4 byte header
                }

                writer.Write(entrySize);
            }

            // Second pass: write file data
            for (int i = 0; i <= maxIndex; i++)
            {
                var entry = entries.FirstOrDefault(e => e.Index == i);
                if (entry == null || entry.IsPlaceholder)
                    continue;

                byte[] fileData = File.ReadAllBytes(entry.FilePath);
                if (compress && fileData.Length > 8)
                {
                    byte[] compressed = Lzss.Compress(fileData);
                    if (compressed.Length < fileData.Length)
                    {
                        // Write compressed data with header
                        writer.Write((ushort)1); // Compression flag
                        writer.Write((uint)fileData.Length); // Uncompressed size
                        writer.Write(compressed);
                    }
                    else
                    {
                        // Write uncompressed with header
                        writer.Write((uint)0); // No compression flag
                        writer.Write(fileData);
                    }
                }
                else
                {
                    // Write uncompressed with header
                    writer.Write((uint)0); // No compression flag
                    writer.Write(fileData);
                }
            }
        }

        private List<PackEntry> PreparePackEntries(string[] files)
        {
            var entries = new List<PackEntry>();
            var regex = new Regex(@"#(\d{5})", RegexOptions.IgnoreCase);

            foreach (var file in files)
            {
                var match = regex.Match(Path.GetFileNameWithoutExtension(file));
                if (!match.Success)
                {
                    throw new ArgumentException($"Invalid filename format: {file}. Expected format: basename#XXXXX.ext where XXXXX is a 5-digit number");
                }

                int index = int.Parse(match.Groups[1].Value);
                entries.Add(new PackEntry 
                { 
                    Index = index,
                    FilePath = file,
                    IsPlaceholder = false
                });
            }

            // Sort entries by index
            entries.Sort((a, b) => a.Index.CompareTo(b.Index));

            // Add placeholder entries for missing indices
            int maxIndex = entries.Max(e => e.Index);
            var missingIndices = Enumerable.Range(0, maxIndex + 1)
                                         .Except(entries.Select(e => e.Index));

            foreach (var index in missingIndices)
            {
                entries.Add(new PackEntry
                {
                    Index = index,
                    FilePath = null,
                    IsPlaceholder = true
                });
            }

            entries.Sort((a, b) => a.Index.CompareTo(b.Index));
            return entries;
        }

        public void Unpack(string filepath, string outputDir)
        {
            using var fs = File.OpenRead(filepath);
            using var reader = new BinaryReader(fs);

            var entries = ReadEntries(reader, filepath);
            ProcessEntries(fs, entries);
            ExtractFiles(fs, entries, outputDir);
        }

        private List<Entry> ReadEntries(BinaryReader reader, string filepath)
        {
            uint count = reader.ReadUInt32();
            if (count == 0 || count > 0xffff)
                throw new InvalidDataException($"Invalid file count: {count}");

            var entries = new List<Entry>();
            string baseName = Path.GetFileNameWithoutExtension(filepath);
            long offset = 4 + count * 4;

            for (int i = 0; i < count; i++)
            {
                uint size = reader.ReadUInt32();
                if (size == 0 || size == uint.MaxValue) 
                    continue;

                var entry = new Entry
                {
                    Name = $"{baseName}#{i:D5}",
                    Offset = offset,
                    Size = size
                };

                if (offset + size > reader.BaseStream.Length)
                    throw new InvalidDataException($"Invalid entry size at index {i}");

                entries.Add(entry);
                offset += size;
            }

            if (entries.Count == 0 || (reader.BaseStream.Length - offset) > 0x80000)
                throw new InvalidDataException("Invalid archive structure");

            return entries;
        }

        private void ProcessEntries(FileStream fs, List<Entry> entries)
        {
            foreach (var entry in entries)
            {
                fs.Position = entry.Offset;
                uint signature = new BinaryReader(fs).ReadUInt32();
                uint headerSize = 6;

                if ((signature & 0xFFFF) == 1)
                {
                    entry.IsPacked = true;
                    fs.Position = entry.Offset + 2;
                    entry.UnpackedSize = new BinaryReader(fs).ReadUInt32();
                }
                else if (signature == 0 || IsOggFile(fs, entry.Offset + 4))
                {
                    headerSize = 4;
                }

                entry.Offset += headerSize;
                entry.Size -= headerSize;

                uint fileSignature = GetSignature(fs, entry);
                if (fileSignature != 0)
                    entry.UpdateExtension(FileSignatures.Detect(fileSignature));
            }
        }

        private bool IsOggFile(FileStream fs, long position)
        {
            fs.Position = position;
            byte[] signature = new byte[4];
            fs.Read(signature, 0, 4);
            return System.Text.Encoding.ASCII.GetString(signature) == "OggS";
        }

        private uint GetSignature(FileStream fs, Entry entry)
        {
            fs.Position = entry.Offset;
            if (!entry.IsPacked)
                return new BinaryReader(fs).ReadUInt32();

            byte[] preview = new byte[16];
            fs.Read(preview, 0, preview.Length);
            return BitConverter.ToUInt32(Lzss.Decompress(preview), 0);
        }

        private void ExtractFiles(FileStream fs, List<Entry> entries, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            byte[] buffer = new byte[32768];

            foreach (var entry in entries)
            {
                string outputPath = Path.Combine(outputDir, entry.Name);
                using var output = File.Create(outputPath);

                fs.Position = entry.Offset;
                if (!entry.IsPacked)
                {
                    var remaining = entry.Size;
                    while (remaining > 0)
                    {
                        var toRead = (int)Math.Min(remaining, buffer.Length);
                        var read = fs.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        output.Write(buffer, 0, read);
                        remaining -= (uint)read;
                    }
                    continue;
                }

                byte[] packedData = new byte[entry.Size];
                fs.Read(packedData, 0, (int)entry.Size);
                byte[] unpackedData = Lzss.Decompress(packedData);

                if (unpackedData.Length != entry.UnpackedSize)
                    throw new InvalidDataException($"Size mismatch in {entry.Name}: Expected {entry.UnpackedSize}, got {unpackedData.Length}");

                output.Write(unpackedData);
            }
        }
    }
}
