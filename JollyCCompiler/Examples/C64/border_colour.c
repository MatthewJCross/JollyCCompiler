/* Phase 7.1: memory-mapped VIC-II border colour ($D020).
   Compile for C64 and RUN the resulting PRG in VICE or on a C64.
   This sample does not install interrupts. */
int main()
{
    volatile unsigned char* border = (volatile unsigned char*)0xD020;
    *border = 6;
    return 0;
}
