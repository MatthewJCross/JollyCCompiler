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
            var layout = _runtime.CreateLayout(generated.MachineCode.Length);

            if (layout.MainAddress != generated.MainAddress)
                throw new InvalidOperationException("The C64 main address changed between code generation and program layout.");

            return _runtime.CreateProgram(layout, generated.MachineCode);
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