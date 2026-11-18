// Phase 8 step 1: nested byte arithmetic and bitwise expression smoke test.
int main()
{
    unsigned char a = 3;
    unsigned char b = 5;
    unsigned char c = 2;
    if (((a + b) - c) & 6)
        __c64_poke(0xD020, 5);
    else
        __c64_poke(0xD020, 2);
    while (1) { }
    return 0;
}
