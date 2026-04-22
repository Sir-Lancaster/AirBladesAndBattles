using Godot;

public partial class Steampunk : CharacterBase
{
	private AnimatedSprite2D _sprite;
	private Vector2 _spriteBasePosition;
	private string _activeAnimation = "idle";
	[Export] public string CharacterLabel = "Steampunk";
	[Export] public float BasicAttackRecovery = 0.40f;
	[Export] public float SpecialAttackRecovery = 2f;
	[Export] public float AttackHitboxDelay = 0.12f;
	[Export] public float AttackAnimOffset = 80f;
	[Export] public float UpAttackLaunchSpeed = 800f;
	private Hitbox _currentHitbox;
	private Hitbox _specialUpHitboxLeft;
	private Hitbox _specialUpHitboxRight;
	private bool _holdingSpecialUp;
	private bool _specialUpBlocked;
	private float _specialUpHeldTime;
	private bool _hasUsedAirUpAttack;
	private bool _wasOnFloor = true;
	private SteampunkProjectile _activeProjectile;
	private static readonly PackedScene HitboxScene = GD.Load<PackedScene>("res://Scenes/Steampunk/Hitbox.tscn");
	private static readonly PackedScene UpboxScene = GD.Load<PackedScene>("res://Scenes/Steampunk/Upbox.tscn");
	private static readonly PackedScene Hitbox2Scene = GD.Load<PackedScene>("res://Scenes/Steampunk/Hitbox2.tscn");
	private static readonly PackedScene ProjectileScene = GD.Load<PackedScene>("res://Scenes/Steampunk/SteampunkProjectile.tscn");

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

			if (!Input.IsActionPressed("special"))
				_specialUpBlocked = false;

			if (Input.IsActionPressed("special") && move.Y < -0.5f && !_holdingSpecialUp && !_specialUpBlocked)
			{
				if (CurrentState != CharacterState.HitStun &&
					CurrentState != CharacterState.Dead &&
					CurrentState != CharacterState.Dodge &&
					CurrentState != CharacterState.Attack)
				{
					_holdingSpecialUp = true;
					SetState(CharacterState.Attack);
					OnSpecialPerformed(SpecialDirection.Up, SpecialDamage);
				}
			}

			if (_holdingSpecialUp && !Input.IsActionPressed("special"))
			{
				_holdingSpecialUp = false;
				_specialUpHeldTime = 0f;
				if (_specialUpHitboxLeft != null && IsInstanceValid(_specialUpHitboxLeft))
					_specialUpHitboxLeft.QueueFree();
				if (_specialUpHitboxRight != null && IsInstanceValid(_specialUpHitboxRight))
					_specialUpHitboxRight.QueueFree();

				_specialUpHitboxLeft = null;
				_specialUpHitboxRight = null;

				if (CurrentState == CharacterState.Attack)
					SetState(CharacterState.Idle);
			}

			if (_holdingSpecialUp && CurrentState == CharacterState.Attack)
			{
				Velocity = new Vector2(move.X * MoveSpeed * 0.4f, Velocity.Y);
				_specialUpHeldTime += (float)delta;

				int currentDamage = SpecialDamage + (int)(_specialUpHeldTime * 3f);

				if (_specialUpHitboxLeft != null && IsInstanceValid(_specialUpHitboxLeft))
					_specialUpHitboxLeft.UpdateDamage(currentDamage);
				if (_specialUpHitboxRight != null && IsInstanceValid(_specialUpHitboxRight))
					_specialUpHitboxRight.UpdateDamage(currentDamage);
			}
		}

		base._PhysicsProcess(delta);

		bool onFloor = IsOnFloor();
		if (onFloor && !_wasOnFloor)
			_hasUsedAirUpAttack = false;
		_wasOnFloor = onFloor;

		if (Mathf.Abs(Velocity.X) > 0.01f)
			UpdateFacing(Velocity.X < 0f);
	}

	private void UpdateFacing(bool facingLeft)
	{
		_sprite.FlipH = facingLeft;
		float xOffset = facingLeft ? 20f : 0f;
		if (_activeAnimation == "attack")
			xOffset += facingLeft ? -AttackAnimOffset : AttackAnimOffset;
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
	protected override void PlayAnimationForState(CharacterState state)
	{
		if (state == CharacterState.Attack) return;
		SetAnimation(state.ToString().ToLowerInvariant());
	}

	private static string GetAttackAnim(AttackDirection dir) => dir switch
	{
		AttackDirection.Horizontal => "attack",
		AttackDirection.Up        => "wheel",
		AttackDirection.DownAir   => "attack",   // swap for "attack_air" when the asset is ready
		_                         => "attack"
	};

	private static string GetSpecialAnim(SpecialDirection dir) => dir switch
	{
		SpecialDirection.Up      => "tornado",
		SpecialDirection.Neutral => "attack",    // swap for "special_neutral" when the asset is ready
		_                        => "attack"
	};

	private void StopSpecialUp()
	{
		_holdingSpecialUp = false;
		_specialUpBlocked = true;
		_specialUpHeldTime = 0f;
		if (_specialUpHitboxLeft != null && IsInstanceValid(_specialUpHitboxLeft))
			_specialUpHitboxLeft.QueueFree();
		if (_specialUpHitboxRight != null && IsInstanceValid(_specialUpHitboxRight))
			_specialUpHitboxRight.QueueFree();
		_specialUpHitboxLeft = null;
		_specialUpHitboxRight = null;
		if (CurrentState == CharacterState.Attack)
			SetState(CharacterState.Idle);
	}

	private async void EndAttackAfter(float seconds)
	{
		await ToSignal(GetTree().CreateTimer(seconds), "timeout");
		_currentHitbox = null;
		if (!IsDead && CurrentState == CharacterState.Attack)
			SetState(CharacterState.Idle);
	}

	protected override void OnAttackPerformed(AttackDirection direction, int damage)
	{
		if (direction == AttackDirection.DownAir && _activeProjectile != null && IsInstanceValid(_activeProjectile))
		{
			SetState(CharacterState.Idle);
			return;
		}
		if (direction == AttackDirection.Up)
		{
			if (!IsOnFloor() && _hasUsedAirUpAttack) { SetState(CharacterState.Idle); return; }
			_hasUsedAirUpAttack = true;
		}
		SetAnimation(GetAttackAnim(direction));
		if (direction == AttackDirection.Up)
		{
			float angleRad = Mathf.DegToRad(70f);
			float facing = _sprite.FlipH ? -1f : 1f;
			Velocity = new Vector2(facing * UpAttackLaunchSpeed * Mathf.Cos(angleRad),
			                       -UpAttackLaunchSpeed * Mathf.Sin(angleRad));
		}
		EndAttackAfter(BasicAttackRecovery);
		SpawnAttackHitboxAfter(AttackHitboxDelay, direction, damage);
	}

	private async void SpawnAttackHitboxAfter(float delay, AttackDirection direction, int damage)
	{
		await ToSignal(GetTree().CreateTimer(delay), "timeout");
		if (!IsDead && CurrentState == CharacterState.Attack)
			SpawnAttackHitbox(direction, damage);
	}

	protected override void OnSpecialPerformed(SpecialDirection direction, int damage)
	{
		if (direction == SpecialDirection.Neutral && _activeProjectile != null && IsInstanceValid(_activeProjectile))
		{
			SetState(CharacterState.Idle);
			return;
		}
		SetAnimation(GetSpecialAnim(direction));
		if (direction != SpecialDirection.Up)
			EndAttackAfter(BasicAttackRecovery);
		SpawnSpecialHitbox(direction, damage);
	}

	private void SpawnAttackHitbox(AttackDirection? dir, int damage)
	{
		if (_currentHitbox != null && IsInstanceValid(_currentHitbox))
			_currentHitbox.QueueFree();

		PackedScene scene = dir == AttackDirection.Up ? UpboxScene : HitboxScene;
		var hitbox = scene.Instantiate<Hitbox>();
		AddChild(hitbox);
		hitbox.Activate(this, damage, BasicAttackRecovery);

		switch (dir)
		{
			case AttackDirection.Horizontal:
				float facing = _sprite.FlipH ? -1f : 1f;
				hitbox.Position = new Vector2(facing > 0f ? 50f : -100f, 0f);
				break;
			case AttackDirection.Up:
				hitbox.Position = new Vector2(_sprite.FlipH ? 20f : 0f, -40f);
				break;
			case AttackDirection.DownAir:
				hitbox.QueueFree(); // not used for this case
				var downProjectile = ProjectileScene.Instantiate<SteampunkProjectile>();
				GetParent().AddChild(downProjectile);
				downProjectile.GlobalPosition = GlobalPosition + new Vector2(0f, 40f);
				downProjectile.LaunchDown(this, damage);
				_activeProjectile = downProjectile;
				downProjectile.TreeExiting += () => _activeProjectile = null;
				return;
		}

		_currentHitbox = hitbox;
	}

	private void SpawnSpecialHitbox(SpecialDirection? dir, int damage)
	{
		if (_currentHitbox != null && IsInstanceValid(_currentHitbox))
			_currentHitbox.QueueFree();

		var hitbox = HitboxScene.Instantiate<Hitbox>();
		AddChild(hitbox);

		switch (dir)
		{
			case SpecialDirection.Up: //tornado, need to increase damage as the attack is held, and needs to be able to move left and right
				{
					hitbox.QueueFree();
					var hitboxLeft = Hitbox2Scene.Instantiate<Hitbox>();
					AddChild(hitboxLeft);
					hitboxLeft.Activate(this, damage, -1f);
					hitboxLeft.Position = new Vector2(-60f, 0f);
					hitboxLeft.RotationDegrees = 0f;
					hitboxLeft.HitLanded += StopSpecialUp;
					_specialUpHitboxLeft = hitboxLeft;

					var hitboxRight = Hitbox2Scene.Instantiate<Hitbox>();
					AddChild(hitboxRight);
					hitboxRight.Activate(this, damage, -1f);
					hitboxRight.Position = new Vector2(20f, 0f);
					hitboxRight.HitLanded += StopSpecialUp;
					_specialUpHitboxRight = hitboxRight;
					break;
				}

			case SpecialDirection.Neutral:
				hitbox.QueueFree(); // not used for this case
				var projectile = ProjectileScene.Instantiate<SteampunkProjectile>();
				GetParent().AddChild(projectile);
				projectile.GlobalPosition = GlobalPosition + new Vector2(_sprite.FlipH ? -40f : 40f, 0f);
				projectile.Launch(this, damage, _sprite.FlipH);
				_activeProjectile = projectile;
				projectile.TreeExiting += () => _activeProjectile = null;
				break;
		}
	}
}