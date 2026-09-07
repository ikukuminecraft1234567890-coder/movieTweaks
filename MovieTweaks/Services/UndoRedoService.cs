using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using MovieTweaks.Models;

namespace MovieTweaks.Services
{
    public class UndoRedoService
    {
        private readonly Stack<string> _undoStack = new();
        private readonly Stack<string> _redoStack = new();
        private const int MaxHistory = 50;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public event EventHandler? StateChanged;

        public void RecordState(Project project)
        {
            try
            {
                var json = JsonSerializer.Serialize(project);
                _undoStack.Push(json);
                if (_undoStack.Count > MaxHistory)
                {
                    // Remove oldest by rebuilding
                    var list = new List<string>(_undoStack);
                    list.RemoveAt(list.Count - 1);
                    _undoStack.Clear();
                    for (int i = list.Count - 1; i >= 0; i--) _undoStack.Push(list[i]);
                }
                _redoStack.Clear();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }

        public bool Undo(Project currentProject)
        {
            if (!CanUndo) return false;

            try
            {
                var currentJson = JsonSerializer.Serialize(currentProject);
                _redoStack.Push(currentJson);

                var previousJson = _undoStack.Pop();
                RestoreProject(currentProject, previousJson);

                StateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Redo(Project currentProject)
        {
            if (!CanRedo) return false;

            try
            {
                var currentJson = JsonSerializer.Serialize(currentProject);
                _undoStack.Push(currentJson);

                var nextJson = _redoStack.Pop();
                RestoreProject(currentProject, nextJson);

                StateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void RestoreProject(Project target, string json)
        {
            var restored = JsonSerializer.Deserialize<Project>(json);
            if (restored == null) return;

            // Restore CutRanges
            target.CutRanges.Clear();
            foreach (var r in restored.CutRanges) target.CutRanges.Add(r);

            // Restore Overlays
            target.Overlays.Clear();
            foreach (var o in restored.Overlays) target.Overlays.Add(o);

            if (restored.SourceVideo != null && target.SourceVideo != null)
            {
                target.SourceVideo.Volume = restored.SourceVideo.Volume;
                target.SourceVideo.PlaybackSpeed = restored.SourceVideo.PlaybackSpeed;
                target.SourceVideo.Opacity = restored.SourceVideo.Opacity;
            }
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
