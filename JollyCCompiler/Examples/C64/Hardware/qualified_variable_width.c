// Phase 8 regression: qualifiers must not alter variable storage widths.
int main()
{
    volatile unsigned char colour = 6;
    unsigned char copy = 0;
    copy = colour;
    __c64_poke(0xD020, copy);
    while (1) { }
    return 0;
}
