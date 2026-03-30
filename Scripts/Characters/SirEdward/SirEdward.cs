using Godot;

public partial class SirEdward: CharacterBase
{
    [Export] public string CharacterLabel = "SirEdward";
    [Export] public float AttackRecovery = 0.30f;
    [Export] public float SpecialAttackRecovery = 0.40f;

    protected override void OnStateChanged(CharacterState fromState, CharacterState toState)
    {
        GD.Print($"{CharacterLabel} state: {fromState} -> {toState}");
    }

    protected override void PlayAnimationForState(CharacterState state)
    {
        GD.Print($"{CharacterLabel} play animation for: {state}");
    }

    protected override void OnHealthChanged(int oldHp, int newHp)
    {
        GD.Print($"{CharacterLabel} HP: {oldHp} -> {newHp}");
    }

    protected override void OnDamaged(int amount)
    {
        GD.Print($"{CharacterLabel} took damage: {amount}");
    }

    protected override void OnDied()
    {
        GD.Print($"{CharacterLabel} died");
    }

    protected override void OnAttackPerformed(AttackDirection direction, int damage)
    {
        GD.Print($"{CharacterLabel} attack: {direction}, damage: {damage}");
        EndAttackAfter(AttackRecovery);
    }

    protected override void OnSpecialPerformed(SpecialDirection direction, int damage)
    {
        GD.Print($"{CharacterLabel} special: {direction}, damage: {damage}");
        EndAttackAfter(SpecialAttackRecovery);
    }

    protected override void OnDodgeStarted(DodgeDirection direction, float dodgeDuration, float iFrameDuration)
    {
        GD.Print($"{CharacterLabel} dodge start: {direction}, duration: {dodgeDuration}, iframes: {iFrameDuration}");
    }

    protected override void OnDodgeEnded(float dodgeCooldown)
    {
        GD.Print($"{CharacterLabel} dodge end, cooldown: {dodgeCooldown}");
    }

    private async void EndAttackAfter(float seconds)
    {
        await ToSignal(GetTree().CreateTimer(seconds), "timeout");
        if (!IsDead && CurrentState == CharacterState.Attack)
            SetState(CharacterState.Idle);
    }
}