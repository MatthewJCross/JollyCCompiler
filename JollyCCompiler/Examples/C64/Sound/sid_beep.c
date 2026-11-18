// Joystick fire holds a SID triangle tone, release stops it.
int main()
{
    unsigned char joy = 0;
    while (1)
    {
        joy = __joystick2();
        if (joy >= 16) __sid_beep(40, 25);
        else __sid_stop();
    }
    return 0;
}
