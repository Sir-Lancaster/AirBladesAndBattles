using Godot;
using System;

public partial class Steampunk : CharacterBase
{
	private AnimatedSprite2D _sprite;
	private CharacterState _previousState;
	[Export] public string CharacterLabel = "Steampunk";
    [Export] public float BasicAttackRecovery = 0.20f;
    [Export] public float SpecialAttackRecovery = 0.35f;

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		base._Ready();
	}

	protected override void OnStateChanged(CharacterState currentState, CharacterState newState)
	{
		_previousState = currentState;
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);

		// Keep the last facing direction when not moving.
		if (Mathf.Abs(Velocity.X) > 0.01f)
			_sprite.FlipH = Velocity.X < 0f;
	}

	private static string AnimName(CharacterState state)
		=> state.ToString().ToLowerInvariant();

	protected override void PlayAnimationForState(CharacterState state)
	{
		_sprite.Play(AnimName(state));
	}

	private async void EndAttackAfter(float seconds)
	{
		await ToSignal(GetTree().CreateTimer(seconds), "timeout");
		if (!IsDead && CurrentState == CharacterState.Attack)
			SetState(CharacterState.Idle);
	}
	protected override void OnAttackPerformed(AttackDirection direction, int damage)
	{
		GD.Print($"{CharacterLabel} attack: {direction}, damage: {damage}");
		EndAttackAfter(BasicAttackRecovery);

		//will need to spawn hitbox
	}

	protected override void OnSpecialPerformed(SpecialDirection direction, int damage)
	{
		GD.Print($"{CharacterLabel} special: {direction}, damage: {damage}");
		EndAttackAfter(SpecialAttackRecovery);

		//will need to spawn hitbox
	}
}
