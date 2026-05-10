using System;

/// <summary>
/// Disposable scope guard for arena battle simulations.
/// Using a depth counter instead of a bool means re-entrant calls and
/// exception-safe exits both work correctly.
/// </summary>
public static class ArenaBattleSimulationScope // FIXED: IDisposable scope prevents stuck IsActive on exception
{
    private static int _depth = 0;
    public static bool IsActive => _depth > 0;

    /// <summary>
    /// Enter the scope. Dispose the returned handle to exit.
    /// Usage: using (ArenaBattleSimulationScope.Enter()) { ... }
    /// </summary>
    public static IDisposable Enter()
    {
        _depth++;
        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_depth > 0) _depth--;
        }
    }
}
