/* Win32 import smoke test. Select the WIN target. */
extern void* GetModuleHandleA(char* moduleName);
extern int MessageBoxA(void* owner, char* message, char* caption, unsigned int flags);

int main()
{
    void* module = GetModuleHandleA(0);
    MessageBoxA(0, "JollyCCompiler native Windows import test", "JollyCCompiler", 0);
    return 0;
}
