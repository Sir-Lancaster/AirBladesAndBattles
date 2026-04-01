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
    [Export] public float SpecialAttackRecovery = 0.40f;

    private static readonly PackedScene HitboxScene =
        GD.Load<PackedScene>("res://Scenes/Utility/Hitbox.tscn");

    public override void _Ready()
    {
        _edward = GetNode<AnimatedSprite2D>("Edward");
        base._Ready();
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
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
                GD.Print($"{CharacterLabel} newutral special: heal 2");
                break;
            
            case SpecialDirection.Up:
                // placeholder for throw+teleport flow
                GD.Print($"{CharacterLabel} up special: TODO throw Halbered and teleport");
                break;
            
            case SpecialDirection.Horizontal:
                // pike Hitbox
                // SpawnSpecialHitbox(direction, damage)
                GD.Print($"{CharacterLabel} horixontal special: Pike");
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
}