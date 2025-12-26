using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DentedPixel;

public class PerformanceTests : MonoBehaviour {

    public bool debug = false;

    public GameObject bulletPrefab;
    public Transform bulletParent;

    private LeanPool bulletPool = new LeanPool();

    private Dictionary<GameObject, int> animIds = new Dictionary<GameObject, int>();

    public float shipSpeed = 1f;
    public float bulletDuration = 5f;
    private float shipDirectionX = 1f;

    public int PoolSize => animIds.Count;

    public int ActiveBulletCount {
        get {
            int activeCount = 0;
            foreach (var pair in animIds)
            {
                if (pair.Key.activeSelf)
                    activeCount++;
            }

            return activeCount;
        }
    }

	// Use this for initialization
	void Start () {

        GameObject[] pool = bulletPool.init(bulletPrefab, 400, bulletParent, true);
        for (int i = 0; i < pool.Length; i++){
            animIds[pool[i]] = -1;
        }
	}
	
	// Update is called once per frame
	void Update () {

        // Spray bullets
        for (int i = 0; i < 10; i++)
        {
            GameObject go = bulletPool.retrieve();
            int animId = animIds[go];
            if (animId >= 0){
                if (debug)
                    Debug.Log("canceling id:" + animId);

                LeanTween.cancel(animId);
            }
            go.transform.position = transform.position;

            float incr = (float)(5-i) * 0.1f;
            Vector3 to = new Vector3(Mathf.Sin(incr) * 180f, 0f, Mathf.Cos(incr) * 180f);

            animIds[go] = LeanTween.move(go, go.transform.position+to, bulletDuration).setOnComplete(() => {
                bulletPool.giveup(go);
            }).id;
        }

        // Move Ship
        if(transform.position.x<-20f){
            shipDirectionX = 1f;
        }else if (transform.position.x > 20f){
            shipDirectionX = -1f;
        }

        var pos = transform.position;
        pos.x += shipDirectionX * Time.deltaTime * shipSpeed;
        transform.position = pos;
	}
}
