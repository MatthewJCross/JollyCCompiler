// Hardware sprite movement with joystick port 2. X position is 0..255.
int main()
{
    unsigned char x = 120;
    unsigned char y = 110;
    unsigned char joy = 0;
    unsigned int delay = 0;
    // 24 pixels across the top row of sprite data at $3000.
    __c64_poke(0x3000, 255);
    __c64_poke(0x3001, 255);
    __c64_poke(0x3002, 255);
    __c64_poke(0x07F8, 192);
    __sprite_colour(0, 1);
    __sprite_position(0, x, y);
    __sprite_enable(1);
    while (1)
    {
        joy = __joystick2();
        if (joy == 4) x = x - 1; // left only
        if (joy == 8) x = x + 1; // right only
        __sprite_position(0, x, y);
        delay = 0;
        while (delay < 400) delay = delay + 1;
    }
    return 0;
}
