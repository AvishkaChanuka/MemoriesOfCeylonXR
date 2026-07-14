using UnityEngine;
using UnityEngine.AI;

public class Enemy : MonoBehaviour
{
    [SerializeField]
    private NavMeshAgent agent;

    [SerializeField]
    private float speed = 1f;

    private void Update()
    {
        if (agent != null)
        {
            Vector3 targetPosition = Camera.main.transform.position;
            agent.SetDestination(targetPosition);
            agent.speed = speed;
        }
    }
}
