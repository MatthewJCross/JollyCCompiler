/* Phase 7.1: read a memory-mapped register through a pointer.
   Copies the border colour to the background colour. */
int main()
{
    volatile unsigned char* border = (volatile unsigned char*)0xD020;
    volatile unsigned char* background = (volatile unsigned char*)0xD021;
    unsigned char colour = 0;
    colour = *border;
    *background = colour;
    return 0;
}
