using JollyCCompiler.Compiler.CodeGen.MOS6502.Core;
using JollyCCompiler.Compiler.CodeGeneration.MOS6502.Core;
using System.Text;

namespace JollyCCompiler.Compiler.CodeGen.MOS6502.Platforms.C64
{
    public sealed class C64Runtime
    {
        public const ushort BasicStartAddress = 0x0801;

        public const int StartupSize = 4;

        public C64ProgramLayout CreateLayout(int mainCodeSize = 0)
        {
            if (mainCodeSize < 0)
                throw new ArgumentOutOfRangeException(nameof(mainCodeSize));

            var loader = CreateBasicLoader(0x080B);
            var startupAddress = (ushort)(BasicStartAddress + loader.Length);
            var mainAddress = (ushort)(startupAddress + StartupSize);

            return new C64ProgramLayout(BasicStartAddress, startupAddress, mainAddress, loader.Length, StartupSize, mainCodeSize);
        }

        public byte[] CreateProgram(C64ProgramLayout layout, byte[] mainCode)
        {
            ArgumentNullException.ThrowIfNull(mainCode);

            if (layout.MainCodeSize != 0 && mainCode.Length != layout.MainCodeSize)
                throw new InvalidOperationException("The supplied main code size does not match the C64 program layout.");

            var loader = CreateBasicLoader(layout.StartupAddress);
            var startup = CreateStartup(layout.MainAddress);
            var expectedStartupAddress = (ushort)(BasicStartAddress + loader.Length);

            if (expectedStartupAddress != layout.StartupAddress)
                throw new InvalidOperationException("The C64 BASIC loader size does not match the calculated program layout.");

            if (startup.Length != layout.StartupSize)
                throw new InvalidOperationException("The C64 startup size does not match the calculated program layout.");

            var program = new byte[loader.Length + startup.Length + mainCode.Length];

            Buffer.BlockCopy(loader, 0, program, 0, loader.Length);
            Buffer.BlockCopy(startup, 0, program, loader.Length, startup.Length);
            Buffer.BlockCopy(mainCode, 0, program, loader.Length + startup.Length, mainCode.Length);

            return program;
        }

        public byte[] CreateBasicLoader(ushort machineCodeAddress)
        {
            var addressText = Encoding.ASCII.GetBytes(machineCodeAddress.ToString());
            var lineSize = 2 + 2 + 1 + addressText.Length + 1;
            var nextLineAddress = (ushort)(BasicStartAddress + lineSize);
            var bytes = new List<byte>(lineSize + 2);

            bytes.Add((byte)(nextLineAddress & 0xFF));
            bytes.Add((byte)((nextLineAddress >> 8) & 0xFF));
            bytes.Add(0x0A);
            bytes.Add(0x00);
            bytes.Add(0x9E);
            bytes.AddRange(addressText);
            bytes.Add(0x00);
            bytes.Add(0x00);
            bytes.Add(0x00);

            return bytes.ToArray();
        }

        public byte[] CreateStartup(ushort mainAddress)
        {
            var assembler = new Mos6502Assembler();
            assembler.Origin = (ushort)(mainAddress - StartupSize);
            assembler.Jsr(mainAddress);
            assembler.Rts();

            return assembler.Assemble();
        }
    }

    public readonly record struct C64ProgramLayout(ushort LoadAddress, ushort StartupAddress, ushort MainAddress, int LoaderSize, int StartupSize, int MainCodeSize);
}