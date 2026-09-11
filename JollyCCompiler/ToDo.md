# JollyCCompiler TODO

## Compiler

1. [x] if / else
2. [x] break / continue
3. [ ] function parameters ← next
4. [ ] multiple functions
5. [ ] function calls
6. [ ] pointers
7. [ ] address-of `&`
8. [ ] dereference `*`
9. [ ] char / strings properly
10. [ ] structs
11. [ ] function pointers
12. [ ] external Win32 API calls
13. [ ] GUI PE subsystem
14. [ ] CreateWindowEx
15. [ ] Windows message loop

---

## GUI

### What I Would Build

```text
┌──────────────────────────────────────────────────────────────┐
│ JollyCCompiler       File  Build  Run  Tools                 │
├───────────────────────┬──────────────────────────────────────┤
│                       │                                      │
│   C Source Editor     │        Compilation Output            │
│                       │                                      │
│   int main()          │  ✓ Lexing successful                 │
│   {                   │  ✓ Parsing successful                │
│       int x = 10;     │  ✓ Code generation successful        │
│                       │                                      │
│       if (x > 5)      │  Generated: Test.exe                 │
│       {               │                                      │
│           printf(...  │                                      │
│       }               │                                      │
│   }                   │                                      │
│                       │                                      │
├───────────────────────┴──────────────────────────────────────┤
│ Errors: 0     Warnings: 0                    Ready           │
└──────────────────────────────────────────────────────────────┘

Features I'd add

Editor

C syntax highlighting
Line numbers
Current-line highlighting
Basic autocomplete
Error squiggles
Go-to-line
Open/save .c files

Compiler panel

Tokens
AST
Generated x64 instructions
PE information
Diagnostics

Build

Compile
Build EXE
Run
Clean
Output path selection

Visual compiler pipeline

We could even have a tab showing:

Source
  ↓
Lexer
  ↓
Tokens
  ↓
Parser
  ↓
AST
  ↓
x64 Code Generator
  ↓
Machine Code
  ↓
PE Writer
  ↓
.exe

Clicking each stage could show the actual output.