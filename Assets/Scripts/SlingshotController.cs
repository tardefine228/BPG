
using UnityEngine;
using UnityEngine.InputSystem;
public class SlingshotController : MonoBehaviour
{
    [Header("References")]
    public Rigidbody2D projectilePrefab;
    public Transform launchPoint;
    public LineRenderer aimLine;

    [Header("Settings")]
    public float maxPullDistance = 2.5f;
    public float launchPower = 8f;
    public int trajectoryPoints = 30;
    public float timeStep = 0.1f;

    private Rigidbody2D projectile;
    private Camera mainCamera;
    private bool isDragging;
    private bool hasLaunched;

    private Vector2 startPosition;

    void Start()
    {
        mainCamera = Camera.main;
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

        hasLaunched = false;

        if (aimLine != null)
            aimLine.positionCount = 0;
    }

    void Update()
    {
        if (projectile == null || hasLaunched)
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

        if (Mouse.current.leftButton.isPressed)
        {
            Aim();
        }

        if (isDragging && Mouse.current.leftButton.wasReleasedThisFrame)
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

        Vector2 pull = startPosition - mouse;

        pull = Vector2.ClampMagnitude(
            pull,
            maxPullDistance
        );

        projectile.position = startPosition - pull;
        DrawTrajectory(pull * launchPower);
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
        Vector2 pull =
            startPosition - projectile.position;

        isDragging = false;
        hasLaunched = true;

        projectile.bodyType = RigidbodyType2D.Dynamic;

        projectile.linearVelocity = pull * launchPower;

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
