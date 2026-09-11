# JollyCCompiler Roadmap

## Current Status

The core compiler foundation is now substantially complete.

### Completed

- [x] `char`
- [x] `unsigned char`
- [x] `short`
- [x] `unsigned short`
- [x] `int`
- [x] `unsigned int`
- [x] `long`
- [x] `unsigned long`
- [x] `long long`
- [x] `unsigned long long`
- [x] `float`
- [x] `double`
- [x] `void`
- [x] Explicit casts
- [x] `sizeof`
- [x] Pointers
- [x] Pointer arithmetic
- [x] Arrays
- [x] Structs
- [x] Unions
- [x] Struct/union member access
- [x] Function parameters
- [x] Multiple arguments
- [x] Function calls
- [x] Function return values
- [x] Integer arithmetic
- [x] Floating-point arithmetic
- [x] Signed comparisons
- [x] Unsigned comparisons
- [x] Signed division/remainder
- [x] Unsigned division/remainder
- [x] Assignment
- [x] Compound assignment
- [x] `++` / `--`
- [x] `if` / `else`
- [x] `for`
- [x] `while`
- [x] `do while`
- [x] `switch`
- [x] `break`
- [x] `continue`
- [x] Mixed integer/floating operations
- [x] Unsigned integer regression tests
- [x] Long/unsigned long regression tests
- [x] Long long/unsigned long long regression tests

The current regression suite completes successfully with matching `got` and `expected` values.

---

# Phase 2 — Storage and Program Structure

## 1. Global Variables

**Priority: High**

The current AST and code generator are primarily function-local. Add program-level variables.

### Parser

Add global declarations to `ProgramNode`.

Example:

```c
int globalValue;
unsigned int globalCounter;
char globalChar;
```

Support:

```c
int globalValue = 123;
unsigned int globalCounter = 42;
```

### AST

Extend `ProgramNode` with a global-variable collection.

For example:

```text
Program
 ├── Structs
 ├── Unions
 ├── Globals
 └── Functions
```

### Code Generator

Generate global storage in the PE data sections.

Separate:

- initialized globals
- zero-initialized globals

### Tests

Add tests for:

- global integer
- global unsigned integer
- global char
- global short
- global long
- global long long
- global float
- global double
- global pointer
- global struct
- global union
- reading globals
- writing globals
- globals accessed from multiple functions

---

## 2. Static Storage

**Priority: High**

Implement file/function static storage.

Examples:

```c
static int counter;
static int value = 10;
```

Inside functions:

```c
int test(void)
{
    static int counter = 0;
    counter++;
    return counter;
}
```

Required behaviour:

- storage exists for the lifetime of the program
- value is preserved between function calls
- initializer is evaluated according to C static-storage rules
- zero initialization when no initializer is supplied

---

## 3. Global Initializer Support

**Priority: High**

Add proper initialization for global objects.

Support:

```c
int values[4] = { 1, 2, 3, 4 };
char text[] = "hello";
struct Point p = { 10, 20 };
```

This requires an initializer representation rather than treating every initializer as a single expression.

Suggested AST direction:

```text
Initializer
 ├── ExpressionInitializer
 └── ListInitializer
      ├── Initializer
      ├── Initializer
      └── ...
```

---

# Phase 3 — Declarations and Type System

## 4. `typedef`

**Priority: High**

Support:

```c
typedef unsigned int uint32;
typedef struct Point Point;
typedef int* IntPtr;
```

Then:

```c
uint32 value;
Point point;
IntPtr pointer;
```

The parser should resolve typedef names as types.

---

## 5. `enum`

**Priority: Medium**

Support:

```c
enum Color
{
    RED,
    GREEN,
    BLUE
};
```

And:

```c
enum Color color;
```

Support explicit values:

```c
enum Error
{
    ERROR_NONE = 0,
    ERROR_FILE = 10,
    ERROR_MEMORY = 20
};
```

---

## 6. Type Qualifiers

**Priority: Medium**

Add:

```c
const
volatile
restrict
```

`const` is already partially supported.

Focus first on correct semantic handling rather than optimisation.

---

## 7. `extern`

**Priority: Medium**

Support declarations such as:

```c
extern int globalValue;
extern int printf(const char*, ...);
```

This becomes particularly important when external Win32 functions are added.

---

# Phase 4 — Function Pointers

## 8. Function Pointer Types

**Priority: High**

Support:

```c
int (*function)(int, int);
```

Assignment:

```c
function = add;
```

Call:

```c
int result = function(10, 20);
```

Support passing function pointers:

```c
int apply(int (*fn)(int, int), int a, int b)
{
    return fn(a, b);
}
```

Required compiler work:

- function symbols as addresses
- function pointer types
- indirect calls
- function pointer parameters
- function pointer returns

---

# Phase 5 — Strings and Character Handling

## 9. String Literals

**Priority: High**

Support:

```c
char* text = "Hello";
```

And:

```c
return "Hello";
```

Generate string data into the PE read-only/data section.

Support:

- escaped characters
- `\`
- `"`
- `\n`
- `\r`
- `\t`
- `\0`

---

## 10. Character Arrays

Support:

```c
char text[] = "Hello";
```

and:

```c
char text[6] = "Hello";
```

Eventually:

```c
char text[10] = { 'H', 'i', 0 };
```

---

# Phase 6 — Complete Expression Support

## 11. Bitwise Operators

**Priority: High**

Implement:

```c
&
|
^
~
```

For example:

```c
unsigned int flags = value & mask;
flags |= FLAG_A;
flags &= ~FLAG_B;
flags ^= FLAG_C;
```

---

## 12. Shift Operators

Implement:

```c
<<
>>
```

Pay particular attention to:

- signed right shift
- unsigned right shift
- shift-count masking
- integer promotions

---

## 13. Conditional Operator

Implement:

```c
condition ? value1 : value2
```

Example:

```c
int result = a > b ? a : b;
```

This will require correct common-type handling for the two result expressions.

---

## 14. Comma Operator

Implement:

```c
a = (x++, y++, x + y);
```

---

## 15. Complete Implicit Conversions

Audit all expressions for C's usual arithmetic conversions.

Important areas:

- `char` → `int`
- `unsigned char` → `int`
- `short` → `int`
- `unsigned short` → `int`
- `int` ↔ `unsigned int`
- `long` ↔ `unsigned long`
- `long long` ↔ `unsigned long long`
- integer ↔ floating point
- pointer conversions

The existing `GetCommonArithmeticType` should be retained where correct, but systematically tested rather than replaced blindly.

---

# Phase 7 — Struct and Union Completeness

## 16. Struct Initializers

Support:

```c
struct Point p = { 10, 20 };
```

Designated initializers can follow later.

---

## 17. Union Initializers

Support:

```c
union Value v = { 123 };
```

---

## 18. Nested Structures

Support:

```c
struct Outer
{
    struct Point point;
    int value;
};
```

And:

```c
outer.point.x = 10;
```

---

## 19. Nested Arrays and Structures

Support combinations such as:

```c
struct Data
{
    int values[4];
    struct Point points[2];
};
```

---

# Phase 8 — Preprocessor

## 20. Basic Preprocessor

**Priority: High for real-world C compatibility**

Implement:

```c
#define
#undef
#ifdef
#ifndef
#if
#else
#elif
#endif
```

Start with object-like macros:

```c
#define SIZE 100
```

Then function-like macros:

```c
#define MAX(a,b) ((a) > (b) ? (a) : (b))
```

---

## 21. Includes

Support:

```c
#include "file.h"
#include <file.h>
```

Initially, only a simple project-local include system is required.

---

# Phase 9 — Multiple Source Files

## 22. Compilation Units

Support compiling:

```text
main.c
math.c
data.c
```

and linking them into one PE executable.

This requires:

- external symbols
- unresolved symbols
- symbol resolution
- cross-file globals
- cross-file functions
- duplicate-symbol diagnostics

---

# Phase 10 — Windows / Runtime Integration

## 23. External Win32 API Calls

Add support for calling Windows APIs.

Initial target examples:

```c
GetStdHandle
WriteFile
ExitProcess
```

Then:

```c
MessageBoxA
CreateWindowExA
```

---

## 24. Calling Convention Support

Initially support the Windows x64 calling convention.

Handle:

- RCX
- RDX
- R8
- R9
- XMM0–XMM3
- stack arguments
- shadow space
- return values

Then formalise this in the type/code-generation system.

---

## 25. C Runtime / Library Functions

Eventually support selected C runtime functions such as:

```c
memcpy
memset
strlen
strcmp
```

Decide whether each function should be:

- compiler intrinsic
- internal runtime implementation
- external DLL import

---

# Phase 11 — Diagnostics

## 26. Parser Diagnostics

Improve errors such as:

```text
expected ';'
expected ')'
expected expression
unknown type
```

Include:

- source file
- line
- column
- source location

---

## 27. Semantic Diagnostics

Detect:

- undefined variables
- duplicate variables
- undefined functions
- duplicate functions
- invalid assignments
- invalid casts
- invalid pointer operations
- incorrect argument counts
- incompatible types
- invalid struct members
- invalid union members
- invalid return types

---

# Phase 12 — Optimisation

Optimisation should come **after correctness**.

## 28. Constant Folding

Examples:

```c
int x = 10 + 20 * 3;
```

becomes:

```text
70
```

---

## 29. Dead Code Elimination

Examples:

```c
if (0)
{
    ...
}
```

---

## 30. Simple Register Improvements

Reduce unnecessary:

- moves
- loads
- stores
- stack traffic

---

## 31. Basic Block Optimisation

Introduce an intermediate representation if the existing direct AST → x64 approach becomes difficult to maintain.

Do not introduce an IR prematurely.

---

# Phase 13 — Remaining C Language Features

After the main infrastructure is complete, work through the remaining language features.

Potential items:

- [ ] `do` / `while` edge cases
- [ ] `goto`
- [ ] labels
- [ ] `switch` edge cases
- [ ] `default`
- [ ] fall-through behaviour
- [ ] variadic functions
- [ ] `...`
- [ ] compound literals
- [ ] designated initializers
- [ ] flexible array members
- [ ] incomplete array types
- [ ] incomplete struct types
- [ ] forward declarations
- [ ] recursive structures
- [ ] `_Bool`
- [ ] `_Static_assert`
- [ ] storage-class rules
- [ ] declaration combinations
- [ ] function declarations without definitions
- [ ] function prototypes
- [ ] pointer-to-pointer cases
- [ ] complex pointer expressions

---

# Phase 14 — Regression Test Organisation

The regression suite is already proving extremely useful.

Keep expanding it by feature rather than relying only on one large total.

Suggested structure:

```text
tests/
    basic/
    integers/
    unsigned/
    floating/
    pointers/
    arrays/
    structs/
    unions/
    functions/
    control-flow/
    casts/
    sizeof/
    globals/
    typedef/
    enums/
    strings/
    function-pointers/
    preprocessor/
```

Each test should ideally provide:

```text
got
expected
running
```

and finish with:

```text
FINAL: got=... expected=... difference=0
```

Also add negative compiler tests where compilation is expected to fail.

---

# Recommended Implementation Order

The recommended order from the current state is:

1. **Global variables**
2. **Static storage**
3. **Global/static initializers**
4. **Initializer lists**
5. **`typedef`**
6. **`enum`**
7. **Function pointers**
8. **String literals**
9. **Bitwise operators**
10. **Shift operators**
11. **Conditional operator**
12. **Complete implicit conversions**
13. **Struct/union initializers**
14. **Preprocessor**
15. **Multiple source files**
16. **External Win32 functions**
17. **Improved diagnostics**
18. **Optimisation**

---

# Immediate Next Target

## `feat: add global variable support`

This is the best next milestone because the compiler currently has a strong function/local-variable foundation, but `ProgramNode` has no global-variable collection.

The first implementation should be deliberately small:

### Step 1

Add globals to the AST:

```text
Program
 ├── Structs
 ├── Unions
 ├── Globals
 └── Functions
```

### Step 2

Allow top-level declarations:

```c
int globalValue;
unsigned int globalCounter;
char globalChar;
```

### Step 3

Generate zero-initialised storage.

### Step 4

Generate reads:

```c
return globalValue;
```

### Step 5

Generate writes:

```c
globalValue = 123;
```

### Step 6

Add initialised globals:

```c
int globalValue = 123;
```

### Step 7

Add regression tests.

Once this is working, move to static storage and proper initializer lists.

---

# Overall Goal

The goal is not simply to make a collection of C examples compile.

The long-term goal is a robust native Windows x64 C compiler with:

- correct C type semantics
- reliable x64 code generation
- PE executable generation
- pointers and aggregates
- global/static storage
- function pointers
- strings
- preprocessor support
- multiple source files
- Windows API interoperability
- strong diagnostics
- comprehensive regression testing

The current compiler has already reached a solid foundation. The next major architectural step is moving from a primarily function-local compiler to a complete program-level compiler through **global variables and static storage**.
