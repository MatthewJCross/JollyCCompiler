// Expected border colour: 6 (six successful checks).
int main()
{
    unsigned char low = 0;
    unsigned char high = 255;
    unsigned int wide = 256;
    unsigned char checks = 0;

    if (low < high) checks = checks + 1;
    if (high > low) checks = checks + 1;
    if (high != low) checks = checks + 1;
    if (low <= low) checks = checks + 1;
    if (wide > high) checks = checks + 1;
    if (high < wide) checks = checks + 1;

    __c64_poke(0xD020, checks);
    while (1) { }
    return 0;
}
