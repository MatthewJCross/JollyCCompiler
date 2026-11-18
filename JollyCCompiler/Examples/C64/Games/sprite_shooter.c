// Phase 8 completion candidate (hardware sprite shooter): joystick 2, sprite movement, firing and score.
// Score is shown by border colour. RESET VICE to exit.
int main()
{
    unsigned char x = 120;
    unsigned char enemy = 40;
    unsigned char shot_y = 0;
    unsigned char shot_x = 120;
    unsigned char joy = 0;
    unsigned char score = 0;
    unsigned char fire_lock = 0;
    unsigned char collisions = 0;
    unsigned char last_tick = 0;
    unsigned char current_tick = 0;
    unsigned char lives = 3;
    unsigned char score_character = 48;
    unsigned char lives_character = 51;
    unsigned char enemy_direction = 1;

    // Initialise every byte of the three 63-byte sprite bitmaps.
    // Player at $3000
    __c64_poke(0x3000, 0);
    __c64_poke(0x3001, 24);
    __c64_poke(0x3002, 0);
    __c64_poke(0x3003, 0);
    __c64_poke(0x3004, 24);
    __c64_poke(0x3005, 0);
    __c64_poke(0x3006, 0);
    __c64_poke(0x3007, 24);
    __c64_poke(0x3008, 0);
    __c64_poke(0x3009, 0);
    __c64_poke(0x300A, 24);
    __c64_poke(0x300B, 0);
    __c64_poke(0x300C, 0);
    __c64_poke(0x300D, 24);
    __c64_poke(0x300E, 0);
    __c64_poke(0x300F, 0);
    __c64_poke(0x3010, 60);
    __c64_poke(0x3011, 0);
    __c64_poke(0x3012, 0);
    __c64_poke(0x3013, 60);
    __c64_poke(0x3014, 0);
    __c64_poke(0x3015, 0);
    __c64_poke(0x3016, 60);
    __c64_poke(0x3017, 0);
    __c64_poke(0x3018, 0);
    __c64_poke(0x3019, 60);
    __c64_poke(0x301A, 0);
    __c64_poke(0x301B, 0);
    __c64_poke(0x301C, 60);
    __c64_poke(0x301D, 0);
    __c64_poke(0x301E, 0);
    __c64_poke(0x301F, 60);
    __c64_poke(0x3020, 0);
    __c64_poke(0x3021, 0);
    __c64_poke(0x3022, 126);
    __c64_poke(0x3023, 0);
    __c64_poke(0x3024, 0);
    __c64_poke(0x3025, 126);
    __c64_poke(0x3026, 0);
    __c64_poke(0x3027, 0);
    __c64_poke(0x3028, 126);
    __c64_poke(0x3029, 0);
    __c64_poke(0x302A, 0);
    __c64_poke(0x302B, 126);
    __c64_poke(0x302C, 0);
    __c64_poke(0x302D, 0);
    __c64_poke(0x302E, 126);
    __c64_poke(0x302F, 0);
    __c64_poke(0x3030, 0);
    __c64_poke(0x3031, 255);
    __c64_poke(0x3032, 0);
    __c64_poke(0x3033, 0);
    __c64_poke(0x3034, 255);
    __c64_poke(0x3035, 0);
    __c64_poke(0x3036, 0);
    __c64_poke(0x3037, 255);
    __c64_poke(0x3038, 0);
    __c64_poke(0x3039, 0);
    __c64_poke(0x303A, 255);
    __c64_poke(0x303B, 0);
    __c64_poke(0x303C, 0);
    __c64_poke(0x303D, 255);
    __c64_poke(0x303E, 0);
    // Enemy at $3040
    __c64_poke(0x3040, 0);
    __c64_poke(0x3041, 0);
    __c64_poke(0x3042, 0);
    __c64_poke(0x3043, 0);
    __c64_poke(0x3044, 0);
    __c64_poke(0x3045, 0);
    __c64_poke(0x3046, 60);
    __c64_poke(0x3047, 0);
    __c64_poke(0x3048, 60);
    __c64_poke(0x3049, 126);
    __c64_poke(0x304A, 255);
    __c64_poke(0x304B, 126);
    __c64_poke(0x304C, 126);
    __c64_poke(0x304D, 255);
    __c64_poke(0x304E, 126);
    __c64_poke(0x304F, 126);
    __c64_poke(0x3050, 255);
    __c64_poke(0x3051, 126);
    __c64_poke(0x3052, 126);
    __c64_poke(0x3053, 255);
    __c64_poke(0x3054, 126);
    __c64_poke(0x3055, 126);
    __c64_poke(0x3056, 255);
    __c64_poke(0x3057, 126);
    __c64_poke(0x3058, 126);
    __c64_poke(0x3059, 255);
    __c64_poke(0x305A, 126);
    __c64_poke(0x305B, 126);
    __c64_poke(0x305C, 255);
    __c64_poke(0x305D, 126);
    __c64_poke(0x305E, 126);
    __c64_poke(0x305F, 255);
    __c64_poke(0x3060, 126);
    __c64_poke(0x3061, 126);
    __c64_poke(0x3062, 255);
    __c64_poke(0x3063, 126);
    __c64_poke(0x3064, 126);
    __c64_poke(0x3065, 255);
    __c64_poke(0x3066, 126);
    __c64_poke(0x3067, 126);
    __c64_poke(0x3068, 255);
    __c64_poke(0x3069, 126);
    __c64_poke(0x306A, 126);
    __c64_poke(0x306B, 255);
    __c64_poke(0x306C, 126);
    __c64_poke(0x306D, 126);
    __c64_poke(0x306E, 255);
    __c64_poke(0x306F, 126);
    __c64_poke(0x3070, 126);
    __c64_poke(0x3071, 255);
    __c64_poke(0x3072, 126);
    __c64_poke(0x3073, 126);
    __c64_poke(0x3074, 255);
    __c64_poke(0x3075, 126);
    __c64_poke(0x3076, 60);
    __c64_poke(0x3077, 0);
    __c64_poke(0x3078, 60);
    __c64_poke(0x3079, 0);
    __c64_poke(0x307A, 0);
    __c64_poke(0x307B, 0);
    __c64_poke(0x307C, 0);
    __c64_poke(0x307D, 0);
    __c64_poke(0x307E, 0);
    // Projectile at $3080
    __c64_poke(0x3080, 0);
    __c64_poke(0x3081, 0);
    __c64_poke(0x3082, 0);
    __c64_poke(0x3083, 0);
    __c64_poke(0x3084, 0);
    __c64_poke(0x3085, 0);
    __c64_poke(0x3086, 0);
    __c64_poke(0x3087, 0);
    __c64_poke(0x3088, 0);
    __c64_poke(0x3089, 0);
    __c64_poke(0x308A, 24);
    __c64_poke(0x308B, 0);
    __c64_poke(0x308C, 0);
    __c64_poke(0x308D, 24);
    __c64_poke(0x308E, 0);
    __c64_poke(0x308F, 0);
    __c64_poke(0x3090, 24);
    __c64_poke(0x3091, 0);
    __c64_poke(0x3092, 0);
    __c64_poke(0x3093, 24);
    __c64_poke(0x3094, 0);
    __c64_poke(0x3095, 0);
    __c64_poke(0x3096, 24);
    __c64_poke(0x3097, 0);
    __c64_poke(0x3098, 0);
    __c64_poke(0x3099, 24);
    __c64_poke(0x309A, 0);
    __c64_poke(0x309B, 0);
    __c64_poke(0x309C, 24);
    __c64_poke(0x309D, 0);
    __c64_poke(0x309E, 0);
    __c64_poke(0x309F, 24);
    __c64_poke(0x30A0, 0);
    __c64_poke(0x30A1, 0);
    __c64_poke(0x30A2, 24);
    __c64_poke(0x30A3, 0);
    __c64_poke(0x30A4, 0);
    __c64_poke(0x30A5, 24);
    __c64_poke(0x30A6, 0);
    __c64_poke(0x30A7, 0);
    __c64_poke(0x30A8, 24);
    __c64_poke(0x30A9, 0);
    __c64_poke(0x30AA, 0);
    __c64_poke(0x30AB, 24);
    __c64_poke(0x30AC, 0);
    __c64_poke(0x30AD, 0);
    __c64_poke(0x30AE, 24);
    __c64_poke(0x30AF, 0);
    __c64_poke(0x30B0, 0);
    __c64_poke(0x30B1, 24);
    __c64_poke(0x30B2, 0);
    __c64_poke(0x30B3, 0);
    __c64_poke(0x30B4, 24);
    __c64_poke(0x30B5, 0);
    __c64_poke(0x30B6, 0);
    __c64_poke(0x30B7, 0);
    __c64_poke(0x30B8, 0);
    __c64_poke(0x30B9, 0);
    __c64_poke(0x30BA, 0);
    __c64_poke(0x30BB, 0);
    __c64_poke(0x30BC, 0);
    __c64_poke(0x30BD, 0);
    __c64_poke(0x30BE, 0);
    __c64_poke(0x07F8, 192);
    __c64_poke(0x07F9, 193);
    __c64_poke(0x07FA, 194);
    __sprite_colour(0, 1);
    __sprite_colour(1, 2);
    __sprite_colour(2, 7);
    __sprite_position(0, x, 220);
    __sprite_position(1, enemy, 80);
    __sprite_position(2, shot_x, 0);
    __sprite_enable(3);

    // Screen memory is $0400. C64 screen codes 48..57 are digits.
    // Show score and remaining lives in the first row.
    __c64_poke(0x0400, 19); // S
    __c64_poke(0x0401, 3);  // C
    __c64_poke(0x0402, 15); // O
    __c64_poke(0x0403, 18); // R
    __c64_poke(0x0404, 5);  // E
    __c64_poke(0x0405, 32); // space
    __c64_poke(0x0407, 48);
    __c64_poke(0x0410, 12); // L
    __c64_poke(0x0411, 9);  // I
    __c64_poke(0x0412, 22); // V
    __c64_poke(0x0413, 5);  // E
    __c64_poke(0x0414, 19); // S
    __c64_poke(0x0416, 51);

    last_tick = __c64_ticks();
    while (1)
    {
        joy = __joystick2();
        if (joy & 4)
        {
            if (x > 25) x = x - 2;
        }
        if (joy & 8)
        {
            if (x < 225) x = x + 2;
        }
        __sprite_position(0, x, 220);

        if (joy & 16)
        {
            if (fire_lock == 0)
            {
                if (shot_y == 0)
                {
                    shot_x = x;
                    shot_y = 210;
                    __sprite_enable(7);
                    __sid_beep(20, 30);
                }
            }
            fire_lock = 1;
        }
        else fire_lock = 0;

        if (shot_y > 0)
        {
            if (shot_y > 8) shot_y = shot_y - 3;
            else
            {
                shot_y = 0;
                __sprite_enable(3);
                __sid_stop();
            }
            __sprite_position(2, shot_x, shot_y);
        }

        if (enemy_direction == 1)
        {
            enemy = enemy + 1;
            if (enemy > 220) enemy_direction = 0;
        }
        else
        {
            enemy = enemy - 1;
            if (enemy < 30) enemy_direction = 1;
        }
        __sprite_position(1, enemy, 80);

        // D01E is a latch: a read clears it. Bits are per sprite,
        // not per collision pair. With three sprites this is approximate.
        collisions = __c64_peek(0xD01E);
        if ((collisions & 6) == 6)
        {
            score = score + 1;
            __c64_poke(0xD020, score);
            score_character = score + 48;
            __c64_poke(0x0407, score_character);
            enemy = 30;
            shot_y = 0;
            __sprite_enable(3);
            __sid_stop();
        }

        // A basic player/enemy contact penalty. VIC-II collision latch
        // identifies participants, not exact pairs; keep this as a demo.
        if ((collisions & 3) == 3)
        {
            if (lives > 0)
            {
                lives = lives - 1;
                lives_character = lives + 48;
                __c64_poke(0x0416, lives_character);
            }
        }
        if (lives == 0)
        {
            __sprite_enable(0);
            __sid_stop();
            while (1) { }
        }

        // KERNAL jiffy clock: wait for the next system tick (~50 Hz PAL).
        // No CIA timer reconfiguration and no CPU-speed-dependent busy loop.
        current_tick = __c64_ticks();
        while (current_tick == last_tick)
        {
            current_tick = __c64_ticks();
        }
        last_tick = current_tick;
    }
    return 0;
}
