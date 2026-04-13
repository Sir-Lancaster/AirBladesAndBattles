using Godot;
using System.Collections.Generic;

public partial class Camera2d : Camera2D
{
    private const float MinX = 550f;
    private const float MaxX = 1200f;
    private const float MinY = 0f;
    private const float MaxY = 850f;

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

        Vector2 center = Vector2.Zero;
        foreach (Vector2 pos in targets)
            center += pos;
        center /= targets.Count;

        GlobalPosition = new Vector2(
            Mathf.Clamp(center.X, MinX, MaxX),
            Mathf.Clamp(center.Y, MinY, MaxY)
        );
    }
}
