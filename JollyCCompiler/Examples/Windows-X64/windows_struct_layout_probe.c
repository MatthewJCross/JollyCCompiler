/* WIN ABI regression: WNDCLASSA size and field offsets. */
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
long long Callback(void* hwnd, unsigned int msg, unsigned long long w, long long l) { return 123; }
int main()
{
    struct WNDCLASSA wc;
    wc.style = 7;
    wc.lpfnWndProc = Callback;
    wc.cbClsExtra = 11;
    wc.cbWndExtra = 13;
    wc.hInstance = 0;
    wc.hIcon = 0;
    wc.hCursor = 0;
    wc.hbrBackground = 0;
    wc.lpszMenuName = 0;
    wc.lpszClassName = "JollyProbe";
    if (sizeof(struct WNDCLASSA) != 72) {
        MessageBoxA(0, "WNDCLASSA size FAILED", "JollyC ABI", 0);
        return 1;
    }
    if (wc.style != 7 || wc.cbClsExtra != 11 || wc.cbWndExtra != 13) {
        MessageBoxA(0, "WNDCLASSA field FAILED", "JollyC ABI", 0);
        return 2;
    }
    MessageBoxA(0, "WNDCLASSA size and integer fields passed", "JollyC ABI", 0);
    return 0;
}
