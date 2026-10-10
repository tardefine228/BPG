
using System;
using UnityEngine;
using UnityEngine.InputSystem;
public class SlingshotController : MonoBehaviour
{
    [Header("References")]
    public Rigidbody2D projectilePrefab;
    public Transform launchPoint;
    public Transform rotatingPart;
    public LineRenderer aimLine;
    public Transform pivotPoint;

    [Header("Settings")]
    public float maxPullDistance = 2.5f;
    public float launchPower = 8f;
    public int trajectoryPoints = 30;
    public float timeStep = 0.1f;

    [Header("Reload Settings")]
    public float spawnDelay = 1.5f;

    private Rigidbody2D projectile;
    private Camera mainCamera;
    private bool isDragging;
    private bool hasLaunched;
    private Vector2 launchVelocity;

    private Vector2 startPosition;

    public int ShotsFired { get; private set; }
    public event Action ProjectileLaunched;

    void Start()
    {
        mainCamera = Camera.main;
        ShotsFired = 0;
        SpawnProjectile();
    }

    
        System.Collections.IEnumerator SpawnNextProjectile()
    {
        yield return new WaitForSeconds(spawnDelay);

        SpawnProjectile();
    }

    
    void SpawnProjectile()
    {
        projectile = Instantiate(
            projectilePrefab,
            launchPoint.position,
            Quaternion.identity
        );

        projectile.bodyType = RigidbodyType2D.Kinematic;
        projectile.gravityScale = 1f;
        projectile.linearVelocity = Vector2.zero;

        projectile.transform.SetParent(null);

        startPosition = launchPoint.position;
        isDragging = false;
        hasLaunched = false;
        launchVelocity = Vector2.zero;

        if (aimLine != null)
            aimLine.positionCount = 0;
    }



    
    void Update()
    {
        if (projectile == null || hasLaunched)
            return;

        if (Mouse.current == null)
            return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mouse = GetMouseWorldPosition();

            Collider2D hit = Physics2D.OverlapPoint(mouse);

            if (hit != null &&
                hit.attachedRigidbody == projectile)
            {
                isDragging = true;
                startPosition = launchPoint.position;
            }
        }

        // Прицеливание только после захвата снаряда
        if (isDragging && Mouse.current.leftButton.isPressed)
        {
            Aim();
        }

        if (isDragging &&
            Mouse.current.leftButton.wasReleasedThisFrame)
        {
            Launch();
        }
    }


    Vector2 GetMouseWorldPosition()
    {
        Vector2 mouseScreen = Mouse.current.position.ReadValue();
        Vector3 mouseWorld = mainCamera.ScreenToWorldPoint(
            new Vector3(mouseScreen.x, mouseScreen.y, Mathf.Abs(mainCamera.transform.position.z))
        );

        return new Vector2(mouseWorld.x, mouseWorld.y);
    }

    
    
    
    void Aim()
    {
        Vector2 mouse = GetMouseWorldPosition();

        // Направление от курсора к оси рычага
        Vector2 pull = (Vector2)pivotPoint.position - mouse;

        if (pull.sqrMagnitude < 0.001f)
            return;

        // Ограничиваем силу натяжения
        pull = Vector2.ClampMagnitude(pull, maxPullDistance);

        // Рассчитываем угол и ограничиваем его
        float angle = Mathf.Atan2(pull.y, pull.x) * Mathf.Rad2Deg;
        angle = Mathf.Clamp(angle, -20f, 70f);

        // Устанавливаем фактический угол рычага
        rotatingPart.rotation = Quaternion.Euler(0f, 0f, angle);

        // Рассчитываем направление выстрела по фактическому углу рычага
        float angleRad = angle * Mathf.Deg2Rad;

        Vector2 direction = new Vector2(
            Mathf.Cos(angleRad),
            Mathf.Sin(angleRad)
        );

        // Сила выстрела зависит от натяжения
        float pullStrength = Mathf.Clamp(
            pull.magnitude,
            0f,
            maxPullDistance
        );

        launchVelocity = direction * pullStrength * launchPower;

        // Синхронизируем снаряд с концом рычага
        projectile.position = launchPoint.position;
        projectile.rotation = angle;

        DrawTrajectory(launchVelocity);
    }



    void DrawTrajectory(Vector2 velocity)
    {
        if (aimLine == null)
            return;

        aimLine.positionCount = trajectoryPoints;

        Vector2 position = projectile.position;

        for (int i = 0; i < trajectoryPoints; i++)
        {
            float t = i * timeStep;

            Vector2 point =
                position +
                velocity * t +
                0.5f * Physics2D.gravity *
                projectile.gravityScale * t * t;

            aimLine.SetPosition(i, point);
        }
    }

    
    void Launch()
    {
        Rigidbody2D firedProjectile = projectile;
        firedProjectile.transform.SetParent(null, true);

        isDragging = false;
        hasLaunched = true;

        firedProjectile.bodyType = RigidbodyType2D.Dynamic;
        firedProjectile.linearVelocity = launchVelocity;

        if (aimLine != null)
            aimLine.positionCount = 0;

        Destroy(firedProjectile.gameObject, 10f);

        ShotsFired++;
        ProjectileLaunched?.Invoke();

        StartCoroutine(SpawnNextProjectile());
    }


    public void CancelInput()
    {
        isDragging = false;
        launchVelocity = Vector2.zero;

        if (aimLine != null)
            aimLine.positionCount = 0;
    }

    public void Reload()
    {
        if (projectile != null)
            Destroy(projectile.gameObject);

        SpawnProjectile();
    }
}
