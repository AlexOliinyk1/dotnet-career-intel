namespace CareerIntel.Web.Services;

public enum ToastType { Info, Success, Warning, Error }

public sealed class ToastItem
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Message { get; init; } = string.Empty;
    public ToastType Type { get; init; }
}

/// <summary>
/// Scoped toast notification service for Blazor Server.
/// Each circuit gets its own instance.
/// </summary>
public sealed class ToastService : IDisposable
{
    private readonly List<ToastItem> _toasts = [];
    private readonly object _lock = new();

    public IReadOnlyList<ToastItem> Toasts
    {
        get { lock (_lock) return _toasts.ToList(); }
    }

    public event Action? OnChange;

    public void Show(string message, ToastType type = ToastType.Info, int autoCloseMs = 5000)
    {
        var item = new ToastItem { Message = message, Type = type };
        lock (_lock) _toasts.Add(item);
        OnChange?.Invoke();

        if (autoCloseMs > 0)
        {
            _ = Task.Delay(autoCloseMs).ContinueWith(_ => Dismiss(item.Id));
        }
    }

    public void Success(string message) => Show(message, ToastType.Success);
    public void Error(string message) => Show(message, ToastType.Error, 8000);
    public void Warning(string message) => Show(message, ToastType.Warning);
    public void Info(string message) => Show(message, ToastType.Info);

    public void Dismiss(string id)
    {
        lock (_lock) _toasts.RemoveAll(t => t.Id == id);
        OnChange?.Invoke();
    }

    public void Dispose()
    {
        lock (_lock) _toasts.Clear();
    }
}
