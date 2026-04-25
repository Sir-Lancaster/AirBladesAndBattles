using Godot;

public partial class GameEnd : CanvasLayer
{
    [Export] private Label  _winnerLabel;
    [Export] private Button _returnButton;

    public override void _Ready()
    {
        int winnerIndex = GameManager.Instance.WinnerSlotIndex;
        string charKey  = "Unknown";

        var slots = GameManager.Instance.CurrentMatch.Slots;
        if (winnerIndex >= 0 && winnerIndex < slots.Length)
            charKey = slots[winnerIndex].Character.ToString();

        _winnerLabel.Text = $"{FormatName(charKey)} Wins!";
        _returnButton.Pressed += GameManager.Instance.GoToMainMenu;
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
