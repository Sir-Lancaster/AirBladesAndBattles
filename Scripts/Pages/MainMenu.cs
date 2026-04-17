using Godot;

/// <summary>
/// Script for the Main Menu scene.
/// Attach this to the root Control node of your MainMenu.tscn.
/// Then drag each button into the matching export slot in the Inspector.
/// </summary>
public partial class MainMenu : Control
{
    [Export] private Button _singlePlayerButton;
    [Export] private Button _multiplayerButton;
    [Export] private Button _quitButton;

    public override void _Ready()
    {
        _singlePlayerButton.Pressed += () =>
        {
            GameManager.Instance.SetMode(GameManager.GameMode.SinglePlayer);
            GameManager.Instance.GoToStageSelect();
        };
        _multiplayerButton.Pressed += () =>
        {
            GameManager.Instance.SetMode(GameManager.GameMode.Multiplayer);
            GameManager.Instance.GoToStageSelect();
        };
        _quitButton.Pressed += GameManager.Instance.QuitGame;
    }
}
