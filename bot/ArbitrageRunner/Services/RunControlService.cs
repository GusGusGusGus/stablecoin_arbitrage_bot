using System;
using System.Threading;

namespace ArbitrageRunner.Services;

public sealed class RunControlService
{
    public const string DefaultKey = "global";

    private readonly object _sync = new();
    private readonly Dictionary<string, CancellationTokenSource> _scopes = new();

    public bool IsRunActive(string key = DefaultKey)
    {
        lock (_sync)
        {
            return _scopes.TryGetValue(key, out var scope) && !scope.IsCancellationRequested;
        }
    }

    public CancellationToken BeginRun(string key, CancellationToken parentToken)
    {
        lock (_sync)
        {
            if (_scopes.TryGetValue(key, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
            _scopes[key] = cts;
            return cts.Token;
        }
    }

    public void EndRun(string key)
    {
        lock (_sync)
        {
            if (_scopes.Remove(key, out var scope))
            {
                scope.Dispose();
            }
        }
    }

    public void CancelRun(string key)
    {
        lock (_sync)
        {
            if (_scopes.TryGetValue(key, out var scope) && !scope.IsCancellationRequested)
            {
                scope.Cancel();
            }
        }
    }

    public void CancelAll()
    {
        lock (_sync)
        {
            foreach (var scope in _scopes.Values)
            {
                if (!scope.IsCancellationRequested)
                {
                    scope.Cancel();
                }
            }
        }
    }

    public void CancelCurrentRun() => CancelAll();
}
