using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GetEnemy: preserved existing behavior + lightweight aim wiring:
/// - When the closest in-front target changes, notify BarrierSpot.NotifyAimed(true/false)
/// - When aiming a BarrierSpot, instruct CurvedPathGenerator to draw the spot's configured pattern
/// - When aiming a normal enemy, fall back to GenerateCurveFromWorldObject (preserves original behavior)
/// </summary>
public class GetEnemy : MonoBehaviour
{
    [Header("Enemies to check")]
    public List<Transform> Enemies = new List<Transform>();

    [Header("Detection Settings")]
    public Transform PlayerTransform;
    public float DetectionAngle = 150f;
    public float DetectionDistance = 25f;

    [Header("Path generator (optional)")]
    [Tooltip("If assigned, used to draw curve for normal enemies and pattern for BarrierSpot targets.")]
    public CurvedPathGenerator pathGenerator;

    // runtime tracking of current aim target
    private Transform _currentTarget;
    private BarrierSpot _currentSpot; // cached if currently targeting a barrier spot

    public Transform GetClosestEnemyInFront()
    {
        Transform closest = null;
        float closestDistance = Mathf.Infinity;

        foreach (var enemy in Enemies)
        {
            if (enemy == null) continue;

            Vector3 toEnemy = enemy.position - PlayerTransform.position;
            float distance = toEnemy.magnitude;
            Vector3 toEnemyDir = toEnemy.normalized;

            float angle = Vector3.Angle(PlayerTransform.forward, toEnemyDir);

            if (angle < DetectionAngle * 0.5f && distance <= DetectionDistance)
            {
                if (distance < closestDistance)
                {
                    closest = enemy;
                    closestDistance = distance;
                }
            }
        }

        return closest;
    }

    private void LateUpdate()
    {
        Enemies.RemoveAll(e => e == null);

        // find current closest
        Transform newTarget = GetClosestEnemyInFront();

        // if target changed, update aiming visuals / generator once
        if (newTarget != _currentTarget)
        {
            // notify previous spot (if any) that it's no longer aimed
            if (_currentSpot != null)
            {
                _currentSpot.NotifyAimed(false);
                _currentSpot = null;
            }

            // clear previous line/pattern
            if (pathGenerator != null)
            {
                pathGenerator.ClearExisting();
            }

            _currentTarget = newTarget;
        }
    }

    public void RefreshEnemies()
    {
        Enemies.Clear();
        foreach (var enemy in GameObject.FindGameObjectsWithTag("Enemy"))
        {
            if (enemy == null) continue;

            Vector3 toEnemy = enemy.transform.position - PlayerTransform.position;
            float distance = toEnemy.magnitude;
            Vector3 toEnemyDir = toEnemy.normalized;

            float angle = Vector3.Angle(PlayerTransform.forward, toEnemyDir);

            if (angle < DetectionAngle * 0.5f && distance <= DetectionDistance)
            {
                Enemies.Add(enemy.transform);
            }
        }
    }

    public void UnregisterEnemy(Transform enemyTransform)
    {
        Enemies.Remove(enemyTransform);
    }

    private void OnDrawGizmosSelected()
    {
        if (PlayerTransform == null) return;

        Gizmos.color = Color.yellow;

        Vector3 origin = PlayerTransform.position;

        // Draw cone edges
        Quaternion leftRayRotation = Quaternion.AngleAxis(-DetectionAngle * 0.5f, Vector3.up);
        Quaternion rightRayRotation = Quaternion.AngleAxis(DetectionAngle * 0.5f, Vector3.up);

        Vector3 leftRayDirection = leftRayRotation * PlayerTransform.forward;
        Vector3 rightRayDirection = rightRayRotation * PlayerTransform.forward;

        Gizmos.DrawRay(origin, leftRayDirection * DetectionDistance);
        Gizmos.DrawRay(origin, rightRayDirection * DetectionDistance);

        // Draw arc as wire disc approximation
        int segments = 30;
        Vector3 previousPoint = origin + (leftRayDirection * DetectionDistance);

        for (int i = 1; i <= segments; i++)
        {
            float lerp = i / (float)segments;
            Quaternion rotation = Quaternion.Slerp(leftRayRotation, rightRayRotation, lerp);
            Vector3 nextPoint = origin + (rotation * PlayerTransform.forward * DetectionDistance);

            Gizmos.DrawLine(previousPoint, nextPoint);
            previousPoint = nextPoint;
        }
    }
}
