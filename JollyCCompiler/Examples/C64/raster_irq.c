// Phase 7.2: KERNAL-vector raster IRQ (PAL and NTSC: line 100).
// The IRQ fires at scanline 100 and sets the border to red.
// Compile as C64 and RUN the resulting PRG in VICE.

__interrupt void raster_irq()
{
    volatile unsigned char* border = (volatile unsigned char*)0xD020;
    volatile unsigned char* vic_irq = (volatile unsigned char*)0xD019;

    *border = 2;
    *vic_irq = 1; // Acknowledge VIC raster IRQ, then chain to KERNAL.
}

int main()
{
    volatile unsigned char* border = (volatile unsigned char*)0xD020;
    *border = 6;

    __install_raster_irq(100);

    while (1)
    {
        // Idle. Do not exit while the interrupt vector points into this PRG.
    }

    return 0;
}
