/* WIN ABI regression: eight stack arguments, 64-bit pointers and return value. */
extern int MessageBoxA(void* hwnd, char* text, char* title, unsigned int flags);
long long Check12(long long a, long long b, long long c, long long d,
                  long long e, long long f, long long g, long long h,
                  long long i, long long j, long long k, long long l)
{
    return a + b*2 + c*3 + d*4 + e*5 + f*6 + g*7 + h*8 + i*9 + j*10 + k*11 + l*12;
}
int main()
{
    long long value = Check12(1,2,3,4,5,6,7,8,9,10,11,12);
    if (value == 650) {
        MessageBoxA(0, "12 argument call passed", "JollyC ABI", 0);
        return 0;
    }
    MessageBoxA(0, "12 argument call FAILED", "JollyC ABI", 0);
    return 1;
}
