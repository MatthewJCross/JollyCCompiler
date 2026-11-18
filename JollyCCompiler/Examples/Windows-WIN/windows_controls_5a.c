/* Windows Phase 5a: native button, edit, label, checkbox and combo box.
   Build with WIN. No diagnostic MessageBoxA before CreateWindowExA. */
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
extern void* GetDlgItem(void* hwnd, int id);
extern int GetWindowTextA(void* hwnd, char* buffer, int maxCount);
extern int SetWindowTextA(void* hwnd, char* text);
extern long long SendMessageA(void* hwnd, unsigned int message, unsigned long long wParam, long long lParam);

long long WindowProc(void* hwnd, unsigned int message, unsigned long long wParam, long long lParam)
{
    if (message == 273) { /* WM_COMMAND */
        unsigned long long controlId = wParam & 65535;
        unsigned long long notification = (wParam >> 16) & 65535;
        if (controlId == 1001 && notification == 0) { /* BN_CLICKED */
            char text[256];
            void* edit = GetDlgItem(hwnd, 1002);
            void* label = GetDlgItem(hwnd, 1003);
            void* check = GetDlgItem(hwnd, 1004);
            void* combo = GetDlgItem(hwnd, 1005);
            int length = GetWindowTextA(edit, text, 256);
            if (length == 0) {
                SetWindowTextA(label, "Please enter some text first.");
                return 0;
            }
            SetWindowTextA(label, text);
            if (SendMessageA(check, 240, 0, 0) == 1) { /* BM_GETCHECK */
                MessageBoxA(hwnd, text, "Checkbox enabled", 0);
            } else {
                MessageBoxA(hwnd, text, "Checkbox disabled", 0);
            }
            if (SendMessageA(combo, 327, 0, 0) == 0) { /* CB_GETCURSEL */
                SetWindowTextA(label, "Selected: First option");
            }
            if (SendMessageA(combo, 327, 0, 0) == 1) {
                SetWindowTextA(label, "Selected: Second option");
            }
            if (SendMessageA(combo, 327, 0, 0) == 2) {
                SetWindowTextA(label, "Selected: Third option");
            }
            return 0;
        }
    }
    if (message == 2) { /* WM_DESTROY */
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
    void* button;
    void* edit;
    void* label;
    void* checkbox;
    void* combo;
    unsigned short classAtom;
    char* className = "JollyCControls5a";

    wc.style = 0;
    wc.lpfnWndProc = WindowProc;
    wc.cbClsExtra = 0;
    wc.cbWndExtra = 0;
    wc.hInstance = instance;
    wc.hIcon = 0;
    wc.hCursor = 0;
    wc.hbrBackground = (void*)6; /* COLOR_WINDOW + 1; repaint on resize */
    wc.lpszMenuName = 0;
    wc.lpszClassName = className;

    classAtom = RegisterClassA(&wc);
    if (classAtom == 0) {
        MessageBoxA(0, "RegisterClassA failed", "Phase 5a", 0);
        return 1;
    }
    hwnd = CreateWindowExA(0, className, "JollyCCompiler - Controls Demo",
        0x10CF0000, 100, 100, 640, 400, 0, 0, instance, 0);
    if (hwnd == 0) {
        MessageBoxA(0, "CreateWindowExA parent failed", "Phase 5a", 0);
        return 2;
    }
    /* WS_CHILD | WS_VISIBLE; IDs supplied as hMenu. */
    edit = CreateWindowExA(0x200, "EDIT", "Type here", 0x50010080,
        24, 24, 320, 30, hwnd, (void*)1002, instance, 0);
    label = CreateWindowExA(0, "STATIC", "Ready", 0x50000000,
        24, 68, 440, 24, hwnd, (void*)1003, instance, 0);
    checkbox = CreateWindowExA(0, "BUTTON", "Enable notification", 0x50010003,
        24, 108, 220, 28, hwnd, (void*)1004, instance, 0);
    combo = CreateWindowExA(0, "COMBOBOX", "", 0x50210203,
        24, 150, 250, 140, hwnd, (void*)1005, instance, 0);
    button = CreateWindowExA(0, "BUTTON", "Apply", 0x50010000,
        24, 205, 140, 36, hwnd, (void*)1001, instance, 0);
    if (edit == 0 || label == 0 || checkbox == 0 || combo == 0 || button == 0) {
        MessageBoxA(hwnd, "One or more controls failed", "Phase 5a", 0);
        return 3;
    }
    SendMessageA(combo, 323, 0, (long long)"First option"); /* CB_ADDSTRING */
    SendMessageA(combo, 323, 0, (long long)"Second option");
    SendMessageA(combo, 323, 0, (long long)"Third option");
    SendMessageA(combo, 334, 0, 0); /* CB_SETCURSEL */
    ShowWindow(hwnd, 5);
    UpdateWindow(hwnd);
    while (GetMessageA(&msg, 0, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageA(&msg);
    }
    return 0;
}
