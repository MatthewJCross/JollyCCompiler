using JollyCCompiler.Compiler.CodeGen.MOS6502;
using JollyCCompiler.Compiler.CodeGen.MOS6502.Core;
using JollyCCompiler.Compiler.CodeGeneration.MOS6502.Core;
using JollyCCompiler.Compiler.Syntax;

namespace JollyCCompiler.Compiler.CodeGen.MOS6502.Platforms.C64
{

    public sealed class C64Target
    {
        private readonly C64Runtime _runtime;
        private readonly C64PrgWriter _prgWriter;
        private readonly Mos6502CodeGenerator _codeGenerator;

        public C64Target()
        {
            _runtime = new C64Runtime();
            _prgWriter = new C64PrgWriter();
            _codeGenerator = new Mos6502CodeGenerator();
        }

        public ushort LoadAddress => C64Runtime.BasicStartAddress;

        public C64ProgramLayout GetLayout(int mainCodeSize = 0)
        {
            return _runtime.CreateLayout(mainCodeSize);
        }

        public Mos6502CodeGenerationResult Generate(ProgramNode program)
        {
            ArgumentNullException.ThrowIfNull(program);

            var layout = _runtime.CreateLayout();

            return _codeGenerator.Generate(program, layout.MainAddress, C64Memory.ReturnValue);
        }

        public byte[] BuildProgram(ProgramNode program)
        {
            ArgumentNullException.ThrowIfNull(program);

            var generated = Generate(program);
            // C64 code begins at $4000, outside VIC bank 0 graphics RAM.
            // Keep it below $A000 to avoid loading across the BASIC ROM window.
            const int codeLimitExclusive = 0xA000;
            int codeEndExclusive = generated.MainAddress + generated.MachineCode.Length;
            if (codeEndExclusive > codeLimitExclusive)
            {
                throw new InvalidOperationException(
                    $"C64 program code ends at ${codeEndExclusive - 1:X4}; " +
                    $"the supported code region is $4000-${codeLimitExclusive - 1:X4}. " +
                    "Reduce code size or implement a bank-aware linker.");
            }

            var layout = _runtime.CreateLayout(generated.MachineCode.Length);

            // Code may begin with an IRQ handler before main. The BASIC startup
            // must JSR the actual main symbol, not the first emitted byte.
            return _runtime.CreateProgram(layout with { MainAddress = generated.MainAddress }, generated.MachineCode);
        }

        public void WriteProgram(string outputPath, ProgramNode program)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ArgumentNullException.ThrowIfNull(program);

            var programBytes = BuildProgram(program);

            _prgWriter.Write(outputPath, LoadAddress, programBytes);
        }

        public byte[] BuildTestProgram()
        {
            var layout = _runtime.CreateLayout();
            var assembler = new Mos6502Assembler();

            assembler.Origin = layout.MainAddress;
            assembler.LdaImmediate(42);
            assembler.Rts();

            var mainCode = assembler.Assemble();

            return _runtime.CreateProgram(layout with { MainCodeSize = mainCode.Length }, mainCode);
        }

        public void WriteTestProgram(string outputPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            var program = BuildTestProgram();

            _prgWriter.Write(outputPath, LoadAddress, program);
        }
    }
}