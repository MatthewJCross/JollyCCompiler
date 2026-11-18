// SID voice 1, sawtooth note with attack/decay/sustain/release.
int main()
{
    unsigned int delay = 0;
    __c64_poke(0xD418, 15);
    __c64_poke(0xD400, 40);
    __c64_poke(0xD401, 25);
    __c64_poke(0xD405, 9);
    __c64_poke(0xD406, 240);
    __c64_poke(0xD404, 33);
    while (1)
    {
        delay = 0;
        while (delay < 20000) delay = delay + 1;
        __c64_poke(0xD404, 32);
        delay = 0;
        while (delay < 20000) delay = delay + 1;
        __c64_poke(0xD404, 33);
    }
    return 0;
}
