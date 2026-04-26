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
    /// When true the return button navigates all peers back to stage select (multiplayer flow).
    /// Set to true by BattleManager for multiplayer matches.
    /// </summary>
    public bool ReturnToStageSelect { get; set; } = false;

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

        if (ReturnToStageSelect)
        {
            if (NetworkManager.Instance.IsHost)
            {
                _returnButton.Text = "Next Round";
                _returnButton.Pressed += () => NetworkManager.Instance.StartStageSelect();
            }
            else
            {
                _returnButton.Text = "Waiting for host...";
                _returnButton.Disabled = true;
            }
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
