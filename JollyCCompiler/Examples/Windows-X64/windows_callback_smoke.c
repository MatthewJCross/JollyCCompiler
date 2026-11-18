/* Select WIN. Callback/function-pointer smoke test, without Win32 structs. */
extern int MessageBoxA(void* owner, char* message, char* caption, unsigned int flags);

int increment(int value)
{
    return value + 1;
}

int invoke(int (*callback)(int), int value)
{
    return callback(value);
}

int main()
{
    int result = invoke(increment, 41);
    if (result == 42)
        MessageBoxA(0, "Callback returned 42", "JollyCCompiler", 0);
    return result == 42 ? 0 : 1;
}
