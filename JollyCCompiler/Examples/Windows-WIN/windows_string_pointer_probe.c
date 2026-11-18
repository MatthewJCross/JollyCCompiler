/* Phase 4.3 regression: pointer identity and string contents. */
extern int MessageBoxA(void*, char*, char*, unsigned int);
int main()
{
    char* className = "JollyCWindow";
    char* second = "JollyCWindow";
    if (className == 0 || second == 0) {
        MessageBoxA(0, "Null string pointer", "Phase 4.3", 0);
        return 1;
    }
    if (className[0] != 'J' || second[0] != 'J' || className[5] != 'C' || second[5] != 'C') {
        MessageBoxA(0, "String data corrupted", "Phase 4.3", 0);
        return 2;
    }
    MessageBoxA(0, "String pointers and bytes passed", "Phase 4.3", 0);
    return 0;
}
