/* Expected: RAM $C800..$C803 = 42,17,99,55. */
int main()
{
    unsigned short base = 0xC800;
    unsigned char offset = 2;
    *((unsigned char *)0xC800) = 42;
    *((unsigned char *)0xC801) = 17;
    *((unsigned char *)0xC802) = 99;
    *((unsigned char *)0xC803) = 55;
    __c64_poke(0xC802, *(base + offset));
    while (1) { }
    return 0;
}
