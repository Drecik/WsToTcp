namespace WsToTcp;

internal sealed class ReloadOptions
{
    public ReloadOptions(string? key)
    {
        Key = string.IsNullOrWhiteSpace(key) ? null : key;
    }

    public string? Key { get; }
}
