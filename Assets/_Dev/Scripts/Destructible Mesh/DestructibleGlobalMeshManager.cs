using UnityEngine;
using Meta.XR.MRUtilityKit;
using System.Collections.Generic;
using Unity.VisualScripting;

public class DestructibleGlobalMeshManager : MonoBehaviour
{
    [SerializeField]
    private DestructibleGlobalMeshSpawner meshSpawner;
    [SerializeField]
    private RayGun rayGun;

    private List<GameObject> segments = new List<GameObject>();
    private DestructibleMeshComponent currentComponent;

    private void Start()
    {
        rayGun.OnShootAndHit.AddListener(DestroyMeshSegment);
        meshSpawner.OnDestructibleMeshCreated.AddListener(SetupDestructableComponents);
    }

    public void SetupDestructableComponents(DestructibleMeshComponent component)
    {
        currentComponent = component;

        component.GetDestructibleMeshSegments(segments);

        foreach(var item in segments)
        {
            item.AddComponent<MeshCollider>();
        }
    }

    public void DestroyMeshSegment(GameObject segment)
    {
        if(segments.Contains(segment) && currentComponent.ReservedSegment != segment)
        {
            currentComponent.DestroySegment(segment);
        }
    }
}
