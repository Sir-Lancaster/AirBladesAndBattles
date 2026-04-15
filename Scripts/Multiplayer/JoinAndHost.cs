using Godot;

public partial class JoinAndHost : Control
{
	private void _on_host_button_pressed()
	{
		Error err = NetworkManager.Instance.StartHost();
		if (err != Error.Ok)
		{
			GD.PushError($"[JoinAndHost] Failed to start host: {err}");
			return;
		}

		// Show the local IP so the host can share it with joining players.
		var dialog = new AcceptDialog
		{
			Title = "Hosting",
			DialogText = $"Your IP address:\n{GetLocalIp()}\n\nShare this with players who want to join."
		};
		AddChild(dialog);
		// dialog.Confirmed += () => GetTree().ChangeSceneToFile("res://Scenes/Multiplayer/StageSelect.tscn");
		dialog.PopupCentered();
	}

	private void _on_join_button_pressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Multiplayer/Join.tscn");
	}

	// Returns the most likely LAN IP address of this machine.
	private static string GetLocalIp()
	{
		foreach (string addr in IP.GetLocalAddresses())
		{
			if (addr.StartsWith("192.168.") || addr.StartsWith("10.") || addr.StartsWith("172."))
				return addr;
		}
		return "127.0.0.1";
	}
}
