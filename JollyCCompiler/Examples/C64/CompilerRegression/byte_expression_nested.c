/* Expected: screen cells $0400..$0405 = 1,1,1,1,1,1. */
int main()
{
    unsigned char a = 7;
    unsigned char b = 3;
    unsigned char c = 2;
    __c64_poke(0x0400, ((a + b) - c) == 8);
    __c64_poke(0x0401, (a * c) == 14);
    __c64_poke(0x0402, (a / c) == 3);
    __c64_poke(0x0403, (a % c) == 1);
    __c64_poke(0x0404, ((a + c) > (b * c)));
    __c64_poke(0x0405, ((a - b) == (c + c)));
    while (1) { }
    return 0;
}
