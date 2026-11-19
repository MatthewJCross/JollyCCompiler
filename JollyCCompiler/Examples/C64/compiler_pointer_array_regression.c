// MOS6502 regression cases for byte memory access and 16-bit offsets.
// Run on C64 and inspect $C800-$C804 (values 42, 17, 99, 55, 7).
int main()
{
    unsigned short base = 0xC800;
    unsigned char offset = 2;
    unsigned char value = 99;
    *((unsigned char *)0xC800) = 42;
    *((unsigned char *)0xC801) = 17;
    *((unsigned char *)0xC802) = value;
    *((unsigned char *)0xC803) = 55;
    *((unsigned char *)0xC804) = 7;
    // Exercise calculated pointer dereference and array-style indexing.
    value = *(base + offset);
    *((unsigned char *)0xC802) = value;
    value = ((unsigned char *)0xC800)[3];
    *((unsigned char *)0xC803) = value;
    while (1) { }
    return 0;
}
