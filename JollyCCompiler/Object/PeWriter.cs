using System.IO;
using System.Text;
using System.Buffers.Binary;
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
        private const ushort SubsystemWindowsCui = 3;
        private const ushort DllCharacteristicsDynamicBase = 0x0040;
        private const ushort DllCharacteristicsNxCompat = 0x0100;
        private const ushort DllCharacteristicsNoSeh = 0x0400;
        private const uint TextCharacteristics = 0x00000020 | 0x40000000 | 0x20000000;
        private const uint RDataCharacteristics = 0x00000040 | 0x40000000;
        private const uint IDataCharacteristics = 0x00000040 | 0x40000000 | 0x80000000;

        public void Write(string filePath, X64CodeGenerationResult result)
        {
            if (result.MachineCode.Length == 0) throw new ArgumentException("Main function contains no generated code.", nameof(result));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);

            const int entryStubSize = 14;
            var mainCode = result.MachineCode;
            var text = new byte[AlignUp(entryStubSize + mainCode.Length, (int)FileAlignment)];
            var mainOffset = entryStubSize;

            Array.Copy(mainCode, 0, text, mainOffset, mainCode.Length);

            var rdata = BuildRDataSection(result.Data, out Dictionary<string, uint> dataRvas);
            var idata = BuildImportSection(out uint printfIatRva, out uint exitProcessIatRva);

            uint entryRva = TextRva;
            uint mainRva = TextRva + (uint)mainOffset;

            text[0] = 0xE8;
            WriteInt32(text, 1, checked((int)(mainRva - (entryRva + 5))));

            text[5] = 0x89;
            text[6] = 0xC1;

            text[7] = 0xFF;
            text[8] = 0x15;

            uint exitNextInstructionRva = TextRva + 13;
            WriteInt32(text, 9, checked((int)(exitProcessIatRva - exitNextInstructionRva)));

            text[13] = 0xCC;

            foreach (var fixup in result.Fixups)
            {
                uint instructionRva = TextRva + (uint)entryStubSize + (uint)fixup.Offset;
                uint nextInstructionRva = instructionRva + 4;

                if (fixup.Kind == X64FixupKind.RipRelative32)
                {
                    uint targetRva;

                    if (dataRvas.TryGetValue(fixup.Symbol, out uint dataRva))
                    {
                        targetRva = dataRva;
                    }
                    else if (string.Equals(fixup.Symbol, "printf", StringComparison.Ordinal))
                    {
                        targetRva = printfIatRva;
                    }
                    else if (string.Equals(fixup.Symbol, "ExitProcess", StringComparison.Ordinal))
                    {
                        targetRva = exitProcessIatRva;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unknown x64 RIP-relative fixup symbol '{fixup.Symbol}'.");
                    }

                    int displacement = checked((int)((long)targetRva - nextInstructionRva));

                    WriteInt32(text, entryStubSize + fixup.Offset, displacement);
                }
                else if (fixup.Kind == X64FixupKind.Relative32)
                {
                    if (!result.Labels.TryGetValue(fixup.Symbol, out int targetOffset))
                    {
                        throw new InvalidOperationException($"Unknown x64 relative label '{fixup.Symbol}'.");
                    }

                    uint targetLabelRva = TextRva + (uint)entryStubSize + (uint)targetOffset;
                    int displacement = checked((int)((long)targetLabelRva - nextInstructionRva));
                    WriteInt32(text, entryStubSize + fixup.Offset, displacement);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported x64 fixup kind '{fixup.Kind}'.");
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

            WriteUInt16(output, optionalOffset + 68, SubsystemWindowsCui);
            WriteUInt16(output, optionalOffset + 70, DllCharacteristicsDynamicBase | DllCharacteristicsNxCompat | DllCharacteristicsNoSeh);

            WriteUInt64(output, optionalOffset + 72, 0x100000);
            WriteUInt64(output, optionalOffset + 80, 0x1000);
            WriteUInt64(output, optionalOffset + 88, 0x100000);
            WriteUInt64(output, optionalOffset + 96, 0x1000);

            WriteUInt32(output, optionalOffset + 104, 0);
            WriteUInt32(output, optionalOffset + 108, 16);

            int importDirectoryOffset = optionalOffset + 112 + (1 * 8);
            WriteUInt32(output, importDirectoryOffset, IDataRva);
            WriteUInt32(output, importDirectoryOffset + 4, 40);

            int sectionOffset = optionalOffset + optionalHeaderSize;

            WriteSectionHeader(output, sectionOffset, ".text", textVirtualSize, TextRva, textRawSize, textRawPointer, TextCharacteristics);
            WriteSectionHeader(output, sectionOffset + sectionHeaderSize, ".rdata", rdataVirtualSize, RDataRva, rdataRawSize, rdataRawPointer, RDataCharacteristics);
            WriteSectionHeader(output, sectionOffset + sectionHeaderSize * 2, ".idata", idataVirtualSize, IDataRva, idataRawSize, idataRawPointer, IDataCharacteristics);

            Array.Copy(text, 0, output, (int)textRawPointer, text.Length);
            Array.Copy(rdata, 0, output, (int)rdataRawPointer, rdata.Length);
            Array.Copy(idata, 0, output, (int)idataRawPointer, idata.Length);

            File.WriteAllBytes(filePath, output);
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

        private static byte[] BuildImportSection(out uint printfIatRva, out uint exitProcessIatRva)
        {
            const int descriptorSize = 20;
            const int descriptorCount = 3;

            const int descriptorOffset = 0;
            const int lookupTableOffset = descriptorSize * descriptorCount;
            const int lookupTableSize = 32;
            const int addressTableOffset = lookupTableOffset + lookupTableSize;
            const int addressTableSize = 32;
            const int printfHintNameOffset = addressTableOffset + addressTableSize;
            const int exitProcessHintNameOffset = printfHintNameOffset + 10;
            const int printfDllNameOffset = exitProcessHintNameOffset + 14;
            const int kernel32DllNameOffset = printfDllNameOffset + 11;

            int size = AlignUp(kernel32DllNameOffset + 13, (int)FileAlignment);
            var data = new byte[size];

            uint lookupBaseRva = IDataRva + (uint)lookupTableOffset;
            uint addressBaseRva = IDataRva + (uint)addressTableOffset;
            uint printfHintNameRva = IDataRva + (uint)printfHintNameOffset;
            uint exitProcessHintNameRva = IDataRva + (uint)exitProcessHintNameOffset;
            uint printfDllNameRva = IDataRva + (uint)printfDllNameOffset;
            uint kernel32DllNameRva = IDataRva + (uint)kernel32DllNameOffset;

            uint printfLookupRva = lookupBaseRva;
            uint exitLookupRva = lookupBaseRva + 16;
            uint printfAddressRva = addressBaseRva;
            uint exitAddressRva = addressBaseRva + 16;

            printfIatRva = printfAddressRva;
            exitProcessIatRva = exitAddressRva;

            WriteUInt32(data, descriptorOffset + 0, printfLookupRva);
            WriteUInt32(data, descriptorOffset + 4, 0);
            WriteUInt32(data, descriptorOffset + 8, 0);
            WriteUInt32(data, descriptorOffset + 12, printfDllNameRva);
            WriteUInt32(data, descriptorOffset + 16, printfAddressRva);

            int kernel32DescriptorOffset = descriptorSize;

            WriteUInt32(data, kernel32DescriptorOffset + 0, exitLookupRva);
            WriteUInt32(data, kernel32DescriptorOffset + 4, 0);
            WriteUInt32(data, kernel32DescriptorOffset + 8, 0);
            WriteUInt32(data, kernel32DescriptorOffset + 12, kernel32DllNameRva);
            WriteUInt32(data, kernel32DescriptorOffset + 16, exitAddressRva);

            WriteUInt64(data, lookupTableOffset, printfHintNameRva);
            WriteUInt64(data, lookupTableOffset + 8, 0);
            WriteUInt64(data, lookupTableOffset + 16, exitProcessHintNameRva);
            WriteUInt64(data, lookupTableOffset + 24, 0);

            WriteUInt64(data, addressTableOffset, printfHintNameRva);
            WriteUInt64(data, addressTableOffset + 8, 0);
            WriteUInt64(data, addressTableOffset + 16, exitProcessHintNameRva);
            WriteUInt64(data, addressTableOffset + 24, 0);

            WriteUInt16(data, printfHintNameOffset, 0);

            var printfName = Encoding.ASCII.GetBytes("printf\0");
            Array.Copy(printfName, 0, data, printfHintNameOffset + 2, printfName.Length);

            WriteUInt16(data, exitProcessHintNameOffset, 0);

            var exitProcessName = Encoding.ASCII.GetBytes("ExitProcess\0");
            Array.Copy(exitProcessName, 0, data, exitProcessHintNameOffset + 2, exitProcessName.Length);

            var printfDll = Encoding.ASCII.GetBytes("msvcrt.dll\0");
            Array.Copy(printfDll, 0, data, printfDllNameOffset, printfDll.Length);

            var kernel32Dll = Encoding.ASCII.GetBytes("kernel32.dll\0");
            Array.Copy(kernel32Dll, 0, data, kernel32DllNameOffset, kernel32Dll.Length);

            return data;
        }

        private static void WriteSectionHeader(byte[] data, int offset, string name, uint virtualSize, uint virtualAddress, uint rawSize, uint rawPointer, uint characteristics)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name);
            Array.Copy(nameBytes, 0, data, offset, Math.Min(nameBytes.Length, 8));

            WriteUInt32(data, offset + 8, virtualSize);
            WriteUInt32(data, offset + 12, virtualAddress);
            WriteUInt32(data, offset + 16, rawSize);
            WriteUInt32(data, offset + 20, rawPointer);
            WriteUInt32(data, offset + 24, 0);
            WriteUInt32(data, offset + 28, 0);
            WriteUInt16(data, offset + 32, 0);
            WriteUInt16(data, offset + 34, 0);
            WriteUInt32(data, offset + 36, characteristics);
        }

        private static int AlignUp(int value, int alignment)
        {
            return checked((value + alignment - 1) / alignment * alignment);
        }

        private static uint AlignUp(uint value, uint alignment)
        {
            return checked((value + alignment - 1) / alignment * alignment);
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
        }

        private static void WriteUInt64(byte[] data, int offset, ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, 8), value);
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
        }
    }
}
