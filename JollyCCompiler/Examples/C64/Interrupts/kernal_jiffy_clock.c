// Read KERNAL jiffy clock instead of reprogramming CIA1 Timer A.
// The low byte wraps every 256 ticks.
int main()
{
    unsigned char previous = 0;
    unsigned char now = 0;
    unsigned char colour = 0;
    previous = __c64_ticks();
    while (1)
    {
        now = __c64_ticks();
        if (now != previous)
        {
            previous = now;
            colour = colour + 1;
            if (colour == 16) colour = 0;
            __c64_poke(0xD020, colour);
        }
    }
    return 0;
}
