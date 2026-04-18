using Godot;

public partial class Join : Control
{
	private LineEdit _ipInput;
	private Label _statusLabel;

	public override void _Ready()
	{
		_ipInput = GetNode<LineEdit>("CanvasLayer/VBoxContainer/HBoxContainer/LineEdit");
		GetNode<Button>("CanvasLayer/VBoxContainer/Button").Pressed += _on_connect_pressed;

		_statusLabel = GetNode<Label>("CanvasLayer/VBoxContainer/StatusLabel");

		NetworkManager.Instance.ConnectedToServer += OnConnectedToServer;
		NetworkManager.Instance.ConnectionFailed  += OnConnectionFailed;
	}

	private void _on_connect_pressed()
	{
		string ip = _ipInput.Text.Trim();
		if (string.IsNullOrEmpty(ip))
		{
			_statusLabel.Text = "Please enter an IP address.";
			return;
		}

		_statusLabel.Text = "Connecting...";
		NetworkManager.Instance.JoinGame(ip);
	}

	private void OnConnectedToServer()
	{
		_statusLabel.Text = "Connected! Waiting for host to start.";
	}

	private void OnConnectionFailed()
	{
		_statusLabel.Text = "Connection failed. Check the IP and try again.";
	}
}
