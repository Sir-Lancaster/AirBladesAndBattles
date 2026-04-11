using Godot;

public partial class SteampunkAi : AiBaseClass
{
	private AnimatedSprite2D _sprite;
	private Vector2 _spriteBasePosition;
	[Export] public CharacterBase Target;
	[Export] public string CharacterLabel = "Steampunk";
	[Export] public float BasicAttackRecovery = 0.20f;
	[Export] public float SpecialAttackRecovery = 0.35f;
	private Hitbox _specialUpHitboxLeft;
	private Hitbox _specialUpHitboxRight;
	private bool _holdingSpecialUp;
	private float _specialUpHeldTime;
	private SteampunkProjectile _activeProjectile;
	private static readonly PackedScene HitboxScene = GD.Load<PackedScene>("res://Scenes/Steampunk/Hitbox.tscn");
	private static readonly PackedScene UpboxScene = GD.Load<PackedScene>("res://Scenes/Steampunk/Upbox.tscn");
	private static readonly PackedScene ProjectileScene = GD.Load<PackedScene>("res://Scenes/Steampunk/SteampunkProjectile.tscn");

	public override void _Ready()
	{
		_sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		_spriteBasePosition = _sprite.Position;
		RegisterAttack(245f, 295f, 0f, 400f, AttackUp);               // above
		RegisterAttack(0f, 360f, 0f, 40f, AttackUp);                   // any direction when very close
		RegisterAttack(315f, 45f, 50f, 100f, AttackHorizontal);        // right
		RegisterAttack(135f, 225f, 50f, 100f, AttackHorizontal);       // left
		RegisterAttack(315f, 45f, 200f, 1000f, SpecialHorizontal, () => !IsInstanceValid(_activeProjectile), isSpecial: true);  // right, ranged
		RegisterAttack(135f, 225f, 200f, 1000f, SpecialHorizontal, () => !IsInstanceValid(_activeProjectile), isSpecial: true); // left, ranged
		base._Ready();
	}

	private void AttackHorizontal() => AiInput.AttackJustPressed = true;
	private void SpecialHorizontal() => AiInput.SpecialJustPressed = true;

	private void AttackUp()
	{
		AiInput.AttackJustPressed = true;
		AiInput.MoveDirection = new Vector2(AiInput.MoveDirection.X, -1f);
	}

	private void SpecialUp()
	{
		AiInput.SpecialHeld = true;
		AiInput.MoveDirection = new Vector2(AiInput.MoveDirection.X, -1f);
	}

	public override void _PhysicsProcess(double delta)
	{
		AiInput = default;

		if (Target != null && !Target.IsDead)
		{
			Vector2 toTarget = Target.GlobalPosition - GlobalPosition;
			if (!TrySelectAttack(toTarget) && !IsInAttackRange(toTarget))
				MoveTowardTarget(toTarget);
		}

		if (AiInput.SpecialHeld && AiInput.MoveDirection.Y < -0.5f && !_holdingSpecialUp)
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

		if (_holdingSpecialUp && !AiInput.SpecialHeld)
		{
			_holdingSpecialUp = false;
			_specialUpHeldTime = 0f;
			if (IsInstanceValid(_specialUpHitboxLeft))
				_specialUpHitboxLeft.QueueFree();
			if (IsInstanceValid(_specialUpHitboxRight))
				_specialUpHitboxRight.QueueFree();

			_specialUpHitboxLeft = null;
			_specialUpHitboxRight = null;

			if (CurrentState == CharacterState.Attack)
				SetState(CharacterState.Idle);
		}

		if (_holdingSpecialUp && CurrentState == CharacterState.Attack)
		{
			Velocity = new Vector2(AiInput.MoveDirection.X * MoveSpeed * 0.4f, Velocity.Y);
			_specialUpHeldTime += (float)delta;

			int currentDamage = SpecialDamage + (int)(_specialUpHeldTime * 3f);

			if (IsInstanceValid(_specialUpHitboxLeft))
				_specialUpHitboxLeft.UpdateDamage(currentDamage);
			if (IsInstanceValid(_specialUpHitboxRight))
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

	protected override void PlayAnimationForState(CharacterState state) =>
		_sprite.Play(state.ToString().ToLowerInvariant());

	private async void EndAttackAfter(float seconds)
	{
		await ToSignal(GetTree().CreateTimer(seconds), "timeout");
		if (!IsDead && CurrentState == CharacterState.Attack)
			SetState(CharacterState.Idle);
	}

	protected override void OnAttackPerformed(AttackDirection direction, int damage)
	{
		if (direction == AttackDirection.DownAir && IsInstanceValid(_activeProjectile))
		{
			SetState(CharacterState.Idle);
			return;
		}
		GD.Print($"{CharacterLabel} attack: {direction}, damage: {damage}");
		EndAttackAfter(BasicAttackRecovery);
		SpawnAttackHitbox(direction, damage);
	}

	protected override void OnSpecialPerformed(SpecialDirection direction, int damage)
	{
		if (direction == SpecialDirection.Neutral && IsInstanceValid(_activeProjectile))
		{
			SetState(CharacterState.Idle);
			return;
		}
		GD.Print($"{CharacterLabel} special: {direction}, damage: {damage}");
		if (direction != SpecialDirection.Up)
			EndAttackAfter(SpecialAttackRecovery);
		SpawnSpecialHitbox(direction, damage);
	}

	private void SpawnAttackHitbox(AttackDirection? dir, int damage)
	{
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
				hitbox.QueueFree();
				var downProjectile = ProjectileScene.Instantiate<SteampunkProjectile>();
				GetParent().AddChild(downProjectile);
				downProjectile.GlobalPosition = GlobalPosition + new Vector2(0f, 40f);
				downProjectile.LaunchDown(this, damage);
				_activeProjectile = downProjectile;
				downProjectile.TreeExiting += () => _activeProjectile = null;
				return;
		}
	}

	private void SpawnSpecialHitbox(SpecialDirection? dir, int damage)
	{
		switch (dir)
		{
			case SpecialDirection.Up:
			{
				var hitboxLeft = HitboxScene.Instantiate<Hitbox>();
				AddChild(hitboxLeft);
				hitboxLeft.Activate(this, damage, -1f);
				hitboxLeft.Position = new Vector2(-120f, 0f);
				_specialUpHitboxLeft = hitboxLeft;

				var hitboxRight = HitboxScene.Instantiate<Hitbox>();
				AddChild(hitboxRight);
				hitboxRight.Activate(this, damage, -1f);
				hitboxRight.Position = new Vector2(40f, 0f);
				_specialUpHitboxRight = hitboxRight;
				break;
			}

			case SpecialDirection.Neutral:
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
