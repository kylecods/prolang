using System;
using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyVersion("1.0.0.0")]
internal static class Program
{
	public static ParseResult parseResult(object val, int pos)
	{
		ParseResult parseResult = default(ParseResult);
		Unsafe.SkipInit(out ParseResult result);
		result.value = val;
		result.nextPos = pos;
		return result;
	}

	public static void main()
	{
		Console.WriteLine("=== Testing ParseResult Struct ===");
		ParseResult parseResult = default(ParseResult);
		parseResult = Program.parseResult("hello", 5);
		Console.WriteLine("Result 1:");
		Console.WriteLine(string.Concat((object?)"  value: ", (object?)parseResult.value.ToString()));
		Console.WriteLine("  nextPos: " + parseResult.nextPos);
		ParseResult parseResult2 = default(ParseResult);
		parseResult2 = Program.parseResult(42, 10);
		Console.WriteLine("Result 2:");
		Console.WriteLine(string.Concat((object?)"  value: ", (object?)parseResult2.value.ToString()));
		Console.WriteLine(string.Concat((object?)"  nextPos: ", (object?)((object)parseResult2.nextPos).ToString()));
		Console.WriteLine("✅ ParseResult struct works!");
	}
}
public struct ParseResult
{
	public object value;

	public int nextPos;
}
