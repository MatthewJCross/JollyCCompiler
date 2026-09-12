using JollyCCompiler.Compiler.CodeGen.X64;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

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
        private const uint RDataCharacteristics = 0x00000040 | 0x40000000 | 0x80000000;
        private const uint IDataCharacteristics = 0x00000040 | 0x40000000 | 0x80000000;
        private const uint RelocCharacteristics = 0x42000040;

        public void Write(string filePath, X64CodeGenerationResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            if (result.MachineCode == null || result.MachineCode.Length == 0)
                throw new InvalidOperationException("Generated machine code is empty.");

            string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            byte[] startup = BuildStartup(0, 0, 0, 0, 0, out int mainCallDispOffset);
            int entryStubSize = startup.Length;
            uint mainRva = TextRva + (uint)entryStubSize;
            PatchRelative32(startup, mainCallDispOffset, TextRva + (uint)(mainCallDispOffset + 4), mainRva);
            byte[] text = new byte[ AlignUp(entryStubSize + result.MachineCode.Length, (int)FileAlignment)];
            Buffer.BlockCopy(startup, 0, text, 0, startup.Length);
            Buffer.BlockCopy(result.MachineCode, 0, text, entryStubSize, result.MachineCode.Length);
            uint textVirtualSize = (uint)text.Length;
            uint rdataRva = AlignUp(TextRva + textVirtualSize, SectionAlignment);
            byte[] rdata = BuildRDataSection(result, rdataRva, out Dictionary<string, uint> dataSymbolRvas, out List<uint> baseRelocationRvas);
            Debug.WriteLine($"TEXT RVA: 0x{TextRva:X}");
            Debug.WriteLine($"TEXT SIZE: 0x{text.Length:X}");
            Debug.WriteLine($"RDATA RVA: 0x{rdataRva:X}");
            Debug.WriteLine($"RDATA SIZE: 0x{rdata.Length:X}");
            foreach (var symbol in dataSymbolRvas)
            {
                Debug.WriteLine($"RDATA SYMBOL: {symbol.Key} = RVA 0x{symbol.Value:X}");
            }

            uint idataRva = AlignUp(rdataRva + (uint)Math.Max(1, rdata.Length), SectionAlignment);
            byte[] idata = BuildImportSection(idataRva, out uint getCommandLineIatRva, out uint commandLineToArgvIatRva, out uint localAllocIatRva, out uint localFreeIatRva, out uint wideCharToMultiByteIatRva, out uint printfIatRva, out uint exitProcessIatRva);
            uint relocRva = AlignUp(idataRva + (uint)Math.Max(1, idata.Length), SectionAlignment);
            byte[] reloc = BuildRelocationSection(baseRelocationRvas);
            startup = BuildStartup(getCommandLineIatRva, commandLineToArgvIatRva, localAllocIatRva, localFreeIatRva, exitProcessIatRva, out mainCallDispOffset);
            entryStubSize = startup.Length;
            mainRva = TextRva + (uint)entryStubSize;
            PatchRelative32(startup, mainCallDispOffset, TextRva + (uint)(mainCallDispOffset + 4), mainRva);
            text = new byte[ AlignUp(entryStubSize + result.MachineCode.Length, (int)FileAlignment)];
            Buffer.BlockCopy(startup, 0, text, 0, startup.Length);
            Buffer.BlockCopy(result.MachineCode, 0, text, entryStubSize, result.MachineCode.Length);
            foreach (var fixup in result.Fixups)
            {
                if (fixup.Kind == X64FixupKind.RipRelative32)
                {
                    uint targetRva;
                    if (dataSymbolRvas.TryGetValue(fixup.Symbol, out targetRva))
                    {
                    }
                    else if (result.Labels.TryGetValue(fixup.Symbol, out int targetOffset))
                    {
                        targetRva = TextRva + (uint)entryStubSize + (uint)targetOffset;
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

                    uint instructionRva = TextRva + (uint)entryStubSize + (uint)fixup.Offset;
                    uint nextInstructionRva = instructionRva + 4;
                    int displacement = checked( (int)( (long)targetRva - nextInstructionRva));
                    Debug.WriteLine($"RIP FIXUP: symbol={fixup.Symbol}, fixupOffset=0x{fixup.Offset:X}, " + $"instructionRva=0x{instructionRva:X}, " + $"nextRva=0x{nextInstructionRva:X}, " + $"targetRva=0x{targetRva:X}, " + $"displacement={displacement}");
                    WriteInt32(text, entryStubSize + fixup.Offset, displacement);
                }
                else if (fixup.Kind == X64FixupKind.Relative32)
                {
                    if (!result.Labels.TryGetValue(fixup.Symbol, out int targetOffset))
                    {
                        throw new InvalidOperationException($"Unknown x64 relative label '{fixup.Symbol}'.");
                    }

                    uint targetLabelRva = TextRva + (uint)entryStubSize + (uint)targetOffset;
                    uint instructionRva = TextRva + (uint)entryStubSize + (uint)fixup.Offset;
                    uint nextInstructionRva = instructionRva + 4;
                    int displacement = checked( (int)( (long)targetLabelRva - nextInstructionRva));
                    WriteInt32(text, entryStubSize + fixup.Offset, displacement);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported x64 fixup kind '{fixup.Kind}'.");
                }
            }

            if (dataSymbolRvas.TryGetValue("$str5", out uint str5Rva))
            {
                int str5Offset = checked( (int)(str5Rva - rdataRva));
                Debug.WriteLine($"FINAL STR5 RVA: 0x{str5Rva:X}");
                Debug.WriteLine($"STR5 OFFSET IN RDATA: 0x{str5Offset:X}");
                if (str5Offset >= 0 && str5Offset + 6 <= rdata.Length)
                {
                    Debug.WriteLine($"STR5 BYTES: " + BitConverter.ToString(rdata, str5Offset, 6));
                }
            }

            if (dataSymbolRvas.TryGetValue("$str6", out uint str6Rva))
            {
                int str6Offset = checked( (int)(str6Rva - rdataRva));
                Debug.WriteLine($"FINAL STR6 RVA: 0x{str6Rva:X}");
                Debug.WriteLine($"STR6 OFFSET IN RDATA: 0x{str6Offset:X}");
                if (str6Offset >= 0 && str6Offset + 6 <= rdata.Length)
                {
                    Debug.WriteLine($"STR6 BYTES: " + BitConverter.ToString(rdata, str6Offset, 6));
                }
            }

            if (dataSymbolRvas.ContainsKey("$str5"))
            {
                foreach (var fixup in result.Fixups)
                {
                    if (!string.Equals(fixup.Symbol, "$str5", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int finalFixupOffset = entryStubSize + fixup.Offset;
                    if (finalFixupOffset >= 0 && finalFixupOffset + 4 <= text.Length)
                    {
                        Debug.WriteLine($"FINAL STR5 FIXUP OFFSET: 0x{finalFixupOffset:X}");
                        Debug.WriteLine($"FINAL STR5 DISP BYTES: " + BitConverter.ToString(text, finalFixupOffset, 4));
                    }

                    break;
                }
            }

            uint textRawSize = AlignUp((uint)text.Length, FileAlignment);
            uint rdataRawSize = AlignUp((uint)Math.Max(1, rdata.Length), FileAlignment);
            uint idataRawSize = AlignUp((uint)Math.Max(1, idata.Length), FileAlignment);
            uint relocRawSize = AlignUp((uint)Math.Max(1, reloc.Length), FileAlignment);
            uint textSectionEndRva = AlignUp(TextRva + (uint)Math.Max(1, text.Length), SectionAlignment);
            uint rdataSectionEndRva = AlignUp(rdataRva + (uint)Math.Max(1, rdata.Length), SectionAlignment);
            uint idataSectionEndRva = AlignUp(idataRva + (uint)Math.Max(1, idata.Length), SectionAlignment);
            uint relocSectionEndRva = AlignUp(relocRva + (uint)Math.Max(1, reloc.Length), SectionAlignment);
            uint sizeOfImage = relocSectionEndRva;
            const ushort numberOfSections = 4;
            const ushort sizeOfOptionalHeader = 0xF0;
            int dosHeaderSize = 0x80;
            int peSignatureSize = 4;
            int fileHeaderSize = 20;
            int optionalHeaderSize = sizeOfOptionalHeader;
            int sectionHeaderSize = 40 * numberOfSections;
            uint headersSize = AlignUp((uint)( dosHeaderSize + peSignatureSize + fileHeaderSize + optionalHeaderSize + sectionHeaderSize), FileAlignment);
            uint textRawPointer = headersSize;
            uint rdataRawPointer = textRawPointer + textRawSize;
            uint idataRawPointer = rdataRawPointer + rdataRawSize;
            uint relocRawPointer = idataRawPointer + idataRawSize;
            byte[] headers = new byte[headersSize];
            WriteDosHeader(headers);
            WritePeHeaders(headers, (uint)text.Length, textRawSize, (uint)Math.Max(1, rdata.Length), rdataRawSize, (uint)Math.Max(1, idata.Length), idataRawSize, (uint)Math.Max(1, reloc.Length), relocRawSize, headersSize, sizeOfImage, numberOfSections, sizeOfOptionalHeader, rdataRva, idataRva, relocRva);
            WriteSectionHeaders(headers, (uint)text.Length, textRawSize, textRawPointer, rdataRva, (uint)Math.Max(1, rdata.Length), rdataRawSize, rdataRawPointer, idataRva, (uint)Math.Max(1, idata.Length), idataRawSize, idataRawPointer, relocRva, (uint)Math.Max(1, reloc.Length), relocRawSize, relocRawPointer);
            using FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(headers, 0, headers.Length);
            WriteSection(stream, text, textRawSize);
            WriteSection(stream, rdata, rdataRawSize);
            WriteSection(stream, idata, idataRawSize);
            WriteSection(stream, reloc, relocRawSize);
        }

        private byte[] BuildStartup(uint getCommandLineIatRva, uint commandLineToArgvIatRva, uint localAllocIatRva, uint localFreeIatRva, uint exitProcessIatRva, out int mainCallDispOffset)
        {
            StartupEmitter b = new StartupEmitter();
            b.Emit(0x48, 0x83, 0xEC, 0x78);
            int getCommandLineCallDispOffset = b.EmitRipCall();
            b.Emit(0x48, 0x89, 0xC1);
            b.Emit(0x48, 0x8D, 0x54, 0x24, 0x70);
            int commandLineToArgvCallDispOffset = b.EmitRipCall();
            b.Emit(0x49, 0x89, 0xC4);
            b.Emit(0x44, 0x8B, 0x74, 0x24, 0x70);
            b.Emit(0x44, 0x89, 0xF0);
            b.Emit(0xFF, 0xC0);
            b.Emit(0x48, 0xC1, 0xE0, 0x03);
            b.Emit(0x89, 0xC2);
            b.Emit(0x33, 0xC9);
            int localAllocArgvCallDispOffset = b.EmitRipCall();
            b.Emit(0x49, 0x89, 0xC5);
            b.Emit(0x33, 0xC0);
            b.Emit(0x4B, 0x89, 0x44, 0xF5, 0x00);
            b.Emit(0x45, 0x33, 0xFF);
            b.Mark("convert_loop");
            b.Emit(0x45, 0x3B, 0xFE);
            b.EmitConditionalJump("convert_done");
            b.Emit(0x4F, 0x8B, 0x04, 0xFC);
            b.Emit(0x4C, 0x89, 0x44, 0x24, 0x68);
            b.Emit(0x33, 0xC0);
            b.Mark("length_loop");
            b.Emit(0x66, 0x41, 0x83, 0x3C, 0x40, 0x00);
            b.EmitConditionalJump("length_done");
            b.Emit(0x48, 0xFF, 0xC0);
            b.EmitJump("length_loop");
            b.Mark("length_done");
            b.Emit(0x48, 0xFF, 0xC0);
            b.Emit(0x48, 0x89, 0xC2);
            b.Emit(0x33, 0xC9);
            int localAllocStringCallDispOffset = b.EmitRipCall();
            b.Emit(0x49, 0x89, 0xC1);
            b.Emit(0x4C, 0x8B, 0x44, 0x24, 0x68);
            b.Emit(0x33, 0xC0);
            b.Mark("copy_loop");
            b.Emit(0x66, 0x41, 0x83, 0x3C, 0x40, 0x00);
            b.EmitConditionalJump("copy_done");
            b.Emit(0x41, 0x0F, 0xB7, 0x0C, 0x40);
            b.Emit(0x41, 0x88, 0x0C, 0x01);
            b.Emit(0x48, 0xFF, 0xC0);
            b.EmitJump("copy_loop");
            b.Mark("copy_done");
            b.Emit(0x41, 0xC6, 0x04, 0x01, 0x00);
            b.Emit(0x4F, 0x89, 0x4C, 0xFD, 0x00);
            b.Emit(0x41, 0xFF, 0xC7);
            b.EmitJump("convert_loop");
            b.Mark("convert_done");
            b.Emit(0x44, 0x89, 0xF1);
            b.Emit(0x4C, 0x89, 0xEA);
            mainCallDispOffset = b.EmitRelativeCall();
            b.Emit(0x89, 0x44, 0x24, 0x74);
            b.Emit(0x45, 0x33, 0xFF);
            b.Mark("free_loop");
            b.Emit(0x45, 0x3B, 0xFE);
            b.EmitConditionalJump("free_done");
            b.Emit(0x4B, 0x8B, 0x4C, 0xFD, 0x00);
            int localFreeCallDispOffset = b.EmitRipCall();
            b.Emit(0x41, 0xFF, 0xC7);
            b.EmitJump("free_loop");
            b.Mark("free_done");
            b.Emit(0x4C, 0x89, 0xE9);
            int localFreeArrayCallDispOffset = b.EmitRipCall();
            b.Emit(0x4C, 0x89, 0xE1);
            int localFreeWideCallDispOffset = b.EmitRipCall();
            b.Emit(0x8B, 0x4C, 0x24, 0x74);
            int exitProcessCallDispOffset = b.EmitRipCall();
            byte[] startup = b.Finish();
            PatchRipRelative32(startup, getCommandLineCallDispOffset, TextRva + (uint)(getCommandLineCallDispOffset + 4), getCommandLineIatRva);
            PatchRipRelative32(startup, commandLineToArgvCallDispOffset, TextRva + (uint)(commandLineToArgvCallDispOffset + 4), commandLineToArgvIatRva);
            PatchRipRelative32(startup, localAllocArgvCallDispOffset, TextRva + (uint)(localAllocArgvCallDispOffset + 4), localAllocIatRva);
            PatchRipRelative32(startup, localAllocStringCallDispOffset, TextRva + (uint)(localAllocStringCallDispOffset + 4), localAllocIatRva);
            PatchRipRelative32(startup, localFreeCallDispOffset, TextRva + (uint)(localFreeCallDispOffset + 4), localFreeIatRva);
            PatchRipRelative32(startup, localFreeArrayCallDispOffset, TextRva + (uint)(localFreeArrayCallDispOffset + 4), localFreeIatRva);
            PatchRipRelative32(startup, localFreeWideCallDispOffset, TextRva + (uint)(localFreeWideCallDispOffset + 4), localFreeIatRva);
            PatchRipRelative32(startup, exitProcessCallDispOffset, TextRva + (uint)(exitProcessCallDispOffset + 4), exitProcessIatRva);
            return startup;
        }

        private byte[] BuildRDataSection(X64CodeGenerationResult result, uint rdataRva, out Dictionary<string, uint> symbolRvas, out List<uint> baseRelocationRvas)
        {
            symbolRvas = new Dictionary<string, uint>(StringComparer.Ordinal);
            baseRelocationRvas = new List<uint>();
            List<byte> buffer = new List<byte>();
            foreach (var item in result.Data)
            {
                AlignList(buffer, 8);
                uint rva = rdataRva + (uint)buffer.Count;
                if (symbolRvas.ContainsKey(item.Symbol))
                    throw new InvalidOperationException($"Duplicate .rdata symbol '{item.Symbol}'.");

                symbolRvas[item.Symbol] = rva;
                buffer.AddRange(item.Data);
            }

            foreach (var item in result.Data)
            {
                if (item.Relocations is null)
                    continue;

                uint itemRva = symbolRvas[item.Symbol];
                foreach (var relocation in item.Relocations)
                {
                    if (!symbolRvas.TryGetValue(relocation.TargetSymbol, out uint targetRva))
                    {
                        throw new InvalidOperationException($"Unknown .rdata relocation target '{relocation.TargetSymbol}'.");
                    }

                    int patchOffset = checked((int)((itemRva - rdataRva) + (uint)relocation.Offset));
                    ulong targetVa = ImageBase + targetRva;
                    WriteUInt64(buffer, patchOffset, targetVa);
                    uint relocationRva = itemRva + (uint)relocation.Offset;
                    baseRelocationRvas.Add(relocationRva);
                }
            }

            return buffer.ToArray();
        }

        private static byte[] BuildRelocationSection(IEnumerable<uint> relocationRvas)
        {
            List<uint> rvas = relocationRvas.Distinct().OrderBy(rva => rva).ToList();
            if (rvas.Count == 0)
                return Array.Empty<byte>();

            using MemoryStream stream = new MemoryStream();
            int index = 0;
            while (index < rvas.Count)
            {
                uint pageRva = rvas[index] & 0xFFFFF000u;
                List<ushort> entries = new List<ushort>();
                while (index < rvas.Count && (rvas[index] & 0xFFFFF000u) == pageRva)
                {
                    uint offset = rvas[index] - pageRva;
                    if (offset > 0xFFF)
                        throw new InvalidOperationException($"Base relocation RVA 0x{rvas[index]:X8} has an invalid page offset.");

                    entries.Add((ushort)(0xA000 | offset));
                    index++;
                }

                int blockSize = 8 + entries.Count * 2;
                if ((blockSize & 3) != 0)
                    blockSize = AlignUp(blockSize, 4);

                byte[] block = new byte[blockSize];
                WriteUInt32(block, 0, pageRva);
                WriteUInt32(block, 4, (uint)blockSize);
                for (int i = 0; i < entries.Count; i++)
                    WriteUInt16(block, 8 + i * 2, entries[i]);

                stream.Write(block, 0, block.Length);
            }

            return stream.ToArray();
        }

        private static void WriteUInt64(List<byte> data, int offset, ulong value)
        {
            byte[] bytes = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(), value);
            for (int i = 0; i < 8; i++)
                data[offset + i] = bytes[i];
        }

        private byte[] BuildImportSection(uint idataRva, out uint getCommandLineIatRva, out uint commandLineToArgvIatRva, out uint localAllocIatRva, out uint localFreeIatRva, out uint wideCharToMultiByteIatRva, out uint printfIatRva, out uint exitProcessIatRva)
        {
            const int kernel32IltOffset = 0x80;
            const int shell32IltOffset = 0xB0;
            const int msvcrtIltOffset = 0xC8;
            const int kernel32IatOffset = 0xE0;
            const int shell32IatOffset = 0x110;
            const int msvcrtIatOffset = 0x128;
            const int getCommandLineHintNameOffset = 0x140;
            const int commandLineToArgvHintNameOffset = 0x160;
            const int localAllocHintNameOffset = 0x180;
            const int localFreeHintNameOffset = 0x190;
            const int wideCharToMultiByteHintNameOffset = 0x1A0;
            const int printfHintNameOffset = 0x1C0;
            const int exitProcessHintNameOffset = 0x1D0;
            const int kernel32NameOffset = 0x1E0;
            const int shell32NameOffset = 0x1F0;
            const int msvcrtNameOffset = 0x200;
            const int size = 0x210;
            byte[] data = new byte[size];
            uint kernel32IltRva = idataRva + kernel32IltOffset;
            uint shell32IltRva = idataRva + shell32IltOffset;
            uint msvcrtIltRva = idataRva + msvcrtIltOffset;
            getCommandLineIatRva = idataRva + kernel32IatOffset;
            localAllocIatRva = idataRva + kernel32IatOffset + 8;
            localFreeIatRva = idataRva + kernel32IatOffset + 16;
            wideCharToMultiByteIatRva = idataRva + kernel32IatOffset + 24;
            exitProcessIatRva = idataRva + kernel32IatOffset + 32;
            commandLineToArgvIatRva = idataRva + shell32IatOffset;
            printfIatRva = idataRva + msvcrtIatOffset;
            uint getCommandLineHintNameRva = idataRva + getCommandLineHintNameOffset;
            uint commandLineToArgvHintNameRva = idataRva + commandLineToArgvHintNameOffset;
            uint localAllocHintNameRva = idataRva + localAllocHintNameOffset;
            uint localFreeHintNameRva = idataRva + localFreeHintNameOffset;
            uint wideCharToMultiByteHintNameRva = idataRva + wideCharToMultiByteHintNameOffset;
            uint printfHintNameRva = idataRva + printfHintNameOffset;
            uint exitProcessHintNameRva = idataRva + exitProcessHintNameOffset;
            uint kernel32NameRva = idataRva + kernel32NameOffset;
            uint shell32NameRva = idataRva + shell32NameOffset;
            uint msvcrtNameRva = idataRva + msvcrtNameOffset;
            WriteImportDescriptor(data, 0x00, kernel32IltRva, kernel32NameRva, idataRva + kernel32IatOffset);
            WriteImportDescriptor(data, 0x14, shell32IltRva, shell32NameRva, idataRva + shell32IatOffset);
            WriteImportDescriptor(data, 0x28, msvcrtIltRva, msvcrtNameRva, idataRva + msvcrtIatOffset);
            WriteUInt64(data, kernel32IltOffset + 0, getCommandLineHintNameRva);
            WriteUInt64(data, kernel32IltOffset + 8, localAllocHintNameRva);
            WriteUInt64(data, kernel32IltOffset + 16, localFreeHintNameRva);
            WriteUInt64(data, kernel32IltOffset + 24, wideCharToMultiByteHintNameRva);
            WriteUInt64(data, kernel32IltOffset + 32, exitProcessHintNameRva);
            WriteUInt64(data, kernel32IltOffset + 40, 0);
            WriteUInt64(data, shell32IltOffset + 0, commandLineToArgvHintNameRva);
            WriteUInt64(data, shell32IltOffset + 8, 0);
            WriteUInt64(data, msvcrtIltOffset + 0, printfHintNameRva);
            WriteUInt64(data, msvcrtIltOffset + 8, 0);
            WriteUInt64(data, kernel32IatOffset + 0, getCommandLineHintNameRva);
            WriteUInt64(data, kernel32IatOffset + 8, localAllocHintNameRva);
            WriteUInt64(data, kernel32IatOffset + 16, localFreeHintNameRva);
            WriteUInt64(data, kernel32IatOffset + 24, wideCharToMultiByteHintNameRva);
            WriteUInt64(data, kernel32IatOffset + 32, exitProcessHintNameRva);
            WriteUInt64(data, kernel32IatOffset + 40, 0);
            WriteUInt64(data, shell32IatOffset + 0, commandLineToArgvHintNameRva);
            WriteUInt64(data, shell32IatOffset + 8, 0);
            WriteUInt64(data, msvcrtIatOffset + 0, printfHintNameRva);
            WriteUInt64(data, msvcrtIatOffset + 8, 0);
            WriteHintName(data, getCommandLineHintNameOffset, "GetCommandLineW");
            WriteHintName(data, commandLineToArgvHintNameOffset, "CommandLineToArgvW");
            WriteHintName(data, localAllocHintNameOffset, "LocalAlloc");
            WriteHintName(data, localFreeHintNameOffset, "LocalFree");
            WriteHintName(data, wideCharToMultiByteHintNameOffset, "WideCharToMultiByte");
            WriteHintName(data, printfHintNameOffset, "printf");
            WriteHintName(data, exitProcessHintNameOffset, "ExitProcess");
            WriteAsciiString(data, kernel32NameOffset, "kernel32.dll");
            WriteAsciiString(data, shell32NameOffset, "shell32.dll");
            WriteAsciiString(data, msvcrtNameOffset, "msvcrt.dll");
            return data;
        }

        private static void WriteImportDescriptor(byte[] data, int offset, uint originalFirstThunk, uint nameRva, uint firstThunk)
        {
            WriteUInt32(data, offset + 0, originalFirstThunk);
            WriteUInt32(data, offset + 4, 0);
            WriteUInt32(data, offset + 8, 0);
            WriteUInt32(data, offset + 12, nameRva);
            WriteUInt32(data, offset + 16, firstThunk);
        }

        private static void WriteHintName(byte[] data, int offset, string name)
        {
            WriteUInt16(data, offset, 0);
            WriteAsciiString(data, offset + 2, name);
        }

        private static void WriteAsciiString(byte[] data, int offset, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
            data[offset + bytes.Length] = 0;
        }

        private static void PatchRipRelative32(byte[] data, int displacementOffset, uint nextInstructionRva, uint targetRva)
        {
            int displacement = checked((int)((long)targetRva - nextInstructionRva));
            WriteInt32(data, displacementOffset, displacement);
        }

        private static void PatchRelative32(byte[] data, int displacementOffset, uint nextInstructionRva, uint targetRva)
        {
            int displacement = checked((int)((long)targetRva - nextInstructionRva));
            WriteInt32(data, displacementOffset, displacement);
        }

        private static void WriteDosHeader(byte[] headers)
        {
            headers[0] = (byte)'M';
            headers[1] = (byte)'Z';
            WriteUInt32(headers, 0x3C, 0x80);
        }

        private void WritePeHeaders(byte[] headers, uint textVirtualSize, uint textRawSize, uint rdataVirtualSize, uint rdataRawSize, uint idataVirtualSize, uint idataRawSize, uint relocVirtualSize, uint relocRawSize, uint sizeOfHeaders, uint sizeOfImage, ushort numberOfSections, ushort sizeOfOptionalHeader, uint rdataRva, uint idataRva, uint relocRva)
        {
            int peOffset = 0x80;
            headers[peOffset + 0] = (byte)'P';
            headers[peOffset + 1] = (byte)'E';
            headers[peOffset + 2] = 0;
            headers[peOffset + 3] = 0;
            int fileHeader = peOffset + 4;
            WriteUInt16(headers, fileHeader + 0, MachineAmd64);
            WriteUInt16(headers, fileHeader + 2, numberOfSections);
            WriteUInt32(headers, fileHeader + 4, 0);
            WriteUInt32(headers, fileHeader + 8, 0);
            WriteUInt32(headers, fileHeader + 12, 0);
            WriteUInt16(headers, fileHeader + 16, sizeOfOptionalHeader);
            WriteUInt16(headers, fileHeader + 18, (ushort)(CharacteristicsExecutableImage | CharacteristicsLargeAddressAware));
            int optional = fileHeader + 20;
            WriteUInt16(headers, optional + 0, 0x20B);
            headers[optional + 2] = 14;
            headers[optional + 3] = 0;
            WriteUInt32(headers, optional + 4, textRawSize);
            WriteUInt32(headers, optional + 8, rdataRawSize + idataRawSize + relocRawSize);
            WriteUInt32(headers, optional + 12, 0);
            WriteUInt32(headers, optional + 16, TextRva);
            WriteUInt32(headers, optional + 20, TextRva);
            WriteUInt64(headers, optional + 24, ImageBase);
            WriteUInt32(headers, optional + 32, SectionAlignment);
            WriteUInt32(headers, optional + 36, FileAlignment);
            WriteUInt16(headers, optional + 40, 6);
            WriteUInt16(headers, optional + 42, 0);
            WriteUInt16(headers, optional + 44, 0);
            WriteUInt16(headers, optional + 46, 0);
            WriteUInt16(headers, optional + 48, 6);
            WriteUInt16(headers, optional + 50, 0);
            WriteUInt32(headers, optional + 52, 0);
            WriteUInt32(headers, optional + 56, sizeOfImage);
            WriteUInt32(headers, optional + 60, sizeOfHeaders);
            WriteUInt32(headers, optional + 64, 0);
            WriteUInt16(headers, optional + 68, SubsystemWindowsCui);
            WriteUInt16(headers, optional + 70, (ushort)(DllCharacteristicsDynamicBase | DllCharacteristicsNxCompat | DllCharacteristicsNoSeh));
            WriteUInt64(headers, optional + 72, 0x100000);
            WriteUInt64(headers, optional + 80, 0x1000);
            WriteUInt64(headers, optional + 88, 0x100000);
            WriteUInt64(headers, optional + 96, 0x1000);
            WriteUInt32(headers, optional + 104, 0);
            WriteUInt32(headers, optional + 108, 16);
            int dataDirectory = optional + 112;
            WriteUInt32(headers, dataDirectory + 8, idataRva);
            WriteUInt32(headers, dataDirectory + 12, 80);
            WriteUInt32(headers, dataDirectory + 40, relocRva);
            WriteUInt32(headers, dataDirectory + 44, relocVirtualSize);
        }

        private static void WriteSectionHeaders(byte[] headers, uint textVirtualSize, uint textRawSize, uint textRawPointer, uint rdataRva, uint rdataVirtualSize, uint rdataRawSize, uint rdataRawPointer, uint idataRva, uint idataVirtualSize, uint idataRawSize, uint idataRawPointer, uint relocRva, uint relocVirtualSize, uint relocRawSize, uint relocRawPointer)
        {
            int sectionOffset = 0x80 + 4 + 20 + 0xF0;
            WriteSectionHeader(headers, sectionOffset, ".text", textVirtualSize, TextRva, textRawSize, textRawPointer, TextCharacteristics);
            WriteSectionHeader(headers, sectionOffset + 40, ".rdata", rdataVirtualSize, rdataRva, rdataRawSize, rdataRawPointer, RDataCharacteristics);
            WriteSectionHeader(headers, sectionOffset + 80, ".idata", idataVirtualSize, idataRva, idataRawSize, idataRawPointer, IDataCharacteristics);
            WriteSectionHeader(headers, sectionOffset + 120, ".reloc", relocVirtualSize, relocRva, relocRawSize, relocRawPointer, RelocCharacteristics);
        }

        private static void WriteSectionHeader(byte[] headers, int offset, string name, uint virtualSize, uint virtualAddress, uint rawSize, uint rawPointer, uint characteristics)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            Buffer.BlockCopy(nameBytes, 0, headers, offset, Math.Min(nameBytes.Length, 8));
            WriteUInt32(headers, offset + 8, virtualSize);
            WriteUInt32(headers, offset + 12, virtualAddress);
            WriteUInt32(headers, offset + 16, rawSize);
            WriteUInt32(headers, offset + 20, rawPointer);
            WriteUInt32(headers, offset + 24, 0);
            WriteUInt32(headers, offset + 28, 0);
            WriteUInt16(headers, offset + 32, 0);
            WriteUInt16(headers, offset + 34, 0);
            WriteUInt32(headers, offset + 36, characteristics);
        }

        private static void WriteSection(FileStream stream, byte[] data, uint rawSize)
        {
            stream.Write(data, 0, data.Length);
            int padding = checked((int)rawSize - data.Length);
            if (padding > 0)
                stream.Write(new byte[padding], 0, padding);
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

        private static int AlignUp(int value, int alignment)
        {
            return checked((value + alignment - 1) / alignment * alignment);
        }

        private static uint AlignUp(uint value, uint alignment)
        {
            return checked((value + alignment - 1) / alignment * alignment);
        }

        private static void AlignList(List<byte> buffer, int alignment)
        {
            while ((buffer.Count % alignment) != 0)
                buffer.Add(0);
        }

        private sealed class StartupEmitter
        {
            private readonly List<byte> _bytes = new List<byte>();
            private readonly Dictionary<string, int> _labels = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly List<(int Offset, string Target, bool RipRelative)> _patches = new List<(int Offset, string Target, bool RipRelative)>();

            public void Emit(params byte[] bytes)
            {
                _bytes.AddRange(bytes);
            }

            public void Mark(string label)
            {
                _labels[label] = _bytes.Count;
            }

            public int EmitRipCall()
            {
                _bytes.Add(0xFF);
                _bytes.Add(0x15);
                int offset = _bytes.Count;
                _bytes.AddRange(new byte[4]);
                return offset;
            }

            public int EmitRelativeCall()
            {
                _bytes.Add(0xE8);
                int offset = _bytes.Count;
                _bytes.AddRange(new byte[4]);
                return offset;
            }

            public void EmitJump(string label)
            {
                _bytes.Add(0xE9);
                int offset = _bytes.Count;
                _bytes.AddRange(new byte[4]);
                _patches.Add((offset, label, false));
            }

            public void EmitConditionalJump(string label)
            {
                _bytes.Add(0x0F);
                _bytes.Add(0x84);
                int offset = _bytes.Count;
                _bytes.AddRange(new byte[4]);
                _patches.Add((offset, label, false));
            }

            public byte[] Finish()
            {
                byte[] result = _bytes.ToArray();
                foreach (var patch in _patches)
                {
                    if (!_labels.TryGetValue(patch.Target, out int targetOffset))
                        throw new InvalidOperationException($"Unknown startup label '{patch.Target}'.");

                    uint targetRva = TextRva + (uint)targetOffset;
                    uint nextInstructionRva = TextRva + (uint)patch.Offset + 4;
                    int displacement = checked((int)((long)targetRva - nextInstructionRva));
                    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(patch.Offset, 4), displacement);
                }

                return result;
            }
        }
    }
}
