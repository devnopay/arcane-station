namespace Content.Shared._Arcane.ERP;

public sealed class ArousedEvent(float amount) : EntityEventArgs
{
    public float Amount = amount;
}

public sealed class ArousalPhaseChangedEvent(ArousalPhase previous, ArousalPhase current) : EntityEventArgs
{
    public ArousalPhase Previous = previous;
    public ArousalPhase Current = current;
}

[ByRefEvent]
public record struct ArousalOrgasmEvent;
