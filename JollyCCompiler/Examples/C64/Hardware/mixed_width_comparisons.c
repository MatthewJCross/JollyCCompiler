// Regression: C64 mixed-width comparisons and signed extension.
int main()
{
    unsigned char small = 200;
    unsigned int large = 300;
    char negative = -1;
    int signedValue = -2;
    unsigned char result = 0;

    if (small < large) result = result + 1;
    if (large > small) result = result + 1;
    if (negative < small) result = result + 1;
    if (signedValue < negative) result = result + 1;
    if (small != large) result = result + 1;
    if (small == small) result = result + 1;
    // Expected border colour: 6 (blue) if all checks succeed.
    __c64_poke(0xD020, result);
    while (1) { }
    return 0;
}
