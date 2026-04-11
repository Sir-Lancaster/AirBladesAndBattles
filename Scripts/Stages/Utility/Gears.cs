using Godot;

public partial class Gears : Node2D
{
    [Export] public bool RotateClockwise = true;

    public override void _Ready()
    {
        var fg = GetNode<Gear>("gear1");
        var bg = GetNode<Gear>("gearinside1");

        fg.RotateClockwise = RotateClockwise;
        bg.RotateClockwise = RotateClockwise;
    }
}
