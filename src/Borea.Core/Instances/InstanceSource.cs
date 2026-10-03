using Borea.Core.Mods;

namespace Borea.Core.Instances;

/// <summary>
/// Describes the origin of an <see cref="Instance"/>: either a specific modpack at a
/// specific version, or a user-curated set of mods with no modpack backing.
/// </summary>
public abstract record InstanceSource
{
    private InstanceSource()
    {
    }

    /// <summary>
    /// An instance materialized from a specific modpack at a specific version.
    /// </summary>
    public sealed record FromModPack(string ModPackId, ModVersion Version) : InstanceSource
    {
        private static readonly IReadOnlySet<string> None = new HashSet<string>(ModIds.Comparer);

        /// <summary>
        /// The pack mods the player detached. A pack update leaves them alone, also after the player removed them.
        /// </summary>
        public IReadOnlySet<string> Detached { get; private init; } = None;

        public FromModPack WithDetached(IEnumerable<string> modIds)
        {
            ArgumentNullException.ThrowIfNull(modIds);
            return this with { Detached = modIds.ToHashSet(ModIds.Comparer) };
        }

        public bool Equals(FromModPack? other)
            => other is not null
                && string.Equals(ModPackId, other.ModPackId, StringComparison.Ordinal)
                && Version == other.Version
                && Detached.SetEquals(other.Detached);

        public override int GetHashCode() => HashCode.Combine(ModPackId, Version, Detached.Count);
    }

    /// <summary>
    /// A user-curated instance with no modpack origin.
    /// </summary>
    public sealed record Custom : InstanceSource
    {
        public static readonly Custom Value = new();

        private Custom()
        {
        }
    }
}
