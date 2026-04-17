using Godot;

/// <summary>
/// GameManager is an Autoload singleton — it persists across all scene changes and acts
/// as the central hub for match configuration and scene navigation.
///
/// HOW TO REGISTER AS AUTOLOAD (do this once in the Godot editor):
///   1. Project → Project Settings → Autoload tab
///   2. Click the folder icon and navigate to res://Scripts/GameManager.cs
///   3. Set the Name field to: GameManager
///   4. Click Add
/// After that, GameManager.Instance is accessible from any script in the project.
/// </summary>
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    // -------------------------------------------------------------------------
    // Data types
    // -------------------------------------------------------------------------

    public enum CharacterType { KernelCowboy, SirEdward, Steampunk, Vampire }
    public enum GameMode { SinglePlayer, Multiplayer }

    public class PlayerSlot
    {
        public bool IsAI;
        public CharacterType Character;
    }

    public class MatchConfig
    {
        /// <summary>
        /// 2–4 entries. Slot 0 is always the human player;
        /// remaining slots can be human or AI.
        /// </summary>
        public PlayerSlot[] Slots = System.Array.Empty<PlayerSlot>();
        public string StageName = string.Empty;

        /// <summary>Lives per player. Defaults to 1; valid range is 1–5.</summary>
        public int Stocks = 1;
    }

    // -------------------------------------------------------------------------
    // Scene paths — replace each placeholder with your actual .tscn path
    // -------------------------------------------------------------------------

    private const string MainMenuScene        = "res://Scenes/Pages/Menu/MainMenu.tscn";       
    private const string StageSelectScene     = "res://Scenes/Pages/StageSelect/StageSelect.tscn";     
    private const string CharacterSelectScene = "res://Scenes/Pages/Menu/CharacterSelect.tscn"; // TODO: update path
    private const string GameScene            = "res://Scenes/Pages/Game.tscn";            // TODO: update path

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    public MatchConfig CurrentMatch { get; private set; } = new();
    public GameMode CurrentMode { get; private set; } = GameMode.SinglePlayer;

    /// <summary>
    /// Emitted when StageManager calls OnMatchOver().
    /// winnerIndex maps to the slot index in CurrentMatch.Slots.
    /// The game scene listens to this signal and shows the results popup.
    /// </summary>
    [Signal] public delegate void MatchOverEventHandler(int winnerIndex);

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        // Enforce singleton — if a duplicate somehow gets created, discard it.
        if (Instance != null) { QueueFree(); return; }
        Instance = this;
    }

    // -------------------------------------------------------------------------
    // Config setters — called by the lobby/select screens before a match starts
    // -------------------------------------------------------------------------

    /// <summary>Called from MainMenu before navigating to stage select.</summary>
    public void SetMode(GameMode mode) => CurrentMode = mode;

    /// <summary>
    /// Called from the stage select screen when the player picks a stage.
    /// In multiplayer mode also pushes the path to NetworkManager so BattleManager can load it.
    /// </summary>
    public void SetStage(string stagePath)
    {
        CurrentMatch.StageName = stagePath;
        // TODO: when multiplayer stage select is wired up, push stagePath to NetworkManager here.
        // NetworkManager.Instance.SelectedStage = stagePath;
    }

    /// <summary>
    /// Called from the character select screen once all slots are filled.
    /// Pass 2–4 PlayerSlot entries.
    /// </summary>
    public void SetSlots(PlayerSlot[] slots) => CurrentMatch.Slots = slots;

    /// <summary>Called from the character select screen. Clamped to 1–5.</summary>
    public void SetStocks(int stocks) => CurrentMatch.Stocks = Mathf.Clamp(stocks, 1, 5);

    // -------------------------------------------------------------------------
    // Scene transitions
    // -------------------------------------------------------------------------

    public void GoToMainMenu()
    {
        CurrentMatch = new MatchConfig(); // wipe config so the next lobby starts fresh
        CurrentMode = GameMode.SinglePlayer; // reset to default
        GetTree().ChangeSceneToFile(MainMenuScene);
    }

    public void GoToStageSelect()     => GetTree().ChangeSceneToFile(StageSelectScene);
    public void GoToCharacterSelect() => GetTree().ChangeSceneToFile(CharacterSelectScene);
    public void StartMatch()          => GetTree().ChangeSceneToFile(GameScene);
    public void QuitGame()            => GetTree().Quit();

    // -------------------------------------------------------------------------
    // Match result — called by StageManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by StageManager when only one player remains.
    /// Emits MatchOver so the game scene can show the results popup.
    /// </summary>
       public void OnMatchOver(int winnerIndex) => EmitSignal(SignalName.MatchOver, (Variant)winnerIndex);
    /// <summary>
    /// Replays the match with the same MatchConfig — skips the lobby entirely.
    /// Called by the "Play Again" button in the results popup.
    /// </summary>
    public void PlayAgain() => GetTree().ChangeSceneToFile(GameScene);
}
