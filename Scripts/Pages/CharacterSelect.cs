using Godot;
using System.Collections.Generic;

/// <summary>
/// Single-player character select. Attach to the root Control of CharacterSelect.tscn.
///
/// SCENE SETUP:
///   - 4 character buttons (KernelCowboy, SirEdward, Steampunk, Vampire)
///   - BackButton, ContinueButton
///   - TitleLabel
///   - StocksLabel + StocksUpButton + StocksDownButton
///   - AiCountLabel + AiCountUpButton + AiCountDownButton
///   - SlotsContainer  — HBoxContainer; PlayerSlot scenes are instantiated here at runtime
///   - PlayerSlotScene — drag PlayerSlot.tscn into this export slot
///   - One Texture2D export per character for portraits (assign in Inspector)
///
/// FLOW:
///   Config phase  — player sets AI count (1-3) and stocks (1-5); slot boxes preview the match layout
///   Picking phase — cycles through each slot; gold highlight shows who is being picked for
/// </summary>
public partial class CharacterSelect : Control
{
    // ── Exports ───────────────────────────────────────────────────────────────

    [Export] private Button _character1Button;
    [Export] private Button _character2Button;
    [Export] private Button _character3Button;
    [Export] private Button _character4Button;

    [Export] private Button _backButton;
    [Export] private Button _continueButton;
    [Export] private Label  _titleLabel;

    [Export] private Label  _stocksLabel;
    [Export] private Button _stocksUpButton;
    [Export] private Button _stocksDownButton;

    [Export] private Label  _aiCountLabel;
    [Export] private Button _aiCountUpButton;
    [Export] private Button _aiCountDownButton;

    /// <summary>HBoxContainer that holds the dynamically created PlayerSlot nodes.</summary>
    [Export] private Control _slotsContainer;

    /// <summary>Drag PlayerSlot.tscn here in the Inspector.</summary>
    [Export] private PackedScene _playerSlotScene;

    // Character portrait textures — assign each in the Inspector.
    [Export] private Texture2D _kernelCowboyPortrait;
    [Export] private Texture2D _sirEdwardPortrait;
    [Export] private Texture2D _steampunkPortrait;
    [Export] private Texture2D _vampirePortrait;

    // ── Visuals ───────────────────────────────────────────────────────────────

    private static readonly Color SelectedTint   = new Color(1.0f, 0.85f, 0.2f);
    private static readonly Color UnselectedTint = new Color(0.55f, 0.55f, 0.55f);

    // ── Private state ─────────────────────────────────────────────────────────

    private enum Phase { Config, Picking }
    private Phase _phase = Phase.Config;

    private int _stocks  = 1;
    private int _aiCount = 1;

    private readonly List<GameManager.PlayerSlot>          _slots          = [];
    private readonly List<PlayerSlot>                      _slotNodes      = [];
    private readonly HashSet<GameManager.CharacterType>    _takenCharacters = [];
    private int _currentSlotIndex;

    private GameManager.CharacterType? _selectedCharacter;
    private Button                     _selectedCharacterButton;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _character1Button.Pressed += () => SelectCharacter(_character1Button, GameManager.CharacterType.KernelCowboy);
        _character2Button.Pressed += () => SelectCharacter(_character2Button, GameManager.CharacterType.SirEdward);
        _character3Button.Pressed += () => SelectCharacter(_character3Button, GameManager.CharacterType.Steampunk);
        _character4Button.Pressed += () => SelectCharacter(_character4Button, GameManager.CharacterType.Vampire);

        _stocksUpButton.Pressed   += () => ChangeStocks(1);
        _stocksDownButton.Pressed += () => ChangeStocks(-1);
        _aiCountUpButton.Pressed  += () => ChangeAiCount(1);
        _aiCountDownButton.Pressed += () => ChangeAiCount(-1);

        _backButton.Pressed     += OnBackPressed;
        _continueButton.Pressed += OnContinuePressed;

        EnterConfigPhase();
    }

    // ── Phases ────────────────────────────────────────────────────────────────

    private void EnterConfigPhase()
    {
        _phase = Phase.Config;

        _titleLabel.Text         = "Set Stocks & Opponents";
        _continueButton.Text     = "Pick Characters";
        _continueButton.Disabled = false;

        UpdateStocksLabel();
        UpdateAiCountLabel();
        UpdateCharacterButtonStates();
        BuildSlotNodes();
    }

    private void EnterPickingPhase()
    {
        _phase            = Phase.Picking;
        _currentSlotIndex = 0;
        _selectedCharacter       = null;
        _selectedCharacterButton = null;
        _slots.Clear();
        _takenCharacters.Clear();

        _continueButton.Disabled    = true;
        _aiCountUpButton.Disabled   = true;
        _aiCountDownButton.Disabled = true;

        HighlightActiveSlot();
        UpdatePickingLabel();
        UpdateCharacterButtonStates();
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void SelectCharacter(Button button, GameManager.CharacterType character)
    {
        if (_phase != Phase.Picking) return;

        _selectedCharacterButton = button;
        _selectedCharacter       = character;
        _continueButton.Disabled = false;

        UpdateCharacterButtonStates();
    }

    private void OnContinuePressed()
    {
        if (_phase == Phase.Config)
        {
            EnterPickingPhase();
            return;
        }

        if (_selectedCharacter == null) return;

        // Show the confirmed character in the active slot box.
        if (_currentSlotIndex < _slotNodes.Count)
            _slotNodes[_currentSlotIndex].ShowCharacter(
                FriendlyName(_selectedCharacter.Value.ToString()),
                PortraitFor(_selectedCharacter.Value.ToString()));

        _takenCharacters.Add(_selectedCharacter.Value);

        bool isAI = _currentSlotIndex >= 1; // slot 0 is always the human
        _slots.Add(new GameManager.PlayerSlot { IsAI = isAI, Character = _selectedCharacter.Value });

        _currentSlotIndex++;
        _selectedCharacter       = null;
        _selectedCharacterButton = null;

        if (_currentSlotIndex >= 1 + _aiCount)
        {
            MusicManager.Instance?.StopMusic();
            GameManager.Instance.SetSlots(_slots.ToArray());
            GameManager.Instance.SetStocks(_stocks);
            GameManager.Instance.StartMatch();
            return;
        }

        _continueButton.Disabled = true;
        HighlightActiveSlot();
        UpdatePickingLabel();
        UpdateCharacterButtonStates();
    }

    private void OnBackPressed()
    {
        if (_phase == Phase.Config)
        {
            GameManager.Instance.GoToStageSelect();
            return;
        }

        // Picking phase — undo the last confirmed slot if there is one.
        if (_currentSlotIndex > 0)
        {
            _currentSlotIndex--;
            _takenCharacters.Remove(_slots[^1].Character);
            _slots.RemoveAt(_slots.Count - 1);
            _slotNodes[_currentSlotIndex].ShowWaiting();
        }
        else
        {
            // Back at the very first slot with nothing confirmed — return to config.
            _aiCountUpButton.Disabled   = false;
            _aiCountDownButton.Disabled = false;
            EnterConfigPhase();
            return;
        }

        _selectedCharacter       = null;
        _selectedCharacterButton = null;
        _continueButton.Disabled = true;

        HighlightActiveSlot();
        UpdatePickingLabel();
        UpdateCharacterButtonStates();
    }

    private void ChangeStocks(int delta)
    {
        _stocks = Mathf.Clamp(_stocks + delta, 1, 5);
        UpdateStocksLabel();
    }

    private void ChangeAiCount(int delta)
    {
        _aiCount = Mathf.Clamp(_aiCount + delta, 1, 3);
        UpdateAiCountLabel();
        BuildSlotNodes(); // rebuild preview whenever count changes
    }

    // ── Slot nodes ────────────────────────────────────────────────────────────

    private void BuildSlotNodes()
    {
        foreach (Node child in _slotsContainer.GetChildren())
            child.QueueFree();
        _slotNodes.Clear();

        int total = 1 + _aiCount;

        for (int i = 0; i < total; i++)
        {
            PlayerSlot slot  = _playerSlotScene.Instantiate<PlayerSlot>();
            string     label = i == 0 ? "Player 1" : $"AI {i}";

            _slotsContainer.AddChild(slot);
            slot.Init(label);
            slot.ShowWaiting();
            _slotNodes.Add(slot);
        }

        // Defer positioning so the container has had a chance to calculate its size.
        CallDeferred(nameof(PositionSlots));
    }

    private void PositionSlots()
    {
        if (_slotNodes.Count == 0) return;

        float containerW = _slotsContainer.Size.X;
        float containerH = _slotsContainer.Size.Y;

        // Always divide into 4 equal columns so each slot index maps to a fixed x position.
        // Player 1 is always column 0, AI 1 is always column 1, etc.
        float sectionW = containerW / 4f;

        for (int i = 0; i < _slotNodes.Count; i++)
        {
            PlayerSlot slot   = _slotNodes[i];
            Vector2    size   = slot.Size;

            float x = i * sectionW + (sectionW - size.X) / 9f;
            float y = (containerH - size.Y) / 9f;

            slot.Position = new Vector2(x, Mathf.Max(y, 0f));
        }
    }

    private void HighlightActiveSlot()
    {
        for (int i = 0; i < _slotNodes.Count; i++)
            _slotNodes[i].SetActive(i == _currentSlotIndex);
    }

    // ── Label updates ─────────────────────────────────────────────────────────

    private void UpdatePickingLabel()
    {
        bool   isAI     = _currentSlotIndex >= 1;
        string slotName = isAI ? $"AI {_currentSlotIndex}" : "Player 1";

        _titleLabel.Text     = $"Now Picking: {slotName}";
        _continueButton.Text = _currentSlotIndex == _aiCount ? "Start Match" : "Confirm";
    }

    private void UpdateStocksLabel()  => _stocksLabel.Text  = $"Stocks: {_stocks}";
    private void UpdateAiCountLabel() => _aiCountLabel.Text = $"AI Opponents: {_aiCount}";

    // ── Visuals ───────────────────────────────────────────────────────────────

    private void UpdateCharacterButtonStates()
    {
        (Button btn, GameManager.CharacterType ch)[] map =
        [
            (_character1Button, GameManager.CharacterType.KernelCowboy),
            (_character2Button, GameManager.CharacterType.SirEdward),
            (_character3Button, GameManager.CharacterType.Steampunk),
            (_character4Button, GameManager.CharacterType.Vampire),
        ];

        foreach (var (btn, ch) in map)
        {
            if (btn == null) continue;

            if (_phase == Phase.Config)
            {
                btn.Disabled = true;
                btn.Modulate = UnselectedTint;
                continue;
            }

            bool taken    = _takenCharacters.Contains(ch);
            bool selected = btn == _selectedCharacterButton;

            btn.Disabled = taken;
            btn.Modulate = selected ? SelectedTint : (taken ? UnselectedTint : Colors.White);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FriendlyName(string enumName) => enumName switch
    {
        "KernelCowboy" => "Kernel Cowboy",
        "SirEdward"    => "Sir Edward",
        "Steampunk"    => "Steampunk",
        "Vampire"      => "Vampire",
        _              => enumName
    };

    private Texture2D PortraitFor(string enumName) => enumName switch
    {
        "KernelCowboy" => _kernelCowboyPortrait,
        "SirEdward"    => _sirEdwardPortrait,
        "Steampunk"    => _steampunkPortrait,
        "Vampire"      => _vampirePortrait,
        _              => null
    };
}
