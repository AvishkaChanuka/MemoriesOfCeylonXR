using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Meta.XR.MRUtilityKit;
using System.Collections;

public class RuntimeNavMeshBuilder : MonoBehaviour
{
    private NavMeshSurface navMeshSurface;

    private void Start()
    {
        navMeshSurface = GetComponent<NavMeshSurface>();
        MRUK.Instance.RegisterSceneLoadedCallback(BuildNavMesh);
    }

    private void BuildNavMesh()
    {
        StartCoroutine(BuildNavMeshRoutine());
    }

    private IEnumerator BuildNavMeshRoutine()
    {
        yield return new WaitForEndOfFrame();
        navMeshSurface.BuildNavMesh();
    }
}
