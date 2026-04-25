using Godot;

public partial class GameEnd : CanvasLayer
{
    [Export] private Label  _winnerLabel;
    [Export] private Button _returnButton;

    /// <summary>
    /// Set before AddChild in multiplayer to bypass the GameManager slot lookup.
    /// Leave empty for single-player — the normal WinnerSlotIndex path is used.
    /// </summary>
    public string OverrideWinnerName { get; set; } = string.Empty;

    /// <summary>
    /// When true the return button closes the multiplayer peer before going to main menu.
    /// Set to true by BattleManager for multiplayer matches.
    /// </summary>
    public bool DisconnectOnReturn { get; set; } = false;

    public override void _Ready()
    {
        string charKey;
        if (!string.IsNullOrEmpty(OverrideWinnerName))
        {
            charKey = OverrideWinnerName;
        }
        else
        {
            int winnerIndex = GameManager.Instance.WinnerSlotIndex;
            var slots       = GameManager.Instance.CurrentMatch.Slots;
            charKey = (winnerIndex >= 0 && winnerIndex < slots.Length)
                ? slots[winnerIndex].Character.ToString()
                : "Unknown";
        }

        _winnerLabel.Text = $"{FormatName(charKey)} Wins!";

        if (DisconnectOnReturn)
        {
            _returnButton.Pressed += () =>
            {
                Multiplayer.MultiplayerPeer?.Close();
                Multiplayer.MultiplayerPeer = null;
                GameManager.Instance.GoToMainMenu();
            };
        }
        else
        {
            _returnButton.Pressed += GameManager.Instance.GoToMainMenu;
        }

        _returnButton.GrabFocus();
    }

    private static string FormatName(string key) => key switch
    {
        "KernelCowboy" => "Kernel Cowboy",
        "SirEdward"    => "Sir Edward",
        "Steampunk"    => "Steampunk",
        "Vampire"      => "Vampire",
        _              => key,
    };
}
