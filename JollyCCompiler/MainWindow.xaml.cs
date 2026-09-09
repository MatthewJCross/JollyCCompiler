using JollyCCompiler.Compiler.CodeGen;
using JollyCCompiler.Compiler.CodeGen.X64;
using JollyCCompiler.Compiler.Compilation;
using JollyCCompiler.Compiler.Syntax;
using JollyCCompiler.Object;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace JollyCCompiler
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string? _currentFile;
        private string? _compiledOutputPath;
        private Process? _consoleProcess;

        public MainWindow()
        {
            InitializeComponent();

            Editor.Text = SampleSource;
            StatusText.Text = "Ready";
        }


        private static string SampleSource =>
        """
        struct Point 
        {
            int x;
            int y;
        };

        struct Data 
        {
            char id;
            int value;
            int count;
        };

        union Value
        {
            int number;
            char letter;
        };
        
        int add(int a, int b) 
        {
            return a + b;
        }

        int subtract(int a, int b) 
        {
            return a - b;
        }

        int multiply(int a, int b) 
        {
            return a * b;
        }

        int increment(int value) 
        {
            return value + 1;
        }

        int decrement(int value)
        {
            return value - 1;
        }

        int square(int value) 
        {
            return value * value;
        }

        int sumArray(int values[10])
        {
            int i;
            int total;

            i = 0;
            total = 0;

            while (i < 10) 
            {
                total += values[i];
                i++;
            }

            return total;
        }

        int sumFirstFive(int values[10]) 
        {
            int i;
            int total;

            i = 0;
            total = 0;

            while (i < 5) 
            {
                total += values[i];
                i++;
            }

            return total;
        }

        int pointerValue(int* value)
        {
            return *value;
        }

        int pointerAdd(int* value, int amount)
        {
            *value += amount;
            return *value;
        }

        int pointerSubtract(int* value, int amount) 
        {
            *value -= amount;
            return *value;
        }

        int getPointX(struct Point* point) 
        {
            return point->x;
        }

        int getPointY(struct Point* point)
        {
            return point->y;
        }

        int pointTotal(struct Point* point)
        {
            return point->x + point->y;
        }

        int getDataValue(struct Data* data)
        {
            return data->value;
        }

        int getDataCount(struct Data* data)
        {
            return data->count;
        }

        int charToInt(char value) 
        {
            return value;
        }

        int testCharArray(char values[10])
        {
            int i;
            int total;

            i = 0;
            total = 0;

            while (i < 10) 
            {
                total += values[i];
                i++;
            }

            return total;
        }

        int testForLoop(int start, int end) 
        {
            int i;
            int total;

            total = 0;

            for (i = start; i < end; i++) 
            {
                total += i;
            }

            return total;
        }

        int testNestedLoops(int limit) 
        {
            int i;
            int j;
            int total;

            i = 0;
            total = 0;

            while (i < limit) 
            {
                j = 0;

                while (j < limit) 
                {
                    total += i;
                    total += j;
                    j++;
                }

                i++;
            }

            return total;
        }

        int testBreak(int limit) 
        {
            int i;
            int total;

            i = 0;
            total = 0;

            while (i < 100) 
            {
                if (i == limit)
                {
                    break;
                }

                total += i;
                i++;
            }

            return total;
        }

        int testContinue(int limit)
        {
            int i;
            int total;

            i = 0;
            total = 0;

            while (i < limit)
            {
                i++;

                if (i % 2 == 0)
                {
                    continue;
                }

                total += i;
            }

            return total;
        }

        int testDoWhile(int limit) 
        {
            int i;
            int total;

            i = 0;
            total = 0;

            do
            {
                total += i;
                i++;
            } while (i < limit);

            return total;
        }

        int testSwitch(int value) 
        {
            int result;

            result = 0;

            switch (value)
            {
                case 0:
                    result = 10;
                    break;

                case 1:
                    result = 20;
                    break;

                case 2:
                    result = 30;
                    break;

                case 3:
                    result = 40;
                    break;

                case 4:
                    result = 50;
                    break;

                default:
                    result = 99;
                    break;
            }

            return result;
        }

        int testCompound(int value)
        {
            int result;

            result = value;

            result += 10;
            result -= 3;
            result *= 2;
            result /= 2;
            result %= 7;

            return result;
        }

        int testIncrement(int value) 
        {
            int result;

            result = value;

            result++;
            ++result;
            result--;
            --result;

            return result;
        }

        int testPointerArithmetic(int* values)
        {
            int* p;
            int total;

            p = values;
            total = 0;

            total += *p;
            p++;
            total += *p;
            p++;
            total += *p;
            p++;
            total += *p;

            return total;
        }

        int testPointerIndexing(int* values) 
        {
            int total;

            total = 0;

            total += values[0];
            total += values[1];
            total += values[2];
            total += values[3];
            total += values[4];

            return total;
        }

        int testPointerWrite(int* value) 
        {
            *value = 1234;
            return *value;
        }

        int testCharPointer(char* value)
        {
            *value = 77;
            return *value;
        }

        int testCharIncrement(char* value) 
        {
            (*value)++;
            return *value;
        }

        int testCharDecrement(char* value)
        {
            (*value)--;
            return *value;
        }

        int testStruct(struct Point* point) 
        {
            point->x += 10;
            point->y += 20;

            return point->x + point->y;
        }

        int testStructValues()
        {
            struct Point point;
            int result;

            point.x = 100;
            point.y = 200;

            result = point.x;
            result += point.y;

            return result;
        }

        int testDataStruct() 
        {
            struct Data data;
            int result;

            data.id = 65;
            data.value = 1000;
            data.count = 50;

            result = data.id;
            result += data.value;
            result += data.count;

            return result;
        }

        int testNestedCalculations() 
        {
            int a;
            int b;
            int c;
            int d;
            int e;
            int result;

            a = 10;
            b = 20;
            c = 30;
            d = 40;
            e = 50;

            result = add(a, b);
            result = add(result, c);
            result = subtract(result, d);
            result = add(result, e);
            result = multiply(result, 2);
            result = subtract(result, 10);
            result = square(result);

            return result;
        }

        int testConditionals(int value)
        {
            int result;

            if (value < 0)
            {
                result = 1;
            } 
            else if (value == 0)
            {
                result = 2;
            }
            else if (value < 10) 
            {
                result = 3;
            }
            else if (value < 100) 
            {
                result = 4;
            } 
            else
            {
                result = 5;
            }

            return result;
        }

        int testLogical(int a, int b)
        {
            int result;

            result = 0;

            if (a > 0 && b > 0) 
            {
                result += 10;
            }

            if (a == 0 || b == 0)
            {
                result += 20;
            }

            if (a != b) 
            {
                result += 30;
            }

            if (a <= b) 
            {
                result += 40;
            }

            if (a >= b)
            {
                result += 50;
            }

            return result;
        }

        int testRelations(int a, int b)
        {
            int result;

            result = 0;

            if (a == b) 
            {
                result += 1;
            }

            if (a != b) 
            {
                result += 2;
            }

            if (a < b) 
            {
                result += 4;
            }

            if (a <= b)
            {
                result += 8;
            }

            if (a > b)
            {
                result += 16;
            }

            if (a >= b) 
            {
                result += 32;
            }

            return result;
        }

        int testArrays() 
        {
            int values[10];
            int result;

            values[0] = 10;
            values[1] = 20;
            values[2] = 30;
            values[3] = 40;
            values[4] = 50;
            values[5] = 60;
            values[6] = 70;
            values[7] = 80;
            values[8] = 90;
            values[9] = 100;

            result = values[0];
            result += values[3];
            result += values[6];
            result += values[9];

            return result;
        }

        int testCharValues()
        {
            char a;
            char b;
            char c;
            char d;
            char e;
            int result;

            a = 10;
            b = 20;
            c = 30;
            d = 40;
            e = 50;

            result = a;
            result += b;
            result += c;
            result += d;
            result += e;

            return result;
        }

        int testLargeCharValues()
        {
            char a;
            char b;
            char c;
            int result;

            a = 255;
            b = 128;
            c = 300;

            result = a;
            result += b;
            result += c;

            return result;
        }

        int testCharArrayValues()
        {
            char values[10];
            int result;

            values[0] = 1;
            values[1] = 2;
            values[2] = 3;
            values[3] = 4;
            values[4] = 5;
            values[5] = 6;
            values[6] = 7;
            values[7] = 8;
            values[8] = 9;
            values[9] = 10;

            result = values[0];
            result += values[1];
            result += values[2];
            result += values[3];
            result += values[4];
            result += values[5];
            result += values[6];
            result += values[7];
            result += values[8];
            result += values[9];

            return result;
        }

        int testCharArrayCompound()
        {
            char values[4];
            int result;

            values[0] = 10;
            values[1] = 20;
            values[2] = 30;
            values[3] = 40;

            values[0] += 5;
            values[1] -= 5;
            values[2] *= 2;
            values[3] /= 2;

            result = values[0];
            result += values[1];
            result += values[2];
            result += values[3];

            return result;
        }

        int testArrayIncrement() 
        {
            int values[5];
            int result;

            values[0] = 10;
            values[1] = 20;
            values[2] = 30;
            values[3] = 40;
            values[4] = 50;

            values[0]++;
            ++values[1];
            values[2]--;
            --values[3];

            result = values[0];
            result += values[1];
            result += values[2];
            result += values[3];
            result += values[4];

            return result;
        }

        int testCharIncrementArray()
        {
            char values[4];
            int result;

            values[0] = 10;
            values[1] = 20;
            values[2] = 30;
            values[3] = 40;

            values[0]++;
            values[1]++;
            values[2]--;
            values[3]--;

            result = values[0];
            result += values[1];
            result += values[2];
            result += values[3];

            return result;
        }

        int testPointerAssignment() 
        {
            int value;
            int* pointer;
            int result;

            value = 100;
            pointer = &value;

            result = *pointer;

            *pointer = 250;

            result += *pointer;

            return result;
        }

        int testPointerCompound()
        {
            int value;
            int* pointer;

            value = 100;
            pointer = &value;

            *pointer += 50;

            return *pointer;
        }

        int testAddressOf() 
        {
            int value;
            int* pointer;

            value = 123;
            pointer = &value;

            return pointerValue(pointer);
        }

        int testMultiplePointers() 
        {
            int a;
            int b;
            int c;
            int* pa;
            int* pb;
            int* pc;
            int result;

            a = 10;
            b = 20;
            c = 30;

            pa = &a;
            pb = &b;
            pc = &c;

            result = *pa;
            result += *pb;
            result += *pc;

            *pa = 100;
            *pb = 200;
            *pc = 300;

            result += *pa;
            result += *pb;
            result += *pc;

            return result;
        }

        int testArrayPointerMove() 
        {
            int values[5];
            int* pointer;
            int result;

            values[0] = 11;
            values[1] = 22;
            values[2] = 33;
            values[3] = 44;
            values[4] = 55;

            pointer = &values[0];

            result = *pointer;

            pointer++;
            result += *pointer;

            pointer++;
            result += *pointer;

            pointer++;
            result += *pointer;

            pointer++;
            result += *pointer;

            return result;
        }

        int testStructPointer() 
        {
            struct Point point;
            struct Point* pointer;
            int result;

            point.x = 12;
            point.y = 34;

            pointer = &point;

            result = pointer->x;
            result += pointer->y;

            pointer->x = 56;
            pointer->y = 78;

            result += pointer->x;
            result += pointer->y;

            return result;
        }

        int testStructChar() 
        {
            struct Data data;
            struct Data* pointer;
            int result;

            data.id = 65;
            data.value = 100;
            data.count = 5;

            pointer = &data;

            result = pointer->id;
            result += pointer->value;
            result += pointer->count;

            pointer->id++;
            pointer->value += 100;
            pointer->count++;

            result += pointer->id;
            result += pointer->value;
            result += pointer->count;

            return result;
        }

        int testSizeof()
        {
            int a;
            char c;
            int values[10];
            int result;

            a = sizeof(int);
            c = sizeof(char);

            result = a;
            result += c;
            result += sizeof(values);

            return result;
        }

        int testSizeofPointers() 
        {
            int value;
            int* pointer;
            char character;
            char* charPointer;
            int result;

            value = 10;
            character = 20;

            pointer = &value;
            charPointer = &character;

            result = sizeof(pointer);
            result += sizeof(charPointer);

            return result;
        }

        int testNull()
        {
            int* pointer;
            int result;

            pointer = NULL;

            if (pointer == NULL)
            {
                result = 123;
            } 
            else 
            {
                result = 456;
            }

            return result;
        }

        int testManyLocals()
        {
            int a;
            int b;
            int c;
            int d;
            int e;
            int f;
            int g;
            int h;
            int i;
            int j;
            int k;
            int l;
            int m;
            int n;
            int o;
            int p;
            int q;
            int r;
            int s;
            int t;
            int result;

            a = 1;
            b = 2;
            c = 3;
            d = 4;
            e = 5;
            f = 6;
            g = 7;
            h = 8;
            i = 9;
            j = 10;
            k = 11;
            l = 12;
            m = 13;
            n = 14;
            o = 15;
            p = 16;
            q = 17;
            r = 18;
            s = 19;
            t = 20;

            result = a;
            result += b;
            result += c;
            result += d;
            result += e;
            result += f;
            result += g;
            result += h;
            result += i;
            result += j;
            result += k;
            result += l;
            result += m;
            result += n;
            result += o;
            result += p;
            result += q;
            result += r;
            result += s;
            result += t;

            return result;
        }

        int testMathChain()
        {
            int value;

            value = 5;

            value = add(value, 10);
            value = multiply(value, 3);
            value = subtract(value, 7);
            value = increment(value);
            value = increment(value);
            value = decrement(value);
            value = square(value);

            return value;
        }

        int testAllArithmetic() 
        {
            int a;
            int b;
            int result;

            a = 100;
            b = 7;

            result = a + b;
            result -= a - b;
            result += a * b;
            result -= a / b;
            result += a % b;

            return result;
        }

        int testNestedIf(int value)
        {
            int result;

            if (value > 0)
            {
                if (value < 10) 
                {
                    result = 1;
                }
                else 
                {
                    if (value < 100)
                    {
                        result = 2;
                    }
                    else
                    {
                        result = 3;
                    }
                }
            }
            else 
            {
                if (value == 0)
                {
                    result = 4;
                } 
                else 
                {
                    result = 5;
                }
            }

            return result;
        }

        int testLoopArithmetic()
        {
            int i;
            int total;

            i = 0;
            total = 0;

            while (i < 20) 
            {
                total += i * 2;
                total -= i / 2;
                total += i % 3;
                i++;
            }

            return total;
        }

        int testArrayLoop() 
        {
            int values[20];
            int i;
            int total;

            i = 0;

            while (i < 20)
            {
                values[i] = i * 3;
                i++;
            }

            i = 0;
            total = 0;

            while (i < 20)
            {
                total += values[i];
                i++;
            }

            return total;
        }

        int testCharLoop() 
        {
            char values[20];
            int i;
            int total;

            i = 0;

            while (i < 20) 
            {
                values[i] = i + 1;
                i++;
            }

            i = 0;
            total = 0;

            while (i < 20) 
            {
                total += values[i];
                i++;
            }

            return total;
        }

        int testArrayPointerAndFunction()
        {
            int values[10];
            int* pointer;
            int result;

            values[0] = 1;
            values[1] = 2;
            values[2] = 3;
            values[3] = 4;
            values[4] = 5;
            values[5] = 6;
            values[6] = 7;
            values[7] = 8;
            values[8] = 9;
            values[9] = 10;

            pointer = &values[0];

            result = pointerValue(pointer);

            pointer++;

            result += pointerValue(pointer);

            pointer++;

            result += pointerValue(pointer);

            return result;
        }

        int testFunctionArguments() 
        {
            int result;

            result = add(1, 2);
            result += add(3, 4);
            result += multiply(5, 6);
            result += subtract(100, 25);
            result += square(7);

            return result;
        }

        int testMultipleArguments()
        {
            int a;
            int b;
            int c;
            int d;
            int result;

            a = 10;
            b = 20;
            c = 30;
            d = 40;

            result = add(a, b);
            result += add(c, d);
            result += multiply(a, c);
            result -= subtract(d, b);

            return result;
        }

        int testReturnPaths(int value)
        {
            if (value == 1)
            {
                return 11;
            }

            if (value == 2)
            {
                return 22;
            }

            if (value == 3)
            {
                return 33;
            }

            if (value == 4)
            {
                return 44;
            }

            return 99;
        }

        int testDeepCalls(int value) 
        {
            int result;

            result = increment(value);
            result = square(result);
            result = add(result, 10);
            result = multiply(result, 2);
            result = subtract(result, 5);
            result = decrement(result);

            return result;
        }

        int testPointerIncrementValues()
        {
            int value;
            int* pointer;
            int result;

            value = 10;
            pointer = &value;

            result = (*pointer)++;
            result += (*pointer)++;
            result += ++(*pointer);
            result += --(*pointer);

            return result;
        }

        int testPointerAllCompound()
        {
            int value;
            int* pointer;

            value = 100;
            pointer = &value;

            *pointer += 20;
            *pointer -= 10;
            *pointer *= 2;
            *pointer /= 2;
            *pointer %= 7;

            return *pointer;
        }

        int testStructAllCompound()
        {
            struct Point point;

            point.x = 100;
            point.y = 200;

            point.x += 10;
            point.x -= 5;
            point.x *= 2;
            point.x /= 5;
            point.x %= 7;

            return point.x;
        }

        int testIntArrayCompound()
        {
            int values[4];

            values[0] = 10;
            values[1] = 20;
            values[2] = 30;
            values[3] = 40;

            values[0] += 5;
            values[1] -= 5;
            values[2] *= 2;
            values[3] /= 2;

            return values[0] + values[1] + values[2] + values[3];
        }

        int testPointerToPointer()
        {
            int value;
            int* pointer;
            int** pointerPointer;

            value = 100;
            pointer = &value;
            pointerPointer = &pointer;

            **pointerPointer = 250;

            return value;
        }

        int testStructMemberIncrementValues()
        {
            struct Point point;
            int result;

            point.x = 10;
            point.y = 20;

            result = point.x++;
            result += ++point.x;
            result += point.y--;
            result += --point.y;

            return result;
        }        

        int testUnionPointerReturn()
        {
        	union Value v;
        	v.number = 123;
        	return getValue(&v)->number;
        }

        int testUnionPointerLocal()
        {
        	union Value v;
        	union Value* p;


        	v.number = 100;
        	p = &v;
        	p->number = 456;

        	return v.number;


        }

        int testUnionCharPointer()
        {
        	union Value v;
        	union Value* p;

        	p = &v;
        	p->letter = 65;

        	return p->letter;


        }

        int testUnionPointerWrite()
        {
        	union Value v;
        	union Value* p;


        	v.number = 100;
        	p = &v;
        	p->number = 999;

        	return v.number;


        }

        union Value* identityValue(union Value* p)
        {
        	return p;
        }

        union Value* getValue(union Value* p)
        {
        	return identityValue(p);
        }

        int testUnionMultipleCalls()
        {
        	union Value v;


        	v.number = 789;

        	return getValue(&v)->number;


        }

        int testStructPointerAccess()
        {
        	struct Point p;
        	struct Point* ptr;


        	p.x = 100;
        	p.y = 200;
        	ptr = &p;

        	return ptr->x + ptr->y;


        }

        struct Point* getPoint(struct Point* p)
        {
        	return p;
        }

        int testStructPointerReturn()
        {
        	struct Point p;


        	p.x = 111;
        	p.y = 222;

        	return getPoint(&p)->y;


        }

        void setPointX(struct Point* p)
        {
        	p->x = 555;
        }

        int testStructPointerWrite()
        {
        	struct Point point;


        	point.x = 100;
        	point.y = 200;

        	setPointX(&point);

        	return point.x;


        }

        void setUnionNumber(union Value* p)
        {
        	p->number = 999;
        }

        int testUnionParameterWrite()
        {
        	union Value value;


        	value.number = 100;
        	setUnionNumber(&value);

        	return value.number;


        }

        int testNestedStructAccess()
        {
        	struct Point topLeft;
        	struct Point bottomRight;


        	topLeft.x = 10;
        	topLeft.y = 20;
        	bottomRight.x = 30;
        	bottomRight.y = 40;

        	return topLeft.x + bottomRight.y;


        }

        struct Rectangle
        {
        	struct Point topLeft;
        	struct Point bottomRight;
        };

        int testNestedStructPointer()
        {
        	struct Rectangle r;
        	struct Rectangle* p;


        	r.topLeft.x = 10;
        	r.topLeft.y = 20;
        	r.bottomRight.x = 30;
        	r.bottomRight.y = 40;

        	p = &r;

        	return p->bottomRight.x + p->topLeft.y;


        }

        struct PointerData
        {
        	int* value;
        };

        int testPointerMember()
        {
        	int number;
        	struct PointerData data;

        	number = 321;
        	data.value = &number;

        	return *data.value;

        }

        int testPointerMemberThroughArrow()
        {
        	int number;
        	struct PointerData data;
        	struct PointerData* p;

        	number = 654;
        	data.value = &number;
        	p = &data;

        	return *p->value;

        }

        int testPointerToPointerRegression()
        {
        	int value;
        	int* p;
        	int** pp;


        	value = 777;
        	p = &value;
        	pp = &p;

        	return **pp;
        }

        int testPointerArithmeticRegression()
        {
        	int values[3];
        	int* p;


        	values[0] = 10;
        	values[1] = 20;
        	values[2] = 30;

        	p = values;

        	return *(p + 2);


        }

        int testShortValues()
        {
            short a;
            short b;
            short c;
            int result;

            a = 10;
            b = 20;
            c = 30;

            result = a;
            result += b;
            result += c;

            return result;
        }

        int testShortNegative()
        {
            short a;
            short b;
            int result;

            a = -100;
            b = -50;

            result = a;
            result += b;

            return result;
        }

        int testShortBoundaries()
        {
            short maximum;
            short minimum;
            int result;

            maximum = 32767;
            minimum = -32768;

            result = maximum;
            result += minimum;

            return result;
        }

        int testShortTruncation()
        {
            short value;
            int result;

            value = 65536;
            result = value;

            return result;
        }

        int testShortWrapPositive()
        {
            short value;
            int result;

            value = 32767;
            value++;

            result = value;

            return result;
        }

        int testShortWrapNegative()
        {
            short value;
            int result;

            value = -32768;
            value--;

            result = value;

            return result;
        }

        int testUnsignedShortValues()
        {
            unsigned short a;
            unsigned short b;
            unsigned short c;
            int result;

            a = 100;
            b = 200;
            c = 300;

            result = a;
            result += b;
            result += c;

            return result;
        }

        int testUnsignedShortMaximum()
        {
            unsigned short value;
            int result;

            value = 65535;
            result = value;

            return result;
        }

        int testUnsignedShortTruncation()
        {
            unsigned short value;
            int result;

            value = 65536;
            result = value;

            return result;
        }

        int testShortCompound()
        {
            short value;

            value = 100;

            value += 50;
            value -= 25;
            value *= 2;
            value /= 5;
            value %= 7;

            return value;
        }

        int testShortArray()
        {
            short values[5];
            int result;

            values[0] = 10;
            values[1] = 20;
            values[2] = 30;
            values[3] = 40;
            values[4] = 50;

            result = values[0];
            result += values[1];
            result += values[2];
            result += values[3];
            result += values[4];

            return result;
        }

        int testShortArrayCompound()
        {
            short values[4];

            values[0] = 10;
            values[1] = 20;
            values[2] = 30;
            values[3] = 40;

            values[0] += 5;
            values[1] -= 5;
            values[2] *= 2;
            values[3] /= 2;

            return values[0] + values[1] + values[2] + values[3];
        }

        int testShortArrayIncrement()
        {
            short values[4];
            int result;

            values[0] = 10;
            values[1] = 20;
            values[2] = 30;
            values[3] = 40;

            values[0]++;
            ++values[1];
            values[2]--;
            --values[3];

            result = values[0];
            result += values[1];
            result += values[2];
            result += values[3];

            return result;
        }

        int testShortPointer()
        {
            short value;
            short* pointer;

            value = 123;
            pointer = &value;

            return *pointer;
        }

        int testShortPointerWrite()
        {
            short value;
            short* pointer;

            value = 100;
            pointer = &value;

            *pointer = 456;

            return *pointer;
        }

        int testShortPointerCompound()
        {
            short value;
            short* pointer;

            value = 100;
            pointer = &value;

            *pointer += 50;
            *pointer -= 25;

            return *pointer;
        }

        int testShortPointerIncrement()
        {
            short value;
            short* pointer;
            int result;

            value = 10;
            pointer = &value;

            result = (*pointer)++;
            result += ++(*pointer);
            result += (*pointer)--;
            result += --(*pointer);

            return result;
        }

        int testShortPointerArray()
        {
            short values[4];
            short* pointer;
            int result;

            values[0] = 10;
            values[1] = 20;
            values[2] = 30;
            values[3] = 40;

            pointer = values;

            result = *pointer;
            pointer++;
            result += *pointer;
            pointer++;
            result += *pointer;
            pointer++;
            result += *pointer;

            return result;
        }

        int testShortStruct()
        {
            struct ShortData
            {
                short value;
                short count;
            };

            struct ShortData data;
            int result;

            data.value = 100;
            data.count = 25;

            result = data.value;
            result += data.count;

            return result;
        }

        int testShortStructCompound()
        {
            struct ShortData
            {
                short value;
                short count;
            };

            struct ShortData data;

            data.value = 100;
            data.count = 50;

            data.value += 25;
            data.count *= 2;

            return data.value + data.count;
        }

        int testShortStructPointer()
        {
            struct ShortData
            {
                short value;
                short count;
            };

            struct ShortData data;
            struct ShortData* pointer;

            data.value = 100;
            data.count = 25;

            pointer = &data;

            pointer->value = 200;
            pointer->count = 50;

            return pointer->value + pointer->count;
        }

        union ShortValue
        {
            short number;
            unsigned short unsignedNumber;
        };

        int testShortUnion()
        {
            union ShortValue value;

            value.number = 1234;

            return value.number;
        }

        int testUnsignedShortUnion()
        {
            union ShortValue value;

            value.unsignedNumber = 65535;

            return value.unsignedNumber;
        }

        short returnShort(short value)
        {
            return value;
        }

        unsigned short returnUnsignedShort(unsigned short value)
        {
            return value;
        }

        int testShortFunction()
        {
            short value;

            value = 1234;

            return returnShort(value);
        }

        int testUnsignedShortFunction()
        {
            unsigned short value;

            value = 60000;

            return returnUnsignedShort(value);
        }

        int testShortFunctionArithmetic()
        {
            short a;
            short b;

            a = 100;
            b = 50;

            return returnShort(a + b);
        }

        int testShortRelations()
        {
            short a;
            short b;
            int result;

            a = -10;
            b = 20;
            result = 0;

            if (a < b)
            {
                result += 1;
            }

            if (a <= b)
            {
                result += 2;
            }

            if (b > a)
            {
                result += 4;
            }

            if (b >= a)
            {
                result += 8;
            }

            if (a != b)
            {
                result += 16;
            }

            return result;
        }

        int testUnsignedShortRelations()
        {
            unsigned short a;
            unsigned short b;
            int result;

            a = 100;
            b = 200;
            result = 0;

            if (a < b)
            {
                result += 1;
            }

            if (a <= b)
            {
                result += 2;
            }

            if (b > a)
            {
                result += 4;
            }

            if (b >= a)
            {
                result += 8;
            }

            if (a != b)
            {
                result += 16;
            }

            return result;
        }
        
        int testUnsignedIntGreater()
        {
            unsigned int a;
            unsigned int b;

            a = 0;
            a--;
            b = 1;

            return a > b;
        }

        int testUnsignedIntLess()
        {
            unsigned int a;
            unsigned int b;

            a = 0;
            a--;
            b = 1;

            return a < b;
        }

        int testUnsignedIntDivision()
        {
            unsigned int a;
            unsigned int b;

            a = 0;
            a--;
            b = 2;

            return a / b;
        }

        int testUnsignedIntRemainder()
        {
            unsigned int a;
            unsigned int b;

            a = 0;
            a--;
            b = 2;

            return a % b;
        }
        
        int testUnsignedIntAssignment()
        {
            unsigned int a;
            unsigned int b;

            a = 2000000000;
            b = a;

            return b == 2000000000;
        }

        int testUnsignedIntAddAssignment()
        {
            unsigned int value;

            value = 100;
            value += 50;

            return value == 150;
        }

        int testUnsignedIntSubAssignment()
        {
            unsigned int value;

            value = 150;
            value -= 25;

            return value == 125;
        }

        int testUnsignedIntMulAssignment()
        {
            unsigned int value;

            value = 125;
            value *= 2;

            return value == 250;
        }

        int testUnsignedIntDivAssignment()
        {
            unsigned int value;

            value = 250;
            value /= 5;

            return value == 50;
        }

        int testUnsignedIntArray()
        {
            unsigned int values[4];

            values[0] = 1000000000;
            values[1] = 1100000000;
            values[2] = 1200000000;
            values[3] = 1300000000;

            return values[0] == 1000000000 &&
                   values[1] == 1100000000 &&
                   values[2] == 1200000000 &&
                   values[3] == 1300000000;
        }

        int testUnsignedIntArrayArithmetic()
        {
            unsigned int values[3];

            values[0] = 2000000000;
            values[1] = 1000000000;
            values[2] = values[0] - values[1];

            return values[2] == 1000000000;
        }

        int testUnsignedIntPointer()
        {
            unsigned int value;
            unsigned int* pointer;

            value = 1000000000;
            pointer = &value;

            *pointer = 2000000000;

            return value == 2000000000;
        }

        int testUnsignedIntPointerArithmetic()
        {
            unsigned int values[3];
            unsigned int* pointer;

            values[0] = 1000000000;
            values[1] = 1100000000;
            values[2] = 1200000000;

            pointer = values;

            return pointer[0] == 1000000000 &&
                   pointer[1] == 1100000000 &&
                   pointer[2] == 1200000000;
        }

        int testUnsignedIntIncrement()
        {
            unsigned int value;
            unsigned int result;

            value = 100;

            result = value++;
            result += value++;

            return result == 201;
        }

        int testUnsignedIntDecrement()
        {
            unsigned int value;

            value = 100;

            value--;
            value--;

            return value == 98;
        }

        int testUnsignedIntPreIncrement()
        {
            unsigned int value;

            value = 100;

            return ++value == 101;
        }

        int testUnsignedIntPreDecrement()
        {
            unsigned int value;

            value = 100;

            return --value == 99;
        }

        unsigned int testUnsignedIntParameter(unsigned int value)
        {
            return value + 100;
        }

        int testUnsignedIntFunctionParameter()
        {
            unsigned int value;

            value = testUnsignedIntParameter(2000000000);

            return value == 2000000100;
        }

        unsigned int testUnsignedIntReturn()
        {
            unsigned int value;

            value = 2000000000;

            return value;
        }

        int testUnsignedIntFunctionReturn()
        {
            return testUnsignedIntReturn() == 2000000000;
        }

        int testUnsignedIntSignedMixed()
        {
            unsigned int value;
            int signedValue;

            value = 2000000000;
            signedValue = 1000000000;

            return value > signedValue &&
                   value != signedValue &&
                   value >= signedValue;
        }

        int testUnsignedIntSignedMixedArithmetic()
        {
            unsigned int value;
            int signedValue;

            value = 2000000000;
            signedValue = 1000000000;

            return value - signedValue == 1000000000;
        }

        int testUnsignedIntZero()
        {
            unsigned int value;

            value = 0;

            return value == 0 &&
                   value <= 0 &&
                   !(value > 0);
        }

        int testUnsignedIntMaximum()
        {
            unsigned int value;

            value = 0;
            value--;

            return value > 2000000000 &&
                   value >= 2000000000;
        }

        int testUnsignedIntWraparound()
        {
            unsigned int value;

            value = 0;
            value--;

            value++;

            return value == 0;
        }

        int testUnsignedIntArrayPointerIncrement()
        {
            unsigned int values[3];
            unsigned int* pointer;
            unsigned int result;

            values[0] = 100;
            values[1] = 200;
            values[2] = 300;

            pointer = values;

            result = *pointer;
            pointer++;
            result += *pointer;
            pointer++;
            result += *pointer;

            return result == 600;
        }
                
        int testLongAssignment()
        {
            long value;

            value = 100;

            return value == 100;
        }

        int testLongArithmetic()
        {
            long value;

            value = 100;
            value += 50;
            value -= 25;
            value *= 2;
            value /= 5;
            value %= 7;

            return value;
        }

        int testLongComparison()
        {
            long a;
            long b;
            int result;

            a = 100;
            b = 200;
            result = 0;

            if (a < b)
                result += 1;

            if (a <= b)
                result += 2;

            if (b > a)
                result += 4;

            if (b >= a)
                result += 8;

            if (a != b)
                result += 16;

            if (a == 100)
                result += 32;

            return result == 63;
        }

        int testLongIncrementDecrement()
        {
            long value;
            int result;

            value = 10;

            result = value++;
            result += ++value;
            result += value--;
            result += --value;

            return result;
        }

        int testUnsignedLongAssignment()
        {
            unsigned long value;

            value = 100;

            return value == 100;
        }

        int testUnsignedLongArithmetic()
        {
            unsigned long value;

            value = 100;
            value += 50;
            value -= 25;
            value *= 2;
            value /= 5;
            value %= 7;

            return value;
        }

        int testUnsignedLongComparison()
        {
            unsigned long a;
            unsigned long b;
            int result;

            a = 100;
            b = 200;
            result = 0;

            if (a < b)
                result += 1;

            if (a <= b)
                result += 2;

            if (b > a)
                result += 4;

            if (b >= a)
                result += 8;

            if (a != b)
                result += 16;

            if (a == 100)
                result += 32;

            return result == 63;
        }

        int testUnsignedLongCompoundDivision()
        {
            unsigned long value;

            value = 250;
            value /= 5;

            return value == 50;
        }

        int testUnsignedLongCompoundRemainder()
        {
            unsigned long value;

            value = 253;
            value %= 5;

            return value == 3;
        }

        int testUnsignedLongWraparound()
        {
            unsigned long value;

            value = 0;
            value--;

            return value == -1;
        }

        int testUnsignedLongIncrementDecrement()
        {
            unsigned long value;
            int result;

            value = 10;

            result = value++;
            result += ++value;
            result += value--;
            result += --value;

            return result;
        }

        int testLongUnsignedIntPromotion()
        {
            long a;
            unsigned int b;
            unsigned long result;

            a = 10;
            b = 20;

            result = a + b;

            return result == 30;
        }

        int testUnsignedLongPromotion()
        {
            unsigned long a;
            int b;
            unsigned long result;

            a = 100;
            b = 25;

            result = a + b;

            return result == 125;
        }
        
        int testLongLongBasic()
        {
            long long a;
            long long b;

            a = 5000000000LL;
            b = 3000000000LL;

            return a + b == 8000000000LL;
        }

        int testLongLongArithmetic()
        {
            long long value;

            value = 5000000000LL;
            value += 2000000000LL;
            value -= 1000000000LL;
            value *= 3LL;
            value /= 2LL;
            value %= 1000000000LL;

            return value == 0LL;
        }

        int testLongLongIncrement()
        {
            long long value;
            long long result;

            value = 5000000000LL;

            result = value++;
            result += ++value;
            result += value--;
            result += --value;

            return result == 20000000004LL;
        }

        int testLongLongNegative()
        {
            long long value;

            value = -5000000000LL;

            return value == -5000000000LL;
        }

        int testLongLongComparison()
        {
            long long a;
            long long b;
            int result;

            a = 5000000000LL;
            b = 4000000000LL;
            result = 0;

            if (a > b)
                result += 1;

            if (a >= b)
                result += 2;

            if (a != b)
                result += 4;

            if (b < a)
                result += 8;

            if (b <= a)
                result += 16;

            return result == 31;
        }

        int testUnsignedLongLong()
        {
            unsigned long long value;

            value = 18446744073709551615ULL;
            value += 1ULL;

            return value == 0ULL;
        }

        int testUnsignedLongLongArithmetic()
        {
            unsigned long long value;

            value = 10000000000ULL;
            value /= 3ULL;
            value %= 1000ULL;

            return value == 333ULL;
        }

        int testLongLongSwitch()
        {
            long long value;

            value = 5000000000LL;

            switch (value)
            {
                case 1000000000LL:
                    return 0;

                case 5000000000LL:
                    return 1;

                case 9000000000LL:
                    return 0;

                default:
                    return 0;
            }
        }
        
        float addFloat(float a, float b)
        {
            return a + b;
        }

        double addDouble(double a, double b)
        {
            return a + b;
        }

        float multiplyFloat(float a, float b)
        {
            return a * b;
        }

        double multiplyDouble(double a, double b)
        {
            return a * b;
        }

        float mixedFloat(float a, int b)
        {
            return a + b;
        }

        double mixedDouble(double a, int b)
        {
            return a + b;
        }

        int testFloatFunction()
        {
            return (int)addFloat(10.0f, 20.0f);
        }

        int testDoubleFunction()
        {
            return (int)addDouble(10.0, 20.0);
        }

        int testFloatFunctionArithmetic()
        {
            return (int)multiplyFloat(5.0f, 6.0f);
        }

        int testDoubleFunctionArithmetic()
        {
            return (int)multiplyDouble(5.0, 6.0);
        }

        int testFloatParameter()
        {
            float value = 12.5f;

            return (int)addFloat(value, 7.5f);
        }

        int testDoubleParameter()
        {
            double value = 12.5;

            return (int)addDouble(value, 7.5);
        }

        int testMixedFloatParameter()
        {
            return (int)mixedFloat(10.0f, 5);
        }

        int testMixedDoubleParameter()
        {
            return (int)mixedDouble(10.0, 5);
        }

        int testNestedFloatCall()
        {
            return (int)addFloat(addFloat(5.0f, 10.0f), 20.0f);
        }

        int testNestedDoubleCall()
        {
            return (int)addDouble(addDouble(5.0, 10.0), 20.0);
        }
        
        int main()
        {
            const int finalExpectedResult = -2147268108;
            int result;
            int temporary;

            result = 0;

            result += 10;
            result += 100;
            result += 1;
            result += 10;
            result += 100;
            result += 200;
            result += 100;
            result += 200;
            result += 65;
            result += 1000;
            result += 10;
            result += 65;
            result += 1000;
            result += 10;
            result += 20;
            result += 30;
            result += 500;
            result += 101;
            result += 2;
            result += 3;
            result += 2;
            result += 3;

            printf("INITIAL: got=%d expected=3532 running=%d\n", 3532, result);

            temporary = testArrays();
            result += temporary;
            printf("testArrays: got=%d expected=220 running=%d\n", temporary, result);

            temporary = testCharValues();
            result += temporary;
            printf("testCharValues: got=%d expected=150 running=%d\n", temporary, result);

            temporary = testCharArrayValues();
            result += temporary;
            printf("testCharArrayValues: got=%d expected=55 running=%d\n", temporary, result);

            temporary = testCharArrayCompound();
            result += temporary;
            printf("testCharArrayCompound: got=%d expected=110 running=%d\n", temporary, result);

            temporary = testArrayIncrement();
            result += temporary;
            printf("testArrayIncrement: got=%d expected=150 running=%d\n", temporary, result);

            temporary = testCharIncrementArray();
            result += temporary;
            printf("testCharIncrementArray: got=%d expected=100 running=%d\n", temporary, result);

            temporary = testPointerAssignment();
            result += temporary;
            printf("testPointerAssignment: got=%d expected=350 running=%d\n", temporary, result);

            temporary = testPointerCompound();
            result += temporary;
            printf("testPointerCompound: got=%d expected=150 running=%d\n", temporary, result);

            temporary = testMultiplePointers();
            result += temporary;
            printf("testMultiplePointers: got=%d expected=660 running=%d\n", temporary, result);

            temporary = testArrayPointerMove();
            result += temporary;
            printf("testArrayPointerMove: got=%d expected=165 running=%d\n", temporary, result);

            temporary = testStructValues();
            result += temporary;
            printf("testStructValues: got=%d expected=300 running=%d\n", temporary, result);

            temporary = testStructPointer();
            result += temporary;
            printf("testStructPointer: got=%d expected=180 running=%d\n", temporary, result);

            temporary = testStructChar();
            result += temporary;
            printf("testStructChar: got=%d expected=442 running=%d\n", temporary, result);

            temporary = testSizeof();
            result += temporary;
            printf("testSizeof: got=%d expected=45 running=%d\n", temporary, result);

            temporary = testSizeofPointers();
            result += temporary;
            printf("testSizeofPointers: got=%d expected=16 running=%d\n", temporary, result);

            temporary = testNull();
            result += temporary;
            printf("testNull: got=%d expected=123 running=%d\n", temporary, result);

            temporary = testManyLocals();
            result += temporary;
            printf("testManyLocals: got=%d expected=210 running=%d\n", temporary, result);

            temporary = testMathChain();
            result += temporary;
            printf("testMathChain: got=%d expected=1521 running=%d\n", temporary, result);

            temporary = testAllArithmetic();
            result += temporary;
            printf("testAllArithmetic: got=%d expected=702 running=%d\n", temporary, result);

            temporary = testConditionals(5);
            result += temporary;
            printf("testConditionals(5): got=%d expected=3 running=%d\n", temporary, result);

            temporary = testConditionals(50);
            result += temporary;
            printf("testConditionals(50): got=%d expected=4 running=%d\n", temporary, result);

            temporary = testConditionals(500);
            result += temporary;
            printf("testConditionals(500): got=%d expected=5 running=%d\n", temporary, result);

            temporary = testLogical(10, 20);
            result += temporary;
            printf("testLogical(10,20): got=%d expected=80 running=%d\n", temporary, result);

            temporary = testRelations(10, 20);
            result += temporary;
            printf("testRelations(10,20): got=%d expected=14 running=%d\n", temporary, result);

            temporary = testNestedIf(5);
            result += temporary;
            printf("testNestedIf(5): got=%d expected=1 running=%d\n", temporary, result);

            temporary = testSwitch(0);
            result += temporary;
            printf("testSwitch(0): got=%d expected=10 running=%d\n", temporary, result);

            temporary = testSwitch(1);
            result += temporary;
            printf("testSwitch(1): got=%d expected=20 running=%d\n", temporary, result);

            temporary = testSwitch(2);
            result += temporary;
            printf("testSwitch(2): got=%d expected=30 running=%d\n", temporary, result);

            temporary = testSwitch(3);
            result += temporary;
            printf("testSwitch(3): got=%d expected=40 running=%d\n", temporary, result);

            temporary = testSwitch(4);
            result += temporary;
            printf("testSwitch(4): got=%d expected=50 running=%d\n", temporary, result);

            temporary = testSwitch(99);
            result += temporary;
            printf("testSwitch(99): got=%d expected=99 running=%d\n", temporary, result);

            temporary = testCompound(100);
            result += temporary;
            printf("testCompound(100): got=%d expected=2 running=%d\n", temporary, result);

            temporary = testIncrement(100);
            result += temporary;
            printf("testIncrement(100): got=%d expected=100 running=%d\n", temporary, result);

            temporary = testForLoop(0, 20);
            result += temporary;
            printf("testForLoop(0,20): got=%d expected=190 running=%d\n", temporary, result);

            temporary = testNestedLoops(5);
            result += temporary;
            printf("testNestedLoops(5): got=%d expected=100 running=%d\n", temporary, result);

            temporary = testBreak(10);
            result += temporary;
            printf("testBreak(10): got=%d expected=45 running=%d\n", temporary, result);

            temporary = testContinue(20);
            result += temporary;
            printf("testContinue(20): got=%d expected=100 running=%d\n", temporary, result);

            temporary = testDoWhile(10);
            result += temporary;
            printf("testDoWhile(10): got=%d expected=45 running=%d\n", temporary, result);

            temporary = testLoopArithmetic();
            result += temporary;
            printf("testLoopArithmetic: got=%d expected=309 running=%d\n", temporary, result);

            temporary = testArrayLoop();
            result += temporary;
            printf("testArrayLoop: got=%d expected=570 running=%d\n", temporary, result);

            temporary = testCharLoop();
            result += temporary;
            printf("testCharLoop: got=%d expected=210 running=%d\n", temporary, result);

            temporary = testArrayPointerAndFunction();
            result += temporary;
            printf("testArrayPointerAndFunction: got=%d expected=6 running=%d\n", temporary, result);

            temporary = testFunctionArguments();
            result += temporary;
            printf("testFunctionArguments: got=%d expected=164 running=%d\n", temporary, result);

            temporary = testMultipleArguments();
            result += temporary;
            printf("testMultipleArguments: got=%d expected=380 running=%d\n", temporary, result);

            temporary = testDeepCalls(10);
            result += temporary;
            printf("testDeepCalls(10): got=%d expected=256 running=%d\n", temporary, result);

            temporary = testReturnPaths(1);
            result += temporary;
            printf("testReturnPaths(1): got=%d expected=11 running=%d\n", temporary, result);

            temporary = testReturnPaths(2);
            result += temporary;
            printf("testReturnPaths(2): got=%d expected=22 running=%d\n", temporary, result);

            temporary = testReturnPaths(3);
            result += temporary;
            printf("testReturnPaths(3): got=%d expected=33 running=%d\n", temporary, result);

            temporary = testReturnPaths(4);
            result += temporary;
            printf("testReturnPaths(4): got=%d expected=44 running=%d\n", temporary, result);

            temporary = testReturnPaths(99);
            result += temporary;
            printf("testReturnPaths(99): got=%d expected=99 running=%d\n", temporary, result);

            temporary = testPointerIncrementValues();
            result += temporary;
            printf("testPointerIncrementValues: got=%d expected=46 running=%d\n", temporary, result);

            temporary = testPointerAllCompound();
            result += temporary;
            printf("testPointerAllCompound: got=%d expected=5 running=%d\n", temporary, result);

            temporary = testStructAllCompound();
            result += temporary;
            printf("testStructAllCompound: got=%d expected=0 running=%d\n", temporary, result);

            temporary = testStructMemberIncrementValues();
            result += temporary;
            printf("testStructMemberIncrementValues: got=%d expected=60 running=%d\n", temporary, result);

            temporary = testIntArrayCompound();
            result += temporary;
            printf("testIntArrayCompound: got=%d expected=110 running=%d\n", temporary, result);

            temporary = testPointerToPointer();
            result += temporary;
            printf("testPointerToPointer: got=%d expected=250 running=%d\n", temporary, result);

        	temporary = testUnionPointerReturn();
        	result += temporary;
        	printf("testUnionPointerReturn: got=%d expected=123 running=%d\n", temporary, result);

        	temporary = testUnionPointerLocal();
        	result += temporary;
        	printf("testUnionPointerLocal: got=%d expected=456 running=%d\n", temporary, result);

        	temporary = testUnionCharPointer();
        	result += temporary;
        	printf("testUnionCharPointer: got=%d expected=65 running=%d\n", temporary, result);

        	temporary = testUnionPointerWrite();
        	result += temporary;
        	printf("testUnionPointerWrite: got=%d expected=999 running=%d\n", temporary, result);

        	temporary = testUnionMultipleCalls();
        	result += temporary;
        	printf("testUnionMultipleCalls: got=%d expected=789 running=%d\n", temporary, result);

        	temporary = testStructPointerAccess();
        	result += temporary;
        	printf("testStructPointerAccess: got=%d expected=300 running=%d\n", temporary, result);

        	temporary = testStructPointerReturn();
        	result += temporary;
        	printf("testStructPointerReturn: got=%d expected=222 running=%d\n", temporary, result);

        	temporary = testStructPointerWrite();
        	result += temporary;
        	printf("testStructPointerWrite: got=%d expected=555 running=%d\n", temporary, result);

        	temporary = testUnionParameterWrite();
        	result += temporary;
        	printf("testUnionParameterWrite: got=%d expected=999 running=%d\n", temporary, result);

        	temporary = testNestedStructAccess();
        	result += temporary;
        	printf("testNestedStructAccess: got=%d expected=50 running=%d\n", temporary, result);

        	temporary = testNestedStructPointer();
        	result += temporary;
        	printf("testNestedStructPointer: got=%d expected=50 running=%d\n", temporary, result);

        	temporary = testPointerMember();
        	result += temporary;
        	printf("testPointerMember: got=%d expected=321 running=%d\n", temporary, result);

        	temporary = testPointerMemberThroughArrow();
        	result += temporary;
        	printf("testPointerMemberThroughArrow: got=%d expected=654 running=%d\n", temporary, result);

        	temporary = testPointerToPointerRegression();
        	result += temporary;
        	printf("testPointerToPointerRegression: got=%d expected=777 running=%d\n", temporary, result);

        	temporary = testPointerArithmeticRegression();
        	result += temporary;
        	printf("testPointerArithmeticRegression: got=%d expected=30 running=%d\n", temporary, result);

            temporary = testShortValues();
            result += temporary;
            printf("testShortValues: got=%d expected=60 running=%d\n", temporary, result);

            temporary = testShortNegative();
            result += temporary;
            printf("testShortNegative: got=%d expected=-150 running=%d\n", temporary, result);

            temporary = testShortBoundaries();
            result += temporary;
            printf("testShortBoundaries: got=%d expected=-1 running=%d\n", temporary, result);

            temporary = testShortTruncation();
            result += temporary;
            printf("testShortTruncation: got=%d expected=0 running=%d\n", temporary, result);

            temporary = testShortWrapPositive();
            result += temporary;
            printf("testShortWrapPositive: got=%d expected=-32768 running=%d\n", temporary, result);

            temporary = testShortWrapNegative();
            result += temporary;
            printf("testShortWrapNegative: got=%d expected=32767 running=%d\n", temporary, result);

            temporary = testUnsignedShortValues();
            result += temporary;
            printf("testUnsignedShortValues: got=%d expected=600 running=%d\n", temporary, result);

            temporary = testUnsignedShortMaximum();
            result += temporary;
            printf("testUnsignedShortMaximum: got=%d expected=65535 running=%d\n", temporary, result);

            temporary = testUnsignedShortTruncation();
            result += temporary;
            printf("testUnsignedShortTruncation: got=%d expected=0 running=%d\n", temporary, result);

            temporary = testShortCompound();
            result += temporary;
            printf("testShortCompound: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testShortArray();
            result += temporary;
            printf("testShortArray: got=%d expected=150 running=%d\n", temporary, result);

            temporary = testShortArrayCompound();
            result += temporary;
            printf("testShortArrayCompound: got=%d expected=110 running=%d\n", temporary, result);

            temporary = testShortArrayIncrement();
            result += temporary;
            printf("testShortArrayIncrement: got=%d expected=100 running=%d\n", temporary, result);

            temporary = testShortPointer();
            result += temporary;
            printf("testShortPointer: got=%d expected=123 running=%d\n", temporary, result);

            temporary = testShortPointerWrite();
            result += temporary;
            printf("testShortPointerWrite: got=%d expected=456 running=%d\n", temporary, result);

            temporary = testShortPointerCompound();
            result += temporary;
            printf("testShortPointerCompound: got=%d expected=125 running=%d\n", temporary, result);

            temporary = testShortPointerIncrement();
            result += temporary;
            printf("testShortPointerIncrement: got=%d expected=44 running=%d\n", temporary, result);

            temporary = testShortPointerArray();
            result += temporary;
            printf("testShortPointerArray: got=%d expected=100 running=%d\n", temporary, result);

            temporary = testShortStruct();
            result += temporary;
            printf("testShortStruct: got=%d expected=125 running=%d\n", temporary, result);

            temporary = testShortStructCompound();
            result += temporary;
            printf("testShortStructCompound: got=%d expected=225 running=%d\n", temporary, result);

            temporary = testShortStructPointer();
            result += temporary;
            printf("testShortStructPointer: got=%d expected=250 running=%d\n", temporary, result);

            temporary = testShortUnion();
            result += temporary;
            printf("testShortUnion: got=%d expected=1234 running=%d\n", temporary, result);

            temporary = testUnsignedShortUnion();
            result += temporary;
            printf("testUnsignedShortUnion: got=%d expected=65535 running=%d\n", temporary, result);

            temporary = testShortFunction();
            result += temporary;
            printf("testShortFunction: got=%d expected=1234 running=%d\n", temporary, result);

            temporary = testUnsignedShortFunction();
            result += temporary;
            printf("testUnsignedShortFunction: got=%d expected=60000 running=%d\n", temporary, result);

            temporary = testShortFunctionArithmetic();
            result += temporary;
            printf("testShortFunctionArithmetic: got=%d expected=150 running=%d\n", temporary, result);

            temporary = testShortRelations();
            result += temporary;
            printf("testShortRelations: got=%d expected=31 running=%d\n", temporary, result);

            temporary = testUnsignedShortRelations();
            result += temporary;
            printf("testUnsignedShortRelations: got=%d expected=31 running=%d\n", temporary, result);
            
            temporary = testUnsignedIntGreater();
            result += temporary;
            printf("testUnsignedIntGreater: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntLess();
            result += temporary;
            printf("testUnsignedIntLess: got=%d expected=0 running=%d\n", temporary, result);

            temporary = testUnsignedIntDivision();
            result += temporary;
            printf("testUnsignedIntDivision: got=%d expected=2147483647 running=%d\n", temporary, result);

            temporary = testUnsignedIntRemainder();
            result += temporary;
            printf("testUnsignedIntRemainder: got=%d expected=1 running=%d\n", temporary, result);
        
            temporary = testUnsignedIntAssignment();
            result += temporary;
            printf("testUnsignedIntAssignment: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntAddAssignment();
            result += temporary;
            printf("testUnsignedIntAddAssignment: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntSubAssignment();
            result += temporary;
            printf("testUnsignedIntSubAssignment: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntMulAssignment();
            result += temporary;
            printf("testUnsignedIntMulAssignment: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntDivAssignment();
            result += temporary;
            printf("testUnsignedIntDivAssignment: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntArray();
            result += temporary;
            printf("testUnsignedIntArray: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntArrayArithmetic();
            result += temporary;
            printf("testUnsignedIntArrayArithmetic: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntPointer();
            result += temporary;
            printf("testUnsignedIntPointer: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntPointerArithmetic();
            result += temporary;
            printf("testUnsignedIntPointerArithmetic: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntIncrement();
            result += temporary;
            printf("testUnsignedIntIncrement: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntDecrement();
            result += temporary;
            printf("testUnsignedIntDecrement: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntPreIncrement();
            result += temporary;
            printf("testUnsignedIntPreIncrement: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntPreDecrement();
            result += temporary;
            printf("testUnsignedIntPreDecrement: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntFunctionParameter();
            result += temporary;
            printf("testUnsignedIntFunctionParameter: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntFunctionReturn();
            result += temporary;
            printf("testUnsignedIntFunctionReturn: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntSignedMixed();
            result += temporary;
            printf("testUnsignedIntSignedMixed: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntSignedMixedArithmetic();
            result += temporary;
            printf("testUnsignedIntSignedMixedArithmetic: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntZero();
            result += temporary;
            printf("testUnsignedIntZero: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntMaximum();
            result += temporary;
            printf("testUnsignedIntMaximum: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntWraparound();
            result += temporary;
            printf("testUnsignedIntWraparound: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedIntArrayPointerIncrement();
            result += temporary;
            printf("testUnsignedIntArrayPointerIncrement: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongAssignment();
            result += temporary;
            printf("testLongAssignment: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongArithmetic();
            result += temporary;
            printf("testLongArithmetic: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongComparison();
            result += temporary;
            printf("testLongComparison: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongIncrementDecrement();
            result += temporary;
            printf("testLongIncrementDecrement: got=%d expected=44 running=%d\n", temporary, result);

            temporary = testUnsignedLongAssignment();
            result += temporary;
            printf("testUnsignedLongAssignment: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedLongArithmetic();
            result += temporary;
            printf("testUnsignedLongArithmetic: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedLongComparison();
            result += temporary;
            printf("testUnsignedLongComparison: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedLongCompoundDivision();
            result += temporary;
            printf("testUnsignedLongCompoundDivision: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedLongCompoundRemainder();
            result += temporary;
            printf("testUnsignedLongCompoundRemainder: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedLongWraparound();
            result += temporary;
            printf("testUnsignedLongWraparound: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedLongIncrementDecrement();
            result += temporary;
            printf("testUnsignedLongIncrementDecrement: got=%d expected=44 running=%d\n", temporary, result);

            temporary = testLongUnsignedIntPromotion();
            result += temporary;
            printf("testLongUnsignedIntPromotion: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedLongPromotion();
            result += temporary;
            printf("testUnsignedLongPromotion: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongLongBasic();
            result += temporary;
            printf("testLongLongBasic: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongLongArithmetic();
            result += temporary;
            printf("testLongLongArithmetic: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongLongIncrement();
            result += temporary;
            printf("testLongLongIncrement: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongLongNegative();
            result += temporary;
            printf("testLongLongNegative: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongLongComparison();
            result += temporary;
            printf("testLongLongComparison: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedLongLong();
            result += temporary;
            printf("testUnsignedLongLong: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testUnsignedLongLongArithmetic();
            result += temporary;
            printf("testUnsignedLongLongArithmetic: got=%d expected=1 running=%d\n", temporary, result);

            temporary = testLongLongSwitch();
            result += temporary;
            printf("testLongLongSwitch: got=%d expected=1 running=%d\n", temporary, result);
        
            temporary = testFloatFunction();
            result += temporary;
            printf("testFloatFunction: got=%d expected=30 running=%d\n", temporary, result);

            temporary = testDoubleFunction();
            result += temporary;
            printf("testDoubleFunction: got=%d expected=30 running=%d\n", temporary, result);

            temporary = testFloatFunctionArithmetic();
            result += temporary;
            printf("testFloatFunctionArithmetic: got=%d expected=30 running=%d\n", temporary, result);

            temporary = testDoubleFunctionArithmetic();
            result += temporary;
            printf("testDoubleFunctionArithmetic: got=%d expected=30 running=%d\n", temporary, result);

            temporary = testFloatParameter();
            result += temporary;
            printf("testFloatParameter: got=%d expected=20 running=%d\n", temporary, result);

            temporary = testDoubleParameter();
            result += temporary;
            printf("testDoubleParameter: got=%d expected=20 running=%d\n", temporary, result);

            temporary = testMixedFloatParameter();
            result += temporary;
            printf("testMixedFloatParameter: got=%d expected=15 running=%d\n", temporary, result);

            temporary = testMixedDoubleParameter();
            result += temporary;
            printf("testMixedDoubleParameter: got=%d expected=15 running=%d\n", temporary, result);

            temporary = testNestedFloatCall();
            result += temporary;
            printf("testNestedFloatCall: got=%d expected=35 running=%d\n", temporary, result);

            temporary = testNestedDoubleCall();
            result += temporary;
            printf("testNestedDoubleCall: got=%d expected=35 running=%d\n", temporary, result);

            printf("FINAL: got=%d expected=%d difference=%d\n", result, finalExpectedResult, result - finalExpectedResult);

            if (result == finalExpectedResult)
                return 0;
            else
                return -1;        
        }
        """;

        private void New_Click(object sender, RoutedEventArgs e)
        {
            Editor.Text = "";
            _currentFile = null;
            StatusText.Text = "New source file";
            Output.Clear();
            TokenList.Items.Clear();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "C source (*.c)|*.c|C header (*.h)|*.h|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            Editor.Text = File.ReadAllText(dialog.FileName);
            _currentFile = dialog.FileName;
            StatusText.Text = $"Opened {Path.GetFileName(dialog.FileName)}";
            Output.Clear();
            TokenList.Items.Clear();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveFile();
        }

        private bool SaveFile()
        {
            if (_currentFile is null)
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "C source (*.c)|*.c|All files (*.*)|*.*",
                    DefaultExt = ".c",
                    FileName = "main.c"
                };

                if (dialog.ShowDialog() != true)
                    return false;

                _currentFile = dialog.FileName;
            }

            File.WriteAllText(_currentFile, Editor.Text);
            StatusText.Text = $"Saved {Path.GetFileName(_currentFile)}";
            return true;
        }

        private void Compile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var compiler = new CCompiler();
                var result = compiler.Compile(Editor.Text);

                TokenList.Items.Clear();

                foreach (var token in result.Tokens)
                    TokenList.Items.Add($"{token.Line,3}:{token.Column,-3}  {token.Kind,-18} {token.Text}");

                var output = new StringBuilder();

                if (result.Success)
                {
                    output.AppendLine("BUILD SUCCEEDED");
                    output.AppendLine();

                    output.AppendLine("AST");
                    output.AppendLine("---");
                    output.AppendLine(AstPrinter.Print(result.Program!));

                    var codeGenerator = new X64CodeGenerator();
                    var nativeCode = codeGenerator.Generate(result.Program!);

                    Debug.WriteLine($"MACHINE CODE LENGTH: {nativeCode.MachineCode.Length}");

                    for (int i = 0; i < nativeCode.MachineCode.Length - 2; i++)
                    {
                        if (nativeCode.MachineCode[i] == 0xD3 &&
                            nativeCode.MachineCode[i + 1] == 0xF8)
                        {
                            Debug.WriteLine(
                                $"MACHINE CODE: D3 F8 at {i:X}, previous={nativeCode.MachineCode[i - 1]:X2}");
                        }

                        if (nativeCode.MachineCode[i] == 0x48 &&
                            nativeCode.MachineCode[i + 1] == 0xD3 &&
                            nativeCode.MachineCode[i + 2] == 0xF8)
                        {
                            Debug.WriteLine(
                                $"MACHINE CODE: 48 D3 F8 at {i:X}");
                        }
                    }


                    var outputDirectory = Path.Combine(AppContext.BaseDirectory, "output");
                    Directory.CreateDirectory(outputDirectory);
                    var executablePath = Path.Combine(outputDirectory, "JollyCProgram.exe");
                    var peWriter = new PeWriter();
                    peWriter.Write(executablePath, nativeCode);
                    _compiledOutputPath = executablePath;

                    output.AppendLine();
                    output.AppendLine("NATIVE x64");
                    output.AppendLine("----------");

                    output.AppendLine(string.Join(" ", nativeCode.MachineCode.Select(b => b.ToString("X2"))));

                    output.AppendLine();
                    output.AppendLine("OUTPUT");
                    output.AppendLine("------");
                    output.AppendLine(executablePath);

                    AstOutput.Text = AstPrinter.Print(result.Program!);

                    NativeCodeGrid.ItemsSource = nativeCode.Instructions;

                    foreach (var instruction in nativeCode.Instructions)
                    {
                        if (instruction.Offset >= 110 && instruction.Offset <= 140)
                        {
                            Debug.WriteLine(
                                $"{instruction.Offset:X4}: {instruction.BytesText,-20} {instruction.Assembly}");
                        }

                        if (instruction.Offset >= 280 && instruction.Offset <= 305)
                        {
                            Debug.WriteLine(
                                $"{instruction.Offset:X4}: {instruction.BytesText,-20} {instruction.Assembly}");
                        }
                    }


                    StatusText.Text = "Compile succeeded";
                }
                else
                {
                    output.AppendLine("BUILD FAILED");
                    output.AppendLine();

                    foreach (var diagnostic in result.Diagnostics)
                        output.AppendLine(diagnostic.ToString());

                    StatusText.Text = $"Compile failed ({result.Diagnostics.Count} error(s))";
                }

                Output.Text = output.ToString();
            }
            catch (Exception ex)
            {
                Output.Text = $"INTERNAL COMPILER ERROR{Environment.NewLine}{Environment.NewLine}{ex}";
                StatusText.Text = "Compiler error";
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void StartConsole()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/K",
                WorkingDirectory = Path.GetDirectoryName(_compiledOutputPath)!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = false
            };

            _consoleProcess = Process.Start(startInfo);
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_compiledOutputPath) || !File.Exists(_compiledOutputPath))
                {
                    Output.Text = "No compiled executable found. Compile first.";
                    StatusText.Text = "Nothing to run";
                    return;
                }

                if (_consoleProcess == null || _consoleProcess.HasExited)
                {
                    StartConsole();
                }

                _consoleProcess!.StandardInput.WriteLine($"\"{_compiledOutputPath}\"");
                _consoleProcess.StandardInput.WriteLine("echo JOLLYC_EXITCODE:%ERRORLEVEL%");
                _consoleProcess.StandardInput.Flush();

                StatusText.Text = "Program running";
            }
            catch (Exception ex)
            {
                Output.AppendText($"{Environment.NewLine}RUN ERROR{Environment.NewLine}{ex}");
                StatusText.Text = "Run failed";
            }
        }
    }
}