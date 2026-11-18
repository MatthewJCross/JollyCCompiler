/* Minimal Win32 diagnostic: GetLastError + FormatMessageA (7 arguments).
   If FormatMessageA itself fails, inspect the ABI tests first. */
extern unsigned int GetLastError(void);
extern unsigned int FormatMessageA(unsigned int flags, void* source, unsigned int code,
    unsigned int language, char* buffer, unsigned int size, void* arguments);
extern int MessageBoxA(void*, char*, char*, unsigned int);
extern void* CreateWindowExA(unsigned int, char*, char*, unsigned int,
    int, int, int, int, void*, void*, void*, void*);
int main()
{
    char buffer[512];
    void* hwnd = CreateWindowExA(0, "NoSuchJollyWindowClass", "Probe", 0,
        0, 0, 100, 100, 0, 0, 0, 0);
    if (hwnd == 0) {
        unsigned int error = GetLastError();
        unsigned int count = FormatMessageA(0x00001000, 0, error, 0, buffer, 512, 0);
        if (count != 0) MessageBoxA(0, buffer, "Windows error from OS", 0);
        else MessageBoxA(0, "FormatMessageA failed", "JollyC", 0);
        return 0;
    }
    MessageBoxA(0, "Unexpected success", "JollyC", 0);
    return 0;
}
