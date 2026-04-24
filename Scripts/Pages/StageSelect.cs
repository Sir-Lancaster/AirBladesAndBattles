using Godot;

/// <summary>
/// Script for the Stage Select scene.
/// Attach to the root Control node of StageSelect.tscn.
/// Drag each node into its matching export slot in the Inspector.
///
/// Flow:
///   Player clicks a stage button  → stage is selected, button highlights
///   Player clicks Continue        → saves choice and navigates forward
///   Player clicks Back            → returns to Main Menu
///
/// Single player  → stores in GameManager, goes to CharacterSelect
/// Multiplayer    → stores in NetworkManager, goes to Multiplayer CharacterSelect
///
/// To add a new stage: add a constant with the scene path, export its button,
/// and wire it in _Ready() the same way as the others.
/// </summary>
public partial class StageSelect : Control
{
    // ── Stage scene path constants ────────────────────────────────────────────
    // Use the full res:// path — both GameManager and NetworkManager need it.

    private const string Stage1Path = "res://Scenes/Stages/Clocktower/Clocktower.tscn";
    private const string Stage2Path = "res://Scenes/Stages/Testing/Testing.tscn";
    private const string Stage3Path = "res://Scenes/Stages/Castle/Castle.tscn";

    private const string Stage4Path = "res://Scenes/Stages/Manor/Manor.tscn";

    // ── Exports ───────────────────────────────────────────────────────────────

    [Export] private Button _stage1Button;
    [Export] private Button _stage2Button; // reserved for future stages
    [Export] private Button _stage3Button; // reserved for future stages
    [Export] private Button _stage4Button; // reserved for future stages

    [Export] private Button _backButton;
    [Export] private Button _continueButton;

    // ── Private state ─────────────────────────────────────────────────────────

    private Button _selectedButton;
    private string _selectedStage;

    // Color applied to the currently selected stage button.
    private static readonly Color SelectedTint   = new Color(1.0f, 0.85f, 0.2f); // golden yellow
    private static readonly Color UnselectedTint = new Color(0.55f, 0.55f, 0.55f); // dimmed

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _stage1Button.Pressed += () => SelectStage(_stage1Button, Stage1Path);
        _stage2Button.Pressed += () => SelectStage(_stage2Button, Stage2Path);
        _stage3Button.Pressed += () => SelectStage(_stage3Button, Stage3Path);
        _stage4Button.Pressed += () => SelectStage(_stage4Button, Stage4Path);

        _backButton.Pressed     += OnBackPressed;
        _continueButton.Pressed += OnContinuePressed;

        // Nothing selected yet — disable Continue until the player makes a choice.
        _continueButton.Disabled = true;

        // In multiplayer only the host picks the stage; clients watch and wait.
        bool clientOnly = GameManager.Instance.CurrentMode == GameManager.GameMode.Multiplayer
                          && !NetworkManager.Instance.IsHost;
        if (clientOnly)
        {
            _stage1Button.Disabled   = true;
            _stage2Button.Disabled   = true;
            _stage3Button.Disabled   = true;
            _stage4Button.Disabled   = true;
            _continueButton.Disabled = true;
            _backButton.Disabled     = true;
        }

        // Dim all stage buttons to start so the golden highlight is clearly meaningful.
        UpdateButtonVisuals();
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void SelectStage(Button button, string stageName)
    {
        if (stageName == null) return;

        _selectedButton = button;
        _selectedStage  = stageName;

        _continueButton.Disabled = false;
        UpdateButtonVisuals();
    }

    private void OnContinuePressed()
    {
        if (_selectedStage == null) return;

        GameManager.Instance.SetStage(_selectedStage);
        GameManager.Instance.GoToCharacterSelect();
    }

    private void OnBackPressed() => GameManager.Instance.GoToMainMenu();

    // ── Visuals ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Highlights the selected stage button in gold and dims all others.
    /// </summary>
    private void UpdateButtonVisuals()
    {
        Button[] stageButtons = { _stage1Button, _stage2Button, _stage3Button, _stage4Button };

        foreach (Button btn in stageButtons)
        {
            if (btn == null) continue;
            btn.Modulate = btn == _selectedButton ? SelectedTint : UnselectedTint;
        }
    }
}
