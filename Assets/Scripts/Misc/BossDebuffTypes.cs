using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct JobDebuff
{
    public JobType jobType;            
    [Range(0.1f, 1f)] public float rateMultiplier; 
    [Min(1)] public int durationHours;           
    public bool appliesWhileIdle;               
}

[Serializable]
public class JobGlobalMod
{
    public JobType jobType;
    public float  multiplier;
    public long   expiresUnix;
    public string sourceBossId;
}
