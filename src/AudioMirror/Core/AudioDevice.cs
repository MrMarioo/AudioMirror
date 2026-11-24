namespace AudioMirror.Core;

/// <summary>A snapshot of an output endpoint. Plain data, so nothing above the audio layer holds COM objects.</summary>
internal sealed record AudioDevice(string Id, string Name);
