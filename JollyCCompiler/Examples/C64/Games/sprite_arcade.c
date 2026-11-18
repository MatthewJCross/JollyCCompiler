// Phase 8 integration smoke game: move sprite, fire to change colour/sound.
// Joystick port 2. Sprite bitmap starts at $3000; screen uses bank 0.
int main()
{
    unsigned char x = 120;
    unsigned char joy = 0;
    unsigned int delay = 0;
    __c64_poke(0x3000, 255);
    __c64_poke(0x3001, 255);
    __c64_poke(0x3002, 255);
    __c64_poke(0x3003, 129);
    __c64_poke(0x3004, 0);
    __c64_poke(0x3005, 129);
    __c64_poke(0x07F8, 192);
    __sprite_colour(0, 1);
    __sprite_enable(1);
    while (1)
    {
        joy = __joystick2();
        if (joy == 4) x = x - 1;
        if (joy == 8) x = x + 1;
        __sprite_position(0, x, 120);
        if (joy >= 16)
        {
            __sprite_colour(0, 2);
            __sid_beep(40, 25);
        }
        else
        {
            __sprite_colour(0, 1);
            __sid_stop();
        }
        delay = 0;
        while (delay < 400) delay = delay + 1;
    }
    return 0;
}
