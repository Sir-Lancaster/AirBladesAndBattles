using Godot;

public partial class KernelCowboy : CharacterBase
{
    private AnimatedSprite2D _KernelCowboy; // NEEDS EDITING: replace "KernelCowboy" node name below in _Ready()
    private AttackDirection _currentAttackDirection;
    private SpecialDirection _currentSpecialDirection;
    private bool _isSpecial;
    private Area2D _currentHitbox;
    private LassoHandler _lassoHandler;

    [Export] public string CharacterLabel = "KernelCowboy";
    [Export] public float AttackRecovery = 0.30f;       // NEEDS EDITING: tune to match attack animation length
    [Export] public float SpecialAttackRecovery = 0.40f; // NEEDS EDITING: tune to match special animation length

    private static readonly PackedScene HitboxScene =
        GD.Load<PackedScene>("res://Scenes/Utility/Hitbox.tscn");

    public override void _Ready()
    {
        _KernelCowboy = GetNode<AnimatedSprite2D>("KernelCowboy"); // NEEDS EDITING: use the actual node name from your scene
        GD.Print("Animations: ", string.Join(", ", _KernelCowboy.SpriteFrames.GetAnimationNames()));

        _lassoHandler = new LassoHandler();
        AddChild(_lassoHandler);

        // Neutral special: arc slam or miss
        _lassoHandler.OnSlamComplete   = () => EndAttackAfter(SpecialAttackRecovery);
        _lassoHandler.OnLassoMissed    = () => EndAttackAfter(SpecialAttackRecovery);

        // Down air: stomp landing or fast-fall
        _lassoHandler.OnDownAirComplete = () => EndAttackAfter(AttackRecovery);

        // Special up: end attack immediately so the player can steer during the launch
        _lassoHandler.OnRecoveryComplete = () => EndAttackAfter(0.05f);

        base._Ready();
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (Velocity.X != 0)
            _KernelCowboy.FlipH = Velocity.X < 0;

        // Debug: press 'delete' to take 10 damage
        if (Input.IsPhysicalKeyPressed(Key.Delete))
        {
            TakeDamage(10);
            GD.Print($"{CharacterLabel} DEBUG: TakeDamage(10) called");
        }
    }

    protected override void OnStateChanged(CharacterState fromState, CharacterState toState)
    {
        GD.Print($"{CharacterLabel} state: {fromState} -> {toState}");
    }

    protected override void PlayAnimationForState(CharacterState state)
    {
        string anim = state switch
        {
            CharacterState.Idle     => "Idle",      
            CharacterState.Run      => "Run",        
            CharacterState.Jump     => "Jump",      
            CharacterState.Dodge    => "Dodge",      // NEEDS EDITING
            CharacterState.Attack   => _isSpecial
                ? $"special_{_currentSpecialDirection.ToString().ToLower()}" // NEEDS EDITING: if your special anims are named differently
                : $"attack_{_currentAttackDirection.ToString().ToLower()}",  // NEEDS EDITING: if your attack anims are named differently
            CharacterState.HitStun  => "hitstun",   // NEEDS EDITING
            CharacterState.Dead     => "dead",       // NEEDS EDITING
            _                       => "Idle"
        };

        _KernelCowboy.Play(anim);
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

        if (direction == AttackDirection.DownAir)
        {
            // Down air: lasso pulls attacker to target and stomps.
            // EndAttackAfter is driven by OnDownAirComplete callback, not here.
            _lassoHandler.LaunchDownAirLasso();
            return;
        }

        // Horizontal and Up attacks are whip hitboxes.
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
                float facing = _KernelCowboy.FlipH ? -1f : 1f;
                _lassoHandler.LaunchLasso(facing);
                GD.Print($"{CharacterLabel} neutral special: lasso launched");
                return; // EndAttackAfter is driven by LassoHandler callbacks, not here.

            case SpecialDirection.Up:
                // Recovery lasso: hooks above and launches owner upward.
                // EndAttackAfter is driven by OnRecoveryComplete callback, not here.
                _lassoHandler.LaunchRecoveryLasso();
                GD.Print($"{CharacterLabel} up special: recovery lasso launched");
                return;

            case SpecialDirection.Horizontal:
                // NEEDS EDITING: implement KernelCowboy's horizontal special
                GD.Print($"{CharacterLabel} horizontal special: TODO");
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
        GD.Print($"{CharacterLabel} dodge end, cooldown: {dodgeCooldown}");
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
        AddChild(hitbox);
        hitbox.Activate(this, damage, AttackRecovery);

        float facing = _KernelCowboy.FlipH ? -1f : 1f;

        switch (dir)
        {
            case AttackDirection.Horizontal:
                hitbox.Position = new Vector2(40f * facing, 0f); // NEEDS EDITING: adjust offset to fit your character's size/art
                hitbox.RotationDegrees = 0f;
                break;

            case AttackDirection.Up:
                hitbox.Position = new Vector2(0f, -40f); // NEEDS EDITING: adjust offset
                hitbox.RotationDegrees = -90f;
                break;

            case AttackDirection.DownAir:
                hitbox.Position = new Vector2(0f, 40f); // NEEDS EDITING: adjust offset
                hitbox.RotationDegrees = 90f;
                break;
        }

        _currentHitbox = hitbox;
    }
}