using ProLang.Text;

namespace ProLang.Symbols;

    internal static class SymbolPrinter
    {
        public static void WriteTo(Symbol symbol, TextWriter writer)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Function:
                    WriteFunctionTo((FunctionSymbol)symbol, writer);
                    break;
                case SymbolKind.GlobalVariable:
                    WriteGlobalVariableTo((GlobalVariableSymbol)symbol, writer);
                    break;
                case SymbolKind.LocalVariable:
                    WriteLocalVariableTo((LocalVariableSymbol)symbol, writer);
                    break;
                case SymbolKind.Parameter:
                    WriteParameterTo((ParameterSymbol)symbol, writer);
                    break;
                case SymbolKind.Type:
                    WriteTypeTo((TypeSymbol)symbol, writer);
                    break;
                case SymbolKind.Struct:
                    WriteStructTo((StructSymbol)symbol, writer);
                    break;
                case SymbolKind.Enum:
                    WriteEnumTo((EnumSymbol)symbol, writer);
                    break;
                case SymbolKind.Field:
                    WriteFieldTo((StructField)symbol, writer);
                    break;
                case SymbolKind.EnumMember:
                    WriteEnumMemberTo((EnumMemberSymbol)symbol, writer);
                    break;
                default:
                    throw new Exception($"Unexpected symbol: {symbol.Kind}");
            }
        }

        private static void WriteFunctionTo(FunctionSymbol symbol, TextWriter writer)
        {
            writer.WriteKeyword("func ");
            writer.WriteIdentifier(symbol.Name);

            // The type parameters were being dropped, so a generic function printed with the same
            // signature as a non-generic one. That is tolerable in a REPL listing and not in the
            // signature an editor shows above the documentation.
            if (symbol.TypeParameters.Length > 0)
            {
                writer.WritePunctuation("<");

                for (var i = 0; i < symbol.TypeParameters.Length; i++)
                {
                    if (i > 0)
                        writer.WritePunctuation(", ");

                    writer.WriteIdentifier(symbol.TypeParameters[i].Name);
                }

                writer.WritePunctuation(">");
            }

            writer.WritePunctuation("(");

            for (int i = 0; i < symbol.Parameters.Length; i++)
            {
                if (i > 0)
                    writer.WritePunctuation(", ");

                symbol.Parameters[i].WriteTo(writer);
            }

            writer.WritePunctuation(")");

            if(symbol.Type != TypeSymbol.Void)
            {
                writer.WritePunctuation(": ");
                WriteTypeReferenceTo(symbol.Type, writer);
            }
        }

        private static void WriteGlobalVariableTo(GlobalVariableSymbol symbol, TextWriter writer)
        {
            writer.WriteKeyword("let ");
            writer.WriteIdentifier(symbol.Name);
            writer.WritePunctuation(": ");
            WriteTypeReferenceTo(symbol.Type, writer);
        }

        private static void WriteLocalVariableTo(LocalVariableSymbol symbol, TextWriter writer)
        {
            writer.WriteKeyword("let " );
            writer.WriteIdentifier(symbol.Name);
            writer.WritePunctuation(": ");
            WriteTypeReferenceTo(symbol.Type, writer);
        }

        /// <summary>
        /// A type written where a type is expected, as opposed to where it is declared.
        /// </summary>
        /// <remarks>
        /// The two are not the same rendering. A struct hovers as <c>struct Point</c>, because
        /// that is what its declaration says; a parameter of that type reads <c>p: Point</c>,
        /// because that is what its declaration says. Dispatching on the symbol's kind here would
        /// print <c>doc: struct Document</c>, which is not a thing anyone can write.
        /// </remarks>
        private static void WriteTypeReferenceTo(TypeSymbol type, TextWriter writer)
        {
            if (type is FunctionTypeSymbol)
            {
                writer.WriteIdentifier(type.ToString());
                return;
            }

            WriteTypeTo(type, writer);
        }

        private static void WriteParameterTo(ParameterSymbol symbol, TextWriter writer)
        {
            writer.WriteIdentifier(symbol.Name);
            writer.WritePunctuation(": ");
            WriteTypeReferenceTo(symbol.Type, writer);

            // A default is what makes a parameter optional and what makes it nameable at a call
            // site, so leaving it out of the signature hid the only two things a caller needs.
            if (symbol.DefaultValue != null)
            {
                writer.WritePunctuation(" = ");
                WriteConstantTo(symbol.DefaultValue, writer);
            }
        }

        private static void WriteConstantTo(object value, TextWriter writer)
        {
            switch (value)
            {
                case bool b:
                    writer.WriteKeyword(b ? "true" : "false");
                    break;
                case string s:
                    writer.WriteString($"\"{s}\"");
                    break;
                default:
                    writer.WriteNumber(value.ToString() ?? string.Empty);
                    break;
            }
        }

        private static void WriteTypeTo(TypeSymbol symbol, TextWriter writer)
        {
            writer.WriteIdentifier(symbol.Name);

            // array<int> used to print as "array". The name alone is not the type.
            if (symbol.TypeArguments.Length == 0)
                return;

            writer.WritePunctuation("<");

            for (var i = 0; i < symbol.TypeArguments.Length; i++)
            {
                if (i > 0)
                    writer.WritePunctuation(", ");

                WriteTypeTo(symbol.TypeArguments[i], writer);
            }

            writer.WritePunctuation(">");
        }

        private static void WriteStructTo(StructSymbol symbol, TextWriter writer)
        {
            // The keyword is the only cue that tells a reader whether assigning this type copies it
            // or aliases it, so hover has to show the one that was written.
            writer.WriteKeyword(symbol.IsReferenceType ? "class " : "struct ");
            writer.WriteIdentifier(symbol.Name);

            var typeParameters = symbol.IsGeneric
                ? symbol.TypeParameters.Select(p => p.Name)
                : symbol.TypeArgs.Select(a => a.ToString());

            var rendered = typeParameters.ToArray();

            if (rendered.Length == 0)
                return;

            writer.WritePunctuation("<");
            writer.WriteIdentifier(string.Join(", ", rendered));
            writer.WritePunctuation(">");
        }

        private static void WriteEnumTo(EnumSymbol symbol, TextWriter writer)
        {
            writer.WriteKeyword("enum ");
            writer.WriteIdentifier(symbol.Name);
        }

        private static void WriteFieldTo(StructField symbol, TextWriter writer)
        {
            writer.WriteIdentifier(symbol.Name);
            writer.WritePunctuation(": ");
            WriteTypeReferenceTo(symbol.Type, writer);
        }

        private static void WriteEnumMemberTo(EnumMemberSymbol symbol, TextWriter writer)
        {
            writer.WriteIdentifier(symbol.Declaring.Name);
            writer.WritePunctuation(".");
            writer.WriteIdentifier(symbol.Name);
            writer.WritePunctuation(" = ");
            writer.WriteNumber(symbol.Value.ToString());
        }
    }