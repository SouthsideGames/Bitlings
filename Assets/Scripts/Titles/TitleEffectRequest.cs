/// <summary>
/// Payload sent from TitleManager to BattleManager when an OnEventTriggerTitleSO fires.
/// BattleManager subscribes to TitlesAdapter.OnTitleEffectRequested to apply the effect.
/// </summary>
public struct TitleEffectRequest
{
    public string ownerId;
    public TitleEffectKind effect;
    public float value;
    public BattleStatKind stat;
    public float buffDurationSeconds;
    public string titleDisplayName;
    public string ownerDisplayName;
}
