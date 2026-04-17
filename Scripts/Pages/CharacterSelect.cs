using Godot;
using System.Collections.Generic;

/// <summary>
/// Single-player character select. Attach to the root Control of CharacterSelect.tscn.
///
/// SCENE SETUP — add a Control (AiCountContainer) with Label + up/down Buttons, mirroring
/// the stocks widget. Drag it into the _aiCountContainer export slot.
///
/// FLOW:
///   Config phase  — player sets AI count and stocks, hits "Begin Selection"
///   Picking phase — cycles through each slot: "Now Picking: Player 1", "Now Picking: AI 1", etc.
/// </summary>
public partial class CharacterSelect : Control
{
    // ── Character name display strings (order must match CharacterType enum) ──
    private static readonly string[] CharacterNames =
    {
        "Kernel Cowboy",
        "Sir Edward",
        "Steampunk",
        "Vampire"
    };

    // ── Exports ───────────────────────────────────────────────────────────────

    [Export] private Button _character1Button;
    [Export] private Button _character2Button;
    [Export] private Button _character3Button;
    [Export] private Button _character4Button;

    [Export] private Button _backButton;
    [Export] private Button _continueButton;

    /// <summary>The title label — shows "Set Up Match" in config phase and "Now Picking: X" in picking phase.</summary>
    [Export] private Label _titleLabel;

    [Export] private Label  _stocksLabel;
    [Export] private Button _stocksUpButton;
    [Export] private Button _stocksDownButton;

    /// <summary>
    /// Wrap your AI count label + buttons in a parent Control and assign it here.
    /// It is hidden automatically in Multiplayer mode.
    /// </summary>
    [Export] private Control _aiCountContainer;
    [Export] private Label   _aiCountLabel;
    [Export] private Button  _aiCountUpButton;
    [Export] private Button  _aiCountDownButton;

    // ── Visuals ───────────────────────────────────────────────────────────────

    private static readonly Color SelectedTint   = new Color(1.0f, 0.85f, 0.2f);  // gold
    private static readonly Color UnselectedTint = new Color(0.55f, 0.55f, 0.55f); // dimmed

    // ── Private state ─────────────────────────────────────────────────────────

    private enum Phase { Config, Picking }
    private Phase _phase = Phase.Config;

    private int _stocks   = 1;
    private int _aiCount  = 1; // only used in single player
    private int _humanCount;   // set from GameManager on _Ready
    private int _totalSlots;   // humanCount + aiCount, calculated when picking starts

    private readonly List<GameManager.PlayerSlot> _slots = new();
    private int _currentSlotIndex;

    private GameManager.CharacterType? _selectedCharacter;
    private Button _selectedCharacterButton;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _character1Button.Pressed += () => SelectCharacter(_character1Button, GameManager.CharacterType.KernelCowboy);
        _character2Button.Pressed += () => SelectCharacter(_character2Button, GameManager.CharacterType.SirEdward);
        _character3Button.Pressed += () => SelectCharacter(_character3Button, GameManager.CharacterType.Steampunk);
        _character4Button.Pressed += () => SelectCharacter(_character4Button, GameManager.CharacterType.Vampire);

        _stocksUpButton.Pressed   += () => ChangeStocks(1);
        _stocksDownButton.Pressed += () => ChangeStocks(-1);

        _aiCountUpButton.Pressed   += () => ChangeAiCount(1);
        _aiCountDownButton.Pressed += () => ChangeAiCount(-1);

        _backButton.Pressed     += OnBackPressed;
        _continueButton.Pressed += OnContinuePressed;

        _humanCount = 1; // single player always has exactly one human

        EnterConfigPhase();
    }

    // ── Phases ────────────────────────────────────────────────────────────────

    private void EnterConfigPhase()
    {
        _phase = Phase.Config;

        _titleLabel.Text         = "Set Up Match";
        _continueButton.Text     = "Begin Selection";
        _continueButton.Disabled = false;

        SetCharacterButtonsDisabled(true);
        UpdateCharacterButtonVisuals();
        UpdateStocksLabel();
        UpdateAiCountLabel();
    }

    private void EnterPickingPhase()
    {
        _phase = Phase.Picking;

        _totalSlots        = _humanCount + _aiCount;
        _currentSlotIndex  = 0;
        _selectedCharacter = null;
        _selectedCharacterButton = null;
        _slots.Clear();

        SetCharacterButtonsDisabled(false);
        _continueButton.Disabled = true; // must pick a character before confirming

        UpdatePickingLabel();
        UpdateCharacterButtonVisuals();
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void SelectCharacter(Button button, GameManager.CharacterType character)
    {
        if (_phase != Phase.Picking) return;

        _selectedCharacterButton = button;
        _selectedCharacter       = character;
        _continueButton.Disabled = false;

        UpdateCharacterButtonVisuals();
    }

    private void OnContinuePressed()
    {
        if (_phase == Phase.Config)
        {
            EnterPickingPhase();
            return;
        }

        // Picking phase — confirm this slot and advance.
        if (_selectedCharacter == null) return;

        bool isAI = _currentSlotIndex >= _humanCount;
        _slots.Add(new GameManager.PlayerSlot { IsAI = isAI, Character = _selectedCharacter.Value });

        _currentSlotIndex++;
        _selectedCharacter       = null;
        _selectedCharacterButton = null;

        if (_currentSlotIndex >= _totalSlots)
        {
            // All slots filled — hand off to GameManager and start.
            GameManager.Instance.SetSlots(_slots.ToArray());
            GameManager.Instance.SetStocks(_stocks);
            GameManager.Instance.StartMatch();
            return;
        }

        _continueButton.Disabled = true;
        UpdatePickingLabel();
        UpdateCharacterButtonVisuals();
    }

    private void OnBackPressed() => GameManager.Instance.GoToStageSelect();

    private void ChangeStocks(int delta)
    {
        _stocks = Mathf.Clamp(_stocks + delta, 1, 5);
        UpdateStocksLabel();
    }

    private void ChangeAiCount(int delta)
    {
        _aiCount = Mathf.Clamp(_aiCount + delta, 1, 3);
        UpdateAiCountLabel();
    }

    // ── Label updates ─────────────────────────────────────────────────────────

    private void UpdatePickingLabel()
    {
        bool isAI        = _currentSlotIndex >= _humanCount;
        int  displayNum  = isAI ? _currentSlotIndex - _humanCount + 1 : _currentSlotIndex + 1;
        string slotName  = isAI ? $"AI {displayNum}" : $"Player {displayNum}";

        _titleLabel.Text     = $"Now Picking: {slotName}";
        _continueButton.Text = _currentSlotIndex == _totalSlots - 1 ? "Start Match" : "Confirm";
    }

    private void UpdateStocksLabel()   => _stocksLabel.Text  = $"Stocks: {_stocks}";
    private void UpdateAiCountLabel()  => _aiCountLabel.Text = $"AI Opponents: {_aiCount}";

    // ── Visuals ───────────────────────────────────────────────────────────────

    private void UpdateCharacterButtonVisuals()
    {
        Button[] buttons = { _character1Button, _character2Button, _character3Button, _character4Button };
        foreach (Button btn in buttons)
        {
            if (btn == null) continue;
            btn.Modulate = btn == _selectedCharacterButton ? SelectedTint : UnselectedTint;
        }
    }

    private void SetCharacterButtonsDisabled(bool disabled)
    {
        _character1Button.Disabled = disabled;
        _character2Button.Disabled = disabled;
        _character3Button.Disabled = disabled;
        _character4Button.Disabled = disabled;
    }
}
