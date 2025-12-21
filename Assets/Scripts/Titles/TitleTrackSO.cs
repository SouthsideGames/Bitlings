// Assets/Scripts/Titles/TitleTrackSO.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TitleTier
{
    [Min(1)] public int levelRequired = 3;
    public List<TitleSO> unlockChoices = new List<TitleSO>();
}

[CreateAssetMenu(menuName = "Data/Titles/Title Track", fileName = "TitleTrack_")]
public sealed class TitleTrackSO : ScriptableObject
{
    public List<TitleTier> tiers = new List<TitleTier>();
}
