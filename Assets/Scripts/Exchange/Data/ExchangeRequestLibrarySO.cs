using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Exchange/Request Library", fileName = "ExchangeRequestLibrary")]
public class ExchangeRequestLibrarySO : ScriptableObject
{
    public List<ExchangeRequestSO> requests = new List<ExchangeRequestSO>();
}
