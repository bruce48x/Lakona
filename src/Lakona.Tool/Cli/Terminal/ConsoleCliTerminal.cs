internal sealed class ConsoleCliTerminal : ICliTerminal
{
    public bool IsInputRedirected => Console.IsInputRedirected;
    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public string? ReadLine()
    {
        return Console.ReadLine();
    }

    public void Write(string value)
    {
        Console.Write(value);
    }

    public void WriteLine(string value)
    {
        Console.WriteLine(value);
    }

    public void WriteErrorLine(string value)
    {
        Console.Error.WriteLine(value);
    }
}
