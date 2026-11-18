// KERNAL keyboard matrix code at $C5; $40 means no key.
int main()
{
    unsigned char key = 64;
    while (1)
    {
        key = __c64_key();
        if (key == 64) __c64_poke(0xD020, 6);
        else __c64_poke(0xD020, 2);
    }
    return 0;
}
