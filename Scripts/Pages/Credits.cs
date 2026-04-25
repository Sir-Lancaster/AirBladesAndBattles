using Godot;

/// <summary>
/// Script for the Credits scene.
/// Attach to the root node of Credits.tscn.
/// Drag the back button into the _backButton export slot in the Inspector.
/// </summary>
public partial class Credits : Control
{
    [Export] private Button _backButton;

    public override void _Ready()
    {
        _backButton.Pressed += GameManager.Instance.GoToMainMenu;

        _backButton.GrabFocus();
    }
}
