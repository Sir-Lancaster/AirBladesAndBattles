using Godot;
using System.Collections.Generic;

public partial class KernelBadlandsCam : Camera2D
{
    /// <summary>World X of the left stage edge. The left viewport edge will never go past this.</summary>
    [Export] private float LeftEdgeX = 0f;
    /// <summary>World X of the right stage edge. The right viewport edge will never go past this.</summary>
    [Export] private float RightEdgeX = 1920f;
    /// <summary>World Y of the top boundary. The camera center will never go above this.</summary>
    [Export] private float MinY = 0f;
    /// <summary>World Y of the floor surface. The bottom viewport edge will never go below this.</summary>
    [Export] private float FloorY = 850f;

    /// <summary>Max player spread (px) at which the camera is fully zoomed in.</summary>
    [Export] private float CloseDistance = 200f;
    /// <summary>Zoom when players are within CloseDistance.</summary>
    [Export] private float ZoomClose = 1.5f;
    /// <summary>Default zoom when players are at a normal distance (further than CloseDistance).</summary>
    [Export] private float ZoomDefault = 0.9f;
    /// <summary>Zoom when players are very far apart and the viewport math needs to show both.</summary>
    [Export] private float ZoomFar = 0.5f;
    /// <summary>World-unit margin added around players when calculating zoom.</summary>
    [Export] private float Padding = 220f;
    /// <summary>Extra downward shift (px) added at full close zoom so more floor is visible.</summary>
    [Export] private float FloorBias = 80f;
    /// <summary>Seconds ahead the camera leads based on center movement speed.</summary>
    [Export] private float LookAheadTime = 0.35f;
    /// <summary>How fast the look-ahead offset lerps to its target.</summary>
    [Export] private float LookAheadSmooth = 4f;
    /// <summary>How fast zoom lerps to its target each second.</summary>
    [Export] private float ZoomSpeed = 3f;
    /// <summary>Zoom level when only one player remains.</summary>
    [Export] private float DeathZoomClose = 1.5f;
    /// <summary>How fast the camera zooms in on the last survivor.</summary>
    [Export] private float DeathZoomSpeed = 6f;

    private Vector2 _prevCenter;
    private Vector2 _lookAhead;
    private bool    _prevCenterSet;

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        var targets = new List<Vector2>();
        foreach (Node node in GetTree().GetNodesInGroup("characters"))
        {
            if (node is CharacterBase cb && !cb.IsDead)
                targets.Add(cb.GlobalPosition);
            else if (node is AiBaseClass ai && !ai.IsDead)
                targets.Add(ai.GlobalPosition);
        }

        if (targets.Count == 0) return;

        Vector2 center = Vector2.Zero;
        foreach (Vector2 pos in targets) center += pos;
        center /= targets.Count;

        float targetZoom;

        if (targets.Count == 1)
        {
            _prevCenter    = center;
            _prevCenterSet = true;
            GlobalPosition = center;
            targetZoom     = DeathZoomClose;
        }
        else
        {
            // ── Look-ahead ────────────────────────────────────────────────────
            if (!_prevCenterSet) { _prevCenter = center; _prevCenterSet = true; }
            Vector2 centerVel = (center - _prevCenter) / dt;
            _prevCenter = center;
            _lookAhead  = _lookAhead.Lerp(centerVel * LookAheadTime, LookAheadSmooth * dt);

            // ── Zoom ──────────────────────────────────────────────────────────
            float spread = 0f;
            foreach (Vector2 pos in targets)
                spread = Mathf.Max(spread, pos.DistanceTo(center));

            if (spread <= CloseDistance)
            {
                targetZoom = ZoomClose;
            }
            else
            {
                // Viewport math: zoom needed to fit all players.
                Vector2 vp   = GetViewport().GetVisibleRect().Size;
                float halfW  = 0f, halfH = 0f;
                foreach (Vector2 pos in targets)
                {
                    halfW = Mathf.Max(halfW, Mathf.Abs(pos.X - center.X));
                    halfH = Mathf.Max(halfH, Mathf.Abs(pos.Y - center.Y));
                }
                halfW += Padding;
                halfH += Padding;

                float vpZoom   = Mathf.Min(vp.X / (2f * halfW), vp.Y / (2f * halfH));
                // Use ZoomDefault unless players are so far apart we must zoom out further.
                targetZoom = Mathf.Clamp(Mathf.Min(ZoomDefault, vpZoom), ZoomFar, ZoomClose);
            }

            // ── Floor bias ────────────────────────────────────────────────────
            float zoomT = Mathf.InverseLerp(ZoomFar, ZoomClose, targetZoom);
            float biasY = FloorBias * zoomT;

            // ── Position with viewport-edge constraints ───────────────────────
            Vector2 viewport = GetViewport().GetVisibleRect().Size;
            float halfVW     = viewport.X / 2f / Zoom.X;
            float halfVH     = viewport.Y / 2f / Zoom.X;
            float leftMinX   = LeftEdgeX  + halfVW;
            float rightMaxX  = RightEdgeX - halfVW;
            float floorMaxY  = FloorY     - halfVH;

            GlobalPosition = new Vector2(
                Mathf.Clamp(center.X + _lookAhead.X,         leftMinX, rightMaxX),
                Mathf.Clamp(center.Y + _lookAhead.Y + biasY, MinY,     floorMaxY)
            );
        }

        float speed    = targets.Count == 1 ? DeathZoomSpeed : ZoomSpeed;
        float smoothed = Mathf.Lerp(Zoom.X, targetZoom, speed * dt);
        Zoom = new Vector2(smoothed, smoothed);
    }
}
