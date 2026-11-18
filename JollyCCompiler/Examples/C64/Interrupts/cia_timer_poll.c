// CIA1 Timer A polling. Does not install an IRQ vector.
int main()
{
    unsigned char elapsed = 0;
    unsigned char colour = 0;
    __cia1_timer_start(255, 255);
    while (1)
    {
        elapsed = __cia1_timer_elapsed();
        if (elapsed)
        {
            __c64_poke(0xD020, colour);
            colour = colour + 1;
            if (colour == 16) colour = 0;
        }
    }
    return 0;
}
