using System;
using System.IO;

namespace ProLang.Portable;

/// <summary>
/// The desktop command line of the portable runtime; the rest of <c>Host</c> is in <c>DesktopHost.cs</c>.
/// </summary>
public static partial class Host
{
    /// <summary>
    /// Runs one program image on the interpreter, never the JIT: the desktop cannot execute MIPS.
    /// </summary>
    private static void Run(byte[] data)
    {
        Runtime.Run(data, data.Length, 0);
    }

    /// <summary>The entry point of the desktop build, which has these forms:</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><c>--self-test</c>: runs the loader, value, heap, plan and ABI suites
    ///   (<c>LoaderTests</c>, <c>HeapTests</c>, <c>PlanTests</c>, <c>AbiTests</c>).</description></item>
    /// <item><description><c>--jit-golden program.dll [--words]</c>: compiles the program with the JIT in capture
    ///   mode and prints a hash of the words for each method (<c>JitGoldenTests</c>).</description></item>
    /// <item><description><c>--repeat program.dll</c>: runs the program twice in one process, to show that all
    ///   state resets.</description></item>
    /// <item><description><c>--reset-test program.dll</c>: runs the program, then a copy with its <c>#Blob</c>
    ///   stream name damaged, and checks the second load fails cleanly.</description></item>
    /// <item><description><c>program.dll [frames]</c>: runs the program on the interpreter and prints its output,
    ///   its FRAME lines and a final OK line. <c>frames</c> (60 by default) is the frame at which <c>Buttons</c>
    ///   reports Start, which ends a frame-limited guest such as the cube. Any failure prints <c>PROLANG FAIL</c>
    ///   and the message and returns 1.</description></item>
    /// </list>
    /// <para>The benchmark harness patches the line that ends a successful run by pattern, so exactly one such line
    ///   must remain.</para>
    /// </remarks>
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                LoaderTests.TestLoaderBounds();
                AbiTests.TestAbi();
                return 0;
            }

            if (args.Length >= 2 && args[0] == "--jit-golden")
            {
                bool words = args.Length == 3 && args[2] == "--words";
                if (args.Length > 3 || (args.Length == 3 && !words))
                {
                    throw new ArgumentException("Portable --jit-golden program.dll [--words]");
                }

                byte[] guest = File.ReadAllBytes(args[1]);
                JitGoldenTests.JitGolden(guest, guest.Length, words);
                return 0;
            }

            if (args.Length == 2 && args[0] == "--repeat")
            {
                byte[] program = File.ReadAllBytes(args[1]);
                Run(program);
                Run(program);
                return 0;
            }

            if (args.Length == 2 && args[0] == "--reset-test")
            {
                byte[] program = File.ReadAllBytes(args[1]);
                Run(program);
                byte[] broken = (byte[])program.Clone();
                byte[] marker = System.Text.Encoding.ASCII.GetBytes("#Blob\0");
                int position = broken.AsSpan().IndexOf(marker);
                if (position < 0)
                {
                    throw new InvalidDataException("missing test metadata marker");
                }
                broken[position + 3] = (byte)'a';
                try
                {
                    Run(broken);
                }
                catch (InvalidOperationException e) when (e.Message == "missing metadata streams")
                {
                    Console.WriteLine("PASS portable malformed second load");
                    return 0;
                }
                throw new Exception("expected missing metadata streams on second load");
            }

            if (args.Length < 1)
            {
                throw new ArgumentException("Portable program.dll [frames]");
            }

            if (args.Length > 1)
            {
                limit = int.Parse(args[1]);
            }

            byte[] data = File.ReadAllBytes(args[0]);
            Run(data);
            Console.WriteLine("PROLANG OK");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("PROLANG FAIL: " + e.Message);
            return 1;
        }
    }
}
