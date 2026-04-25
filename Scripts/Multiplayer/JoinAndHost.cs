using Godot;
using System.Net.NetworkInformation;
using System.Net.Sockets;

public partial class JoinAndHost : Control
{
	[Export] private Button _hostButton;
	[Export] private Button _joinButton;

	public override void _Ready()
	{
		_hostButton?.GrabFocus();
	}

	private void _on_host_button_pressed()
	{
		Error err = NetworkManager.Instance.StartHost();
		if (err != Error.Ok)
		{
			GD.PushError($"[JoinAndHost] Failed to start host: {err}");
			return;
		}

		// Show the local IP so the host can share it with joining players.
		// When the host clicks OK they have decided everyone is ready — send all peers to StageSelect.
		var dialog = new AcceptDialog
		{
			Title = "Hosting",
			DialogText = $"Your IP address:\n{GetLocalIp()}\n\nShare this with players who want to join.\n\nClick OK when everyone has connected."
		};
		AddChild(dialog);
		dialog.Confirmed += () =>
		{
			GD.Print($"[JoinAndHost] Host confirmed start. Connected peers: {NetworkManager.Instance.ConnectedPeers.Count}");
			NetworkManager.Instance.StartStageSelect();
		};
		dialog.PopupCentered();
	}

	private void _on_join_button_pressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Multiplayer/Join.tscn");
	}

	// Returns the LAN IP of the first active physical network adapter, skipping VPNs.
	private static string GetLocalIp()
	{
		foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
		{
			if (nic.OperationalStatus != OperationalStatus.Up) continue;
			if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

			string name = nic.Name.ToLowerInvariant();
			if (name.Contains("zerotier") || name.Contains("hamachi") ||
			    name.Contains("virtual") || name.Contains("vpn") ||
			    name.Contains("tunnel") || name.Contains("bluetooth"))
				continue;

			foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
			{
				if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
				string ip = addr.Address.ToString();
				if (ip.StartsWith("169.254.")) continue; // skip link-local
				return ip;
			}
		}
		return "127.0.0.1";
	}
}
