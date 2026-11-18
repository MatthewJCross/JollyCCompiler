// Phase 8 regression: compare two variables, including mixed widths.
int main()
{
    unsigned char a = 12;
    unsigned char b = 18;
    unsigned int wide = 300;
    unsigned char passed = 0;
    if (a < b) passed = passed + 1;
    if (b > a) passed = passed + 1;
    if (a != b) passed = passed + 1;
    if (a == a) passed = passed + 1;
    if (a < wide) passed = passed + 1;
    if (wide > b) passed = passed + 1;
    __c64_poke(0xD020, passed); // Expected blue (6).
    while (1) { }
    return 0;
}
