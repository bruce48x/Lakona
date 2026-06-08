using System.Text;

internal static class LakonaBrand
{
    public const string Text = "૮ •ᴥ• ა  Lakona";

    private const string PurpleBg  = "\x1b[48;2;85;37;131m";           // #552583
    private const string GoldOnPurple = "\x1b[38;2;253;185;39;48;2;85;37;131m";
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

        int width = GetConsoleWidth();

        // Top: purple bar
        PrintBar(width);

        // Middle: purple bar with gold text overlaid
        Console.Write(PurpleBg);
        Console.Write(new string(' ', width));
        Console.Write('\r');
        Console.Write(GoldOnPurple);
        int pad = Math.Max(0, (width - Text.Length) / 2);
        Console.Write(new string(' ', pad));
        Console.Write(Text);
        Console.Write(Reset);
        Console.WriteLine();

        // Bottom: purple bar
        PrintBar(width);
    }

    private static int GetConsoleWidth()
    {
        try
        {
            return Math.Max(Console.WindowWidth, 40);
        }
        catch
        {
            return 80;
        }
    }

    private static void PrintBar(int width)
    {
        Console.Write(PurpleBg);
        Console.Write(new string(' ', width));
        Console.Write(Reset);
        Console.WriteLine();
    }
}
