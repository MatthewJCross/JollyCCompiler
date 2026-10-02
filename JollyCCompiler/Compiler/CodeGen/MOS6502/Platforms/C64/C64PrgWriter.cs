using System.IO;

namespace JollyCCompiler.Compiler.CodeGen.MOS6502.Platforms.C64
{
    public sealed class C64PrgWriter
    {
        public const ushort DefaultLoadAddress = 0x0801;

        public void Write(string outputPath, byte[] machineCode)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentNullException.ThrowIfNull(machineCode);

            Write(outputPath, DefaultLoadAddress, machineCode);
        }

        public void Write(string outputPath, ushort loadAddress, byte[] machineCode)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentNullException.ThrowIfNull(machineCode);

            using var stream = File.Create(outputPath);
            Write(stream, loadAddress, machineCode);
        }

        public void Write(Stream stream, ushort loadAddress, byte[] machineCode)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(machineCode);

            stream.WriteByte((byte)(loadAddress & 0xFF));
            stream.WriteByte((byte)((loadAddress >> 8) & 0xFF));
            stream.Write(machineCode, 0, machineCode.Length);
        }
    }
}