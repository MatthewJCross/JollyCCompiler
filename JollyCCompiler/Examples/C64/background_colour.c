/* Phase 7.1: VIC-II background register ($D021).
   Uses a directly cast hardware address. */
int main()
{
    *(volatile unsigned char*)0xD021 = 0;
    return 0;
}
