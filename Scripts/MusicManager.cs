using Godot;

/// <summary>
/// Autoload singleton that owns the menu music AudioStreamPlayer so it
/// survives scene changes throughout the entire menu flow.
///
/// SETUP (do this once in the Godot editor):
///   1. Create Scenes/Utility/MusicManager.tscn
///        Root node: Node  (attach this script)
///        Child node: AudioStreamPlayer  (name it exactly "MusicPlayer")
///   2. On the AudioStreamPlayer: assign your music file to Stream,
///      tick Autoplay OFF (this script calls Play), enable Loop on the stream.
///   3. Project → Project Settings → Autoload:
///        Path: res://Scenes/Utility/MusicManager.tscn  (with the * prefix)
///        Name: MusicManager
///   Music starts the moment the game launches and stops when a match begins.
/// </summary>
public partial class MusicManager : Node
{
    public static MusicManager Instance { get; private set; }

    [Export] private AudioStreamPlayer _player;

    public override void _Ready()
    {
        if (Instance != null) { QueueFree(); return; }
        Instance = this;

        if (_player == null)
        {
            GD.PushWarning("[MusicManager] No AudioStreamPlayer wired into _player export.");
            return;
        }

        _player.Play();
    }

    /// <summary>Stop the menu music. Call this just before loading a battle scene.</summary>
    public void StopMusic()  => _player?.Stop();

    /// <summary>Resume the menu music (e.g. after returning to main menu).</summary>
    public void PlayMusic()
    {
        if (_player != null && !_player.Playing)
            _player.Play();
    }
}