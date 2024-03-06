namespace CrealityKEGCodeRunner;

internal class CrealityDataReceivedEventArgs(string data) : EventArgs
{
    public string Data { get; } = data;
}
