internal interface ICliTerminal
{
    bool IsInputRedirected { get; }
    bool IsOutputRedirected { get; }
    string? ReadLine();
    void Write(string value);
    void WriteLine(string value);
    void WriteErrorLine(string value);
}
