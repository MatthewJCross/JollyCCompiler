// Joystick port 2: fire button makes border red, otherwise blue.
int main()
{
    unsigned char joy = 0;
    while (1)
    {
        joy = __joystick2();
        if (joy >= 16) __c64_poke(0xD020, 2);
        else __c64_poke(0xD020, 6);
    }
    return 0;
}
