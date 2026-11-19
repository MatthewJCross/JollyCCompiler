/* MOS6502 expression comparison smoke test.
   Expected: screen cells 0..3 contain 'P' and cell 4 contains 'D'.
   Run generated PRG in VICE; these are not automated runtime tests. */
void main(void)
{
    unsigned char a = 4;
    unsigned char b = 7;
    unsigned char c = 2;
    __c64_cls();
    if ((a + c) < b) __c64_putchar(0, 0, 80, 1);
    if ((a * 2) > b) __c64_putchar(1, 0, 80, 1);
    if ((b - c) == (a + 1)) __c64_putchar(2, 0, 80, 1);
    if ((a + c) != (b - c)) __c64_putchar(3, 0, 80, 1);
    if ((b % 2) == 1) __c64_putchar(4, 0, 68, 1);
    while (1) { }
}
