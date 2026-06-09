using System.Text;

internal static class LakonaBrand
{
    private static readonly string[] Text = {
        @" /\_/\",
        @"( ･ᴥ･ )  Lakona",
        @" U___U",
    };

    private const int Width = 30;

    private const string PurpleBg  = "\x1b[48;2;46;8;84m";               // #2E0854
    private const string GoldOnPurple = "\x1b[38;2;253;185;39;48;2;46;8;84m";
    private const string Reset     = "\x1b[0m";

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

        // Top: purple bar
        PrintBar();

        // Middle: purple bars with gold text overlaid (3 lines)
        foreach (var line in Text)
        {
            Console.Write(PurpleBg);
            Console.Write(new string(' ', Width));
            Console.Write('\r');
            Console.Write(GoldOnPurple);
            int pad = Math.Max(0, (Width - line.Length) / 2);
            Console.Write(new string(' ', pad));
            Console.Write(line);
            Console.Write(Reset);
            Console.WriteLine();
        }

        // Bottom: purple bar
        PrintBar();
    }

    private static void PrintBar()
    {
        Console.Write(PurpleBg);
        Console.Write(new string(' ', Width));
        Console.Write(Reset);
        Console.WriteLine();
    }
}
