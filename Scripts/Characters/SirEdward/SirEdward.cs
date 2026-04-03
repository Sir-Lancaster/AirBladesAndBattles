using Godot;

public partial class SirEdward: CharacterBase
{
    private AnimatedSprite2D _edward;
    private AttackDirection _currentAttackDirection;
    private SpecialDirection _currentSpecialDirection;
    private bool _isSpecial;
    private Area2D _currentHitbox;

    [Export] public string CharacterLabel = "SirEdward";
    [Export] public float AttackRecovery = 0.30f;
    [Export] public float SpecialAttackRecovery = 1.2f;

    private static readonly PackedScene HitboxScene =
        GD.Load<PackedScene>("res://Scenes/Utility/Hitbox.tscn");

    private static readonly PackedScene HalberdScene =
        GD.Load<PackedScene>("res://Scenes/Edward/Halbered.tscn");

    public override void _Ready()
    {
        _edward = GetNode<AnimatedSprite2D>("Edward");
        base._Ready();
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        // Debug: press Delete to take 10 damage.
        if (Input.IsPhysicalKeyPressed(Key.Delete))
        {
            TakeDamage(10);
            GD.Print($"{CharacterLabel} DEBUG: TakeDamage(10) called");
        }

        // Keep facing direction when not moving.
        if (Mathf.Abs(Velocity.X) > 0.01f)
            _edward.FlipH = Velocity.X < 0f;
    }

    protected override void OnStateChanged(CharacterState fromState, CharacterState toState)
    {
        GD.Print($"{CharacterLabel} state: {fromState} -> {toState}");
    }

    protected override void PlayAnimationForState(CharacterState state)
    {
        string anim = state switch
        {
            CharacterState.Idle => "idle",
            CharacterState.Run => "run",
            CharacterState.Jump => "jump",
            CharacterState.Dodge => "dodge",
            CharacterState.Attack => _isSpecial
                ? $"special_{_currentSpecialDirection.ToString().ToLower()}"
                : $"attack_{_currentAttackDirection.ToString().ToLower()}",
            CharacterState.HitStun => "hitstun",
            CharacterState.Dead => "dead",
            _ => "idle"
        };

        _edward.Play(anim);
        GD.Print($"{CharacterLabel} play animation for: {state}");
    }

    protected override void OnHealthChanged(int oldHp, int newHp)
    {
        GD.Print($"{CharacterLabel} HP: {oldHp} -> {newHp}");
    }

    protected override void OnDamaged(int amount)
    {
        GD.Print($"{CharacterLabel} took damage: {amount}");
        PlayAnimationForState(CharacterState.HitStun);
    }

    protected override void OnDied()
    {
        GD.Print($"{CharacterLabel} died");
        PlayAnimationForState(CharacterState.Dead);
    }

    protected override void OnAttackPerformed(AttackDirection direction, int damage)
    {
        _currentAttackDirection = direction;
        _isSpecial = false;

        GD.Print($"{CharacterLabel} attack: {direction}, damage: {damage}");
        EndAttackAfter(AttackRecovery);
        SpawnAttackHitbox(direction, damage);
    }

    protected override void OnSpecialPerformed(SpecialDirection direction, int damage)
    {
        _currentSpecialDirection = direction;
        _isSpecial = true;
        
        switch (direction)
        {
            case SpecialDirection.Neutral:
                // healing
                int oldHp = CurrentHP;
                CurrentHP = Mathf.Min(MaxHP, CurrentHP + 2);
                OnHealthChanged(oldHp, CurrentHP);
                GD.Print($"{CharacterLabel} neutral special: heal 2");
                break;

            case SpecialDirection.Up:
                SpawnUpSpecialHalberd(damage);
                GD.Print($"{CharacterLabel} up special: halberd throw");
                break;

            case SpecialDirection.Horizontal:
                // TODO: Pike stance hitbox.
                GD.Print($"{CharacterLabel} horizontal special: pike");
                break;
        }

        EndAttackAfter(SpecialAttackRecovery);
    }

    protected override void OnDodgeStarted(DodgeDirection direction, float dodgeDuration, float iFrameDuration)
    {
        PlayAnimationForState(CharacterState.Dodge);
        GD.Print($"{CharacterLabel} dodge start: {direction}, duration: {dodgeDuration}, iframes: {iFrameDuration}");
    }

    protected override void OnDodgeEnded(float dodgeCooldown)
    {
        PlayAnimationForState(CharacterState.Idle);
    }

    private async void EndAttackAfter(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), "timeout");
        _currentHitbox = null;

        if (!IsDead && CurrentState == CharacterState.Attack)
            SetState(CharacterState.Idle);
    }

    private void SpawnAttackHitbox(AttackDirection? dir, int damage)
    {
        var hitbox = HitboxScene.Instantiate<Hitbox>();
        AddChild(hitbox); // local to Edward
        hitbox.Activate(this, damage, AttackRecovery);

        float facing = _edward.FlipH ? -1f : 1f;

        switch (dir)
        {
            case AttackDirection.Horizontal:
                hitbox.Position = new Vector2(40f * facing, 0f); // in front
                hitbox.RotationDegrees = 0f;
                break;

            case AttackDirection.Up:
                hitbox.Position = new Vector2(0f, -40f); // above
                hitbox.RotationDegrees = -90f; // adjust visually as needed
                break;

            case AttackDirection.DownAir:
                hitbox.Position = new Vector2(0f, 40f); // below
                hitbox.RotationDegrees = 90f;
                break;
        }

        _currentHitbox = hitbox;
    }

    private void SpawnUpSpecialHalberd(int damage)
    {
        var halberd = HalberdScene.Instantiate<Halberd>();

        // Add to world so it flies independently.
        GetParent().AddChild(halberd);
        halberd.GlobalPosition = GlobalPosition + new Vector2(0f, -18f);

        float facing = _edward.FlipH ? -1f : 1f;
        Vector2 throwDirection = new Vector2(facing, -1.2f).Normalized(); // ~20 deg up

        halberd.Launch(this, throwDirection, damage);

        // Connect despawn signal to teleport Edward.
        halberd.Despawned += OnHalberdDespawned;
    }

    private async void OnHalberdDespawned(Vector2 despawnPosition)
    {
        // Smoothly move to despawn position over 0.2 seconds.
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Quad);
        tween.SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(this, "global_position", despawnPosition, 0.2f);

        // Wait for tween to finish, then hover for 0.5 seconds.
        await ToSignal(tween, "finished");
        Velocity = new Vector2(Velocity.X, 0f);

        GD.Print($"{CharacterLabel} teleport complete");
    }
}