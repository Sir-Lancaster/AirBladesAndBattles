using Godot;
using System;
using System.Collections.Generic;

public partial class Vampire : CharacterBase
{
	private AnimatedSprite2D _sprite;
	private Vector2 _spriteBasePosition;
	private string _activeAnimation = "idle";
	private static readonly Dictionary<string, float> AnimationOffsets = new()
	{
		{ "attack", 60f },
		{ "attack_up", 0f },
		{ "attack_air", 0f },
		{ "special_up", 0f },
		{ "special_neutral", 64f },
		{ "dodge", -16f },
		{ "run", -32f },
		{ "idle", 0f },
		{ "jump", 0f },
		{ "hitstun", 0f },
		{ "dead", 0f }
	};
	[Export] public string CharacterLabel = "Vampire";
	[Export] public float BasicAttackRecovery = 0.68f;
    [Export] public float UpAttackRecovery = 0.5f;
    [Export] public float DownAttackRecovery = 0.45f;
    [Export] public float NeutralSpecialRecovery = 0.95f;
    [Export] public float UpSpecialDelay = 0.9167f;
    [Export] public float UpSpecialRecovery = 2.0f;
    [Export] public float UpSpecialVelocity = 150f;
    [Export] public float UpSpecialCooldown = 3.0f;
	[Export] public float AttackHitboxDelay = 0.2046f;
    [Export] public float UpAttackHitboxDelay = 0.2f;
    [Export] public float DownAttackHitboxDelay = 0.1f;
    [Export] public float NeutralSpecialHitboxDelay = 0.556f;
	[Export] public float DownVelocityBoost = 400f;
	[Export] public float NeutralSpecialHitboxLifetime = 0.3f;
	[Export] public float AttackHitboxLifetime = 0.3f;
	[Export] public float UpAttackHitboxLifetime = 0.3f;
	[Export] public float DownAttackHitboxLifetime = 0.3f;
	private Hitbox _currentHitbox;
	private bool _holdingSpecialUp;
	private float _specialUpChargeTime;
	private bool _specialUpEffectActive;
	private float _specialUpEffectDuration;
	private float _specialUpCooldownRemaining;
	private static readonly PackedScene SpecialboxScene = GD.Load<PackedScene>("res://Scenes/Vampire/Specialbox.tscn");
    private static readonly PackedScene HitboxScene = GD.Load<PackedScene>("res://Scenes/Vampire/Hitbox.tscn");
    private static readonly PackedScene UpboxScene = GD.Load<PackedScene>("res://Scenes/Vampire/Upbox.tscn");
    private static readonly PackedScene UpSpecialboxScene = GD.Load<PackedScene>("res://Scenes/Vampire/UpSpecialbox.tscn");
    private static readonly PackedScene DownboxScene = GD.Load<PackedScene>("res://Scenes/Vampire/Downbox.tscn");

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		_spriteBasePosition = _sprite.Position;
		base._Ready();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsMultiplayerAuthority())
		{
		Vector2 move = Input.GetVector("move_left", "move_right", "move_up", "move_down");

		if (Input.IsActionPressed("special") && move.Y < -0.5f && !_holdingSpecialUp && _specialUpCooldownRemaining <= 0)
		{
			if (CurrentState != CharacterState.HitStun &&
				CurrentState != CharacterState.Dead &&
				CurrentState != CharacterState.Dodge &&
				CurrentState != CharacterState.Attack)
			{
				_holdingSpecialUp = true;
				_specialUpChargeTime = 0f;
				SetState(CharacterState.Attack);
				OnSpecialPerformed(SpecialDirection.Up, SpecialDamage);
			}
		}

		if (_holdingSpecialUp && !Input.IsActionPressed("special"))
		{
			if (!_specialUpEffectActive)
			{
				bool isEarlyRelease = _specialUpChargeTime < UpSpecialDelay;
				_specialUpEffectActive = true;
				_specialUpEffectDuration = UpSpecialRecovery;
				_specialUpCooldownRemaining = UpSpecialCooldown;
				SpawnSpecialHitbox(SpecialDirection.Up, SpecialDamage);
				
				if (isEarlyRelease && _sprite.IsPlaying())
					_sprite.Frame = 12;
			}
			_holdingSpecialUp = false;
		}

		if (_specialUpChargeTime < UpSpecialDelay && Input.IsActionPressed("special") && _holdingSpecialUp)
		{
			_specialUpChargeTime += (float)delta;
			if (_specialUpChargeTime >= UpSpecialDelay && !_specialUpEffectActive)
			{
				_specialUpEffectActive = true;
				_specialUpEffectDuration = UpSpecialRecovery;
				_specialUpCooldownRemaining = UpSpecialCooldown;
				SpawnSpecialHitbox(SpecialDirection.Up, SpecialDamage);
			}
		}

		if (_specialUpEffectActive)
		{
			_specialUpEffectDuration -= (float)delta;
			float chargeProgress = Mathf.Clamp(_specialUpChargeTime / UpSpecialDelay, 0f, 1f);
			float scaledVelocity = UpSpecialVelocity * chargeProgress;
			Velocity = new Vector2(Velocity.X, -scaledVelocity);
			
			if (_specialUpEffectDuration <= 0)
			{
				_specialUpEffectActive = false;
				_specialUpChargeTime = 0f;
				if (CurrentState == CharacterState.Attack)
					SetState(CharacterState.Idle);
			}
		}

		if (_specialUpCooldownRemaining > 0)
			_specialUpCooldownRemaining = Mathf.Max(0f, _specialUpCooldownRemaining - (float)delta);
		}

		base._PhysicsProcess(delta);

		if (Mathf.Abs(Velocity.X) > 0.01f)
			UpdateFacing(Velocity.X < 0f);
	}

	private void UpdateFacing(bool facingLeft)
    {
        _sprite.FlipH = facingLeft;
        float xOffset = facingLeft ? 8f : 0f;
        
        if (AnimationOffsets.TryGetValue(_activeAnimation, out float animOffset))
            xOffset += facingLeft ? -animOffset : animOffset;
        
        _sprite.Position = _spriteBasePosition + new Vector2(xOffset, 0f);
    }

	// SetAnimation is the single place that updates _activeAnimation, plays the clip,
	// and refreshes the facing offset so the wider attack sprite shifts correctly.
	private void SetAnimation(string name)
	{
		_activeAnimation = name;
		_sprite.Play(name);
		UpdateFacing(_sprite.FlipH);
	}

	// Attack state animations are deferred to OnAttackPerformed/OnSpecialPerformed so we
	// know the direction before choosing a clip. All other states map directly to name.
	protected override void PlayAnimationByName(string animName) => SetAnimation(animName);

	protected override void PlayAnimationForState(CharacterState state)
	{
		if (state == CharacterState.Attack) return;
		SetAnimation(state.ToString().ToLowerInvariant());
	}

	private static string GetAttackAnim(AttackDirection dir) => dir switch
	{
		AttackDirection.Horizontal => "attack",
		AttackDirection.Up        => "attack_up",   // swap for "attack_up" when the asset is ready
		AttackDirection.DownAir   => "attack_air",   // swap for "attack_air" when the asset is ready
		_                         => "attack"
	};

	private static string GetSpecialAnim(SpecialDirection dir) => dir switch
	{
		SpecialDirection.Up      => "special_up",    // swap for "special_up" when the asset is ready
		SpecialDirection.Neutral => "special_neutral",    // swap for "special_neutral" when the asset is ready
		_                        => "special"
	};

	private async void EndAttackAfter(float seconds)
	{
		await ToSignal(GetTree().CreateTimer(seconds), "timeout");
		_currentHitbox = null;
		if (!IsDead && CurrentState == CharacterState.Attack)
			SetState(CharacterState.Idle);
	}

	protected override void OnAttackPerformed(AttackDirection direction, int damage)
	{
		GD.Print($"{CharacterLabel} attack: {direction}, damage: {damage}");
		BroadcastAnimation(GetAttackAnim(direction));
		
		if (direction == AttackDirection.Horizontal)
		{
			EndAttackAfter(BasicAttackRecovery);
			SpawnAttackHitboxAfter(AttackHitboxDelay, direction, damage);
		}
		else if (direction == AttackDirection.Up)
		{
			EndAttackAfter(UpAttackRecovery);
			SpawnAttackHitboxAfter(UpAttackHitboxDelay, direction, damage);
		}
		else if (direction == AttackDirection.DownAir)
		{
			EndAttackAfter(DownAttackRecovery);
			SpawnAttackHitboxAfter(DownAttackHitboxDelay, direction, damage);
		}
	}

	private async void SpawnAttackHitboxAfter(float delay, AttackDirection direction, int damage)
	{
		await ToSignal(GetTree().CreateTimer(delay), "timeout");
		if (!IsDead && CurrentState == CharacterState.Attack)
			SpawnAttackHitbox(direction, damage);
	}

	private async void SpawnSpecialHitboxAfter(float delay, SpecialDirection direction, int damage)
	{
		await ToSignal(GetTree().CreateTimer(delay), "timeout");
		if (!IsDead && CurrentState == CharacterState.Attack)
			SpawnSpecialHitbox(direction, damage);
	}

	protected override void OnSpecialPerformed(SpecialDirection direction, int damage)
	{
		GD.Print($"{CharacterLabel} special: {direction}, damage: {damage}");
		BroadcastAnimation(GetSpecialAnim(direction));
		
		if (direction == SpecialDirection.Neutral)
		{
			EndAttackAfter(NeutralSpecialRecovery);
			SpawnSpecialHitboxAfter(NeutralSpecialHitboxDelay, direction, damage);
		}
		else if (direction == SpecialDirection.Up)
		{
			// Don't spawn hitbox yet - wait for charge to complete
		}
	}

	private void SpawnAttackHitbox(AttackDirection? dir, int damage)
	{
		if (_currentHitbox != null && IsInstanceValid(_currentHitbox))
			_currentHitbox.QueueFree();

		PackedScene scene = dir switch
		{
			AttackDirection.Up => UpboxScene,
			AttackDirection.DownAir => DownboxScene,
			_ => HitboxScene
		};
		var hitbox = scene.Instantiate<Hitbox>();
		AddChild(hitbox);

		float recoveryDuration = dir switch
		{
			AttackDirection.Up => UpAttackHitboxLifetime,
			AttackDirection.DownAir => DownAttackHitboxLifetime,
			_ => AttackHitboxLifetime
		};

		hitbox.Activate(this, damage, recoveryDuration);
		switch (dir)
		{
			case AttackDirection.Horizontal:
				float facing = _sprite.FlipH ? -1f : 1f;
				hitbox.Position = new Vector2(facing > 0f ? 60f : -60f, -20f);
				break;
			case AttackDirection.Up:
				hitbox.Position = new Vector2(0f, -40f);
				break;
			case AttackDirection.DownAir:
				hitbox.Position = new Vector2(0f, 40f);
				Velocity = new Vector2(Velocity.X, DownVelocityBoost);
				break;
		}

		_currentHitbox = hitbox;
	}

	private void SpawnSpecialHitbox(SpecialDirection? dir, int damage)
	{
		if (_currentHitbox != null && IsInstanceValid(_currentHitbox))
			_currentHitbox.QueueFree();

		PackedScene sceneToUse = dir switch
		{
			SpecialDirection.Up => UpSpecialboxScene,
			SpecialDirection.Neutral => SpecialboxScene,
			_ => HitboxScene
		};

		var hitbox = sceneToUse.Instantiate<Hitbox>();
		AddChild(hitbox);

		float duration = dir switch
		{
			SpecialDirection.Up => UpSpecialRecovery,
			SpecialDirection.Neutral => NeutralSpecialHitboxLifetime,
			_ => 0f
		};

		switch (dir)
		{
			case SpecialDirection.Up:
				hitbox.Activate(this, damage, duration);
				hitbox.Position = new Vector2(0f, 0f);
				_currentHitbox = hitbox;
				break;

			case SpecialDirection.Neutral:
				hitbox.Activate(this, damage, duration);
				float facing = _sprite.FlipH ? -1f : 1f;
				hitbox.Position = new Vector2(facing > 0f ? 112f : -112f, -12f);
				_currentHitbox = hitbox;
				break;
		}
	}
}