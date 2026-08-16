using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EcloudLite.Infrastructure
{
    internal static class PeDependencyResolver
    {
        private sealed class Section
        {
            public uint VirtualAddress;
            public uint VirtualSize;
            public uint RawOffset;
            public uint RawSize;
        }

        public static List<string> Resolve(string root, IEnumerable<string> entries, Action<string> progress)
        {
            string fullRoot = Path.GetFullPath(root);
            Dictionary<string, string> localFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] files = Directory.GetFiles(fullRoot, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string extension = Path.GetExtension(files[i]);
                if (!extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileName(files[i]);
                if (!localFiles.ContainsKey(name)) localFiles[name] = files[i];
            }

            Queue<string> pending = new Queue<string>();
            foreach (string entry in entries)
                if (!string.IsNullOrEmpty(entry) && File.Exists(entry)) pending.Enqueue(Path.GetFullPath(entry));

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> resolvedFiles = new List<string>();
            while (pending.Count > 0)
            {
                string file = pending.Dequeue();
                if (!visited.Add(file)) continue;
                List<string> imports;
                try { imports = ReadImports(file); }
                catch (Exception exception)
                {
                    if (progress != null) progress("PE 解析跳过 " + Relative(fullRoot, file) + "：" + exception.Message);
                    continue;
                }
                resolvedFiles.Add(file);
                for (int i = 0; i < imports.Count; i++)
                {
                    string dependency;
                    if (localFiles.TryGetValue(imports[i], out dependency)) pending.Enqueue(dependency);
                }
            }
            return resolvedFiles;
        }

        private static List<string> ReadImports(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII))
            {
                if (reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("缺少 MZ 标记");
                stream.Position = 0x3c;
                uint peOffset = reader.ReadUInt32();
                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550) throw new InvalidDataException("缺少 PE 标记");
                reader.ReadUInt16();
                ushort sectionCount = reader.ReadUInt16();
                stream.Position += 12;
                ushort optionalSize = reader.ReadUInt16();
                stream.Position += 2;
                long optionalOffset = stream.Position;
                ushort magic = reader.ReadUInt16();
                int dataDirectoryOffset = magic == 0x20b ? 112 : magic == 0x10b ? 96 : -1;
                if (dataDirectoryOffset < 0) throw new InvalidDataException("未知 PE 可选头");
                stream.Position = optionalOffset + dataDirectoryOffset + 8;
                uint importRva = reader.ReadUInt32();
                uint importSize = reader.ReadUInt32();

                stream.Position = optionalOffset + optionalSize;
                List<Section> sections = new List<Section>();
                for (int i = 0; i < sectionCount; i++)
                {
                    stream.Position += 8;
                    uint virtualSize = reader.ReadUInt32();
                    uint virtualAddress = reader.ReadUInt32();
                    uint rawSize = reader.ReadUInt32();
                    uint rawOffset = reader.ReadUInt32();
                    stream.Position += 16;
                    sections.Add(new Section
                    {
                        VirtualAddress = virtualAddress,
                        VirtualSize = virtualSize,
                        RawOffset = rawOffset,
                        RawSize = rawSize
                    });
                }

                List<string> result = new List<string>();
                if (importRva == 0 || importSize == 0) return result;
                long descriptorOffset = RvaToOffset(importRva, sections);
                for (int i = 0; i < 4096; i++)
                {
                    stream.Position = descriptorOffset + i * 20L;
                    uint originalFirstThunk = reader.ReadUInt32();
                    uint timeDateStamp = reader.ReadUInt32();
                    uint forwarderChain = reader.ReadUInt32();
                    uint nameRva = reader.ReadUInt32();
                    uint firstThunk = reader.ReadUInt32();
                    if (originalFirstThunk == 0 && timeDateStamp == 0 && forwarderChain == 0 && nameRva == 0 && firstThunk == 0) break;
                    long returnPosition = stream.Position;
                    stream.Position = RvaToOffset(nameRva, sections);
                    string name = ReadAsciiZ(reader);
                    if (!string.IsNullOrEmpty(name)) result.Add(name);
                    stream.Position = returnPosition;
                }
                return result;
            }
        }

        private static long RvaToOffset(uint rva, List<Section> sections)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                Section section = sections[i];
                uint span = Math.Max(section.VirtualSize, section.RawSize);
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + span)
                    return section.RawOffset + (rva - section.VirtualAddress);
            }
            throw new InvalidDataException("RVA 不在 PE section 中：0x" + rva.ToString("x"));
        }

        private static string ReadAsciiZ(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            for (int i = 0; i < 4096; i++)
            {
                byte value = reader.ReadByte();
                if (value == 0) break;
                bytes.Add(value);
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        internal static string Relative(string root, string path)
        {
            Uri rootUri = new Uri(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(path)).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
