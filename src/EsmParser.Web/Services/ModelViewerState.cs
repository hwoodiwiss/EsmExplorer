namespace EsmParser.Web.Services;

/// <summary>
/// Carries a model path from the plugin explorer to the model viewer page,
/// which loads it on initialization.
/// </summary>
public sealed class ModelViewerState
{
    /// <summary>A Data-relative model path (as stored in a MODL field) awaiting display.</summary>
    public string? RequestedModelPath { get; private set; }

    public event Action? Changed;

    public void Request(string path)
    {
        RequestedModelPath = path;
        Changed?.Invoke();
    }

    /// <summary>Consumes the pending request (called by the viewer once loading starts).</summary>
    public string? TakeRequest()
    {
        string? path = RequestedModelPath;
        RequestedModelPath = null;
        return path;
    }
}
