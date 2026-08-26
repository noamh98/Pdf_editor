namespace PdfEditor.Core.History;

/// <summary>A reversible edit. Implementations must be pure with respect to the UI thread.</summary>
public interface IUndoableAction
{
    /// <summary>Short Hebrew description shown in the UI, e.g. "הוספת תיבת טקסט".</summary>
    string Description { get; }

    void Apply();
    void Revert();
}

/// <summary>Convenience action built from two delegates.</summary>
public sealed class DelegateAction(string description, Action apply, Action revert) : IUndoableAction
{
    public string Description { get; } = description;
    public void Apply() => apply();
    public void Revert() => revert();
}

/// <summary>Groups several actions so undo treats them as one step.</summary>
public sealed class CompositeAction(string description, IReadOnlyList<IUndoableAction> actions) : IUndoableAction
{
    public string Description { get; } = description;
    public IReadOnlyList<IUndoableAction> Actions { get; } = actions;

    public void Apply()
    {
        for (int i = 0; i < Actions.Count; i++) Actions[i].Apply();
    }

    public void Revert()
    {
        for (int i = Actions.Count - 1; i >= 0; i--) Actions[i].Revert();
    }
}

/// <summary>
/// Bounded undo/redo history. Pushing a new action after an undo discards the redo branch,
/// which is what every editor does and what users expect.
/// </summary>
public sealed class UndoRedoStack
{
    private readonly List<IUndoableAction> _undo = [];
    private readonly List<IUndoableAction> _redo = [];
    private readonly int _capacity;
    private int _savedDepth;
    private List<IUndoableAction>? _transaction;
    private string? _transactionDescription;

    public UndoRedoStack(int capacity = 200)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public string? NextUndoDescription => CanUndo ? _undo[^1].Description : null;
    public string? NextRedoDescription => CanRedo ? _redo[^1].Description : null;

    /// <summary>True when the current state differs from the last state marked as saved.</summary>
    public bool IsDirty => _undo.Count != _savedDepth;

    /// <summary>
    /// Runs an action and records it. The action is applied immediately; if it throws, nothing is
    /// recorded so the history stays consistent with the model.
    /// </summary>
    public void Execute(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action.Apply();
        Push(action);
    }

    /// <summary>Records an action that the caller has already applied.</summary>
    public void Push(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_transaction is not null) { _transaction.Add(action); return; }

        _redo.Clear();
        _undo.Add(action);
        if (_undo.Count > _capacity)
        {
            _undo.RemoveAt(0);
            if (_savedDepth > 0) _savedDepth--;
            else _savedDepth = -1;   // the saved state fell out of the history
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (_transaction is not null) throw new InvalidOperationException("Cannot undo inside a transaction.");
        if (!CanUndo) return false;
        var action = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        action.Revert();
        _redo.Add(action);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (_transaction is not null) throw new InvalidOperationException("Cannot redo inside a transaction.");
        if (!CanRedo) return false;
        var action = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        action.Apply();
        _undo.Add(action);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Collects every action pushed until the returned scope is disposed into a single undo step.
    /// Used for drag operations and multi-selection edits.
    /// </summary>
    public IDisposable BeginTransaction(string description)
    {
        if (_transaction is not null) throw new InvalidOperationException("A transaction is already open.");
        _transaction = [];
        _transactionDescription = description;
        return new TransactionScope(this);
    }

    private void EndTransaction()
    {
        var actions = _transaction;
        var description = _transactionDescription ?? "עריכה";
        _transaction = null;
        _transactionDescription = null;
        if (actions is null || actions.Count == 0) return;
        Push(actions.Count == 1 ? actions[0] : new CompositeAction(description, actions));
    }

    public void MarkSaved()
    {
        _savedDepth = _undo.Count;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _savedDepth = 0;
        _transaction = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class TransactionScope(UndoRedoStack owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.EndTransaction();
        }
    }
}
