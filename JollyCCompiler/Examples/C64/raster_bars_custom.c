// Phase 7.4: programmable raster bands (compile-time line/colour pairs).
// Lines must increase, from 0 to 255; colours are C64 palette indexes 0..15.
// Run in VICE with the C64 target.
int main()
{
    __install_raster_bars(40, 2, 64, 8, 88, 7, 112, 5,
                          136, 4, 160, 6, 184, 14, 208, 3, 240, 0);
    while (1) { }
    return 0;
}
