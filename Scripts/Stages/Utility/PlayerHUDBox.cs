using Godot;

public partial class PlayerHUDBox : Control
{
    private TextureRect _portrait;
    private Label _nameLabel;
    private Label _healthLabel;
    private Label _stockLabel;

    public override void _Ready()
    {
        _portrait    = GetNode<TextureRect>("MarginContainer/VBoxContainer/HBoxContainer/characterImage");
        _healthLabel = GetNode<Label>("MarginContainer/VBoxContainer/HBoxContainer/HealthLabel");
        _stockLabel  = GetNode<Label>("MarginContainer/VBoxContainer/HBoxContainer2/StockLabel");
        _nameLabel   = GetNode<Label>("MarginContainer/VBoxContainer/HBoxContainer2/CharacterNameLabel");
    }

    public void SetPlayerName(string playerName) => _nameLabel.Text = playerName;
    public void SetPortrait(Texture2D texture)   => _portrait.Texture = texture;
    public void SetHealth(float health)          => _healthLabel.Text = Mathf.RoundToInt(health).ToString();
    public void SetStocks(int stocks)            => _stockLabel.Text = $"* {stocks}";
}
