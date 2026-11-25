using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NextnityStudio.SplineRoad
{
    /// <summary>
    /// Cancelable progress bar for operations that touch many terrain tiles.
    /// It is a no-op outside the editor and below <see cref="MinStepsForProgressBar"/> steps,
    /// so a two-tile apply never flashes a progress bar for a fraction of a second.
    /// </summary>
    public sealed class RoadProgressReporter : IDisposable
    {
        private const string DefaultTitle = "Spline Road";
        private const int MinStepsForProgressBar = 8;

        private readonly int _totalSteps;
        private readonly string _title;
        private readonly bool _enabled;
        private bool _disposed;

        public RoadProgressReporter(int totalSteps, string title = DefaultTitle)
        {
            _totalSteps = Mathf.Max(1, totalSteps);
            _title = string.IsNullOrEmpty(title) ? DefaultTitle : title;
            _enabled = _totalSteps >= MinStepsForProgressBar && !Application.isBatchMode;
        }

        /// <summary>True once the user pressed Cancel in the progress bar.</summary>
        public bool Cancelled { get; private set; }

        /// <summary>Reports the work in progress. Returns false when the caller must stop.</summary>
        public bool Report(int completedSteps, string step)
        {
            if (Cancelled) return false;
            if (!_enabled) return true;

#if UNITY_EDITOR
            var progress = Mathf.Clamp01(completedSteps / (float)_totalSteps);
            var current = Mathf.Min(completedSteps + 1, _totalSteps);
            var message = $"{step} ({current}/{_totalSteps})";

            if (EditorUtility.DisplayCancelableProgressBar(_title, message, progress))
            {
                Cancelled = true;
            }
#endif

            return !Cancelled;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

#if UNITY_EDITOR
            if (_enabled)
            {
                EditorUtility.ClearProgressBar();
            }
#endif
        }
    }
}
