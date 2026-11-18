/* WIN target: tests x64 Win32 structure layout and callback field parsing.
   Expected size: 72 bytes (WNDCLASSA on Windows x64). */
struct WNDCLASSA {
    unsigned int style;
    long long (*lpfnWndProc)(void*, unsigned int, unsigned long long, long long);
    int cbClsExtra;
    int cbWndExtra;
    void* hInstance;
    void* hIcon;
    void* hCursor;
    void* hbrBackground;
    char* lpszMenuName;
    char* lpszClassName;
};
extern int MessageBoxA(void*, char*, char*, unsigned int);
int main() {
    if (sizeof(struct WNDCLASSA) == 72) {
        MessageBoxA(0, "WNDCLASSA layout is 72 bytes", "JollyCCompiler Phase 3", 0);
        return 0;
    }
    return 1;
}
