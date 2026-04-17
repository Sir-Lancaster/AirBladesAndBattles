using Godot;
using Godot.Collections;

public partial class GameHUD : CanvasLayer
{
    public struct PlayerInfo
    {
        public string Name;
        public Texture2D Portrait;
        public float Health;
        public int Stocks;
    }

    [Export] private PackedScene _playerHUDBoxScene;

    private HBoxContainer _playerBoxContainer;
    private Array<PlayerHUDBox> _hudBoxes = new();

    public override void _Ready()
    {
        _playerBoxContainer = GetNode<HBoxContainer>("Control/PlayerBoxContainer");
    }

    // Call this once at match start with all active players.
    public void InitializePlayers(PlayerInfo[] players)
    {
        foreach (var box in _hudBoxes)
            box.QueueFree();
        _hudBoxes.Clear();

        foreach (var info in players)
        {
            var box = _playerHUDBoxScene.Instantiate<PlayerHUDBox>();
            _playerBoxContainer.AddChild(box);

            box.SetPlayerName(info.Name);
            box.SetPortrait(info.Portrait);
            box.SetHealth(info.Health);
            box.SetStocks(info.Stocks);

            _hudBoxes.Add(box);
        }
    }

    // Call these during gameplay when values change.
    public void UpdateHealth(int playerIndex, float health)
    {
        if (playerIndex < 0 || playerIndex >= _hudBoxes.Count) return;
        _hudBoxes[playerIndex].SetHealth(health);
    }

    public void UpdateStocks(int playerIndex, int stocks)
    {
        if (playerIndex < 0 || playerIndex >= _hudBoxes.Count) return;
        _hudBoxes[playerIndex].SetStocks(stocks);
    }
}
