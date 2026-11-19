// Extracted helpers contain no local variables to avoid shared MOS6502 variable slots.
void sound_init()
{
    // Voice 1 ($D400): four-step alien march, triangle waveform.
    // Voice 2 ($D407): player laser, pulse waveform.
    // Voice 3 ($D40E): alien hit, noise waveform.
    __c64_poke(0xD418, 15); // Master volume.
    __c64_poke(0xD404, 0);
    __c64_poke(0xD40B, 0);
    __c64_poke(0xD412, 0);

    // Pulse-wave march: explicit pulse width and a fast, audible envelope.
    __c64_poke(0xD402, 0x00);
    __c64_poke(0xD403, 0x08);
    __c64_poke(0xD405, 0x00);
    __c64_poke(0xD406, 0xF0);
    __c64_poke(0xD409, 0x00);
    __c64_poke(0xD40A, 0x08);
    __c64_poke(0xD40C, 0x00);
    __c64_poke(0xD40D, 0xF0);
    __c64_poke(0xD413, 0x00);
    __c64_poke(0xD414, 0xF0);
    __c64_poke(0xD417, 0); // No filter routing.
    __c64_poke(0xD418, 15); // Filter off, full master volume.

    // Initialise every byte of the three 63-byte sprite bitmaps.
    // Player at $3800
    // Audible diagnostic tone on voice 1; march will replace it on the first beat.
    __c64_poke(0xD400, 0x00);
    __c64_poke(0xD401, 0x18);
    __c64_poke(0xD404, 0x51);
}

void sound_laser()
{
    __c64_poke(0xD40B, 0);
    __c64_poke(0xD407, 0x30);
    __c64_poke(0xD408, 0x50);
    __c64_poke(0xD40B, 0x41);
}

void sound_explosion()
{
    __c64_poke(0xD412, 0);
    __c64_poke(0xD40E, 0x70);
    __c64_poke(0xD40F, 0x22);
    __c64_poke(0xD412, 0x81);
}

void sound_note_0()
{
    __c64_poke(0xD401, 0x12);
}

void sound_note_1()
{
    __c64_poke(0xD401, 0x18);
}

void sound_note_2()
{
    __c64_poke(0xD401, 0x15);
}

void sound_note_3()
{
    __c64_poke(0xD401, 0x0E);
}

// Phase 9: Space Invaders playable prototype for JollyCCompiler's C64 backend.
// Joystick port 2: left/right and fire. Reset VICE to exit.
// 3 hardware sprites: cannon, mystery UFO and shot; 55 character invaders.
void init_charset()
{
    // Character ROM copied to writable VIC charset RAM at $3800.
    // Custom glyphs: three invader rows and the shield block.
    __c64_charset_init();
    __c64_define_char(90, 24, 60, 126, 219, 255, 36, 66, 129);
    __c64_define_char(91, 36, 126, 219, 255, 126, 36, 66, 165);
    __c64_define_char(92, 60, 126, 255, 219, 255, 90, 129, 66);
    __c64_define_char(93, 255, 255, 255, 255, 255, 255, 255, 255);
    // Vertical alien projectile (screen character code 94).
    __c64_define_char(94, 24, 24, 60, 24, 24, 60, 24, 24);
    // Alternating leg positions for each invader type.
    __c64_define_char(95, 24, 60, 126, 219, 255, 36, 129, 66);
    __c64_define_char(96, 36, 126, 219, 255, 126, 66, 36, 165);
    __c64_define_char(97, 60, 126, 255, 219, 255, 36, 66, 129);

}

void draw_title()
{
    // TITLE: simple character-based logo; fire on joystick port 2 advances.
    __c64_print(15, 5, "SPACE");
    __c64_print(13, 8, "INVADERS");
    __c64_print(10, 13, "JOLLYCCOMPILER");
    __c64_print(12, 16, "VERSION 1.0");
    __c64_print(10, 20, "JOYSTICK PORT 2");
    __c64_print(12, 22, "PRESS FIRE");
}

void draw_attract()
{
    // ATTRACT: scoring reference and start prompt.
    __c64_cls();
    __c64_print(12, 2, "PLAY");
    __c64_print(9, 4, "SPACE INVADERS");
    __c64_print(7, 8, "*SCORE ADVANCE TABLE*");
    __c64_putchar(11, 10, 90, 1);
    __c64_print(14, 10, "= 30 POINTS");
    __c64_putchar(11, 12, 91, 1);
    __c64_print(14, 12, "= 20 POINTS");
    __c64_putchar(11, 14, 92, 5);
    __c64_print(14, 14, "= 10 POINTS");
    __c64_print(29, 2, "SPACE");
    __c64_print(29, 3, "INVADERS");
    __c64_print(29, 7, "SCORE");
    __c64_print(29, 9, "00000");
    __c64_print(29, 12, "HI SCORE");
    __c64_print(29, 14, "00000");
    __c64_print(11, 21, "PRESS FIRE");
}

void init_sprite_bitmaps()
{
    __c64_poke(0x3700, 0);
    __c64_poke(0x3701, 24);
    __c64_poke(0x3702, 0);
    __c64_poke(0x3703, 0);
    __c64_poke(0x3704, 24);
    __c64_poke(0x3705, 0);
    __c64_poke(0x3706, 0);
    __c64_poke(0x3707, 24);
    __c64_poke(0x3708, 0);
    __c64_poke(0x3709, 0);
    __c64_poke(0x370A, 24);
    __c64_poke(0x370B, 0);
    __c64_poke(0x370C, 0);
    __c64_poke(0x370D, 24);
    __c64_poke(0x370E, 0);
    __c64_poke(0x370F, 0);
    __c64_poke(0x3710, 60);
    __c64_poke(0x3711, 0);
    __c64_poke(0x3712, 0);
    __c64_poke(0x3713, 60);
    __c64_poke(0x3714, 0);
    __c64_poke(0x3715, 0);
    __c64_poke(0x3716, 60);
    __c64_poke(0x3717, 0);
    __c64_poke(0x3718, 0);
    __c64_poke(0x3719, 60);
    __c64_poke(0x371A, 0);
    __c64_poke(0x371B, 0);
    __c64_poke(0x371C, 60);
    __c64_poke(0x371D, 0);
    __c64_poke(0x371E, 0);
    __c64_poke(0x371F, 60);
    __c64_poke(0x3720, 0);
    __c64_poke(0x3721, 0);
    __c64_poke(0x3722, 126);
    __c64_poke(0x3723, 0);
    __c64_poke(0x3724, 0);
    __c64_poke(0x3725, 126);
    __c64_poke(0x3726, 0);
    __c64_poke(0x3727, 0);
    __c64_poke(0x3728, 126);
    __c64_poke(0x3729, 0);
    __c64_poke(0x372A, 0);
    __c64_poke(0x372B, 126);
    __c64_poke(0x372C, 0);
    __c64_poke(0x372D, 0);
    __c64_poke(0x372E, 126);
    __c64_poke(0x372F, 0);
    __c64_poke(0x3730, 0);
    __c64_poke(0x3731, 255);
    __c64_poke(0x3732, 0);
    __c64_poke(0x3733, 0);
    __c64_poke(0x3734, 255);
    __c64_poke(0x3735, 0);
    __c64_poke(0x3736, 0);
    __c64_poke(0x3737, 255);
    __c64_poke(0x3738, 0);
    __c64_poke(0x3739, 0);
    __c64_poke(0x373A, 255);
    __c64_poke(0x373B, 0);
    __c64_poke(0x373C, 0);
    __c64_poke(0x373D, 255);
    __c64_poke(0x373E, 0);

    // Enemy at $3840
    __c64_poke(0x3740, 0);
    __c64_poke(0x3741, 0);
    __c64_poke(0x3742, 0);
    __c64_poke(0x3743, 0);
    __c64_poke(0x3744, 0);
    __c64_poke(0x3745, 0);
    __c64_poke(0x3746, 60);
    __c64_poke(0x3747, 0);
    __c64_poke(0x3748, 60);
    __c64_poke(0x3749, 126);
    __c64_poke(0x374A, 255);
    __c64_poke(0x374B, 126);
    __c64_poke(0x374C, 126);
    __c64_poke(0x374D, 255);
    __c64_poke(0x374E, 126);
    __c64_poke(0x374F, 126);
    __c64_poke(0x3750, 255);
    __c64_poke(0x3751, 126);
    __c64_poke(0x3752, 126);
    __c64_poke(0x3753, 255);
    __c64_poke(0x3754, 126);
    __c64_poke(0x3755, 126);
    __c64_poke(0x3756, 255);
    __c64_poke(0x3757, 126);
    __c64_poke(0x3758, 126);
    __c64_poke(0x3759, 255);
    __c64_poke(0x375A, 126);
    __c64_poke(0x375B, 126);
    __c64_poke(0x375C, 255);
    __c64_poke(0x375D, 126);
    __c64_poke(0x375E, 126);
    __c64_poke(0x375F, 255);
    __c64_poke(0x3760, 126);
    __c64_poke(0x3761, 126);
    __c64_poke(0x3762, 255);
    __c64_poke(0x3763, 126);
    __c64_poke(0x3764, 126);
    __c64_poke(0x3765, 255);
    __c64_poke(0x3766, 126);
    __c64_poke(0x3767, 126);
    __c64_poke(0x3768, 255);
    __c64_poke(0x3769, 126);
    __c64_poke(0x376A, 126);
    __c64_poke(0x376B, 255);
    __c64_poke(0x376C, 126);
    __c64_poke(0x376D, 126);
    __c64_poke(0x376E, 255);
    __c64_poke(0x376F, 126);
    __c64_poke(0x3770, 126);
    __c64_poke(0x3771, 255);
    __c64_poke(0x3772, 126);
    __c64_poke(0x3773, 126);
    __c64_poke(0x3774, 255);
    __c64_poke(0x3775, 126);
    __c64_poke(0x3776, 60);
    __c64_poke(0x3777, 0);
    __c64_poke(0x3778, 60);
    __c64_poke(0x3779, 0);
    __c64_poke(0x377A, 0);
    __c64_poke(0x377B, 0);
    __c64_poke(0x377C, 0);
    __c64_poke(0x377D, 0);
    __c64_poke(0x377E, 0);

    // Projectile at $3880
    __c64_poke(0x3780, 0);
    __c64_poke(0x3781, 0);
    __c64_poke(0x3782, 0);
    __c64_poke(0x3783, 0);
    __c64_poke(0x3784, 0);
    __c64_poke(0x3785, 0);
    __c64_poke(0x3786, 0);
    __c64_poke(0x3787, 0);
    __c64_poke(0x3788, 0);
    __c64_poke(0x3789, 0);
    __c64_poke(0x378A, 24);
    __c64_poke(0x378B, 0);
    __c64_poke(0x378C, 0);
    __c64_poke(0x378D, 24);
    __c64_poke(0x378E, 0);
    __c64_poke(0x378F, 0);
    __c64_poke(0x3790, 24);
    __c64_poke(0x3791, 0);
    __c64_poke(0x3792, 0);
    __c64_poke(0x3793, 24);
    __c64_poke(0x3794, 0);
    __c64_poke(0x3795, 0);
    __c64_poke(0x3796, 24);
    __c64_poke(0x3797, 0);
    __c64_poke(0x3798, 0);
    __c64_poke(0x3799, 24);
    __c64_poke(0x379A, 0);
    __c64_poke(0x379B, 0);
    __c64_poke(0x379C, 24);
    __c64_poke(0x379D, 0);
    __c64_poke(0x379E, 0);
    __c64_poke(0x379F, 24);
    __c64_poke(0x37A0, 0);
    __c64_poke(0x37A1, 0);
    __c64_poke(0x37A2, 24);
    __c64_poke(0x37A3, 0);
    __c64_poke(0x37A4, 0);
    __c64_poke(0x37A5, 24);
    __c64_poke(0x37A6, 0);
    __c64_poke(0x37A7, 0);
    __c64_poke(0x37A8, 24);
    __c64_poke(0x37A9, 0);
    __c64_poke(0x37AA, 0);
    __c64_poke(0x37AB, 24);
    __c64_poke(0x37AC, 0);
    __c64_poke(0x37AD, 0);
    __c64_poke(0x37AE, 24);
    __c64_poke(0x37AF, 0);
    __c64_poke(0x37B0, 0);
    __c64_poke(0x37B1, 24);
    __c64_poke(0x37B2, 0);
    __c64_poke(0x37B3, 0);
    __c64_poke(0x37B4, 24);
    __c64_poke(0x37B5, 0);
    __c64_poke(0x37B6, 0);
    __c64_poke(0x37B7, 0);
    __c64_poke(0x37B8, 0);
    __c64_poke(0x37B9, 0);
    __c64_poke(0x37BA, 0);
    __c64_poke(0x37BB, 0);
    __c64_poke(0x37BC, 0);
    __c64_poke(0x37BD, 0);
    __c64_poke(0x37BE, 0);
    // VIC-II sprite pointers: $3800, $3840, $3880.
    __c64_poke(0x07F8, 220);
    __c64_poke(0x07F9, 221);
    __c64_poke(0x07FA, 222);
}

void draw_hud_initial()
{
    // HUD: SCORE and LIVES, C64 screen codes.
    __c64_poke(1024, 19);
    __c64_poke(1025, 3);
    __c64_poke(1026, 15);
    __c64_poke(1027, 18);
    __c64_poke(1028, 5);
    __c64_poke(1030, 48);
    __c64_poke(1040, 12);
    __c64_poke(1041, 9);
    __c64_poke(1042, 22);
    __c64_poke(1043, 5);
    __c64_poke(1044, 19);
    __c64_poke(1046, 51);
}

int main()
{
    unsigned char x = 120;
    unsigned char player_x_high = 0;
    unsigned char shot_x_high = 0;
    unsigned char enemy = 45;
    unsigned char direction = 1;
    unsigned char shot_x = 120;
    unsigned char shot_y = 0;
    unsigned char joy = 0;
    unsigned char fire_lock = 0;
    unsigned char score = 0; // Units digit; every standard alien awards a multiple of ten.
    unsigned char score_tens = 0;
    unsigned char score_hundreds = 0;
    unsigned char score_thousands = 0;
    unsigned char lives = 3;
    unsigned char last_tick = 0;
    unsigned char tick = 0;
    unsigned char step = 0;
    unsigned char col = 0;
    unsigned char march_note = 0;
    unsigned char animation_frame = 0;
    unsigned char march_wait = 0;
    unsigned char laser_wait = 0;
    unsigned char hit_wait = 0;
    unsigned char row = 0;
    unsigned char index = 0;
    unsigned char alive = 55;

    // Leftmost character column 0; rightmost occupied column is x + 20.
    // 11 aliens spaced two columns occupy 21 columns; maximum offset is 19.
    unsigned char formation_x = 0;
    unsigned char formation_y = 0;
    unsigned char glyph = 90;
    unsigned short cell = 0; // C64 screen address: 16-bit
    unsigned char ufo_x = 0;
    unsigned char ufo_active = 0;
    unsigned char ufo_high_x = 0;
    unsigned char ufo_timer = 0;
    unsigned char wave = 1;
    unsigned char enemy_shot_y = 0;
    unsigned char enemy_shot_x = 0;
    unsigned char enemy_shot_active = 0;
    unsigned char enemy_shot_wait = 0;
    unsigned char enemy_shot_cooldown = 45;
    unsigned char enemy_shot_col = 0;
    unsigned char enemy_shot_row = 0;
    unsigned char player_col = 0;
    unsigned char player_invulnerable = 0;
    unsigned char game_over = 0;
    unsigned char game_over_drawn = 0;
    unsigned char shot_col = 0;
    unsigned char shot_row = 0;
    unsigned char lowest_row = 0;
    unsigned char shooter_index = 0;

    __c64_cls();
    __c64_poke(0xD020, 0);
    __c64_poke(0xD021, 0);

    init_charset();
    draw_title();
    joy = __joystick2();
    while ((joy & 16) == 0) joy = __joystick2();
    while (joy & 16) joy = __joystick2();

    draw_attract();
    joy = __joystick2();
    while ((joy & 16) == 0) joy = __joystick2();
    while (joy & 16) joy = __joystick2();
    __c64_cls();

    // SID: three independent voices, updated without blocking gameplay.
    sound_init();

    init_sprite_bitmaps();
    __c64_poke(0xD010, player_x_high); // Clear all sprite X high bits.
    __sprite_colour(0, 1);
    __sprite_colour(1, 5); // Green mystery UFO
    __sprite_colour(2, 7);
    __sprite_position(0, x, 220);
    __sprite_position(1, 0, 40);
    __sprite_position(2, shot_x, 0);
    __sprite_enable(1);

    draw_hud_initial();
    // One byte per invader in reserved RAM $36C0-$36F6 (1 = alive).
    index = 0;
    while (index < 55)
    {
        __c64_poke(0x36C0 + index, 1);
        index = index + 1;
    }

    // Five rows x eleven columns. Top row 30 points, next two 20,
    // bottom two 10. The 8x8 glyphs are spaced two columns apart.
    row = 0;
    while (row < 5)
    {
        col = 0;
        while (col < 11)
        {
            cell = 1024 + 120 + row * 80 + col * 2;
            glyph = 92;
            if (row == 0) glyph = 90;
            if (row == 1) glyph = 91;
            if (row == 2) glyph = 91;
            __c64_poke(cell, glyph);
            __c64_poke(cell + 0xD400, 1);
            if (row >= 3) __c64_poke(cell + 0xD400, 5);
            col = col + 1;
        }
        row = row + 1;
    }

    col = 0;
    while (col < 6)
    {
        __c64_putchar(3 + col, 19, 93, 5);
        __c64_putchar(3 + col, 20, 93, 5);
        __c64_putchar(12 + col, 19, 93, 5);
        __c64_putchar(12 + col, 20, 93, 5);
        __c64_putchar(21 + col, 19, 93, 5);
        __c64_putchar(21 + col, 20, 93, 5);
        __c64_putchar(30 + col, 19, 93, 5);
        __c64_putchar(30 + col, 20, 93, 5);
        col = col + 1;
    }

    __c64_print(1, 24, "LIVES");
    __c64_putchar(7, 24, 51, 5);
    __c64_putchar(10, 24, 92, 5);
    __c64_putchar(12, 24, 92, 5);

    last_tick = __c64_ticks();
    while (1)
    {
        joy = __joystick2();
        if (game_over == 1)
        {
            if (game_over_drawn == 0)
            {
                __sprite_enable(0);
                __c64_poke(0xD404, 0);
                __c64_poke(0xD40B, 0);
                __c64_poke(0xD412, 0);
                __c64_poke(0xD418, 0);
                shot_y = 0;
                __c64_cls();
                __c64_print(14, 12, "GAME OVER");
                __c64_print(12, 14, "PRESS FIRE");
                game_over_drawn = 1;
            }

            if (joy & 16)
            {
                lives = 3;
                x = 120;
                player_x_high = 0;
                shot_x_high = 0;
                score = 0;
                score_tens = 0;
                score_hundreds = 0;
                score_thousands = 0;
                alive = 55;
                formation_x = 0;
                formation_y = 0;
                direction = 1;
                enemy_shot_active = 0;
                enemy_shot_cooldown = 45;
                ufo_active = 0;
                ufo_high_x = 0;
                ufo_timer = 0;
                __c64_poke(0xD010, player_x_high);
                march_wait = 0;
                march_note = 0;
                laser_wait = 0;
                hit_wait = 0;
                player_invulnerable = 0;
                game_over = 0;
                game_over_drawn = 0;
                shot_y = 0;
                fire_lock = 1;
                step = 13;
                __sprite_enable(1);
                __c64_cls();
                sound_init();
                __c64_print(0, 0, "SCORE");
                __c64_print(16, 0, "LIVES");
                __c64_print(1, 24, "LIVES");
                __c64_putchar(7, 24, 51, 5);
                __c64_putchar(10, 24, 92, 5);
                __c64_putchar(12, 24, 92, 5);
                __sprite_position(0, x, 220);
                col = 0;
                while (col < 6)
                {
                    __c64_putchar(3 + col, 19, 93, 5);
                    __c64_putchar(3 + col, 20, 93, 5);
                    __c64_putchar(12 + col, 19, 93, 5);
                    __c64_putchar(12 + col, 20, 93, 5);
                    __c64_putchar(21 + col, 19, 93, 5);
                    __c64_putchar(21 + col, 20, 93, 5);
                    __c64_putchar(30 + col, 19, 93, 5);
                    __c64_putchar(30 + col, 20, 93, 5);
                    col = col + 1;
                }

                index = 0;
                while (index < 55)
                {
                    __c64_poke(0x36C0 + index, 1);
                    index = index + 1;
                }
            }
        }

        if (game_over == 0)
        {
            // VIC sprite X uses 9 bits. Keep the cannon within the visible
            // playfield (24..320), with 2-pixel movement across the 255 boundary.
            if (joy & 4)
            {
                if (player_x_high == 1)
                {
                    if (x < 2)
                    {
                        player_x_high = 0;
                        x = 254;
                    }
                    else x = x - 2;
                }
                else if (x > 24) x = x - 2;
            }

            if (joy & 8)
            {
                if (player_x_high == 0)
                {
                    if (x >= 254)
                    {
                        player_x_high = 1;
                        x = 0;
                    }
                    else x = x + 2;
                }
                else if (x < 64) x = x + 2;
            }

            __c64_poke(0xD010, player_x_high + ufo_high_x * 2 + shot_x_high * 4);
            __sprite_position(0, x, 220);

            if (joy & 16)
            {
                if (fire_lock == 0)
                {
                    if (shot_y == 0)
                    {
                        shot_x = x + 9;
                        shot_x_high = player_x_high;
                        if (player_x_high == 0 && shot_x < x) shot_x_high = 1;
                        shot_y = 205;
                        __c64_poke(0xD010, player_x_high + ufo_high_x * 2 + shot_x_high * 4);
                        __sprite_position(2, shot_x, shot_y);
                        __sprite_enable(5);
                        // Short pulse laser on SID voice 2.
                        sound_laser();
                        laser_wait = 4;
                    }
                }
                fire_lock = 1;
            }
            else
                fire_lock = 0;

            if (shot_y > 0)
            {
                if (shot_y > 40)
                    shot_y = shot_y - 4;
                else
                    shot_y = 0;

                if (shot_y == 0)
                {
                    shot_x_high = 0;
                    __c64_poke(0xD010, player_x_high + ufo_high_x * 2);
                    __sprite_enable(1 + ufo_active * 2);
                }
                else
                {
                    __sprite_position(2, shot_x, shot_y);
                    __sprite_enable(5);
                }
            }

            // Formation: 55 independently tracked aliens, five rows of eleven.
            // Repaint from the alive flags, never resurrect destroyed invaders.
            step = step + 1;
            if (step >= 14)
            {
                step = 0;
                animation_frame = 1 - animation_frame;
                row = 0;
                while (row < 5)
                {
                    col = 0;
                    while (col < 11)
                    {
                        cell = 1024 + 120 + row * 80 + col * 2 + formation_x + formation_y * 40;
                        __c64_poke(cell, 32);
                        col = col + 1;
                    }
                    row = row + 1;
                }
                if (direction == 1)
                {
                    formation_x = formation_x + 1;
                    if (formation_x >= 19)
                    {
                        direction = 0;
                        formation_y = formation_y + 1;
                    }
                }
                else
                {
                    if (formation_x > 0) formation_x = formation_x - 1;
                    if (formation_x == 0)
                    {
                        direction = 1;
                        formation_y = formation_y + 1;
                    }
                }

                row = 0;
                index = 0;
                while (row < 5)
                {
                    col = 0;
                    while (col < 11)
                    {
                        if (__c64_peek(0x36C0 + index) == 1)
                        {
                            cell = 1024 + 120 + row * 80 + col * 2 + formation_x + formation_y * 40;
                            glyph = 92;
                            if (row == 0) glyph = 90;
                            if (row == 1) glyph = 91;
                            if (row == 2) glyph = 91;
                            if (animation_frame == 1) glyph = glyph + 5;
                            __c64_poke(cell, glyph);
                            __c64_poke(cell + 0xD400, 1);
                            if (row >= 3) __c64_poke(cell + 0xD400, 5);
                        }

                        index = index + 1;
                        col = col + 1;
                    }
                    row = row + 1;
                }
            }

            // Mystery ship: periodic bonus target crossing the upper screen.
            ufo_timer = ufo_timer + 1;
            if (ufo_timer >= 240 && ufo_active == 0)
            {
                ufo_timer = 0;
                ufo_active = 1;
                ufo_x = 0;
                ufo_high_x = 0;
                __c64_poke(0xD010, player_x_high + shot_x_high * 4);
            }

            if (ufo_active == 1)
            {
                ufo_x = ufo_x + 1;
                if (ufo_x == 0) ufo_high_x = 1;
                __c64_poke(0xD010, player_x_high + ufo_high_x * 2 + shot_x_high * 4);
                __sprite_position(1, ufo_x, 48);
                __sprite_enable(3);
                if (ufo_high_x == 1)
                {
                    if (ufo_x >= 80) ufo_active = 0;
                }
            }

            if (ufo_active == 0)
            {
                __c64_poke(0xD010, player_x_high + shot_x_high * 4);
                if (shot_y > 0) __sprite_enable(5);
                else __sprite_enable(1);
            }

            // Mystery UFO collision: the laser must reach the top of the screen.
            // Test its visible X position against the UFO's 24-pixel sprite width.
            if (ufo_active == 1 && shot_y > 0)
            {
                if (shot_y >= 40 && shot_y <= 56)
                {
                    if (shot_x_high == ufo_high_x)
                    {
                        if (shot_x >= ufo_x || shot_x + 11 >= ufo_x)
                        {
                            if (shot_x <= ufo_x + 12)
                            {
                                ufo_active = 0;
                                ufo_high_x = 0;
                                shot_y = 0;
                                shot_x_high = 0;
                                score_hundreds = score_hundreds + 1; // 100 bonus points.
                                if (score_hundreds >= 10)
                                {
                                    score_hundreds = score_hundreds - 10;
                                    score_thousands = score_thousands + 1;
                                }
                                __c64_putchar(6, 0, 48 + score_thousands, 1);
                                __c64_putchar(7, 0, 48 + score_hundreds, 1);
                                __c64_putchar(8, 0, 48 + score_tens, 1);
                                __c64_putchar(9, 0, 48, 1);
                                __c64_poke(0xD010, player_x_high);
                                __sprite_enable(1);
                                sound_explosion();
                            }
                        }
                    }
                }
            }

            // Convert the VIC-II sprite coordinates to the actual moving
            // character formation. Screen starts at sprite X=24, Y=50.
            // Each alien occupies one cell, spaced two columns / two rows.
            if (shot_y > 0)
            {
                if (shot_x_high == 0 && shot_x >= 24)
                {
                    shot_col = (shot_x - 13) / 8; // Sprite bitmap has its visible laser at X+11.
                    if (shot_col >= formation_x)
                    {
                        shot_col = shot_col - formation_x;
                        if (shot_col < 21)
                        {
                            if (shot_y >= 74)
                            {
                                if (shot_y >= 74)
                                {
                                    shot_row = (shot_y - 74) / 8; // Screen row 3 starts at VIC Y=74.
                                    if (shot_row >= formation_y)
                                    {
                                        shot_row = shot_row - formation_y;
                                        if (shot_row < 9)
                                        {
                                            if (shot_row < 9)
                                            {
                                                row = shot_row / 2;
                                                col = shot_col / 2;
                                                index = row * 11 + col;
                                                if (__c64_peek(0x36C0 + index) == 1)
                                                {
                                                    __c64_poke(0x36C0 + index, 0);
                                                    alive = alive - 1;
                                                    // Arcade scoring: top 30, middle 20, bottom 10.
                                                    if (row == 0) score_tens = score_tens + 3;
                                                    if (row == 1) score_tens = score_tens + 2;
                                                    if (row == 2) score_tens = score_tens + 2;
                                                    if (row >= 3) score_tens = score_tens + 1;
                                                    if (score_tens >= 10)
                                                    {
                                                        score_tens = score_tens - 10;
                                                        score_hundreds = score_hundreds + 1;
                                                    }
                                                    if (score_hundreds >= 10)
                                                    {
                                                        score_hundreds = score_hundreds - 10;
                                                        score_thousands = score_thousands + 1;
                                                    }
                                                    __c64_putchar(6, 0, 48 + score_thousands, 1);
                                                    __c64_putchar(7, 0, 48 + score_hundreds, 1);
                                                    __c64_putchar(8, 0, 48 + score_tens, 1);
                                                    __c64_putchar(9, 0, 48, 1);
                                                    cell = 1024 + 120 + row * 80 + col * 2 + formation_x + formation_y * 40;
                                                    __c64_poke(cell, 32);
                                                    shot_y = 0;
                                                    __sprite_enable(1);
                                                    sound_explosion();
                                                    hit_wait = 7;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (alive == 0)
            {
                wave = wave + 1;
                alive = 55;
                formation_x = 0;
                formation_y = 0;
                index = 0;
                while (index < 55)
                {
                    __c64_poke(0x36C0 + index, 1);
                    index = index + 1;
                }
            }

            // Alien fire: pick a living invader in a rotating column.
            // One projectile at a time; use character cells for predictable hits.
            if (enemy_shot_active == 0)
            {
                if (enemy_shot_cooldown > 0)
                    enemy_shot_cooldown = enemy_shot_cooldown - 1;
                else
                {
                    enemy_shot_col = enemy_shot_col + 1;
                    if (enemy_shot_col >= 11) enemy_shot_col = 0;
                    lowest_row = 5;
                    row = 0;
                    while (row < 5)
                    {
                        shooter_index = row * 11 + enemy_shot_col;
                        if (__c64_peek(0x36C0 + shooter_index) == 1) lowest_row = row;
                        row = row + 1;
                    }

                    if (lowest_row < 5)
                    {
                        enemy_shot_x = formation_x + enemy_shot_col * 2;
                        enemy_shot_y = 3 + lowest_row * 2 + formation_y + 1;
                        enemy_shot_active = 1;
                    }
                    enemy_shot_cooldown = 35;
                }
            }

            if (enemy_shot_active == 1)
            {
                enemy_shot_wait = enemy_shot_wait + 1;
                if (enemy_shot_wait >= 5)
                {
                    enemy_shot_wait = 0;
                    if (enemy_shot_y < 24)
                    {
                        __c64_putchar(enemy_shot_x, enemy_shot_y, 32, 0);
                        enemy_shot_y = enemy_shot_y + 1;
                        if (enemy_shot_y < 24)
                        {
                            __c64_putchar(enemy_shot_x, enemy_shot_y, 94, 7);
                        }
                        else enemy_shot_active = 0;
                    }
                    else enemy_shot_active = 0;
                }
            }

            // VIC sprite X includes a 24-pixel border offset; character columns do not.
            // Sprite 0 is 24 pixels wide; allow a three-column hitbox.
            player_col = (x - 24) / 8;
            if (player_invulnerable > 0) player_invulnerable = player_invulnerable - 1;
            if (enemy_shot_active == 1)
            {
                if (enemy_shot_y >= 21)
                {
                    if (enemy_shot_y <= 23)
                    {
                        if (enemy_shot_x >= player_col)
                        {
                            if (enemy_shot_x <= player_col + 2)
                            {
                                if (player_invulnerable == 0)
                                {
                                    lives = lives - 1;
                                    player_invulnerable = 80;
                                    sound_explosion();
                                    hit_wait = 12;
                                    if (lives == 0) game_over = 1;
                                }
                                __c64_putchar(enemy_shot_x, enemy_shot_y, 32, 0);
                                enemy_shot_active = 0;
                            }
                        }
                    }
                }
            }

            // Allow the formation to descend farther before invasion ends the game.
            // The lowest alien reaches row 23 at formation_y == 12.
            if (formation_y >= 12)
            {
                lives = 0;
                game_over = 1;
            }

            __c64_putchar(7, 24, 48 + lives, 5);
            __c64_putchar(10, 24, 32, 0);
            __c64_putchar(12, 24, 32, 0);
            if (lives > 1)
                __c64_putchar(10, 24, 92, 5);

            if (lives > 2)
                __c64_putchar(12, 24, 92, 5);
        }

        tick = __c64_ticks();
        while (tick == last_tick) tick = __c64_ticks();
        last_tick = tick;

        // Advance sound envelopes once per PAL tick; no busy-waiting.
        if (laser_wait > 0)
        {
            laser_wait = laser_wait - 1;
            if (laser_wait == 0)
                __c64_poke(0xD40B, 0x40);
        }
        if (hit_wait > 0)
        {
            hit_wait = hit_wait - 1;
            if (hit_wait == 0)
                __c64_poke(0xD412, 0x80);
        }

        // Two-note march: DUH - DAH - DUH - DAH.
        if (game_over == 0)
            march_wait = march_wait + 1;

        if (game_over == 0 && march_wait == 1)
            __c64_poke(0xD404, 0x40);

        if (game_over == 0 && march_wait >= 14)
        {
            march_wait = 0;
            __c64_poke(0xD404, 0x40);
            __c64_poke(0xD400, 0x00);
            if (march_note == 0)
                sound_note_0();

            if (march_note == 1)
                sound_note_1();

            __c64_poke(0xD404, 0x41);
            march_note = 1 - march_note;
        }
    }

    return 0;
}
