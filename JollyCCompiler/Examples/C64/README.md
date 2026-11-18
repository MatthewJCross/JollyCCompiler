# C64 samples

Select the **C64** target, compile one sample at a time, and load the PRG in VICE or on a Commodore 64. The generated BASIC loader can be started with `RUN`.

- `border_colour.c`: set VIC-II `$D020` to colour 6 (blue).
- `background_colour.c`: set VIC-II `$D021` to colour 0 (black).
- `vic_readback.c`: read the border register and copy it to the background register.

These deliberately use byte-wide accesses. `volatile` is accepted in declarations and casts; the MOS6502 backend emits actual loads/stores for dereferences and does not perform read-elimination optimisation.

**Not yet supported:** `__interrupt`, raster IRQ setup, KERNAL IRQ chaining, NMI, general pointer arithmetic or arbitrary-width pointer dereferences. Interrupts are the next phase after these hardware-access basics.

## Phase 7.2 — KERNAL raster IRQ

- `raster_irq.c`: installs a C64 raster IRQ at scanline 100. The IRQ changes the border to red and acknowledges `$D019`.
- Declare the handler as `__interrupt void name()` (no parameters, no `return` statements).
- Call `__install_raster_irq(100)` from `main`; the argument must be a constant scanline from 0 to 255. The compiler supports **one** IRQ handler per translation unit.
- The generated handler saves A, X and Y and chains to the normal KERNAL IRQ entry at `$EA31`; this is **not** a raw hardware-vector RTI handler. KERNAL keyboard/timer handling remains in place.
- **Important:** The C64 backend currently allocates locals at fixed addresses and uses zero-page scratch locations. Interrupt handlers are therefore **not reentrant** with arbitrary foreground C code. The sample leaves the foreground idle. Avoid calling regular C functions or modifying shared variables in an IRQ until interrupt-safe storage and calling conventions are implemented.
- Do not exit the program while its IRQ vector is installed. Reset VICE to remove the handler.
- Raster lines 256–311, NMI, vector restoration and general-purpose IRQ chaining are not supported yet.

## Phase 7.4 — programmable raster bars

- `raster_bars_custom.c`: custom nine-band palette using constant (line, colour) pairs.
- `raster_bars_two_tone.c`: simple two-band example.
- `__install_raster_bars(60, 6, 180, 0)` installs blue at raster line 60 and black at 180.
- Supply 1–16 pairs, strictly increasing scanlines (0–255), and colours 0–15.
- The old no-argument `__install_raster_bars()` remains supported.

## Phase 7.5: animated raster palette

`raster_bars_animated.c` demonstrates changing raster bar colours while the
IRQ handler is active:

```c
__install_raster_bars(40, 2, 80, 8, 120, 7, 160, 5, 200, 0);
__set_raster_bar_colour(0, 6); // bar index 0, C64 colour 6
```

The colour table is allocated in writable program RAM and read by the
register-only IRQ handler. The first argument to `__set_raster_bar_colour`
must be a compile-time constant index in range. The second can be a literal,
variable or supported expression; only its low four bits are used.

This phase does **not** animate raster positions: scanlines are still fixed
at compile time. It does not make general C interrupt handlers reentrant.
The earlier Phase 7.4 `call` variable-shadowing error is also corrected.

**Validation:** Source/package checks only; build and VICE execution are
required on Windows before treating the effect as verified.

## Phase 8 hardware samples

See `Hardware/`, `Sprites/`, `Input/`, `Sound/`, `Interrupts/` and `Games/`. See `Docs/C64-Phase8.md` for built-ins, limitations and testing.
