using UnityEngine;
using Meta.XR.MRUtilityKit;

public class EnemySpawnerOld : MonoBehaviour
{
    [SerializeField]
    private GameObject enemyPrefab;

    [SerializeField]
    private float spawnTimer = 1f;

    [SerializeField]
    private float normalOffset;

    [SerializeField]
    private float minEdgeDistance = 0.3f;
    public MRUKAnchor.SceneLabels spawnLabels;

    private float timer;

    public int spawnTry = 100;

    private void Update()
    {
        if(!MRUK.Instance && !MRUK.Instance.IsInitialized)
            return;

        timer += Time.deltaTime;
        if (timer > spawnTimer)
        {
            SpawnEnemy();
            timer -= spawnTimer;
        }
    }

    public void SpawnEnemy()
    {
        MRUKRoom room = MRUK.Instance.GetCurrentRoom();

        int currentTry = 0;

        while (currentTry < spawnTry)
        {
            bool hasFoundPosition = room.GenerateRandomPositionOnSurface(MRUK.SurfaceType.VERTICAL, minEdgeDistance, LabelFilter.Included(spawnLabels), out Vector3 pos, out Vector3 norm);

            if (hasFoundPosition)
            {
                Vector3 randomPositionNormalOffset = pos + norm * normalOffset;
                randomPositionNormalOffset.y = 0; // Keep the enemy on the ground

                Instantiate(enemyPrefab, randomPositionNormalOffset, Quaternion.identity);

                return;
            }
            else
            {
                currentTry++;
            }
        }
    }
}
