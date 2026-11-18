// A simple 24x21 hardware sprite at (120,100).
// Sprite bitmap stored at $3000; sprite pointer $C0.
int main()
{
    __c64_poke(0x3000, 255);
    __c64_poke(0x3001, 255);
    __c64_poke(0x3002, 255);
    __c64_poke(0x3003, 129);
    __c64_poke(0x3004, 0);
    __c64_poke(0x3005, 129);
    __c64_poke(0x3006, 255);
    __c64_poke(0x3007, 255);
    __c64_poke(0x3008, 255);
    __c64_poke(0x07F8, 192);
    __c64_poke(0xD000, 120);
    __c64_poke(0xD001, 100);
    __c64_poke(0xD027, 1);
    __c64_poke(0xD010, 0);
    __c64_poke(0xD015, 1);
    while (1) { }
    return 0;
}
