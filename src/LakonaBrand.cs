using System.Text;

internal static class LakonaBrand
{
    private static readonly string[] Text = {
        @" /\_/\   L A K O N A",
        @"( oᴥo )",
        @" U_⌨_U",
    };

    private const int Width = 30;

    private const string PurpleFg  = "\x1b[38;2;46;8;84m";               // #2E0854 border
    private const string GoldFg   = "\x1b[38;2;253;185;39m";               // gold text
    private const string Reset    = "\x1b[0m";

    public static void Print()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Best-effort: if we can't set UTF-8, print anyway.
        }

        // Top border
        PrintBorder('╔', '═', '╗');

        // Content lines with side borders
        foreach (var line in Text)
        {
            var contentWidth = 6 + line.Length;
            var rightPadding = Width - contentWidth;

            Console.Write(PurpleFg);
            Console.Write('║');
            Console.Write(Reset);

            Console.Write(GoldFg);
            Console.Write(new string(' ', 6));
            Console.Write(line);
            if (rightPadding > 0)
                Console.Write(new string(' ', rightPadding));
            Console.Write(Reset);

            Console.Write(PurpleFg);
            Console.Write('║');
            Console.Write(Reset);

            Console.WriteLine();
        }

        // Bottom border
        PrintBorder('╚', '═', '╝');
    }

    private static void PrintBorder(char left, char fill, char right)
    {
        Console.Write(PurpleFg);
        Console.Write(left);
        Console.Write(new string(fill, Width));
        Console.Write(right);
        Console.Write(Reset);
        Console.WriteLine();
    }
}
