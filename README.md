# JollyCCompiler

A small C compiler written in C# that generates native x64 Windows executables.

JollyCCompiler is being built from scratch as a learning project, with the goal of understanding the complete compiler pipeline from C source code through to machine code and a Windows PE executable.

## Current Features

* C-style lexer
* C parser
* Abstract Syntax Tree (AST)
* x64 machine-code generation
* Native Windows PE32+ executable generation
* `main()` function
* Integer return values
* `printf()` support
* String literals
* Common string escape sequences
* Windows x64 calling convention
* `ExitProcess` integration
* `.text`, `.rdata` and `.idata` PE sections

## Example

```c
int main()
{
    printf("Hello from JollyC!\n");
    return 42;
}
```

JollyCCompiler generates a native Windows executable which produces:

```text
Hello from JollyC!
```

The process returns:

```text
42
```

## Architecture

```text
C Source
   |
   v
Lexer
   |
   v
Parser
   |
   v
AST
   |
   v
x64 Code Generator
   |
   v
x64 Emitter
   |
   v
PE Writer
   |
   v
Windows .exe
```

## Project Status

JollyCCompiler is an active development project.

The current focus is expanding the x64 backend with arithmetic expressions and additional C language features.

Planned features include:

* Addition
* Subtraction
* Multiplication
* Division
* Modulo
* Parenthesised expressions
* Variables
* Assignments
* Comparisons
* Conditional statements
* Loops
* Functions
* More complete C syntax

## Requirements

* Windows
* .NET 10
* Visual Studio 2026 or compatible .NET SDK

## Building

Clone the repository and open the solution in Visual Studio.

Build the project normally. Generated executables are written to the project's output directory.

## Disclaimer

JollyCCompiler is primarily an educational and experimental compiler project. It is not intended to be a complete or standards-compliant C compiler.

## License

License information will be added as the project develops.
