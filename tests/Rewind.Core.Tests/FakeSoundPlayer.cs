using Rewind.Core.Sounds;

namespace Rewind.Core.Tests;

/// <summary>Records each sound it's asked to play instead of playing it (TEST-5).</summary>
internal sealed class FakeSoundPlayer : ISoundPlayer
{
    private readonly List<Sound> _played = [];

    public IReadOnlyList<Sound> Played => _played;

    public void Play(Sound sound) => _played.Add(sound);
}
