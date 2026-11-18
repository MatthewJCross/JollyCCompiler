// Expected border colour: 5 (five successful checks).
int main()
{
    unsigned char a = 0;
    unsigned char b = 3;
    unsigned char checks = 0;

    if (!a) checks = checks + 1;
    if (!!b) checks = checks + 1;
    if (!(a == b)) checks = checks + 1;
    if (!(a && b)) checks = checks + 1;
    if (!(a || 0)) checks = checks + 1;

    __c64_poke(0xD020, checks);
    while (1) { }
    return 0;
}
