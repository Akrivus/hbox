using System;
using System.Collections.Generic;

public sealed class ChatManagerBinding : IDisposable
{
    private readonly List<Action> _unbind = new();
    private bool _disposed;

    public void Bind<TDelegate>(Action<TDelegate> add, Action<TDelegate> remove, TDelegate handler) where TDelegate : Delegate
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChatManagerBinding));
        if (add == null) throw new ArgumentNullException(nameof(add));
        if (remove == null) throw new ArgumentNullException(nameof(remove));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        add(handler);
        _unbind.Add(() => remove(handler));
    }

    public void Unbind<TDelegate>(Action<TDelegate> remove, TDelegate handler) where TDelegate : Delegate
    {
        if (_disposed) return;
        if (remove == null) throw new ArgumentNullException(nameof(remove));
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        remove(handler);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (int i = _unbind.Count - 1; i >= 0; i--)
            _unbind[i]?.Invoke();
        _unbind.Clear();
    }
}
