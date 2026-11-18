// Variable comparison regression: unsigned 8-bit and 16-bit.
int main()
{
    unsigned char a = 5;
    unsigned char b = 7;
    unsigned int wide_a = 256;
    unsigned int wide_b = 257;
    if (a < b) __c64_poke(0xD020, 5);
    if (b > a) __c64_poke(0xD021, 6);
    if (a != b) __c64_poke(0xD020, 7);
    if (wide_a < wide_b) __c64_poke(0xD021, 8);
    if (wide_a == wide_b) __c64_poke(0xD020, 2);
    while (1) { }
    return 0;
}
