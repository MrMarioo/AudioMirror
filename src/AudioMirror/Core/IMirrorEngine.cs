namespace AudioMirror.Core;

/// <summary>Copies audio from the primary device to the target devices.</summary>
internal interface IMirrorEngine
{
    bool IsRunning { get; }

    /// <summary>Incremented on every <see cref="Start"/>; lets callers ignore faults from an old pipeline.</summary>
    int Session { get; }

    /// <exception cref="InvalidOperationException">The engine is already running.</exception>
    void Start(MirrorRouting routing);

    /// <summary>Stops and releases the pipeline. Safe to call when not running.</summary>
    void Stop();

    /// <summary>A device failed while running. May be raised on any thread.</summary>
    event Action<EngineFault>? Faulted;
}

internal sealed record EngineFault(int Session, Exception Error);
