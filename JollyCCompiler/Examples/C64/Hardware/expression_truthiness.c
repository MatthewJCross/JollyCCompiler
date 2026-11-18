// Phase 8: arithmetic and mask truth tests.
int main()
{
    unsigned char value = 5;
    unsigned char flags = 16;
    if (flags & 16) __c64_poke(0xD020, 5);
    if (value - 5) __c64_poke(0xD021, 2);
    if (value + 1) __c64_poke(0xD021, 6);
    if ((flags & 16) == 16) __c64_poke(0xD020, 3);
    while (1) { }
    return 0;
}
