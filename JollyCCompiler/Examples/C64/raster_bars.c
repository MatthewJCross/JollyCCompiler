// Phase 7.3: self-contained raster bars with register-safe IRQ handler.
// Compile for C64, load in VICE and RUN.
// The handler cycles border colours at lines 40,64,...,208,240.
// Works on PAL and NTSC (all raster lines below 256).
int main()
{
    __install_raster_bars();
    while (1)
    {
        // Main code remains free to do other work.
    }
    return 0;
}
