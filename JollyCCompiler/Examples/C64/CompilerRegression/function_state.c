/* Expected: $0400..$0402 = 7,12,7. Checks nested calls and locals. */
unsigned char add_one(unsigned char x) { unsigned char t = x + 1; return t; }
unsigned char add_two(unsigned char x) { return add_one(add_one(x)); }
int main()
{
    unsigned char a = 5;
    __c64_poke(0x0400, add_two(a));
    __c64_poke(0x0401, add_two(10));
    __c64_poke(0x0402, add_two(a));
    while (1) { }
    return 0;
}
