using Godot;
using System;


public partial class JoinAndHost : Control
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GetNode<Button>("HostButton").Pressed += _on_host_button_pressed;
		GetNode<Button>("JoinButton").Pressed += _on_join_button_pressed;

		NetworkManager.Instance.ConnectedToServer += () => GD.Print("Connected to server!");
		NetworkManager.Instance.ConnectionFailed += () => GD.Prin("Connection failed.");
	}

	private void _on_host_button_pressed(string ip)
	{
		NetworkManager.Instance.StartHost();
		// TODO: Make a scene to popup the IP and a continue button to go choose a stage and character as normal.
	}

	private void _on_join_button_pressed()
	{
		// TODO: make a join popup where they enter IP and join the session. 
	}

	private void _on_connect_pressed(string ip)
	{
		NetworkManager.Instance.JoinGame(ip);
	}
}
