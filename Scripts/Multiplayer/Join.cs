using Godot;

public partial class Join : Control
{
	private LineEdit _ipInput;

	public override void _Ready()
	{
		_ipInput = GetNode<LineEdit>("CanvasLayer/VBoxContainer/HBoxContainer/LineEdit");
		GetNode<Button>("CanvasLayer/VBoxContainer/Button").Pressed += _on_connect_pressed;

		NetworkManager.Instance.ConnectedToServer += OnConnectedToServer;
		NetworkManager.Instance.ConnectionFailed  += OnConnectionFailed;
	}

	private void _on_connect_pressed()
	{
		string ip = _ipInput.Text.Trim();
		if (string.IsNullOrEmpty(ip))
		{
			GD.PushWarning("[Join] No IP entered.");
			return;
		}

		NetworkManager.Instance.JoinGame(ip);
	}

	private void OnConnectedToServer()
	{
		// TODO: transition to lobby scene once it exists.
		GD.Print("[Join] Connected! Waiting for host to start.");
	}

	private void OnConnectionFailed()
	{
		// TODO: show an error label in the scene.
		GD.PushError("[Join] Connection failed. Check the IP and try again.");
	}
}
