namespace ProLang.Portable;

/// <summary>The whitelist of calls a guest program may make outside itself, and their implementations.</summary>
/// <remarks>
/// <para><c>ResolveNative</c> runs at decode time: it maps a call target (declaring type, method name and
///   signature) to a native id and fails for anything not listed, so an unsupported call is refused when the
///   program loads. <c>Call</c> runs at execution time and switches on the id. They sit together because they share
///   the ids declared below.</para>
/// <para><c>DelegateInvoke</c> and <c>DelegateCtor</c> are ids too, but <c>Interpreter.ExecuteResolved</c> handles
///   them itself rather than through <c>Call</c>. The JIT emits no code for them: it runs them through the Slow
///   helper, which executes the one instruction in <c>ExecuteResolved</c>.</para>
/// </remarks>
internal static class Natives
{
    // Native ids: what ResolveNative returns for each method it allows, and what Call switches on. Ids 1 to 26
    // are plain calls. DelegateInvoke and DelegateCtor never reach Call: Interpreter.ExecuteResolved handles them
    // itself, and the JIT has no code for them, so a call or newobj of either runs through its Slow helper.
    // Call's case labels stay numeric literals (a C case label cannot be a variable); each comment names the id.
    internal const int PspInit = 1; // PspOps.Init
    internal const int PspClear = 2; // PspOps.Clear
    internal const int PspFillRect = 3; // PspOps.FillRect
    internal const int PspDrawLine = 4; // PspOps.DrawLine
    internal const int PspDrawText = 5; // PspOps.DrawText
    internal const int PspSwapBuffers = 6; // PspOps.SwapBuffers
    internal const int PspVsync = 7; // PspOps.Vsync
    internal const int PspButtonsHeld = 8; // PspOps.ButtonsHeld
    internal const int PspButtonPressed = 9; // PspOps.ButtonPressed
    internal const int OutputInitialize = 10; // Output.Initialize
    internal const int OutputFlush = 11; // Output.Flush
    internal const int OutputWrite = 12; // Output.Write
    internal const int StringFrom = 13; // StringOps.From
    internal const int StringConcat = 14; // String.Concat
    internal const int StringEquals = 15; // String.op_Equality
    internal const int StringNotEquals = 16; // String.op_Inequality
    internal const int StringLength = 17; // String.get_Length
    internal const int StringCharAt = 18; // StringOps.CharAt and StringOps.CharCode
    internal const int StringSubstring = 19; // StringOps.Substring
    internal const int StringIndexOf = 20; // String.IndexOf
    internal const int MathMin = 21; // Math.Min
    internal const int MathMax = 22; // Math.Max
    internal const int TestAssert = 23; // TestOps.Assert
    internal const int ConsoleWrite = 24; // ConsoleOps.Write
    internal const int ThreadSleep = 25; // Thread.Sleep
    internal const int TimeMillis = 26; // TimeOps.Millis
    internal const int DelegateInvoke = 100; // Func`n.Invoke and Action.Invoke
    internal const int DelegateCtor = 101; // the Func and Action constructors

    // The largest delay Thread.Sleep accepts, in milliseconds.
    internal const int MaxSleepMillis = 60000;
    // The character code written after each Output.Write.
    internal const int NewlineChar = 10;

    /// <summary>
    /// The handle of the buffered <c>print</c> output, a string object, or 0 when nothing is buffered. Each write
    /// appends a line. <c>Flush</c> sends it to the host, and the collector treats it as a root.
    /// </summary>
    internal static int OutputHandle;

    /// <summary>Empties the output buffer.</summary>
    internal static void Reset()
    {
        OutputHandle = 0;
    }

    /// <summary>
    /// Returns the native id for a call or <c>newobj</c> of a method outside the guest program, or fails with
    /// 'unsupported native signature' if the method is not on the whitelist. The whitelist is grouped by the type
    /// that declares the method, and the groups are tried in the order below: the first group to recognise the
    /// method decides. Each group returns 0 for no match, which is never a native id.
    /// </summary>
    /// <remarks>
    /// <para>The declaring type is the one <c>TypeSystem.ResolveTypeName</c> last resolved; <c>nameOffset</c> is
    ///   the file offset of the method name.</para>
    /// </remarks>
    internal static int ResolveNative(int nameOffset, MethodSignature signature)
    {
        int nativeId = ResolveDelegateNative(nameOffset, signature);
        if (nativeId != 0)
        {
            return nativeId;
        }

        nativeId = ResolvePspNative(nameOffset, signature);
        if (nativeId != 0)
        {
            return nativeId;
        }

        nativeId = ResolveOutputNative(nameOffset, signature);
        if (nativeId != 0)
        {
            return nativeId;
        }

        nativeId = ResolveStringOpsNative(nameOffset, signature);
        if (nativeId != 0)
        {
            return nativeId;
        }

        nativeId = ResolveSystemStringNative(nameOffset, signature);
        if (nativeId != 0)
        {
            return nativeId;
        }

        nativeId = ResolveMathNative(nameOffset, signature);
        if (nativeId != 0)
        {
            return nativeId;
        }

        nativeId = ResolveServiceNative(nameOffset, signature);
        if (nativeId != 0)
        {
            return nativeId;
        }

        return Runtime.Fail("unsupported native signature");
    }

    /// <summary>The type of a method's first parameter, or 0 when it has none.</summary>
    private static int FirstParameterType(MethodSignature signature)
    {
        return signature.ParamCount > 0 ? signature.ParamTypes[0] : 0;
    }

    /// <summary>
    /// True when the type that <c>TypeSystem.ResolveTypeName</c> last resolved has this namespace and name.
    /// </summary>
    private static bool ResolvedTypeIs(string typeNamespace, string typeName)
    {
        return Metadata.NameEqualsText(TypeSystem.ResolvedTypeNamespace, typeNamespace) && Metadata.NameEqualsText(TypeSystem.ResolvedTypeName, typeName);
    }

    /// <summary>
    /// True when <c>signature</c> has exactly this HASTHIS flag, return type and parameter count, and its first
    /// <c>paramCount</c> parameter types, up to five, equal <c>param0</c> to <c>param4</c>.
    /// </summary>
    private static bool SignatureMatches(MethodSignature signature, int hasThis, int returnType, int paramCount, int param0, int param1, int param2, int param3, int param4)
    {
        if (signature.HasThis != hasThis || signature.ReturnType != returnType || signature.ParamCount != paramCount)
        {
            return false;
        }

        return (paramCount < 1 || signature.ParamTypes[0] == param0) && (paramCount < 2 || signature.ParamTypes[1] == param1) && (paramCount < 3 || signature.ParamTypes[2] == param2) && (paramCount < 4 || signature.ParamTypes[3] == param3) && (paramCount < 5 || signature.ParamTypes[4] == param4);
    }

    /// <summary>
    /// <c>System.Func</c> and <c>System.Action</c>: <c>Invoke</c>, and the constructor that wraps a function
    /// pointer.
    /// </summary>
    private static int ResolveDelegateNative(int nameOffset, MethodSignature signature)
    {
        if (Metadata.NameEqualsText(TypeSystem.ResolvedTypeNamespace, "System") && (Metadata.NameStartsWith(TypeSystem.ResolvedTypeName, "Func`") || Metadata.NameStartsWith(TypeSystem.ResolvedTypeName, "Action")))
        {
            if (Metadata.NameEqualsText(nameOffset, "Invoke") && signature.HasThis == 1)
            {
                return DelegateInvoke;
            }

            if (Metadata.NameEqualsText(nameOffset, ".ctor") && SignatureMatches(signature, 1, ElementType.Void, 2, ElementType.Object, ElementType.I, 0, 0, 0))
            {
                return DelegateCtor;
            }
        }

        return 0;
    }

    /// <summary><c>ProLang.Runtime.PspOps</c>: the PSP host calls (display, drawing, controller).</summary>
    private static int ResolvePspNative(int nameOffset, MethodSignature signature)
    {
        if (ResolvedTypeIs("ProLang.Runtime", "PspOps"))
        {
            if (Metadata.NameEqualsText(nameOffset, "Init") && SignatureMatches(signature, 0, ElementType.Void, 0, 0, 0, 0, 0, 0))
            {
                return PspInit;
            }

            if (Metadata.NameEqualsText(nameOffset, "Clear") && SignatureMatches(signature, 0, ElementType.Void, 1, ElementType.I4, 0, 0, 0, 0))
            {
                return PspClear;
            }

            if (Metadata.NameEqualsText(nameOffset, "FillRect") && SignatureMatches(signature, 0, ElementType.Void, 5, ElementType.I4, ElementType.I4, ElementType.I4, ElementType.I4, ElementType.I4))
            {
                return PspFillRect;
            }

            if (Metadata.NameEqualsText(nameOffset, "DrawLine") && SignatureMatches(signature, 0, ElementType.Void, 5, ElementType.I4, ElementType.I4, ElementType.I4, ElementType.I4, ElementType.I4))
            {
                return PspDrawLine;
            }

            if (Metadata.NameEqualsText(nameOffset, "DrawText") && SignatureMatches(signature, 0, ElementType.Void, 4, ElementType.I4, ElementType.I4, ElementType.String, ElementType.I4, 0))
            {
                return PspDrawText;
            }

            if (Metadata.NameEqualsText(nameOffset, "SwapBuffers") && SignatureMatches(signature, 0, ElementType.Void, 0, 0, 0, 0, 0, 0))
            {
                return PspSwapBuffers;
            }

            if (Metadata.NameEqualsText(nameOffset, "Vsync") && SignatureMatches(signature, 0, ElementType.Void, 0, 0, 0, 0, 0, 0))
            {
                return PspVsync;
            }

            if (Metadata.NameEqualsText(nameOffset, "ButtonsHeld") && SignatureMatches(signature, 0, ElementType.I4, 0, 0, 0, 0, 0, 0))
            {
                return PspButtonsHeld;
            }

            if (Metadata.NameEqualsText(nameOffset, "ButtonPressed") && SignatureMatches(signature, 0, ElementType.Boolean, 1, ElementType.I4, 0, 0, 0, 0))
            {
                return PspButtonPressed;
            }
        }

        return 0;
    }

    /// <summary>
    /// <c>ProLang.Runtime.Output</c>: the buffered <c>print</c> output (initialize, flush, write one value).
    /// </summary>
    private static int ResolveOutputNative(int nameOffset, MethodSignature signature)
    {
        int firstParam = FirstParameterType(signature);
        if (ResolvedTypeIs("ProLang.Runtime", "Output"))
        {
            if (Metadata.NameEqualsText(nameOffset, "Initialize") && SignatureMatches(signature, 0, ElementType.Void, 0, 0, 0, 0, 0, 0))
            {
                return OutputInitialize;
            }

            if (Metadata.NameEqualsText(nameOffset, "Flush") && SignatureMatches(signature, 0, ElementType.Void, 0, 0, 0, 0, 0, 0))
            {
                return OutputFlush;
            }

            if (Metadata.NameEqualsText(nameOffset, "Write") && signature.HasThis == 0 && signature.ReturnType == ElementType.Void && signature.ParamCount == 1 && (firstParam == ElementType.Boolean || firstParam == ElementType.Char || firstParam == ElementType.I4 || firstParam == ElementType.U4 || firstParam == ElementType.String || firstParam == ElementType.Object))
            {
                return OutputWrite;
            }
        }

        return 0;
    }

    /// <summary><c>ProLang.Runtime.StringOps</c>: string conversion, character access and substring.</summary>
    private static int ResolveStringOpsNative(int nameOffset, MethodSignature signature)
    {
        int firstParam = FirstParameterType(signature);
        if (ResolvedTypeIs("ProLang.Runtime", "StringOps"))
        {
            if (Metadata.NameEqualsText(nameOffset, "From") && signature.HasThis == 0 && signature.ReturnType == ElementType.String && signature.ParamCount == 1 && (firstParam == ElementType.Boolean || firstParam == ElementType.Char || firstParam == ElementType.I4 || firstParam == ElementType.U4 || firstParam == ElementType.Object))
            {
                return StringFrom;
            }

            if ((Metadata.NameEqualsText(nameOffset, "CharAt") && SignatureMatches(signature, 0, ElementType.Char, 2, ElementType.String, ElementType.I4, 0, 0, 0)) || (Metadata.NameEqualsText(nameOffset, "CharCode") && SignatureMatches(signature, 0, ElementType.I4, 2, ElementType.String, ElementType.I4, 0, 0, 0)))
            {
                return StringCharAt;
            }

            if (Metadata.NameEqualsText(nameOffset, "Substring") && SignatureMatches(signature, 0, ElementType.String, 3, ElementType.String, ElementType.I4, ElementType.I4, 0, 0))
            {
                return StringSubstring;
            }
        }

        return 0;
    }

    /// <summary><c>System.String</c>: concatenation, equality, length and <c>IndexOf</c>.</summary>
    private static int ResolveSystemStringNative(int nameOffset, MethodSignature signature)
    {
        if (ResolvedTypeIs("System", "String"))
        {
            if (Metadata.NameEqualsText(nameOffset, "Concat") && (SignatureMatches(signature, 0, ElementType.String, 2, ElementType.String, ElementType.String, 0, 0, 0) || SignatureMatches(signature, 0, ElementType.String, 2, ElementType.Object, ElementType.Object, 0, 0, 0)))
            {
                return StringConcat;
            }

            if (Metadata.NameEqualsText(nameOffset, "op_Equality") && SignatureMatches(signature, 0, ElementType.Boolean, 2, ElementType.String, ElementType.String, 0, 0, 0))
            {
                return StringEquals;
            }

            if (Metadata.NameEqualsText(nameOffset, "op_Inequality") && SignatureMatches(signature, 0, ElementType.Boolean, 2, ElementType.String, ElementType.String, 0, 0, 0))
            {
                return StringNotEquals;
            }

            if (Metadata.NameEqualsText(nameOffset, "get_Length") && SignatureMatches(signature, 1, ElementType.I4, 0, 0, 0, 0, 0, 0))
            {
                return StringLength;
            }

            if (Metadata.NameEqualsText(nameOffset, "IndexOf") && SignatureMatches(signature, 1, ElementType.I4, 1, ElementType.String, 0, 0, 0, 0))
            {
                return StringIndexOf;
            }
        }

        return 0;
    }

    /// <summary><c>System.Math</c>: <c>Min</c> and <c>Max</c> over two ints.</summary>
    private static int ResolveMathNative(int nameOffset, MethodSignature signature)
    {
        if (ResolvedTypeIs("System", "Math") && SignatureMatches(signature, 0, ElementType.I4, 2, ElementType.I4, ElementType.I4, 0, 0, 0))
        {
            if (Metadata.NameEqualsText(nameOffset, "Min"))
            {
                return MathMin;
            }

            if (Metadata.NameEqualsText(nameOffset, "Max"))
            {
                return MathMax;
            }
        }

        return 0;
    }

    /// <summary>
    /// The remaining one-method groups: assertion (<c>TestOps</c>), console output (<c>ConsoleOps</c>), sleep
    /// (<c>Thread</c>) and the clock (<c>TimeOps</c>).
    /// </summary>
    private static int ResolveServiceNative(int nameOffset, MethodSignature signature)
    {
        if (ResolvedTypeIs("ProLang.Runtime", "TestOps") && Metadata.NameEqualsText(nameOffset, "Assert") && SignatureMatches(signature, 0, ElementType.Void, 2, ElementType.Boolean, ElementType.String, 0, 0, 0))
        {
            return TestAssert;
        }

        if (ResolvedTypeIs("ProLang.Runtime", "ConsoleOps") && Metadata.NameEqualsText(nameOffset, "Write") && SignatureMatches(signature, 0, ElementType.Void, 1, ElementType.String, 0, 0, 0, 0))
        {
            return ConsoleWrite;
        }

        if (ResolvedTypeIs("System.Threading", "Thread") && Metadata.NameEqualsText(nameOffset, "Sleep") && SignatureMatches(signature, 0, ElementType.Void, 1, ElementType.I4, 0, 0, 0, 0))
        {
            return ThreadSleep;
        }

        if (ResolvedTypeIs("ProLang.Runtime", "TimeOps") && Metadata.NameEqualsText(nameOffset, "Millis") && SignatureMatches(signature, 0, ElementType.I4, 0, 0, 0, 0, 0, 0))
        {
            return TimeMillis;
        }

        return 0;
    }

    /// <summary>
    /// Writes the buffered <c>print</c> output to the host and empties the buffer. It runs for <c>Output.Flush</c>,
    /// before a console write or an assertion message, and when <c>Runtime.Run</c> finishes.
    /// </summary>
    internal static void Flush()
    {
        if (OutputHandle != 0 && Heap.HandleTable[OutputHandle].Count > 0)
        {
            Host.Write(Heap.HandleTable[OutputHandle].Chars, Heap.HandleTable[OutputHandle].Count);
        }

        OutputHandle = 0;
    }

    /// <summary>
    /// Runs the native <c>op.NativeId</c>. Its arguments are the values at <c>Interpreter.Memory[firstArg]</c>
    /// onward, and it returns the result, or <c>default(Value)</c> for a native that returns nothing. A failed
    /// check ends the program through <c>Runtime.Fail</c>.
    /// </summary>
    /// <remarks>
    /// <para>This is a hot path. Its case labels are the ids declared at the top of the class as numeric literals,
    ///   each with a comment naming the id, because a C case label cannot be a variable.</para>
    /// </remarks>
    internal static Value Call(ColdOp op, int firstArg)
    {
        int nativeId = op.NativeId;
        // Case labels are the native ids declared at the top of Natives, kept as literals because a C case label cannot be a variable.
        switch (nativeId)
        {
            case 1: // PspInit
                Host.Init();
                break;
            case 2: // PspClear
                Host.Clear(Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 0]));
                break;
            case 3: // PspFillRect
                Host.Rect(Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 0]), Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 1]), Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 2]), Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 3]), Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 4]));
                break;
            case 4: // PspDrawLine
                Host.Line(Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 0]), Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 1]), Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 2]), Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 3]), Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 4]));
                break;
            case 5: // PspDrawText
                {
                    int text = Objects.RequireHandle(Interpreter.Memory[firstArg + 2], ValueKind.String);
                    Host.Text(Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 0]), Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 1]), Heap.HandleTable[text].Chars, Heap.HandleTable[text].Count, Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 3]));
                    break;
                }

            case 6: // PspSwapBuffers
                Host.Present(Interpreter.Steps, Interpreter.Calls);
                Interpreter.Steps = 0;
                Interpreter.Calls = 0;
                Host.CollectStats();
                break;
            case 7: // PspVsync
                Host.Vsync();
                break;
            case 8: // PspButtonsHeld
                return Arithmetic.MakeInt32(Host.Buttons());
            case 9: // PspButtonPressed
                return Arithmetic.MakeInt32((Host.Buttons() & Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 0])) != 0 ? 1 : 0);
            case 10: // OutputInitialize
                OutputHandle = 0;
                break;
            case 11: // OutputFlush
                Flush();
                break;
            case 12: // OutputWrite
                {
                    int formatted = Objects.FormatValue(Interpreter.Memory[firstArg + 0], op.Type);
                    int newline = Objects.NewText(Heap.HandleTable[formatted].Count + 1, 0);
                    for (int i = 0; i < Heap.HandleTable[formatted].Count; i++)
                    {
                        Heap.HandleTable[newline].Chars[i] = Heap.HandleTable[formatted].Chars[i];
                    }

                    Heap.HandleTable[newline].Chars[Heap.HandleTable[formatted].Count] = NewlineChar;
                    OutputHandle = OutputHandle == 0 ? newline : Objects.Concat(OutputHandle, newline);
                    break;
                }

            case 13: // StringFrom
                return Objects.MakeReference(ValueKind.String, Objects.FormatValue(Interpreter.Memory[firstArg + 0], op.Type));
            case 14: // StringConcat
                return Objects.MakeReference(ValueKind.String, Objects.Concat(Objects.FormatValue(Interpreter.Memory[firstArg + 0], ElementType.Object), Objects.FormatValue(Interpreter.Memory[firstArg + 1], ElementType.Object)));
            case 15: // StringEquals
                return Arithmetic.MakeInt32(Objects.ValuesEqual(Interpreter.Memory[firstArg + 0], Interpreter.Memory[firstArg + 1], 0) ? 1 : 0);
            case 16: // StringNotEquals
                return Arithmetic.MakeInt32(Objects.ValuesEqual(Interpreter.Memory[firstArg + 0], Interpreter.Memory[firstArg + 1], 0) ? 0 : 1);
            case 17: // StringLength
                return Arithmetic.MakeInt32(Heap.HandleTable[Objects.RequireHandle(Interpreter.Memory[firstArg + 0], ValueKind.String)].Count);
            case 18: // StringCharAt
                {
                    int text = Objects.RequireHandle(Interpreter.Memory[firstArg + 0], ValueKind.String);
                    int index = Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 1]);
                    if (index < 0 || index >= Heap.HandleTable[text].Count)
                    {
                        Runtime.Fail("string index out of bounds");
                    }

                    return Arithmetic.MakeInt32(Heap.HandleTable[text].Chars[index]);
                }

            case 19: // StringSubstring
                {
                    int text = Objects.RequireHandle(Interpreter.Memory[firstArg + 0], ValueKind.String);
                    int start = Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 1]), end = Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 2]);
                    if (start < 0 || end < start || end > Heap.HandleTable[text].Count)
                    {
                        Runtime.Fail("substring range");
                    }

                    int result = Objects.NewText(end - start, 0);
                    for (int i = 0; i < end - start; i++)
                    {
                        Heap.HandleTable[result].Chars[i] = Heap.HandleTable[text].Chars[start + i];
                    }

                    return Objects.MakeReference(ValueKind.String, result);
                }

            case 20: // StringIndexOf
                {
                    int text = Objects.RequireHandle(Interpreter.Memory[firstArg + 0], ValueKind.String);
                    int needle = Objects.RequireHandle(Interpreter.Memory[firstArg + 1], ValueKind.String);
                    for (int i = 0; i <= Heap.HandleTable[text].Count - Heap.HandleTable[needle].Count; i++)
                    {
                        int isMatch = 1;
                        for (int j = 0; j < Heap.HandleTable[needle].Count; j++)
                        {
                            if (Heap.HandleTable[text].Chars[i + j] != Heap.HandleTable[needle].Chars[j])
                            {
                                isMatch = 0;
                            }
                        }

                        if (isMatch != 0)
                        {
                            return Arithmetic.MakeInt32(i);
                        }
                    }

                    return Arithmetic.MakeInt32(-1);
                }

            case 21: // MathMin
                {
                    int left = Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 0]), right = Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 1]);
                    return Arithmetic.MakeInt32(left < right ? left : right);
                }

            case 22: // MathMax
                {
                    int left = Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 0]), right = Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 1]);
                    return Arithmetic.MakeInt32(left > right ? left : right);
                }

            case 23: // TestAssert
                if (Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 0]) == 0)
                {
                    Flush();
                    int message = Objects.RequireHandle(Interpreter.Memory[firstArg + 1], ValueKind.String);
                    Host.Write(Heap.HandleTable[message].Chars, Heap.HandleTable[message].Count);
                    Runtime.Fail("assertion failed");
                }

                break;
            case 24: // ConsoleWrite
                {
                    Flush();
                    int text = Objects.RequireHandle(Interpreter.Memory[firstArg + 0], ValueKind.String);
                    Host.Write(Heap.HandleTable[text].Chars, Heap.HandleTable[text].Count);
                    break;
                }

            case 25: // ThreadSleep
                {
                    int millis = Arithmetic.ExpectInt32(Interpreter.Memory[firstArg + 0]);
                    if (millis < 0 || millis > MaxSleepMillis)
                    {
                        Runtime.Fail("sleep range");
                    }

                    Host.Sleep(millis);
                    break;
                }

            case 26: // TimeMillis
                return Arithmetic.MakeInt32(Host.Millis());
            default:
                Runtime.Fail("unsupported native call");
                break;
        }

        return default(Value);
    }
}
