namespace JollyCCompiler.Compiler.CodeGen.MOS6502.Platforms.C64
{
    public static class C64Memory
    {
        public const ushort Screen = 0x0400;
        public const ushort ColorRam = 0xD800;
        public const ushort Vic = 0xD000;
        public const ushort Sid = 0xD400;
        public const ushort Cia1 = 0xDC00;
        public const ushort Cia2 = 0xDD00;
        public const ushort RuntimeWorkspace = 0xC000;
        public const ushort ReturnValue = RuntimeWorkspace;
    }
}