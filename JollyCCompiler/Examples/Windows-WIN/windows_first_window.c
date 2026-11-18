/* JollyCCompiler WIN Phase 4: first real native Win32 window.
   This is an integration test, not yet verified on Windows.
   Close the window using its title-bar X to terminate the message loop. */
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
struct POINT { int x; int y; };
struct MSG {
    void* hwnd;
    unsigned int message;
    unsigned long long wParam;
    long long lParam;
    unsigned int time;
    struct POINT pt;
    unsigned int lPrivate;
};
extern void* GetModuleHandleA(char* name);
extern unsigned short RegisterClassA(struct WNDCLASSA* cls);
extern void* CreateWindowExA(unsigned int exStyle, char* className, char* title,
    unsigned int style, int x, int y, int width, int height,
    void* parent, void* menu, void* instance, void* param);
extern int ShowWindow(void* hwnd, int command);
extern int UpdateWindow(void* hwnd);
extern int GetMessageA(struct MSG* msg, void* hwnd, unsigned int minFilter, unsigned int maxFilter);
extern int TranslateMessage(struct MSG* msg);
extern long long DispatchMessageA(struct MSG* msg);
extern long long DefWindowProcA(void* hwnd, unsigned int message, unsigned long long wParam, long long lParam);
extern void PostQuitMessage(int exitCode);
extern int MessageBoxA(void* hwnd, char* text, char* caption, unsigned int flags);

long long WindowProc(void* hwnd, unsigned int message, unsigned long long wParam, long long lParam)
{
    if (message == 2) {
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcA(hwnd, message, wParam, lParam);
}

int main()
{
    struct WNDCLASSA wc;
    struct MSG msg;
    void* instance = GetModuleHandleA(0);
    void* hwnd;
    char* className = "JollyCWindow";
    wc.style = 0;
    wc.lpfnWndProc = WindowProc;
    wc.cbClsExtra = 0;
    wc.cbWndExtra = 0;
    wc.hInstance = instance;
    wc.hIcon = 0;
    wc.hCursor = 0;
    wc.hbrBackground = 0;
    wc.lpszMenuName = 0;
    wc.lpszClassName = className;
    if (RegisterClassA(&wc) == 0) {
        MessageBoxA(0, "RegisterClassA failed", "JollyC", 0);
        return 1;
    }
    hwnd = CreateWindowExA(0, className, "JollyCCompiler - First Window",
        0x10CF0000, 100, 100, 640, 400, 0, 0, instance, 0);
    if (hwnd == 0) {
        MessageBoxA(0, "CreateWindowExA failed", "JollyC", 0);
        return 2;
    }
    ShowWindow(hwnd, 5);
    UpdateWindow(hwnd);
    while (GetMessageA(&msg, 0, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageA(&msg);
    }
    return 0;
}
