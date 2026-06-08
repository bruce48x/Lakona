using System.Text;

internal static class LakonaBrand
{
    public const string Text = "૮ •ᴥ• ა  Lakona";

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

        Console.WriteLine(Text);
    }
}
