using Godot;

public partial class KernelCowboy : CharacterBase
{
    private AnimatedSprite2D _KernelCowboy;
    private LassoHandler _lassoHandler;
    private bool _waitingForLassoPause;
    private Hitbox _stompHitbox;

    [Export] public string CharacterLabel = "KernelCowboy";
    [Export] public float AttackRecovery = 0.30f;
    [Export] public float SpecialAttackRecovery = 0.40f;
    [Export] public float NeutralSpecialCooldown = 2f;

    /// <summary>Frame of the Special animation to pause on while waiting for a lasso hit.</summary>
    [Export] public int LassoPauseFrame = 3;

    private float _neutralSpecialCooldownRemaining;

    private static readonly PackedScene HitboxScene =
        GD.Load<PackedScene>("res://Scenes/Utility/Hitbox.tscn");

    public override void _Ready()
    {
        _KernelCowboy = GetNode<AnimatedSprite2D>("KernelCowboy");

        _lassoHandler = GetNode<LassoHandler>("LassoHandler");

        // Neutral special: pause animation at throw frame, resume on connect or miss
        _lassoHandler.OnLassoConnected = () => _KernelCowboy.SpeedScale = 1.0f;
        _lassoHandler.OnLassoMissed    = () => { _KernelCowboy.SpeedScale = 1.0f; _KernelCowboy.Stop(); EndAttackAfter(SpecialAttackRecovery); _neutralSpecialCooldownRemaining = NeutralSpecialCooldown; };
        _lassoHandler.OnSlamComplete   = () => { _KernelCowboy.SpeedScale = 1.0f; EndAttackAfter(SpecialAttackRecovery); _neutralSpecialCooldownRemaining = NeutralSpecialCooldown; };
        _lassoHandler.OnSlamSplash     = SpawnSlamSplashHitbox;

        // Pause the Special animation only during a neutral special throw, and only once.
        _KernelCowboy.FrameChanged += () =>
        {
            if (_waitingForLassoPause && _KernelCowboy.Frame == LassoPauseFrame)
            {
                _waitingForLassoPause = false;
                _KernelCowboy.SpeedScale = 0.0f;
            }
        };

        // Down air: stomp hitbox spawns when lasso hooks the floor, shockwave spawns when character lands
        _lassoHandler.OnFloorHooked = SpawnStompHitbox;
        _lassoHandler.OnStompLanded = pos =>
        {
            // Kill the stomp hitbox the instant the character hits the ground
            if (_stompHitbox != null && IsInstanceValid(_stompHitbox))
                _stompHitbox.QueueFree();
            _stompHitbox = null;
            SpawnShockwaveHitboxes(pos);
        };
        _lassoHandler.OnDownAirComplete = () => EndAttackAfter(AttackRecovery);

        // Special up: end attack immediately so the player can steer during the launch
        _lassoHandler.OnRecoveryComplete = () => EndAttackAfter(0.05f);
        _lassoHandler.OnRecoveryHooked   = SpawnRecoveryHookHitbox;

        base._Ready();
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (Velocity.X != 0)
            _KernelCowboy.FlipH = Velocity.X < 0;

        if (_neutralSpecialCooldownRemaining > 0f)
            _neutralSpecialCooldownRemaining -= (float)delta;
    }

    protected override void PlayAnimationByName(string animName) => _KernelCowboy.Play(animName);

    protected override void PlayAnimationByName(string animName) => _KernelCowboy.Play(animName);

    protected override void PlayAnimationByName(string animName) => _KernelCowboy.Play(animName);

    protected override void PlayAnimationForState(CharacterState state)
    {
        string anim = state switch
        {
            CharacterState.Idle     => "Idle",
            CharacterState.Run      => "Run",
            CharacterState.Jump     => "Jump",
            CharacterState.Dodge    => "Dodge",
            CharacterState.HitStun  => "Hurt",
            CharacterState.Dead     => "dead",
            _                       => "Idle"
        };

        _KernelCowboy.Play(anim);
    }

    protected override void OnDamaged(int amount)
    {
        PlayAnimationForState(CharacterState.HitStun);
    }

    protected override void OnDied()
    {
        PlayAnimationForState(CharacterState.Dead);
    }

    protected override void OnAttackPerformed(AttackDirection direction, int damage)
    {
        string attackAnim = direction switch
        {
            AttackDirection.Up      => "Up",
            AttackDirection.DownAir => "Down",
            _                       => "Horizontal"
        };
        BroadcastAnimation(attackAnim);
        GD.Print($"{CharacterLabel} attack: {direction}, damage: {damage}");

        if (direction == AttackDirection.DownAir)
        {
            // Down air: lasso pulls attacker to target and stomps.
            // EndAttackAfter is driven by OnDownAirComplete callback, not here.
            _lassoHandler.LaunchDownAirLasso();
            return;
        }

        // Horizontal and Up attacks are whip hitboxes — end when animation finishes.
        EndAttackOnAnimationFinished();
        SpawnAttackHitbox(direction, damage);

        if (Multiplayer.MultiplayerPeer != null && IsMultiplayerAuthority())
        {
            float facing = _KernelCowboy.FlipH ? -1f : 1f;
            Rpc(nameof(SpawnWhipHitboxRpc), (int)direction, facing);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnWhipHitboxRpc(int dirInt, float facing)
    {
        var dir = (AttackDirection)dirInt;
        var hitbox = HitboxScene.Instantiate<Hitbox>();
        var shape = hitbox.GetNode<CollisionShape2D>("CollisionShape2D");

        switch (dir)
        {
            case AttackDirection.Horizontal:
                shape.Shape = MakeCapsule(30f, 100f);
                hitbox.Position = new Vector2(70f * facing, 40f);
                hitbox.RotationDegrees = 0f;
                break;
            case AttackDirection.Up:
                shape.Shape = MakeCapsule(45f, 100f);
                hitbox.Position = new Vector2(0f, -40f);
                hitbox.RotationDegrees = -90f;
                break;
        }

        AddChild(hitbox);
        hitbox.Monitoring = false; // visual-only on remote peers — damage is routed by the authority via ReceiveHitRpc
        GetTree().CreateTimer(AttackRecovery).Timeout += () => { if (IsInstanceValid(hitbox)) hitbox.QueueFree(); };
    }

    protected override void OnSpecialPerformed(SpecialDirection direction, int damage)
    {
        string specialAnim = direction == SpecialDirection.Up ? "UpSpecial" : "Special";
        BroadcastAnimation(specialAnim);

        switch (direction)
        {
            case SpecialDirection.Neutral:
                if (_neutralSpecialCooldownRemaining > 0f)
                {
                    EndAttackAfter(0f);
                    return;
                }
                float facing = _KernelCowboy.FlipH ? -1f : 1f;
                _waitingForLassoPause = true;
                _lassoHandler.LaunchLasso(facing);
                return; // EndAttackAfter is driven by LassoHandler callbacks, not here.

            case SpecialDirection.Up:
                // Recovery lasso: hooks above and launches owner upward.
                // If recovery is unavailable (already used mid-air), cancel immediately
                // so the Attack state doesn't get stuck with no callback to end it.
                if (!_lassoHandler.LaunchRecoveryLasso())
                {
                    EndAttackAfter(0f);
                    return;
                }
                return;
        }

        EndAttackAfter(SpecialAttackRecovery);
    }

    protected override void OnDodgeStarted(DodgeDirection direction, float dodgeDuration, float iFrameDuration)
    {
        PlayAnimationForState(CharacterState.Dodge);
    }

    protected override void OnDodgeEnded(float dodgeCooldown)
    {
        PlayAnimationForState(CharacterState.Idle);
    }

    // Waits for the currently playing animation to finish, then ends the attack state.
    // Falls back to a fixed timer for moves where the animation keeps looping (lasso hold, etc.)
    private async void EndAttackOnAnimationFinished()
    {
        await ToSignal(_KernelCowboy, AnimatedSprite2D.SignalName.AnimationFinished);

        if (!IsDead && CurrentState == CharacterState.Attack)
            SetState(CharacterState.Idle);
    }

    // Used by lasso callbacks where game logic (not animation) determines when the move ends.
    private async void EndAttackAfter(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

        if (!IsDead && CurrentState == CharacterState.Attack)
            SetState(CharacterState.Idle);
    }

    private static CapsuleShape2D MakeCapsule(float radius, float height)
    {
        var c = new CapsuleShape2D();
        c.Radius = radius;
        c.Height = height;
        return c;
    }

    private void SpawnAttackHitbox(AttackDirection? dir, int damage)
    {
        var hitbox = HitboxScene.Instantiate<Hitbox>();
        var shape = hitbox.GetNode<CollisionShape2D>("CollisionShape2D");

        float facing = _KernelCowboy.FlipH ? -1f : 1f;

        switch (dir)
        {
            case AttackDirection.Horizontal:
                shape.Shape = MakeCapsule(
                    /*radius (half-width of the hitbox) */ 30f,
                    /*height (reach in front of character) */ 100f);
                hitbox.Position = new Vector2(
                    /*how far in front of the character */ 70f * facing,
                    /*vertical offset (+ = down, - = up) */ 40f);
                hitbox.RotationDegrees = 0f;
                break;

            case AttackDirection.Up:
                shape.Shape = MakeCapsule(
                    /*radius */ 45f,
                    /*height (reach above character) */ 100f);
                hitbox.Position = new Vector2(
                    /*horizontal offset */ 0f,
                    /*how far above the character (keep negative) */ -40f);
                hitbox.RotationDegrees = -90f;
                break;
        }

        AddChild(hitbox);
        hitbox.Activate(this, damage, AttackRecovery);
    }

    // Spawns immediately when the lasso hooks the floor — positioned below the player's collision body.
    // Stays active until the character actually lands (freed manually by OnStompLanded callback).
    private void SpawnStompHitbox(Vector2 _)
    {
        _stompHitbox = HitboxScene.Instantiate<Hitbox>();
        var stompShape = _stompHitbox.GetNode<CollisionShape2D>("CollisionShape2D");
        stompShape.Shape = MakeCapsule(
            /*radius (how wide the stomp is) */ 45f,
            /*height (how tall the stomp hitbox is) */ 100f);
        AddChild(_stompHitbox);
        _stompHitbox.GlobalPosition = GlobalPosition + new Vector2(
            /*horizontal offset from character center */ 0f,
            /*how far below the collision body (keep positive) */ 40f);
        // Large lifetime so it never expires on its own — freed by OnStompLanded when character lands.
        _stompHitbox.Activate(this, _lassoHandler.StompDamage, 5f);
    }

    // Spawns when the character body arrives at the floor — left and right shockwave hitboxes.
    private void SpawnShockwaveHitboxes(Vector2 landingPos)
    {
        var shockLeft = HitboxScene.Instantiate<Hitbox>();
        var shockLeftShape = shockLeft.GetNode<CollisionShape2D>("CollisionShape2D");
        shockLeftShape.Shape = MakeCapsule(
            /*radius (height of shockwave zone) */ 13f,
            /*height (how far left the shockwave reaches) */ 120f);
        AddChild(shockLeft);
        shockLeft.GlobalPosition = landingPos + new Vector2(
            /*horizontal distance to the left (keep negative) */ -_lassoHandler.ShockwaveWidth * 1f,
            /*vertical offset from landing point */ 60f);
        shockLeft.RotationDegrees = -90f;
        shockLeft.Activate(this, _lassoHandler.ShockwaveDamage, _lassoHandler.ShockwaveLifetime);

        var shockRight = HitboxScene.Instantiate<Hitbox>();
        var shockRightShape = shockRight.GetNode<CollisionShape2D>("CollisionShape2D");
        shockRightShape.Shape = MakeCapsule(
            /*radius */ 13f,
            /*height */ 120f);
        AddChild(shockRight);
        shockRight.GlobalPosition = landingPos + new Vector2(
            /*horizontal distance to the right (keep positive) */ _lassoHandler.ShockwaveWidth * 1f,
            /*vertical offset from landing point */ 60f);
        shockRight.RotationDegrees = 90f;
        shockRight.Activate(this, _lassoHandler.ShockwaveDamage, _lassoHandler.ShockwaveLifetime);
    }

    // Spawns at the recovery hook point the instant the lasso connects — damages anyone caught in the impact zone.
    // Added to the stage (not KC) so it stays planted at the hook position while KC flies upward.
    private void SpawnRecoveryHookHitbox(Vector2 hookPos)
    {
        var hook = HitboxScene.Instantiate<Hitbox>();
        var hookShape = hook.GetNode<CollisionShape2D>("CollisionShape2D");
        hookShape.Shape = MakeCapsule(
            /*radius (how wide the impact zone is) */ 40f,
            /*height (how tall the impact zone is) */ 80f);
        GetParent().AddChild(hook);
        hook.GlobalPosition = hookPos;
        hook.Activate(this, _lassoHandler.RecoveryHookDamage, 2f);
    }

    // Spawns at the slam landing point — damages any bystander standing nearby when a grabbed target is slammed.
    // The grabbed target itself already takes full SlamDamage directly; this only hits others.
    private void SpawnSlamSplashHitbox(Vector2 landingPos)
    {
        var splash = HitboxScene.Instantiate<Hitbox>();
        var splashShape = splash.GetNode<CollisionShape2D>("CollisionShape2D");
        splashShape.Shape = MakeCapsule(
            /*radius (how tall the splash zone is vertically) */ 35f,
            /*height (how wide the splash zone is horizontally) */ 140f);
        AddChild(splash);
        splash.GlobalPosition = landingPos + new Vector2(
            /*horizontal offset from landing point */ 0f,
            /*vertical offset from landing point (0 = right at landing, negative = higher up) */ 60f);
        splash.RotationDegrees = 0f;
        splash.Activate(this, _lassoHandler.SlamSplashDamage, 0.12f);
    }
}
