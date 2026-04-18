using Godot;

/// <summary>
/// Attach to the root Control of PlayerSlot.tscn.
/// Wire up the three child nodes in the Inspector.
/// Call Init() once after instantiation, then ShowWaiting() / ShowCharacter() as state changes.
/// </summary>
public partial class PlayerSlot : Control
{
    [Export] private TextureRect _portraitRect;
    [Export] private Label       _playerLabel;
    [Export] private Label       _characterNameLabel;

    private static readonly Color ReadyColor   = new Color(0.2f, 1.0f, 0.4f);
    private static readonly Color WaitingColor = new Color(1.0f, 1.0f, 1.0f);

    /// <summary>Set the player number and whether this is the local player. Call once after instantiation.</summary>
    public void Init(int playerNumber, bool isLocal)
    {
        _playerLabel.Text = isLocal ? $"Player {playerNumber} (You)" : $"Player {playerNumber}";
        GD.Print($"[PlayerSlot] Added slot for Player {playerNumber}{(isLocal ? " (local)" : "")}");
    }

    /// <summary>Show the waiting state — no portrait, "Waiting..." in white.</summary>
    public void ShowWaiting()
    {
        _portraitRect.Texture       = null;
        _characterNameLabel.Text    = "Waiting...";
        _characterNameLabel.Modulate = WaitingColor;
    }

    /// <summary>Show a confirmed pick — fills portrait and name, tints label green.</summary>
    public void ShowCharacter(string displayName, Texture2D portrait)
    {
        _portraitRect.Texture        = portrait;
        _characterNameLabel.Text     = displayName;
        _characterNameLabel.Modulate = ReadyColor;
    }
}