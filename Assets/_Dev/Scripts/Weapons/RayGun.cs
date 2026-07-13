using UnityEngine;

public class RayGun : MonoBehaviour
{
    public LayerMask LayerMask;

    [SerializeField]
    private OVRInput.RawButton shootingButton;

    [SerializeField]
    private LineRenderer linePrefab;
    [SerializeField]
    private GameObject impactEffectPrefab;

    [SerializeField]
    private Transform shootingPoint;

    [SerializeField]
    private float maxLineDistance = 5f;
    [SerializeField]
    private float lineDuration = 0.3f;


    private void Update()
    {
        if (OVRInput.GetDown(shootingButton))
        {
            Shoot();
        }
    }

    private void Shoot()
    {
        Ray ray = new Ray(shootingPoint.position, shootingPoint.forward);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, maxLineDistance, LayerMask);

        Vector3 endPoint = Vector3.zero;

        if (hit)
        {
            endPoint = hitInfo.point;

            Quaternion rayImpactRotation = Quaternion.LookRotation(-hitInfo.normal);
            GameObject impactEffect = Instantiate(impactEffectPrefab, hitInfo.point, rayImpactRotation);

            Destroy(impactEffect, 1f);
        }
        else
        {
            endPoint = shootingPoint.position + shootingPoint.forward * maxLineDistance;
        }

        LineRenderer line = Instantiate(linePrefab);
        line.positionCount = 2;
        line.SetPosition(0, shootingPoint.position);
    
        line.SetPosition(1, endPoint);

        Destroy(line.gameObject, lineDuration);
    }
}
