/// <summary>
/// Payload sent from TitleManager to BattleManager when a StatusApplyTitleSO fires.
/// BattleManager subscribes to TitlesAdapter.OnTitleStatusRequested to apply the status.
/// </summary>
public struct TitleStatusRequest
{
    public StatusType status;
    public TitleStatusTarget target;
    public int turns;
    public bool persistent;
    public float magnitude;
    public string titleDisplayName;
    public string ownerDisplayName;
}
