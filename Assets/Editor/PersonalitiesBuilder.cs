#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class PersonalitiesBuilder
{
    private const string OutputFolder = "Assets/Resources/MonsterPersonalities";

    [MenuItem("Bitlings/Personality/Create Personalities from Presets")]
    public static void CreateAll()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "MonsterPersonalities");

        void Make(string name, MonsterPersonalitySO.PersonalityGroup group, string desc, System.Action<MonsterPersonalitySO> fill)
        {
            var path = $"{OutputFolder}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<MonsterPersonalitySO>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MonsterPersonalitySO>();
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.group = group;
            asset.description = desc;
            fill(asset);
            EditorUtility.SetDirty(asset);
        }

        // OFFENSIVE (10)
        Make("Aggressive", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "Prefers raw offense and constant pressure; rarely defends.",
            p => { p.attackWeight=8; p.defendWeight=1; p.focusWeight=1; p.runWeight=0;
                   p.lowHpThreshold=0.30f; p.lowHpDefendBonus=2; p.lowHpRunBonus=1;
                   p.superEffectiveAttackBonus=3; p.badMatchDefendBonus=1; p.badMatchRunBonus=1;
                   p.eachTurnAttackBonus=1; });

        Make("Reckless", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "Swings wildly; ignores defense even when low.",
            p => { p.attackWeight=10; p.defendWeight=0; p.focusWeight=0; p.runWeight=0;
                   p.lowHpThreshold=0.25f; p.superEffectiveAttackBonus=4; p.eachTurnAttackBonus=2; });

        Make("Bruiser", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "Trades hits, grows bolder as the fight goes on.",
            p => { p.attackWeight=6; p.defendWeight=4; p.focusWeight=1;
                   p.lowHpThreshold=0.30f; p.lowHpDefendBonus=3; p.eachTurnAttackBonus=1; });

        Make("Berserker", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "All gas, no brakes; refuses to run.",
            p => { p.attackWeight=9; p.defendWeight=1; p.focusWeight=0; p.runWeight=0;
                   p.superEffectiveAttackBonus=3; p.eachTurnAttackBonus=2; });

        Make("Duelist", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "Picks clean strikes and keeps pressure high.",
            p => { p.attackWeight=7; p.defendWeight=2; p.focusWeight=2;
                   p.superEffectiveAttackBonus=3; p.eachTurnAttackBonus=1; });

        Make("Executioner", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "Hunts for finishing blows on weakened foes.",
            p => { p.attackWeight=7; p.defendWeight=1; p.focusWeight=3;
                   p.superEffectiveAttackBonus=4; p.eachTurnAttackBonus=1; });

        Make("Relentless", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "Damage ramps every turn; never retreats.",
            p => { p.attackWeight=7; p.defendWeight=1; p.focusWeight=1; p.eachTurnAttackBonus=2; });

        Make("Momentum", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "Starts strong, snowballs with time.",
            p => { p.attackWeight=6; p.defendWeight=2; p.focusWeight=2; p.eachTurnAttackBonus=2; });

        Make("Snowball", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "Cautious opener that rapidly escalates.",
            p => { p.attackWeight=4; p.defendWeight=3; p.focusWeight=3; p.eachTurnAttackBonus=3; });

        Make("Finisher", MonsterPersonalitySO.PersonalityGroup.Offensive,
            "Strikes hardest when the matchup is favorable.",
            p => { p.attackWeight=6; p.defendWeight=1; p.focusWeight=4; p.runWeight=1;
                   p.superEffectiveAttackBonus=4; p.lowHpRunBonus=2; });

        // DEFENSIVE (8)
        Make("Defensive", MonsterPersonalitySO.PersonalityGroup.Defensive,
            "Plays safe, values survival and counters.",
            p => { p.attackWeight=3; p.defendWeight=7; p.focusWeight=2;
                   p.lowHpThreshold=0.30f; p.lowHpDefendBonus=3; p.badMatchDefendBonus=3; });

        Make("Tanky", MonsterPersonalitySO.PersonalityGroup.Defensive,
            "High durability; refuses to flee.",
            p => { p.attackWeight=2; p.defendWeight=8; p.focusWeight=1; p.runWeight=0;
                   p.lowHpThreshold=0.20f; p.lowHpDefendBonus=4; p.badMatchDefendBonus=3; });

        Make("Guardian", MonsterPersonalitySO.PersonalityGroup.Defensive,
            "Unflinching protector; steady, reliable.",
            p => { p.attackWeight=3; p.defendWeight=8; p.focusWeight=2; });

        Make("Sentinel", MonsterPersonalitySO.PersonalityGroup.Defensive,
            "Immovable watcher; punishes mistakes.",
            p => { p.attackWeight=2; p.defendWeight=9; p.focusWeight=2;
                   p.lowHpThreshold=0.20f; p.lowHpDefendBonus=5; });

        Make("Fortress", MonsterPersonalitySO.PersonalityGroup.Defensive,
            "Maximum turtling; wins wars of attrition.",
            p => { p.attackWeight=1; p.defendWeight=9; p.focusWeight=3; });

        Make("Stoic", MonsterPersonalitySO.PersonalityGroup.Defensive,
            "Unshaken by pressure; slow, deliberate play.",
            p => { p.attackWeight=3; p.defendWeight=7; p.focusWeight=3;
                   p.lowHpThreshold=0.25f; p.lowHpDefendBonus=4; });

        Make("Counter", MonsterPersonalitySO.PersonalityGroup.Defensive,
            "Endures, then retaliates when it’s safe.",
            p => { p.attackWeight=3; p.defendWeight=7; p.focusWeight=2;
                   p.badMatchDefendBonus=4; });

        Make("Staller", MonsterPersonalitySO.PersonalityGroup.Defensive,
            "Maximizes survive time; avoids risk.",
            p => { p.attackWeight=2; p.defendWeight=8; p.focusWeight=3; p.runWeight=1;
                   p.lowHpDefendBonus=4; });

        // TACTICAL (7)
        Make("Strategic", MonsterPersonalitySO.PersonalityGroup.Tactical,
            "Observes early, sets up with focus, strikes later.",
            p => { p.attackWeight=4; p.defendWeight=3; p.focusWeight=5; p.runWeight=1;
                   p.lowHpThreshold=0.40f; p.lowHpDefendBonus=3; });

        Make("Tactician", MonsterPersonalitySO.PersonalityGroup.Tactical,
            "Balances tools, values tempo and read advantage.",
            p => { p.attackWeight=5; p.defendWeight=3; p.focusWeight=5; p.runWeight=0;
                   p.superEffectiveAttackBonus=3; });

        Make("Analyst", MonsterPersonalitySO.PersonalityGroup.Tactical,
            "Waits for clear advantage; heavy focus user.",
            p => { p.attackWeight=3; p.defendWeight=3; p.focusWeight=6; p.runWeight=1;
                   p.lowHpThreshold=0.35f; p.lowHpDefendBonus=2; });

        Make("Commander", MonsterPersonalitySO.PersonalityGroup.Tactical,
            "Disciplined control; wins on planning.",
            p => { p.attackWeight=5; p.defendWeight=3; p.focusWeight=6; });

        Make("Patient", MonsterPersonalitySO.PersonalityGroup.Tactical,
            "Charges power through focus before acting.",
            p => { p.attackWeight=3; p.defendWeight=3; p.focusWeight=6; p.eachTurnAttackBonus=1; });

        Make("Hexer", MonsterPersonalitySO.PersonalityGroup.Tactical,
            "Setups and trickery; avoids direct slugfests.",
            p => { p.attackWeight=4; p.defendWeight=2; p.focusWeight=6; p.runWeight=1; });

        Make("Channeler", MonsterPersonalitySO.PersonalityGroup.Tactical,
            "Channels strength via focus, then pivots to offense.",
            p => { p.attackWeight=4; p.defendWeight=2; p.focusWeight=6; p.superEffectiveAttackBonus=3; });

        // REACTIVE (5)
        Make("Opportunist", MonsterPersonalitySO.PersonalityGroup.Reactive,
            "Moderate baseline; pounces on super-effective windows.",
            p => { p.attackWeight=4; p.defendWeight=3; p.focusWeight=3; p.runWeight=1;
                   p.superEffectiveAttackBonus=5; });

        Make("Adapter", MonsterPersonalitySO.PersonalityGroup.Reactive,
            "Shifts to defense in bad matchups, otherwise even-keeled.",
            p => { p.attackWeight=4; p.defendWeight=4; p.focusWeight=3; p.runWeight=1;
                   p.badMatchDefendBonus=3; p.lowHpThreshold=0.35f; p.lowHpDefendBonus=2; });

        Make("Retaliator", MonsterPersonalitySO.PersonalityGroup.Reactive,
            "Absorbs pressure, answers back when hurt or outmatched.",
            p => { p.attackWeight=4; p.defendWeight=6; p.focusWeight=2;
                   p.lowHpDefendBonus=3; p.badMatchDefendBonus=4; });

        Make("Equalizer", MonsterPersonalitySO.PersonalityGroup.Reactive,
            "Stabilizes fights; never overcommits early.",
            p => { p.attackWeight=4; p.defendWeight=5; p.focusWeight=3; p.eachTurnAttackBonus=1; });

        Make("TempoReader", MonsterPersonalitySO.PersonalityGroup.Reactive,
            "Reads the flow; ramps attack as turns pass.",
            p => { p.attackWeight=5; p.defendWeight=3; p.focusWeight=3; p.eachTurnAttackBonus=2; });

        // EVASIVE (4)
        Make("Evasive", MonsterPersonalitySO.PersonalityGroup.Evasive,
            "Elusive and mobile; withdraws under pressure.",
            p => { p.attackWeight=3; p.defendWeight=1; p.focusWeight=3; p.runWeight=3;
                   p.lowHpThreshold=0.50f; p.lowHpRunBonus=4; });

        Make("Pouncer", MonsterPersonalitySO.PersonalityGroup.Evasive,
            "Fast striker; engages and disengages quickly.",
            p => { p.attackWeight=7; p.defendWeight=1; p.focusWeight=2; p.runWeight=2;
                   p.lowHpThreshold=0.35f; p.lowHpRunBonus=3; p.eachTurnAttackBonus=1; });

        Make("Trickster", MonsterPersonalitySO.PersonalityGroup.Evasive,
            "Harasses, repositions, rarely takes fair trades.",
            p => { p.attackWeight=4; p.defendWeight=2; p.focusWeight=5; p.runWeight=2;
                   p.lowHpRunBonus=3; });

        Make("Phantom", MonsterPersonalitySO.PersonalityGroup.Evasive,
            "Hit-and-fade specialist; vanishes when threatened.",
            p => { p.attackWeight=4; p.defendWeight=1; p.focusWeight=4; p.runWeight=3;
                   p.lowHpThreshold=0.45f; p.lowHpRunBonus=4; });

        // SUPPORT (3)
        Make("Supportive", MonsterPersonalitySO.PersonalityGroup.Support,
            "Protective and steady; favors focus/defense over offense.",
            p => { p.attackWeight=3; p.defendWeight=5; p.focusWeight=5; p.runWeight=0;
                   p.lowHpThreshold=0.45f; p.lowHpDefendBonus=3; });

        Make("Healer", MonsterPersonalitySO.PersonalityGroup.Support,
            "Conserves strength; builds with focus, avoids risky exchanges.",
            p => { p.attackWeight=2; p.defendWeight=5; p.focusWeight=7; p.runWeight=0;
                   p.lowHpThreshold=0.45f; p.lowHpDefendBonus=3; });

        Make("Bulwark", MonsterPersonalitySO.PersonalityGroup.Support,
            "Team shield; maximizes mitigation and stability.",
            p => { p.attackWeight=2; p.defendWeight=8; p.focusWeight=4; p.runWeight=0;
                   p.lowHpThreshold=0.30f; p.lowHpDefendBonus=4; });

        // CHAOTIC (3)
        Make("Gambler", MonsterPersonalitySO.PersonalityGroup.Chaotic,
            "Risk-on; spikes between focus and reckless attack.",
            p => { p.attackWeight=7; p.defendWeight=0; p.focusWeight=6; p.runWeight=1; p.eachTurnAttackBonus=2; });

        Make("Unpredictable", MonsterPersonalitySO.PersonalityGroup.Chaotic,
            "Erratic choices; hard to read by design.",
            p => { p.attackWeight=6; p.defendWeight=3; p.focusWeight=4; p.runWeight=2;
                   p.lowHpThreshold=0.35f; p.lowHpDefendBonus=2; p.lowHpRunBonus=2;
                   p.superEffectiveAttackBonus=3; p.badMatchDefendBonus=2; p.badMatchRunBonus=2;
                   p.eachTurnAttackBonus=1; });

        Make("Wildling", MonsterPersonalitySO.PersonalityGroup.Chaotic,
            "Primal aggression; ignores danger, rarely defends.",
            p => { p.attackWeight=9; p.defendWeight=0; p.focusWeight=1; p.runWeight=0;
                   p.lowHpThreshold=0.30f; p.superEffectiveAttackBonus=3; p.eachTurnAttackBonus=1; });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Created/updated 40 Monster Personality presets in " + OutputFolder);
    }
}
#endif
