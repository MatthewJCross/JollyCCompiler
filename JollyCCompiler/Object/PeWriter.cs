using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using JollyCCompiler.Compiler.CodeGen.X64;

namespace JollyCCompiler.Object
{
    public sealed class PeWriter
    {
        private const ulong ImageBase = 0x140000000;
        private const uint SectionAlignment = 0x1000;
        private const uint FileAlignment = 0x200;
        private const uint TextRva = 0x1000;
        private const uint RDataRva = 0x2000;
        private const uint IDataRva = 0x3000;
        private const ushort MachineAmd64 = 0x8664;
        private const ushort CharacteristicsExecutableImage = 0x0002;
        private const ushort CharacteristicsLargeAddressAware = 0x0020;
        private const ushort DllCharacteristicsDynamicBase = 0x0040;
        private const ushort DllCharacteristicsNxCompat = 0x0100;
        private const ushort DllCharacteristicsNoSeh = 0x0400;
        private const uint TextCharacteristics = 0x00000020 | 0x40000000 | 0x20000000;
        private const uint RDataCharacteristics = 0x00000040 | 0x40000000;
        private const uint IDataCharacteristics = 0x00000040 | 0x40000000 | 0x80000000;

        public void Write(string filePath, X64CodeGenerationResult result)
        {
            Write(filePath, result, PeSubsystem.Console);
        }

        public void Write(string filePath, X64CodeGenerationResult result, PeSubsystem subsystem)
        {
            if (result.MachineCode.Length == 0)
                throw new ArgumentException("Main function contains no generated code.", nameof(result));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            const int entryStubSize = 18; // 14;
            var mainCode = result.MachineCode;
            var text = new byte[AlignUp(entryStubSize + mainCode.Length, (int)FileAlignment)];
            var mainOffset = entryStubSize;
            Array.Copy(mainCode, 0, text, mainOffset, mainCode.Length);
            var rdata = BuildRDataSection(result.Data, out Dictionary<string, uint> dataRvas);
            var idata = BuildImportSection(subsystem, result.Imports, out Dictionary<string, uint> importIatRvas);

            uint exitProcessIatRva = importIatRvas["ExitProcess"];

            foreach (var import in importIatRvas)
            {
                Debug.WriteLine($"Import IAT: {import.Key} = 0x{import.Value:X8}");
            }

            uint entryRva = TextRva;
            uint mainRva = TextRva + (uint)mainOffset;

            text[0] = 0x48;
            text[1] = 0x83;
            text[2] = 0xE4;
            text[3] = 0xF0;

            text[4] = 0xE8;
            WriteInt32(text, 5, checked((int)(mainRva - (entryRva + 9))));

            text[9] = 0x89;
            text[10] = 0xC1;

            text[11] = 0xFF;
            text[12] = 0x15;

            uint exitNextInstructionRva = TextRva + 17;

            WriteInt32(text, 13, checked((int)(exitProcessIatRva - exitNextInstructionRva)));

            text[17] = 0xCC;            

            foreach (var fixup in result.Fixups)
            {
                Debug.WriteLine($"Fixup: {fixup.Kind} Symbol={fixup.Symbol} Offset=0x{fixup.Offset:X}");
            }

            foreach (var fixup in result.Fixups)
            {
                if (fixup.Kind == X64FixupKind.Relative32)
                {
                    if (!result.Labels.TryGetValue(fixup.Symbol, out int labelOffset))
                    {
                        throw new InvalidOperationException($"Unknown x64 label '{fixup.Symbol}'.");
                    }

                    long targetOffset = entryStubSize + labelOffset;
                    long nextInstructionOffset = entryStubSize + fixup.Offset + 4;
                    int displacement = checked((int)(targetOffset - nextInstructionOffset));
                    WriteInt32(text, entryStubSize + fixup.Offset, displacement);
                    continue;
                }

                if (fixup.Kind != X64FixupKind.RipRelative32)
                {
                    throw new NotSupportedException($"Unsupported x64 fixup kind '{fixup.Kind}'.");
                }

                uint targetRva;

                if (dataRvas.TryGetValue(fixup.Symbol, out uint dataRva))
                {
                    targetRva = dataRva;
                }
                else if (importIatRvas.TryGetValue(fixup.Symbol, out uint importIatRva))
                {
                    targetRva = importIatRva;
                }
                else
                {
                    throw new InvalidOperationException($"Unknown x64 fixup symbol '{fixup.Symbol}'.");
                }

                int instructionOffset = fixup.Offset;


                int absoluteFixupOffset = entryStubSize + instructionOffset;

                if (instructionOffset >= 3 && text[entryStubSize + instructionOffset - 3] == 0x48 && text[entryStubSize + instructionOffset - 2] == 0x8D)
                {
                    long instructionRva = TextRva + entryStubSize + instructionOffset - 3;
                    long nextInstructionRva = instructionRva + 7;
                    int ripDisplacement = checked((int)((long)targetRva - nextInstructionRva));
                    WriteInt32(text, entryStubSize + fixup.Offset, ripDisplacement);
                } 
                else if (instructionOffset >= 2 && text[entryStubSize + instructionOffset - 2] == 0xFF && text[entryStubSize + instructionOffset - 1] == 0x15)
                {
                    long instructionRva = TextRva + entryStubSize + instructionOffset - 2;
                    long nextInstructionRva = instructionRva + 6;
                    int ripDisplacement = checked((int)((long)targetRva - nextInstructionRva));
                    WriteInt32(text, entryStubSize + fixup.Offset, ripDisplacement);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported RIP-relative instruction at x64 offset 0x{instructionOffset:X}.");
                }
            }

            const int peHeaderOffset = 0x80;
            const int coffHeaderSize = 20;
            const int optionalHeaderSize = 240;
            const int sectionHeaderSize = 40;
            const int sectionCount = 3;
            int headersSize = peHeaderOffset + 4 + coffHeaderSize + optionalHeaderSize + sectionHeaderSize * sectionCount;
            headersSize = AlignUp(headersSize, (int)FileAlignment);
            uint textRawSize = (uint)AlignUp(text.Length, (int)FileAlignment);
            uint rdataRawSize = (uint)AlignUp(rdata.Length, (int)FileAlignment);
            uint idataRawSize = (uint)AlignUp(idata.Length, (int)FileAlignment);
            uint textVirtualSize = (uint)(entryStubSize + mainCode.Length);
            uint rdataVirtualSize = (uint)rdata.Length;
            uint idataVirtualSize = (uint)idata.Length;
            uint textRawPointer = (uint)headersSize;
            uint rdataRawPointer = textRawPointer + textRawSize;
            uint idataRawPointer = rdataRawPointer + rdataRawSize;
            uint sizeOfImage = IDataRva + AlignUp(idataVirtualSize, SectionAlignment);
            uint sizeOfHeaders = (uint)headersSize;
            var output = new byte[checked((int)(idataRawPointer + idataRawSize))];
            WriteUInt16(output, 0x00, 0x5A4D);
            WriteUInt32(output, 0x3C, (uint)peHeaderOffset);
            var dosMessage = Encoding.ASCII.GetBytes("This program cannot be run in DOS mode.\r\n$");
            Array.Copy(dosMessage, 0, output, 0x40, dosMessage.Length);
            WriteUInt32(output, peHeaderOffset, 0x00004550);
            int coffOffset = peHeaderOffset + 4;
            WriteUInt16(output, coffOffset + 0, MachineAmd64);
            WriteUInt16(output, coffOffset + 2, sectionCount);
            WriteUInt32(output, coffOffset + 4, 0);
            WriteUInt32(output, coffOffset + 8, 0);
            WriteUInt32(output, coffOffset + 12, 0);
            WriteUInt16(output, coffOffset + 16, optionalHeaderSize);
            WriteUInt16(output, coffOffset + 18, (ushort)(CharacteristicsExecutableImage | CharacteristicsLargeAddressAware));
            int optionalOffset = coffOffset + coffHeaderSize;
            WriteUInt16(output, optionalOffset + 0, 0x20B);
            output[optionalOffset + 2] = 14;
            output[optionalOffset + 3] = 0;
            WriteUInt32(output, optionalOffset + 4, textRawSize);
            WriteUInt32(output, optionalOffset + 8, rdataRawSize + idataRawSize);
            WriteUInt32(output, optionalOffset + 12, 0);
            WriteUInt32(output, optionalOffset + 16, entryRva);
            WriteUInt32(output, optionalOffset + 20, TextRva);
            WriteUInt64(output, optionalOffset + 24, ImageBase);
            WriteUInt32(output, optionalOffset + 32, SectionAlignment);
            WriteUInt32(output, optionalOffset + 36, FileAlignment);
            WriteUInt16(output, optionalOffset + 40, 6);
            WriteUInt16(output, optionalOffset + 42, 0);
            WriteUInt16(output, optionalOffset + 44, 0);
            WriteUInt16(output, optionalOffset + 46, 0);
            WriteUInt16(output, optionalOffset + 48, 6);
            WriteUInt16(output, optionalOffset + 50, 0);
            WriteUInt32(output, optionalOffset + 52, 0);
            WriteUInt32(output, optionalOffset + 56, sizeOfImage);
            WriteUInt32(output, optionalOffset + 60, sizeOfHeaders);
            WriteUInt32(output, optionalOffset + 64, 0);
            WriteUInt16(output, optionalOffset + 68, (ushort)subsystem);
            WriteUInt16(output, optionalOffset + 70, DllCharacteristicsDynamicBase | DllCharacteristicsNxCompat | DllCharacteristicsNoSeh);
            WriteUInt64(output, optionalOffset + 72, 0x100000);
            WriteUInt64(output, optionalOffset + 80, 0x1000);
            WriteUInt64(output, optionalOffset + 88, 0x100000);
            WriteUInt64(output, optionalOffset + 96, 0x1000);
            WriteUInt32(output, optionalOffset + 104, 0);
            WriteUInt32(output, optionalOffset + 108, 16);
            int importDirectoryOffset = optionalOffset + 112 + (1 * 8);
            WriteUInt32(output, importDirectoryOffset, IDataRva);
            WriteUInt32(output, importDirectoryOffset + 4, 60);
            int sectionOffset = optionalOffset + optionalHeaderSize;
            WriteSectionHeader(output, sectionOffset, ".text", textVirtualSize, TextRva, textRawSize, textRawPointer, TextCharacteristics);
            WriteSectionHeader(output, sectionOffset + sectionHeaderSize, ".rdata", rdataVirtualSize, RDataRva, rdataRawSize, rdataRawPointer, RDataCharacteristics);
            WriteSectionHeader(output, sectionOffset + sectionHeaderSize * 2, ".idata", idataVirtualSize, IDataRva, idataRawSize, idataRawPointer, IDataCharacteristics);
            Array.Copy(text, 0, output, (int)textRawPointer, text.Length);
            Array.Copy(rdata, 0, output, (int)rdataRawPointer, rdata.Length);
            Array.Copy(idata, 0, output, (int)idataRawPointer, idata.Length);
            File.WriteAllBytes(filePath, output);

            int textFileOffset = checked((int)textRawPointer);
            var finalBytes = File.ReadAllBytes(filePath);

            int disp = BitConverter.ToInt32(finalBytes, textFileOffset + 0x8A);

            long nextRva = 0x108E;
            long targRva = nextRva + disp;
        }

        private static byte[] BuildRDataSection(IReadOnlyList<X64DataItem> items, out Dictionary<string, uint> symbolRvas)
        {
            symbolRvas = new Dictionary<string, uint>(StringComparer.Ordinal);
            int totalSize = 0;
            foreach (var item in items)
            {
                symbolRvas[item.Symbol] = RDataRva + (uint)totalSize;
                totalSize += item.Data.Length;
            }

            totalSize = AlignUp(totalSize, (int)FileAlignment);
            var data = new byte[totalSize];
            int offset = 0;
            foreach (var item in items)
            {
                Array.Copy(item.Data, 0, data, offset, item.Data.Length);
                offset += item.Data.Length;
            }

            return data;
        }

        private static byte[] BuildImportSection(PeSubsystem subsystem, IReadOnlyList<X64Import> requestedImports, out Dictionary<string, uint> importIatRvas)
        {
            var imports = new List<X64Import>();

            if (subsystem == PeSubsystem.Windows)
            {
                AddImport(imports, "kernel32.dll", "ExitProcess");
            }
            else
            {
                AddImport(imports, "kernel32.dll", "ExitProcess");
            }

            foreach (var import in requestedImports)
            {
                AddImport(imports, import.DllName, import.FunctionName);
            }

            importIatRvas = new Dictionary<string, uint>(StringComparer.Ordinal);

            var groupedImports = imports.GroupBy(import => import.DllName, StringComparer.OrdinalIgnoreCase).ToList();

            const int descriptorSize = 20;
            int descriptorCount = groupedImports.Count + 1;

            int descriptorOffset = 0;
            int lookupTableOffset = descriptorOffset + descriptorSize * descriptorCount;

            int lookupTableSize = 0;

            foreach (var group in groupedImports)
            {
                lookupTableSize += (group.Count() + 1) * 8;
            }

            int addressTableOffset = lookupTableOffset + lookupTableSize;
            int addressTableSize = lookupTableSize;

            int hintNameOffset = addressTableOffset + addressTableSize;

            var hintNameOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
            var dllNameOffsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var import in imports)
            {
                string key = import.DllName + "\0" + import.FunctionName;

                if (!hintNameOffsets.ContainsKey(key))
                {
                    hintNameOffsets[key] = hintNameOffset;

                    hintNameOffset += 2;
                    hintNameOffset += Encoding.ASCII.GetByteCount(import.FunctionName) + 1;
                }
            }

            foreach (var group in groupedImports)
            {
                dllNameOffsets[group.Key] = hintNameOffset;
                hintNameOffset += Encoding.ASCII.GetByteCount(group.Key) + 1;
            }

            int rawSize = AlignUp(hintNameOffset, (int)FileAlignment);
            var data = new byte[rawSize];

            int lookupOffset = lookupTableOffset;
            int addressOffset = addressTableOffset;
            int descriptorIndex = 0;

            foreach (var group in groupedImports)
            {
                var groupImports = group.ToList();

                uint lookupRva = IDataRva + (uint)lookupOffset;
                uint addressRva = IDataRva + (uint)addressOffset;
                uint dllNameRva = IDataRva + (uint)dllNameOffsets[group.Key];

                int descriptorOffsetForGroup = descriptorOffset + descriptorIndex * descriptorSize;

                WriteUInt32(data, descriptorOffsetForGroup + 0, lookupRva);
                WriteUInt32(data, descriptorOffsetForGroup + 4, 0);
                WriteUInt32(data, descriptorOffsetForGroup + 8, 0);
                WriteUInt32(data, descriptorOffsetForGroup + 12, dllNameRva);
                WriteUInt32(data, descriptorOffsetForGroup + 16, addressRva);

                for (int index = 0; index < groupImports.Count; index++)
                {
                    var import = groupImports[index];

                    string hintNameKey = import.DllName + "\0" + import.FunctionName;
                    uint hintNameRva = IDataRva + (uint)hintNameOffsets[hintNameKey];

                    WriteUInt64(data, lookupOffset + index * 8, hintNameRva);
                    WriteUInt64( data, addressOffset + index * 8, hintNameRva);

                    uint iatRva = IDataRva + (uint)(addressOffset + index * 8);

                    if (importIatRvas.ContainsKey(import.FunctionName))
                    {
                        throw new InvalidOperationException($"Duplicate imported function name '{import.FunctionName}'.");
                    }

                    importIatRvas[import.FunctionName] = iatRva;
                }

                WriteUInt64(data, lookupOffset + groupImports.Count * 8, 0);
                WriteUInt64(data, addressOffset + groupImports.Count * 8, 0);

                lookupOffset += (groupImports.Count + 1) * 8;
                addressOffset += (groupImports.Count + 1) * 8;

                descriptorIndex++;
            }

            foreach (var import in imports)
            {
                string key = import.DllName + "\0" + import.FunctionName;
                int offset = hintNameOffsets[key];

                WriteUInt16(data, offset, 0);
                var name = Encoding.ASCII.GetBytes(import.FunctionName + "\0");
                Array.Copy(name, 0, data, offset + 2, name.Length);
            }

            foreach (var dllName in dllNameOffsets)
            {
                var name = Encoding.ASCII.GetBytes(dllName.Key + "\0");
                Array.Copy(name, 0, data, dllName.Value, name.Length);
            }

            return data;
        }

        private static void AddImport(List<X64Import> imports, string dllName, string functionName)
        {
            if (imports.Any(import => string.Equals(import.DllName, dllName, StringComparison.OrdinalIgnoreCase) && string.Equals(import.FunctionName, functionName, StringComparison.Ordinal)))
            {
                return;
            }

            imports.Add(new X64Import(dllName, functionName));
        }

        private static void WriteSectionHeader(byte[] output, int offset, string name, uint virtualSize, uint virtualAddress, uint rawSize, uint rawPointer, uint characteristics)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name);
            Array.Copy(nameBytes, 0, output, offset, Math.Min(nameBytes.Length, 8));
            WriteUInt32(output, offset + 8, virtualSize);
            WriteUInt32(output, offset + 12, virtualAddress);
            WriteUInt32(output, offset + 16, rawSize);
            WriteUInt32(output, offset + 20, rawPointer);
            WriteUInt32(output, offset + 24, 0);
            WriteUInt32(output, offset + 28, 0);
            WriteUInt16(output, offset + 32, 0);
            WriteUInt16(output, offset + 34, 0);
            WriteUInt32(output, offset + 36, characteristics);
        }

        private static int AlignUp(int value, int alignment)
        {
            return checked((value + alignment - 1) / alignment * alignment);
        }

        private static uint AlignUp(uint value, uint alignment)
        {
            return checked((value + alignment - 1) / alignment * alignment);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), value);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset, 8), value);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), value);
        }
    }
}