using System;
using System.IO;

namespace ProLang.Portable;

public static partial class Host
{
    static void Run(byte[] data)
    {
        VM.Run(data, data.Length, 0);
    }

    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                VM.TestLoaderBounds();
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
