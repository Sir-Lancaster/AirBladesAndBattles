using Godot;

public partial class EvilEdwardAi : AiBaseClass
{
    /// <summary>
    /// This is the animated sprite child node of the CharacterBody2D.
    /// </summary>
    private AnimatedSprite2D _edward;

    /// <summary>
    /// Tracking the current attack or special directions.
    /// </summary>
    private AttackDirection _currentAttackDirection;
    private SpecialDirection _currentSpecialDirection;

    /// <summary>
    /// A boolean powering a switch for special or normal attack animations.
    /// </summary>
    private bool _isSpecial;

    /// <summary>
    /// Tracks whether a hitbox exists as a child node currently.
    /// </summary>
    private Area2D _currentHitbox;
    private Hitbox _downAirHitbox;
    private bool _hasUsedAirUpAttack;
    private bool _wasOnFloor = true;
    private int _healCount = 0;
    [Export] public string CharacterLabel = "SirEdward";

    /// <summary>
    /// The time that passes after an attack has completed.
    /// </summary>
    [Export] public float AttackRecovery = 0.80f;

    /// <summary>
    /// The time that passes after a special attack has completed before you can use it again. 
    /// </summary>
    [Export] public float SpecialAttackRecovery = 2f;

    /// <summary>
    /// Get the hitbox scene so it can be spawned in when attacking.
    /// </summary>
    private static readonly PackedScene HitboxScene =
        GD.Load<PackedScene>("res://Scenes/Utility/Hitbox.tscn");

    /// <summary>
    /// Get the halberd scene so that it can be spawned in when doing the up special.
    /// </summary>
    private static readonly PackedScene HalberdScene =
        GD.Load<PackedScene>("res://Scenes/Edward/Halberd.tscn");


    /// <summary>
    /// Get's and sets the private variable to get the node for the character. 
    /// Calls the base class' _Ready() method.
    /// </summary>
    public override void _Ready()
    {
        _edward = GetNode<AnimatedSprite2D>("Edward");
        // Horizontal attack — target in front, melee range
        RegisterAttack(315f, 45f, 0f, 100f, AttackHorizontal);   // right
        RegisterAttack(135f, 225f, 0f, 100f, AttackHorizontal);   // left
        // Up attack — target above, close range
        RegisterAttack(245f, 295f, 0f, 120f, AttackUp);
        // Down-air — target below, airborne only
        RegisterAttack(65f, 115f, 0f, 150f, AttackDown, () => !IsOnFloor());
        // Neutral special — self-heal, used at any range when HP ≤ 50% and charges remain
        RegisterAttack(0f, 360f, 0f, 1000f, SpecialHorizontal,
            () => _healCount < 5 && CurrentHP < MaxHP * 0.5f, isSpecial: true);
        // Up special — halberd throw, target above at medium range
        RegisterAttack(225f, 315f, 150f, 500f, SpecialUp, isSpecial: true);
        base._Ready();
    }





    protected override void AggressiveFallbackAttack()
    {
        if (_target == null) return;
        Vector2 toTarget = _target.GlobalPosition - GlobalPosition;
        if (IsInAttackRange(toTarget))
            AttackHorizontal();
        else
            MoveTowardTarget(toTarget);
    }

    protected override bool TryRecoveryMove()
    {
        if (_hasUsedAirUpAttack) return false;
        _hasUsedAirUpAttack = true;
        SpecialUp();
        return true;
    }

    private void AttackHorizontal() => AiInput.AttackJustPressed = true;
    private void SpecialHorizontal() => AiInput.SpecialJustPressed = true;

    private void AttackDown()
    {
        AiInput.AttackJustPressed = true;
        AiInput.MoveDirection = new Vector2(AiInput.MoveDirection.X, 1f);
    }

    private void AttackUp()
    {
        AiInput.AttackJustPressed = true;
        AiInput.MoveDirection = new Vector2(AiInput.MoveDirection.X, -1f);
    }

    private void SpecialUp()
    {
        AiInput.SpecialJustPressed = true;
        AiInput.MoveDirection = new Vector2(AiInput.MoveDirection.X, -1f);
    }



    /// <summary>
    /// Extends the base class' _PhysicsProcess() function, keeps the character facing the direction of their movement.
    /// </summary>
    /// <param name="delta"></param>
    public override void _PhysicsProcess(double delta)
    {
        AiInput = default;
        RunAiBehavior();
        base._PhysicsProcess(delta);

        bool onFloor = IsOnFloor();
        if (onFloor && !_wasOnFloor)
            _hasUsedAirUpAttack = false;
        _wasOnFloor = onFloor;

        // Keep facing direction when not moving.
        if (_target != null)
            _edward.FlipH = _target.GlobalPosition.X < GlobalPosition.X;

        if (_downAirHitbox != null && IsOnFloor())
        {
            _downAirHitbox.QueueFree();
            _downAirHitbox = null;
        }
    }

    /// <summary>
    /// Contains a switch that converts each state to the string name of the animation sets defined in the animationPlayer on Godot for Edward.
    /// Uses _isSpecial boolean, current attack and special directions to determine which animation to play in the attack state.
    /// Calls the animation to play on the AnimatedSprite2D.
    /// </summary>
    /// <param name="state">The Character state that needs to be animated.</param>
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

        // Sprite-only correction for horizontal normal attack frame alignment.
        if (state == CharacterState.Attack && !_isSpecial && _currentAttackDirection == AttackDirection.Horizontal)
        {
            float facing = _edward.FlipH ? -1f : 1f; // left = -1, right = +1
            _edward.Offset = new Vector2(15f * facing, -6f);
        }
        else if (state == CharacterState.Attack && !_isSpecial && _currentAttackDirection == AttackDirection.Up)
        {
            float facing = _edward.FlipH ? -1f : 1f;
            _edward.Offset = new Vector2(7.5f * facing, -17.5f);
        }
        else if (state == CharacterState.Attack && _isSpecial && _currentSpecialDirection == SpecialDirection.Neutral)
        {
            _edward.Offset = new Vector2(_edward.Position.X, -10f);
        }
        else
        {
            _edward.Offset = Vector2.Zero;
        }

        _edward.Play(anim);
        GD.Print($"{CharacterLabel} play animation for: {state}");
    }

    /// <summary>
    /// Keeping it only for if it becomes useful later when updating the UI or playing sound effects.
    /// </summary>
    /// <param name="oldHp">The intiger of the HP before change.</param>
    /// <param name="newHp">The intiger of the HP after the change.</param>
    protected override void OnHealthChanged(int oldHp, int newHp)
    {
        GD.Print($"{CharacterLabel} HP: {oldHp} -> {newHp}");
    }

    /// <summary>
    /// Plays the animation for being hit.
    /// </summary>
    /// <param name="amount"></param>
    protected override void OnDamaged(int amount)
    {
        GD.Print($"{CharacterLabel} took damage: {amount}");
        PlayAnimationForState(CharacterState.HitStun);
    }

    /// <summary>
    /// When a character dies, call the animation for being hit.
    /// </summary>
    protected override void OnDied()
    {
        GD.Print($"{CharacterLabel} died");
        PlayAnimationForState(CharacterState.Dead);
    }

    /// <summary>
    /// Plays the animation for the attack that is being performed.
    /// Spawns the hitbox for the attack.
    /// </summary>
    /// <param name="direction">Tracks the direction of the attack.</param>
    /// <param name="damage">Tracks the damage to be dealt.</param>
    protected override void OnAttackPerformed(AttackDirection direction, int damage)
    {
        _currentAttackDirection = direction;
        _isSpecial = false;

        GD.Print($"{CharacterLabel} attack: {direction}, damage: {damage}");
        PlayAnimationForState(CharacterState.Attack);
        EndAttackAfter(AttackRecovery);
        SpawnAttackHitbox(direction, damage);
    }

    /// <summary>
    /// Tracks the direction, sets the boolean to true, and uses the direction in a switch to handle behavior.
    /// Neutral represents a lack of direction, and heals Edward half a hit. Can be used twice per life.
    /// Up throws the halbered until it collides with something, then moves Edward to that point and resets velocity.
    /// Forces the character to be timed out for the recovery duration.
    /// </summary>
    /// <param name="direction">The direction of the attack.</param>
    /// <param name="damage">The damage the attack is capable of doing to an enemy.</param>
    protected override void OnSpecialPerformed(SpecialDirection direction, int damage)
    {
        _currentSpecialDirection = direction;
        _isSpecial = true;

        // Play animation AFTER setting _isSpecial
        PlayAnimationForState(CharacterState.Attack);

        switch (direction)
        {
            case SpecialDirection.Neutral:
                // Healing.
                int oldHp = CurrentHP;
                _healCount += 1;
                if (_healCount < 3)
                {
                    CurrentHP = Mathf.Min(MaxHP, CurrentHP + 2);
                    OnHealthChanged(oldHp, CurrentHP);
                    GD.Print($"{CharacterLabel} neutral special: heal 2");
                }

                // TODO: Once sound effect has been found, play error sound. 
                break;

            case SpecialDirection.Up:
                SpawnUpSpecialHalberd(damage);
                GD.Print($"{CharacterLabel} up special: halberd throw");
                break;
        }

        EndAttackAfter(AttackRecovery);
        _isSpecial = false;
    }

    /// <summary>
    /// Plays the animation for dodging.
    /// </summary>
    /// <param name="direction">The direction of the dodges movement.</param>
    /// <param name="dodgeDuration">The length of the dodge.</param>
    /// <param name="iFrameDuration">The amount of time that the character is invulnerable.</param>
    protected override void OnDodgeStarted(DodgeDirection direction, float dodgeDuration, float iFrameDuration)
    {
        PlayAnimationForState(CharacterState.Dodge);
        GD.Print($"{CharacterLabel} dodge start: {direction}, duration: {dodgeDuration}, iframes: {iFrameDuration}");
    }

    /// <summary>
    /// When dodge ends, play idle animation.
    /// </summary>
    /// <param name="dodgeCooldown"></param>
    protected override void OnDodgeEnded(float dodgeCooldown)
    {
        PlayAnimationForState(CharacterState.Idle);
    }

    /// <summary>
    /// Creates a timer and keeps the character in the attack state until after the timer expires.
    /// Resets teh tracking of the current hitbox.
    /// </summary>
    /// <param name="seconds">The attack cooldown timer length.</param> 
    private async void EndAttackAfter(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), "timeout");
        _currentHitbox = null;

        if (!IsDead && CurrentState == CharacterState.Attack)
            SetState(CharacterState.Idle);
    }

    /// <summary>
    /// Calls the hitbox scene and spawns in the hitbox in the direction of the attack.
    /// uses a switch to handle the different directions.
    /// </summary>
    /// <param name="dir">A nullable direction where the default is horizontal in the facing direction of the character.</param>
    /// <param name="damage">The amound of damage the attack will do.</param>
    private void SpawnAttackHitbox(AttackDirection? dir, int damage)
    {
        var hitbox = HitboxScene.Instantiate<Hitbox>();
        AddChild(hitbox); // local to Edward

        float facing = _edward.FlipH ? -1f : 1f;

        switch (dir)
        {
            case AttackDirection.Horizontal:
                hitbox.Position = new Vector2(50f * facing, 0f); // in front
                hitbox.RotationDegrees = 0f;
                hitbox.Activate(this, damage, AttackRecovery);

                break;

            case AttackDirection.Up:
                hitbox.Position = new Vector2(0f, -50f); // above
                hitbox.RotationDegrees = -90f; // adjust visually as needed
                hitbox.Activate(this, damage, AttackRecovery);
                break;

            case AttackDirection.DownAir:
                hitbox.Position = new Vector2(0f, 40f); // below
                hitbox.RotationDegrees = 90f;
                hitbox.Activate(this, damage, 0f);
                _downAirHitbox = hitbox;
                break;
        }

        _currentHitbox = hitbox;
    }

    /// <summary>
    /// A helper to spawn in the halbered scene and launch the halberd up for the up special.
    /// Tracks the despawn position globably to tween the character to it.
    /// </summary>
    /// <param name="damage">Tracks the damage the halberd can do.</param>
    private void SpawnUpSpecialHalberd(int damage)
    {
        var halberd = HalberdScene.Instantiate<Halberd>();

        // Add to world so it flies independently.
        GetParent().AddChild(halberd);
        halberd.GlobalPosition = GlobalPosition + new Vector2(0f, -20f);

        float facing = _edward.FlipH ? -1f : 1f;
        Vector2 throwDirection = new Vector2(facing, -1.2f).Normalized(); // ~20 deg up

        halberd.Launch(this, throwDirection, damage);

        // Connect despawn signal to teleport Edward.
        halberd.Despawned += OnHalberdDespawned;
    }

    /// <summary>
    /// When the halberd despawns, it moves Edward to the position of the despawn.
    /// When teleport is complete, play jump animation.
    /// </summary>
    /// <param name="despawnPosition">The global position where the halberd despawned.</param>
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
        PlayAnimationForState(CharacterState.Jump);
    }
}