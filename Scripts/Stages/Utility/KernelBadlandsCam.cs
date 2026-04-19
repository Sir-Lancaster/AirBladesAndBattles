using Godot;
using System.Collections.Generic;

public partial class KernelBadlandsCam : Camera2D
{
    [Export] private float MinX = 550f;
    [Export] private float MaxX = 1200f;
    [Export] private float MinY = 0f;
    [Export] private float MaxY = 850f;

    /// <summary>
    /// Offsets the camera vertically. Negative = camera moves up = less void visible below the stage.
    /// Tune this in the Inspector until the platform sits where you want it on screen.
    /// </summary>
    [Export] private float YOffset = -200f;

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

        GlobalPosition = new Vector2(
            Mathf.Clamp(center.X, MinX, MaxX),
            Mathf.Clamp(center.Y + YOffset, MinY, MaxY)
        );
    }
}
