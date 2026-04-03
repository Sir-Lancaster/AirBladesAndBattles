using Godot;

public partial class Steampunk : CharacterBase
{
	private AnimatedSprite2D _sprite;
	private Vector2 _spriteBasePosition;
	[Export] public string CharacterLabel = "Steampunk";
	[Export] public float BasicAttackRecovery = 0.20f;
	[Export] public float SpecialAttackRecovery = 0.35f;
	private Hitbox _currentHitbox;
	private Hitbox _specialUpHitboxLeft;
	private Hitbox _specialUpHitboxRight;
	private bool _holdingSpecialUp;
	private float _specialUpHeldTime;
	private static readonly PackedScene HitboxScene = GD.Load<PackedScene>("res://Scenes/Steampunk/Hitbox.tscn");
	private static readonly PackedScene UpboxScene = GD.Load<PackedScene>("res://Scenes/Steampunk/Upbox.tscn");

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		_spriteBasePosition = _sprite.Position;
		base._Ready();
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector2 move = Input.GetVector("move_left", "move_right", "move_up", "move_down");

		if (Input.IsActionPressed("special") && move.Y < -0.5f && !_holdingSpecialUp)
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

			int currentDamage = SpecialDamage + (int)(_specialUpHeldTime * 1.5f);

			if (_specialUpHitboxLeft != null && IsInstanceValid(_specialUpHitboxLeft))
				_specialUpHitboxLeft.UpdateDamage(currentDamage);
			if (_specialUpHitboxRight != null && IsInstanceValid(_specialUpHitboxRight))
				_specialUpHitboxRight.UpdateDamage(currentDamage);
		}

		base._PhysicsProcess(delta);

		if (Mathf.Abs(Velocity.X) > 0.01f)
			UpdateFacing(Velocity.X < 0f);
	}

	private void UpdateFacing(bool facingLeft)
	{
		_sprite.FlipH = facingLeft;
		_sprite.Position = _spriteBasePosition + (facingLeft ? new Vector2(20f, 0f) : Vector2.Zero);
	}

	private static string AnimName(CharacterState state) => state.ToString().ToLowerInvariant();

	protected override void PlayAnimationForState(CharacterState state) => _sprite.Play(AnimName(state));

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
		EndAttackAfter(BasicAttackRecovery);
		SpawnAttackHitbox(direction, damage);
	}

	protected override void OnSpecialPerformed(SpecialDirection direction, int damage)
	{
		GD.Print($"{CharacterLabel} special: {direction}, damage: {damage}");
		if (direction != SpecialDirection.Up)
			EndAttackAfter(SpecialAttackRecovery);
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
				hitbox.Position = new Vector2(facing > 0f ? 40f : -120f, 0f);
				break;
			case AttackDirection.Up:
				hitbox.Position = new Vector2(0f, -40f);
				break;
			case AttackDirection.DownAir:
				hitbox.Position = new Vector2(0f, 40f);
				hitbox.RotationDegrees = 90f;
				break;
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
			case SpecialDirection.Horizontal:
				float facing = _sprite.FlipH ? -1f : 1f;
				hitbox.Activate(this, damage, SpecialAttackRecovery);
				hitbox.Position = new Vector2(facing > 0f ? 40f : -120f, 0f);
				_currentHitbox = hitbox;
				break;

			case SpecialDirection.Up:
				hitbox.Activate(this, damage, -1f);
				hitbox.Position = new Vector2(-120f, 0f);
				_specialUpHitboxLeft = hitbox;

				var hitboxRight = HitboxScene.Instantiate<Hitbox>();
				AddChild(hitboxRight);
				hitboxRight.Activate(this, damage, -1f);
				hitboxRight.Position = new Vector2(40f, 0f);
				_specialUpHitboxRight = hitboxRight;
				break;

			case SpecialDirection.Neutral:
				hitbox.Activate(this, damage, SpecialAttackRecovery);
				hitbox.Position = new Vector2(0f, 40f);
				hitbox.RotationDegrees = 90f;
				_currentHitbox = hitbox;
				break;
		}
	}
}