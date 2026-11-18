// Phase 7.5: animate raster-bar colours without reinstalling the IRQ.
// Compile for C64 and run in VICE.
int main()
{
    unsigned char colour = 0;
    unsigned int delay;

    __install_raster_bars(40, 2, 80, 8, 120, 7, 160, 5, 200, 0);

    while (1)
    {
        __set_raster_bar_colour(0, colour);
        __set_raster_bar_colour(1, colour + 1);
        __set_raster_bar_colour(2, colour + 2);
        __set_raster_bar_colour(3, colour + 3);

        colour = colour + 1;
        if (colour == 16) colour = 0;

        delay = 0;
        while (delay < 12000) delay = delay + 1;
    }
}
