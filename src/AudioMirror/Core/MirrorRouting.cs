namespace AudioMirror.Core;

/// <summary>Where audio comes from and where copies of it go.</summary>
/// <remarks>
/// Two routings are equal when they use the same device IDs, regardless of display names,
/// so a renamed device doesn't cause a restart.
/// </remarks>
internal sealed record MirrorRouting
{
    public AudioDevice Primary { get; }

    /// <summary>Sorted by ID, never contains the primary device.</summary>
    public IReadOnlyList<AudioDevice> Targets { get; }

    private MirrorRouting(AudioDevice primary, IReadOnlyList<AudioDevice> targets)
    {
        Primary = primary;
        Targets = targets;
    }

    /// <summary>Picks the selected devices that are currently active, excluding the primary one.</summary>
    public static MirrorRouting Resolve(
        AudioDevice primary, IEnumerable<AudioDevice> activeOutputs, IReadOnlySet<string> selectedIds)
    {
        var targets = activeOutputs
            .Where(d => d.Id != primary.Id && selectedIds.Contains(d.Id))
            .DistinctBy(d => d.Id)
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
        return new MirrorRouting(primary, targets);
    }

    public bool Equals(MirrorRouting? other)
        => other is not null
           && Primary.Id == other.Primary.Id
           && Targets.Select(t => t.Id).SequenceEqual(other.Targets.Select(t => t.Id));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Primary.Id);
        foreach (var t in Targets) hash.Add(t.Id);
        return hash.ToHashCode();
    }
}
