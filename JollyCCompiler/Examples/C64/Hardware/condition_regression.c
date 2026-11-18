// Phase 8.8 condition regression: AND/OR short-circuit, 16-bit arithmetic.
int main()
{
    unsigned char a = 3;
    unsigned char b = 7;
    unsigned int counter = 255;
    counter = counter + 1;
    if (a == 3 && b == 7) __c64_poke(0xD020, 5);
    if (a == 1 || b == 7) __c64_poke(0xD021, 6);
    if (counter > 255) __c64_poke(0xD020, 3);
    while (1) { }
    return 0;
}
