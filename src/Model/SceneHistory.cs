#nullable enable
// Edit history — the editor's `commit()` pattern: every mutation pushes
// the pre-edit scene onto the undo stack as a deep-copy snapshot and
// clears redo. Bounded so long sessions don't grow memory without limit.
using System.Collections.Generic;

namespace DmMap;

public class SceneHistory
{
    public const int DefaultCap = 50;

    readonly List<Scene> _undo = [];
    readonly List<Scene> _redo = [];
    readonly int _cap;

    /// <summary>The live scene — mutate a clone, then Commit it back.</summary>
    public Scene Current { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoDepth => _undo.Count;
    public int RedoDepth => _redo.Count;

    public SceneHistory(Scene initial, int cap = DefaultCap)
    {
        Current = initial;
        _cap = cap;
    }

    /// <summary>Push the current scene onto the undo stack (deep copy) and
    /// make `next` the live scene. Clears the redo stack — a new edit
    /// branch invalidates redos, same as the web editor.</summary>
    public void Commit(Scene next)
    {
        _undo.Add(Current.DeepClone());
        while (_undo.Count > _cap)
            _undo.RemoveAt(0);
        _redo.Clear();
        Current = next;
    }

    /// <summary>Step back one edit. Returns the restored scene, or null
    /// when the stack is empty.</summary>
    public Scene? Undo()
    {
        if (_undo.Count == 0)
            return null;
        _redo.Add(Current.DeepClone());
        Current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return Current;
    }

    /// <summary>Re-apply the last undone edit, or null when nothing was
    /// undone.</summary>
    public Scene? Redo()
    {
        if (_redo.Count == 0)
            return null;
        _undo.Add(Current.DeepClone());
        Current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return Current;
    }
}
