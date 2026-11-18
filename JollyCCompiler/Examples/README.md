# Compiler examples

- `C64/` — Commodore 64 `.prg` programs (MOS6502 target).
- `Windows-WIN/` — Win32 GUI applications and interactive controls (Windows x64 GUI subsystem).
- `Windows-X64/` — Windows x64 ABI, imports, structures and callback tests.

Choose the appropriate target in JollyCCompiler and compile an individual `.c` file. Examples are not combined into one translation unit.

C64 Phase 7.2 adds `C64/raster_irq.c` (KERNAL IRQ vector + raster interrupt).

C64 Phase 7.3: `C64/raster_bars.c` uses `__install_raster_bars()` to install a standalone IRQ handler. This is a fixed 9-band demo, not a general configurable raster scheduler. Do not combine it with `__interrupt`.

C64 Phase 7.4: `C64/raster_bars_custom.c` and `C64/raster_bars_two_tone.c` demonstrate compile-time configurable raster colour bands.
