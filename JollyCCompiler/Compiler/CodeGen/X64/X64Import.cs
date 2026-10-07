namespace JollyCCompiler.Compiler.CodeGen.X64
{
    public sealed class X64Import
    {
        public X64Import(string dllName, string functionName)
        {
            DllName = dllName;
            FunctionName = functionName;
        }

        public string DllName { get; }
        public string FunctionName { get; }
    }
}
