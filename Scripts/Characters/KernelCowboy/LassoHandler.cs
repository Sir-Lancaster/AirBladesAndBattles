using System;
using Godot;

/// <summary>
/// Handles all three lasso-based moves for KernelCowboy.
///   LaunchLasso()          — neutral special: snare enemy with lasso, arc over head, slam.
///   LaunchDownAirLasso()   — down air: lasso downward, pull self to target, stomp.
///   LaunchRecoveryLasso()  — special up: hook above, launch self upward.
/// Attach as a child node of KernelCowboy in the scene.
///
/// Visual setup (set these in the Godot editor):
///   HandAnchor  — Marker2D placed at the character's wrist/hand in the scene
///   LassoHead   — Sprite2D (your lasso loop tip sprite), child of the scene root
///   LassoRope   — Line2D with at least 3 points, child of the scene root
/// </summary>
public partial class LassoHandler : Node
{
    // ── Neutral special exports ───────────────────────────────────────────────

    /// <summary>Speed the neutral lasso travels horizontally (px/sec).</summary>
    [Export] public float LassoSpeed = 400f;

    /// <summary>Max horizontal distance before the lasso retracts.</summary>
    [Export] public float LassoRange = 250f;

    /// <summary>Peak height of the over-head arc. Should be at least 2x the tallest character's height.</summary>
    [Export] public float ArcHeight = 160f;

    /// <summary>Seconds to complete the full arc.</summary>
    [Export] public float ArcDuration = 0.6f;

    /// <summary>Speed multiplier applied after the arc peak (arcT > 0.5). 1 = constant, 2 = twice as fast on the way down.</summary>
    [Export] public float SlamSpeedMultiplier = 2.0f;

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

    /// <summary>Upward velocity applied on recovery launch.</summary>
    [Export] public float RecoveryLaunchSpeed = 650f;

    // ── Visual exports ────────────────────────────────────────────────────────

    /// <summary>
    /// Marker2D placed at the character's hand/wrist in the scene.
    /// The rope Line2D starts from this point.
    /// </summary>
    [Export] public Marker2D HandAnchor;

    /// <summary>
    /// Sprite2D representing the lasso loop/hook tip.
    /// Moves to match the lasso head position each frame.
    /// </summary>
    [Export] public Node2D LassoHead;

    /// <summary>
    /// Line2D used to draw the rope. Needs exactly 3 points (set up in editor or created here).
    /// Point 0 = anchor, Point 1 = midpoint (sag), Point 2 = lasso head.
    /// </summary>
    [Export] public Line2D LassoRope;

    /// <summary>Max downward sag of the rope at its midpoint (px). Fades to 0 when taut.</summary>
    [Export] public float RopeSag = 25f;

    /// <summary>Seconds for the recovery lasso head to fly to the hook point (visual only).</summary>
    [Export] public float RecoveryFlyTime = 0.12f;

    /// <summary>Seconds for the recovery lasso head to retract back (visual only).</summary>
    [Export] public float RecoveryRetractTime = 0.08f;

    // ── Callbacks ─────────────────────────────────────────────────────────────

    /// <summary>Fired when the neutral slam lands.</summary>
    public Action OnSlamComplete;

    /// <summary>Fired when the neutral lasso retracts without a hit.</summary>
    public Action OnLassoMissed;

    /// <summary>Fired the moment the neutral lasso snares a target (before the arc begins).</summary>
    public Action OnLassoConnected;

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

    // Visual
    private Vector2 _headPos;      // world-space position of the lasso tip this frame
    private bool _ropeVisible;     // whether to draw the rope at all

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        _owner = GetParent<CharacterBase>();

        // Ensure the Line2D has exactly 3 points so we can index them directly.
        if (LassoRope != null)
        {
            while (LassoRope.GetPointCount() < 3)
                LassoRope.AddPoint(Vector2.Zero);
            LassoRope.Visible = false;
        }

        if (LassoHead != null)
            LassoHead.Visible = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        TickNeutralLasso(dt);
        TickNeutralArc(dt);
        TickDownAirLasso(dt);
        TickDownAirPull(dt);
        UpdateRopeVisual();
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
        _lassoArea.BodyEntered += OnNeutralBodyEntered;
        _owner.AddChild(_lassoArea);
        _lassoArea.GlobalPosition = _owner.GlobalPosition;

        _headPos = AnchorPos();
        _ropeVisible = true;
    }

    private void TickNeutralLasso(float dt)
    {
        if (!_lassoActive || !IsInstanceValid(_lassoArea)) return;

        float move = LassoSpeed * dt;
        _lassoTraveled += move;
        _lassoArea.GlobalPosition += _lassoDirection * move;

        // Keep visual head in sync with the hitbox.
        _headPos = _lassoArea.GlobalPosition;

        if (_lassoTraveled >= LassoRange)
        {
            FreeArea(ref _lassoArea);
            _lassoActive = false;
            _ropeVisible = false;
            OnLassoMissed?.Invoke();
        }
    }

    private void TickNeutralArc(float dt)
    {
        if (!IsLassoing) return;
        if (!IsInstanceValid(_lassoTarget))
        {
            IsLassoing = false;
            _ropeVisible = false;
            return;
        }

        float speed = _arcT >= 0.5f ? SlamSpeedMultiplier : 1.0f;
        _arcT = Mathf.Min(_arcT + dt / ArcDuration * speed, 1f);

        float x = Mathf.Lerp(_arcStart.X, _arcEnd.X, _arcT);
        float y = Mathf.Lerp(_arcStart.Y, _arcEnd.Y, _arcT) - ArcHeight * Mathf.Sin(_arcT * Mathf.Pi);
        _lassoTarget.GlobalPosition = new Vector2(x, y);

        // Rope follows the captured target during the arc.
        _headPos = _lassoTarget.GlobalPosition;

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

        // Freeze the target so gravity and MoveAndSlide don't fight our arc positioning.
        _lassoTarget.SetPhysicsProcess(false);

        OnLassoConnected?.Invoke();
    }

    private void ExecuteSlam()
    {
        IsLassoing = false;
        _ropeVisible = false;

        if (_lassoTarget != null && IsInstanceValid(_lassoTarget))
        {
            // Re-enable physics before dealing damage so hitstun/knockback work normally.
            _lassoTarget.SetPhysicsProcess(true);
            _lassoTarget.TakeDamage(SlamDamage);
        }
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
        _downAirArea.BodyEntered += OnDownAirBodyEntered;
        _owner.AddChild(_downAirArea);
        _downAirArea.GlobalPosition = _owner.GlobalPosition;

        _headPos = AnchorPos();
        _ropeVisible = true;
    }

    private void TickDownAirLasso(float dt)
    {
        if (!_downAirActive || !IsInstanceValid(_downAirArea)) return;

        float move = DownAirLassoSpeed * dt;
        _downAirTraveled += move;
        _downAirArea.GlobalPosition += Vector2.Down * move;

        _headPos = _downAirArea.GlobalPosition;

        if (_downAirTraveled >= DownAirLassoRange)
        {
            // Nothing found — just fast fall.
            FreeArea(ref _downAirArea);
            _downAirActive = false;
            _ropeVisible = false;
            _owner.Velocity = new Vector2(_owner.Velocity.X, FastFallSpeed);
            OnDownAirComplete?.Invoke();
        }
    }

    private void TickDownAirPull(float dt)
    {
        if (!_downAirPulling) return;

        Vector2 toTarget = _downAirTarget - _owner.GlobalPosition;
        float step = DownAirPullSpeed * dt;

        // Rope head is fixed at the hook point while the owner flies to it.
        _headPos = _downAirTarget;

        if (toTarget.Length() <= step)
        {
            _owner.GlobalPosition = _downAirTarget;
            _downAirPulling = false;
            _ropeVisible = false;

            if (_downAirContact != null && IsInstanceValid(_downAirContact))
                _downAirContact.TakeDamage(StompDamage);

            SpawnShockwave(_downAirTarget);

            _downAirContact = null;
            OnDownAirComplete?.Invoke();
        }
        else
        {
            // Zero velocity so MoveAndSlide doesn't fight our direct position update.
            _owner.Velocity = Vector2.Zero;
            _owner.GlobalPosition += toTarget.Normalized() * step;
        }
    }

    private void OnDownAirBodyEntered(Node2D body)
    {
        if (!_downAirActive || body == _owner) return;

        // Only pull toward CharacterBase targets. Hitting the floor or walls fast-falls instead.
        if (body is not CharacterBase contact)
        {
            FreeArea(ref _downAirArea);
            _downAirActive = false;
            _ropeVisible = false;
            _owner.Velocity = new Vector2(_owner.Velocity.X, FastFallSpeed);
            OnDownAirComplete?.Invoke();
            return;
        }

        FreeArea(ref _downAirArea);
        _downAirActive = false;

        _downAirTarget = contact.GlobalPosition;
        _downAirContact = contact;
        _downAirPulling = true;
    }

    // ── Recovery lasso (special up) ───────────────────────────────────────────

    /// <summary>
    /// Always launches the owner upward at full speed — no miss case.
    /// The rope plays a cosmetic tween to the top of the lasso range.
    /// </summary>
    public void LaunchRecoveryLasso()
    {
        Vector2 hookPoint = _owner.GlobalPosition + Vector2.Up * RecoveryLassoLength;
        _owner.Velocity = new Vector2(_owner.Velocity.X, -RecoveryLaunchSpeed);

        OnRecoveryComplete?.Invoke();

        // Cosmetic tween: head flies to hook then retracts.
        AnimateRecoveryRope(hookPoint);
    }

    private async void AnimateRecoveryRope(Vector2 hookPoint)
    {
        _headPos = AnchorPos();
        _ropeVisible = true;

        // Phase 1: fly to hook.
        float elapsed = 0f;
        Vector2 start = AnchorPos();
        while (elapsed < RecoveryFlyTime)
        {
            await ToSignal(_owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
            elapsed += (float)_owner.GetPhysicsProcessDeltaTime();
            _headPos = start.Lerp(hookPoint, Mathf.Min(elapsed / RecoveryFlyTime, 1f));
        }
        _headPos = hookPoint;

        // Phase 2: retract back to anchor.
        elapsed = 0f;
        Vector2 retractStart = hookPoint;
        while (elapsed < RecoveryRetractTime)
        {
            await ToSignal(_owner.GetTree(), SceneTree.SignalName.PhysicsFrame);
            elapsed += (float)_owner.GetPhysicsProcessDeltaTime();
            _headPos = retractStart.Lerp(AnchorPos(), Mathf.Min(elapsed / RecoveryRetractTime, 1f));
        }

        _ropeVisible = false;
    }

    // ── Visual ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called every physics frame. Syncs LassoHead position and redraws LassoRope
    /// between the hand anchor and the current lasso head position.
    /// When nothing is active the nodes are hidden.
    /// </summary>
    private void UpdateRopeVisual()
    {
        bool hasNodes = HandAnchor != null && LassoHead != null && LassoRope != null;
        if (!hasNodes) return;

        if (!_ropeVisible)
        {
            LassoHead.Visible = false;
            LassoRope.Visible = false;
            return;
        }

        Vector2 anchor = AnchorPos();

        LassoHead.GlobalPosition = _headPos;
        LassoHead.Visible = true;

        // Sag scales with how much rope is "out" relative to total distance,
        // so it looks slack when close and taut when the rope is fully extended.
        float dist = anchor.DistanceTo(_headPos);
        float sagScale = Mathf.Clamp(dist / LassoRange, 0f, 1f);
        Vector2 midGlobal = (anchor + _headPos) * 0.5f + Vector2.Down * RopeSag * sagScale;

        // Line2D points are in the node's local space, so convert from global.
        LassoRope.SetPointPosition(0, LassoRope.ToLocal(anchor));
        LassoRope.SetPointPosition(1, LassoRope.ToLocal(midGlobal));
        LassoRope.SetPointPosition(2, LassoRope.ToLocal(_headPos));
        LassoRope.Visible = true;
    }

    /// <summary>Returns the hand anchor world position, falling back to the owner position if unset.</summary>
    private Vector2 AnchorPos() =>
        HandAnchor != null ? HandAnchor.GlobalPosition : _owner.GlobalPosition;

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
        await ToSignal(_owner.GetTree(), SceneTree.SignalName.PhysicsFrame);

        if (!IsInstanceValid(area)) return;

        foreach (var body in area.GetOverlappingBodies())
        {
            if (body is CharacterBase target && target != _owner && target != _downAirContact)
                target.TakeDamage(ShockwaveDamage);
        }

        await ToSignal(_owner.GetTree().CreateTimer(ShockwaveLifetime), SceneTreeTimer.SignalName.Timeout);
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
