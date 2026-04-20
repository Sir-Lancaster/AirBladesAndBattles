using Godot;
using System.Collections.Generic;

public partial class KernelBadlandsCam : Camera2D
{
    [Export] private float MinX = 550f;
    [Export] private float MaxX = 1200f;
    [Export] private float MinY = 0f;
    [Export] private float MaxY = 850f;

    /// <summary>Negative = camera moves up = less void visible below the stage.</summary>
    [Export] private float YOffset = -200f;

    /// <summary>Zoom when all players are on top of each other.</summary>
    [Export] private float ZoomClose = 1.0f;
    /// <summary>Zoom when players are at maximum spread.</summary>
    [Export] private float ZoomFar = 0.5f;
    /// <summary>Player spread in pixels that maps to ZoomFar.</summary>
    [Export] private float ZoomSpreadRange = 600f;
    /// <summary>How fast zoom lerps to its target each second during normal play.</summary>
    [Export] private float ZoomSpeed = 3f;
    /// <summary>Zoom level when only one player remains.</summary>
    [Export] private float DeathZoomClose = 1.5f;
    /// <summary>How fast the camera zooms in on the last survivor.</summary>
    [Export] private float DeathZoomSpeed = 6f;

    public override void _Process(double delta)
    {
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
            // Last player alive — follow them directly, no stage boundary clamp.
            GlobalPosition = center;
            targetZoom = DeathZoomClose;
        }
        else
        {
            // Multiple players — clamp to stage bounds and zoom out by spread.
            GlobalPosition = new Vector2(
                Mathf.Clamp(center.X, MinX, MaxX),
                Mathf.Clamp(center.Y + YOffset, MinY, MaxY)
            );

            float spread = 0f;
            foreach (Vector2 pos in targets)
                spread = Mathf.Max(spread, center.DistanceTo(pos));

            float t = Mathf.Clamp(spread / (ZoomSpreadRange * 0.5f), 0f, 1f);
            targetZoom = Mathf.Lerp(ZoomClose, ZoomFar, t);
        }

        float speed    = targets.Count == 1 ? DeathZoomSpeed : ZoomSpeed;
        float smoothed = Mathf.Lerp(Zoom.X, targetZoom, speed * (float)delta);
        Zoom = new Vector2(smoothed, smoothed);
    }
}
