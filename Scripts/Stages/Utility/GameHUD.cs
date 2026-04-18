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

    /// <summary>Positive values push the HUD boxes down, negative moves them up.</summary>
    [Export] private float _verticalOffset = 0f;

    private Control _playerBoxContainer;
    private Array<PlayerHUDBox> _hudBoxes = new();

    public override void _Ready()
    {
        _playerBoxContainer = GetNode<Control>("Control/PlayerBoxContainer");
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

        CallDeferred(nameof(PositionHUDBoxes));
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

    private void PositionHUDBoxes()
    {
        if (_hudBoxes.Count == 0) return;

        float containerW = _playerBoxContainer.Size.X;
        float containerH = _playerBoxContainer.Size.Y;
        float sectionW   = containerW / 4f;

        for (int i = 0; i < _hudBoxes.Count; i++)
        {
            PlayerHUDBox box  = _hudBoxes[i];
            Vector2      size = box.Size;

            float x = i * sectionW + (sectionW - size.X) / 2f;
            float y = (containerH - size.Y) / 2f + _verticalOffset;

            box.Position = new Vector2(x, Mathf.Max(y, 0f));
        }
    }
}