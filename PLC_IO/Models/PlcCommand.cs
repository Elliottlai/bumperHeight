namespace PLC_IO.Models;

/// <summary>
/// PLC ³q°T©R¥O
/// </summary>
public sealed class PlcCommand
{
    public string Command { get; }
    public byte[] BytesCommand { get; }

    public PlcCommand(string command, byte[] bytesCommand)
    {
        Command = command;
        BytesCommand = bytesCommand;
    }
}