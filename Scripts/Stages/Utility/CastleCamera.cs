using Godot;
using System.Collections.Generic;

public partial class CastleCamera : Camera2D
{
    private const float MinX = 600f;
    private const float MaxX = 2900f;
    private const float MinY = 0f;
    private const float MaxY = 1200f;

    private const float MinZoom = 0.5f;
    private const float MaxZoom = 1.0f;
    private const float Padding = 300f;
    private const float ZoomSpeed = 2.0f;
    private const float PositionSpeed = 6.0f;

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

        if (targets.Count == 0)
            return;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var pos in targets)
        {
            if (pos.X < minX) minX = pos.X;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.Y > maxY) maxY = pos.Y;
        }

        Vector2 center = new((minX + maxX) / 2f, (minY + maxY) / 2f);
        Vector2 targetPos = new(
            Mathf.Clamp(center.X, MinX, MaxX),
            Mathf.Clamp(center.Y, MinY, MaxY)
        );

        GlobalPosition = GlobalPosition.Lerp(targetPos, (float)delta * PositionSpeed);

        float spreadX = (maxX - minX) + Padding * 2f;
        float spreadY = (maxY - minY) + Padding * 2f;
        Vector2 viewport = GetViewportRect().Size;

        float targetZoom = Mathf.Clamp(Mathf.Min(viewport.X / spreadX, viewport.Y / spreadY), MinZoom, MaxZoom);
        float newZoom = Mathf.Lerp(Zoom.X, targetZoom, (float)delta * ZoomSpeed);
        Zoom = new Vector2(newZoom, newZoom);
    }
}
