// Direct VIC-II memory mapped I/O using the Phase 8 built-ins.
int main()
{
    unsigned char border = 0;
    __c64_poke(0xD020, 6);
    border = __c64_peek(0xD020);
    __c64_poke(0xD021, border);
    while (1) { }
    return 0;
}
