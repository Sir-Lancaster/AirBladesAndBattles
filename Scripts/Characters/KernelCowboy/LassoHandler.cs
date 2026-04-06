using System;
using Godot;

/// <summary>
/// Handles all three lasso-based moves for KernelCowboy.
///   LaunchLasso()          — neutral special: snare enemy with lasso, arc over head, slam.
///   LaunchDownAirLasso()   — down air: lasso downward, pull self to target, stomp.
///   LaunchRecoveryLasso()  — special up: hook above, launch self upward.
/// Attach as a child node of KernelCowboy in the scene.
/// </summary>
public partial class LassoHandler : Node
{
    // ── Neutral special exports ───────────────────────────────────────────────

    /// <summary>Speed the neutral lasso travels horizontally (px/sec).</summary>
    [Export] public float LassoSpeed = 400f;

    /// <summary>Max horizontal distance before the lasso retracts.</summary>
    [Export] public float LassoRange = 250f;

    /// <summary>Peak height of the over-head arc.</summary>
    [Export] public float ArcHeight = 120f;

    /// <summary>Seconds to complete the full arc.</summary>
    [Export] public float ArcDuration = 0.6f;

    /// <summary>Damage on slam landing.</summary>
    [Export] public int SlamDamage = 20;

    /// <summary>How far behind the attacker the lasso target lands.</summary>
    [Export] public float LandingOffset = 80f; // NEEDS EDITING: tune to character size/art

    /// <summary>Collision radius of the neutral lasso tip.</summary>
    [Export] public float LassoRadius = 20f; // NEEDS EDITING: tune to lasso art

    // ── Down air exports ──────────────────────────────────────────────────────

    /// <summary>Speed the down-air lasso travels downward (px/sec).</summary>
    [Export] public float DownAirLassoSpeed = 600f;

    /// <summary>Max downward distance before the lasso gives up and fast-falls.</summary>
    [Export] public float DownAirLassoRange = 300f;

    /// <summary>Speed the attacker is pulled toward the lasso target (px/sec).</summary>
    [Export] public float DownAirPullSpeed = 900f;

    /// <summary>Damage dealt on stomp arrival.</summary>
    [Export] public int StompDamage = 15;

    /// <summary>Downward velocity applied when the lasso misses entirely.</summary>
    [Export] public float FastFallSpeed = 900f;

    /// <summary>Collision radius of the down-air lasso tip.</summary>
    [Export] public float DownAirLassoRadius = 20f; // NEEDS EDITING: tune to lasso art

    /// <summary>Damage dealt to enemies caught in the shockwave (not directly stomped).</summary>
    [Export] public int ShockwaveDamage = 6;

    /// <summary>How far to each side the shockwave extends from the landing point (px).</summary>
    [Export] public float ShockwaveWidth = 100f; // NEEDS EDITING: tune to feel

    /// <summary>Height of the shockwave hitbox.</summary>
    [Export] public float ShockwaveHeight = 30f; // NEEDS EDITING: tune to feel

    /// <summary>How long the shockwave hitbox lives in seconds.</summary>
    [Export] public float ShockwaveLifetime = 0.12f;

    // ── Recovery lasso exports ────────────────────────────────────────────────

    /// <summary>Raycast length upward to find a hookable surface.</summary>
    [Export] public float RecoveryLassoLength = 350f;

    /// <summary>Upward velocity applied when the hook finds a surface.</summary>
    [Export] public float RecoveryLaunchSpeed = 650f;

    /// <summary>Upward velocity applied when the hook finds nothing (partial recovery).</summary>
    [Export] public float RecoveryMissSpeed = 350f;

    // ── Callbacks ─────────────────────────────────────────────────────────────

    /// <summary>Fired when the neutral slam lands.</summary>
    public Action OnSlamComplete;

    /// <summary>Fired when the neutral lasso retracts without a hit.</summary>
    public Action OnLassoMissed;

    /// <summary>Fired when the down-air stomp lands or the lasso misses.</summary>
    public Action OnDownAirComplete;

    /// <summary>Fired immediately after the recovery lasso launches the owner.</summary>
    public Action OnRecoveryComplete;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>True while the neutral lasso arc is in progress.</summary>
    public bool IsLassoing { get; private set; }

    // ── Private state ─────────────────────────────────────────────────────────

    private CharacterBase _owner;

    // Neutral special
    private bool _lassoActive;
    private Area2D _lassoArea;
    private float _lassoTraveled;
    private Vector2 _lassoDirection;
    private CharacterBase _lassoTarget;
    private float _arcT;
    private Vector2 _arcStart;
    private Vector2 _arcEnd;

    // Down air
    private bool _downAirActive;
    private Area2D _downAirArea;
    private float _downAirTraveled;
    private bool _downAirPulling;
    private Vector2 _downAirTarget;
    private CharacterBase _downAirContact;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _owner = GetParent<CharacterBase>();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        TickNeutralLasso(dt);
        TickNeutralArc(dt);
        TickDownAirLasso(dt);
        TickDownAirPull(dt);
    }

    // ── Neutral special ───────────────────────────────────────────────────────

    /// <summary>
    /// Launches the horizontal lasso. facing: +1 right, -1 left.
    /// </summary>
    public void LaunchLasso(float facing)
    {
        if (_lassoActive || IsLassoing) return;

        _lassoDirection = new Vector2(facing, 0f);
        _lassoTraveled = 0f;
        _lassoActive = true;

        _lassoArea = CreateArea(LassoRadius);
        _lassoArea.GlobalPosition = _owner.GlobalPosition;
        _lassoArea.BodyEntered += OnNeutralBodyEntered;
        _owner.AddChild(_lassoArea);
    }

    private void TickNeutralLasso(float dt)
    {
        if (!_lassoActive || !IsInstanceValid(_lassoArea)) return;

        float move = LassoSpeed * dt;
        _lassoTraveled += move;
        _lassoArea.GlobalPosition += _lassoDirection * move;

        if (_lassoTraveled >= LassoRange)
        {
            FreeArea(ref _lassoArea);
            _lassoActive = false;
            OnLassoMissed?.Invoke();
        }
    }

    private void TickNeutralArc(float dt)
    {
        if (!IsLassoing || !IsInstanceValid(_lassoTarget)) return;

        _arcT = Mathf.Min(_arcT + dt / ArcDuration, 1f);

        float x = Mathf.Lerp(_arcStart.X, _arcEnd.X, _arcT);
        float y = Mathf.Lerp(_arcStart.Y, _arcEnd.Y, _arcT) - ArcHeight * Mathf.Sin(_arcT * Mathf.Pi);
        _lassoTarget.GlobalPosition = new Vector2(x, y);

        if (_arcT >= 1f)
            ExecuteSlam();
    }

    private void OnNeutralBodyEntered(Node2D body)
    {
        if (body is not CharacterBase target || target == _owner) return;

        FreeArea(ref _lassoArea);
        _lassoActive = false;

        _lassoTarget = target;
        IsLassoing = true;
        _arcT = 0f;
        _arcStart = target.GlobalPosition;
        _arcEnd = _owner.GlobalPosition + new Vector2(-_lassoDirection.X * LandingOffset, 0f);
    }

    private void ExecuteSlam()
    {
        IsLassoing = false;
        if (_lassoTarget != null && IsInstanceValid(_lassoTarget))
            _lassoTarget.TakeDamage(SlamDamage);
        _lassoTarget = null;
        OnSlamComplete?.Invoke();
    }

    // ── Down air ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Launches the lasso straight down. Pulls the attacker to whatever it hits,
    /// then stomps. If nothing is in range the attacker fast-falls instead.
    /// </summary>
    public void LaunchDownAirLasso()
    {
        if (_downAirActive || _downAirPulling) return;

        _downAirTraveled = 0f;
        _downAirActive = true;
        _downAirContact = null;

        _downAirArea = CreateArea(DownAirLassoRadius);
        _downAirArea.GlobalPosition = _owner.GlobalPosition;
        _downAirArea.BodyEntered += OnDownAirBodyEntered;
        _owner.AddChild(_downAirArea);
    }

    private void TickDownAirLasso(float dt)
    {
        if (!_downAirActive || !IsInstanceValid(_downAirArea)) return;

        float move = DownAirLassoSpeed * dt;
        _downAirTraveled += move;
        _downAirArea.GlobalPosition += Vector2.Down * move;

        if (_downAirTraveled >= DownAirLassoRange)
        {
            // Nothing found — just fast fall.
            FreeArea(ref _downAirArea);
            _downAirActive = false;
            _owner.Velocity = new Vector2(_owner.Velocity.X, FastFallSpeed);
            OnDownAirComplete?.Invoke();
        }
    }

    private void TickDownAirPull(float dt)
    {
        if (!_downAirPulling) return;

        Vector2 toTarget = _downAirTarget - _owner.GlobalPosition;
        float step = DownAirPullSpeed * dt;

        if (toTarget.Length() <= step)
        {
            _owner.GlobalPosition = _downAirTarget;
            _downAirPulling = false;

            if (_downAirContact != null && IsInstanceValid(_downAirContact))
                _downAirContact.TakeDamage(StompDamage);

            SpawnShockwave(_downAirTarget);

            _downAirContact = null;
            OnDownAirComplete?.Invoke();
        }
        else
        {
            _owner.GlobalPosition += toTarget.Normalized() * step;
        }
    }

    private void OnDownAirBodyEntered(Node2D body)
    {
        if (!_downAirActive || body == _owner) return;

        FreeArea(ref _downAirArea);
        _downAirActive = false;

        _downAirTarget = body.GlobalPosition;
        _downAirContact = body as CharacterBase;
        _downAirPulling = true;
    }

    // ── Recovery lasso (special up) ───────────────────────────────────────────

    /// <summary>
    /// Raycast upward. If a surface is found, launch the owner upward at full speed.
    /// If nothing is found, still apply a partial upward boost.
    /// </summary>
    public void LaunchRecoveryLasso()
    {
        var spaceState = _owner.GetWorld2D().DirectSpaceState;
        var query = PhysicsRayQueryParameters2D.Create(
            _owner.GlobalPosition,
            _owner.GlobalPosition + Vector2.Up * RecoveryLassoLength
        );
        query.Exclude = new Godot.Collections.Array<Rid> { _owner.GetRid() };

        var result = spaceState.IntersectRay(query);

        float launchSpeed = result.Count > 0 ? RecoveryLaunchSpeed : RecoveryMissSpeed;
        _owner.Velocity = new Vector2(_owner.Velocity.X, -launchSpeed);

        OnRecoveryComplete?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns two short-lived shockwave hitboxes going left and right from the landing point.
    /// Each one checks for any overlapping CharacterBase after one physics frame and deals
    /// ShockwaveDamage. This excludes the direct stomp contact who already took full damage.
    /// </summary>
    private void SpawnShockwave(Vector2 landingPoint)
    {
        SpawnShockwaveSide(landingPoint, -1f); // left
        SpawnShockwaveSide(landingPoint,  1f); // right
    }

    private async void SpawnShockwaveSide(Vector2 landingPoint, float side)
    {
        var area = new Area2D();
        var shape = new CollisionShape2D();
        var rect = new RectangleShape2D();
        rect.Size = new Vector2(ShockwaveWidth, ShockwaveHeight);
        shape.Shape = rect;
        area.AddChild(shape);

        // Center each box so its inner edge starts at the landing point.
        area.GlobalPosition = landingPoint + new Vector2(side * ShockwaveWidth * 0.5f, 0f);
        _owner.AddChild(area);

        // Wait one physics frame so Godot registers overlaps.
        await ToSignal(_owner.GetTree(), "physics_frame");

        if (!IsInstanceValid(area)) return;

        foreach (var body in area.GetOverlappingBodies())
        {
            if (body is CharacterBase target && target != _owner && target != _downAirContact)
                target.TakeDamage(ShockwaveDamage);
        }

        await _owner.GetTree().CreateTimer(ShockwaveLifetime).ToSignal("timeout");
        if (IsInstanceValid(area))
            area.QueueFree();
    }

    private Area2D CreateArea(float radius)
    {
        var area = new Area2D();
        var shape = new CollisionShape2D();
        var circle = new CircleShape2D();
        circle.Radius = radius;
        shape.Shape = circle;
        area.AddChild(shape);
        return area;
    }

    private void FreeArea(ref Area2D area)
    {
        if (area != null && IsInstanceValid(area))
        {
            area.QueueFree();
            area = null;
        }
    }
}
