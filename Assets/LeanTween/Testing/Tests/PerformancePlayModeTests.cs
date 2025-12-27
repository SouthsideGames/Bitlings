#if UNITY_INCLUDE_TESTS
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PerformancePlayModeTests
{
    [UnityTest]
    public IEnumerator BulletsRecycleWithinDuration()
    {
        var bulletPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bulletPrefab.name = "TestBulletPrefab";
        bulletPrefab.SetActive(false);

        var runnerObject = new GameObject("PerformanceTestsRunner");
        var performance = runnerObject.AddComponent<PerformanceTests>();
        performance.bulletPrefab = bulletPrefab;
        performance.bulletParent = runnerObject.transform;
        performance.shipSpeed = 0f;
        performance.bulletDuration = 0.2f;

        yield return null; // Wait for Start to initialize pool

        Assert.AreEqual(400, performance.PoolSize, "Pool should initialize expected bullets.");

        const int spawnFrames = 5;
        for (int i = 0; i < spawnFrames; i++)
        {
            yield return null;
        }

        Assert.Greater(performance.ActiveBulletCount, 0, "Bullets should be spawned during updates.");
        Assert.LessOrEqual(performance.ActiveBulletCount, performance.PoolSize, "Active bullets cannot exceed pool size.");

        performance.enabled = false;

        float completionWindow = performance.bulletDuration + 0.1f;
        float waitStart = Time.time;
        while (Time.time - waitStart < completionWindow && performance.ActiveBulletCount > 0)
        {
            yield return null;
        }

        float elapsed = Time.time - waitStart;

        Assert.AreEqual(0, performance.ActiveBulletCount, "All bullets should return to the pool after tweens complete.");
        Assert.LessOrEqual(elapsed, completionWindow + 0.05f, "Tween completion exceeded expected timing window.");

        Object.Destroy(bulletPrefab);
        Object.Destroy(runnerObject);
    }
}
#endif
