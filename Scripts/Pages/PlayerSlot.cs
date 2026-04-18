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

    /// <summary>Set the player label directly. Used by single-player for "Player 1", "AI 1", etc.</summary>
    public void Init(string label)
    {
        _playerLabel.Text = label;
        GD.Print($"[PlayerSlot] Added slot: {label}");
    }

    /// <summary>Multiplayer overload — builds label from player number and local flag.</summary>
    public void Init(int playerNumber, bool isLocal)
    {
        Init(isLocal ? $"Player {playerNumber} (You)" : $"Player {playerNumber}");
    }

    /// <summary>Highlights this slot gold when it is the active picker; resets to white otherwise.</summary>
    public void SetActive(bool active)
    {
        Modulate = active ? new Color(1.0f, 0.85f, 0.2f) : Colors.White;
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