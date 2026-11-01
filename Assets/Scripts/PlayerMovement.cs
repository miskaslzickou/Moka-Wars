using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Fyzikální pohyb a ovládání hráče pro Worms-style hru (chůze, skok, střelba).
// Odděleno od logiky životů (PlayerHealth.cs).
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Horizontální Pohyb")]
    public float moveSpeed = 6.5f;
    public float groundAcceleration = 48f;
    public float airAcceleration = 24f;
    public float groundDeceleration = 40f;

    [Header("Skákání (Jump)")]
    public float jumpForce = 11.5f;
    public float coyoteTime = 0.18f;
    private float coyoteTimer;
    public float jumpBufferTime = 0.18f;
    private float jumpBufferTimer;
    public LayerMask groundLayer = ~0;
    public bool isGrounded = false;

    [Header("Střelba (Shooting)")]
    public GameObject projectilePrefab;
    public Transform firePoint;
    public float projectileSpeed = 18f;
    public float fireCooldown = 0.22f;
    private float nextFireTime;

    [Header("Fyzikální Reakce na Zásah (Knockback)")]
    public bool enableTumble = false;
    [Range(0f, 45f)] public float maxTiltAngle = 14f;
    [Range(0f, 360f)] public float tumbleAngularVelocity = 140f;
    public float knockbackTimer = 0f;

    private Rigidbody2D rb;
    private Collider2D col;
    private SpriteRenderer sr;
    private PlayerHealth health;

    [Header("Input (PlayerActions)")]
    private PlayerAction playerActions;
    private bool ownsActions = false;

    private float inputH = 0f;
    private bool jumpRequested = false;
    private bool jumpCancelled = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        health = GetComponent<PlayerHealth>();

        if (firePoint == null) firePoint = transform;

        // Inicializace PlayerActions z InputManageru nebo vlastní instance
        if (InputManager.Actions != null)
        {
            playerActions = InputManager.Actions;
            ownsActions = false;
        }
        else
        {
            playerActions = new PlayerAction();
            playerActions.Enable();
            ownsActions = true;
        }

        // Fyzikální nastavení hráče
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // Hladký fyzikální materiál pro plynulý pohyb po svazích terénu
        if (col != null && col.sharedMaterial == null)
        {
            var mat = new PhysicsMaterial2D("SmoothPlayer") { friction = 0.04f, bounciness = 0.05f };
            col.sharedMaterial = mat;
        }

        // Zabezpečení masky země, aby nikdy nebyla neplatná
        if (groundLayer.value == 0 || groundLayer.value == 8)
        {
            groundLayer = ~0;
        }
    }

    void OnEnable()
    {
        if (ownsActions && playerActions != null)
        {
            playerActions.Enable();
        }
    }

    void OnDisable()
    {
        if (ownsActions && playerActions != null)
        {
            playerActions.Disable();
        }
    }

    void OnDestroy()
    {
        if (ownsActions && playerActions != null)
        {
            playerActions.Dispose();
            playerActions = null;
        }
    }

    void Update()
    {
        if (health != null && health.IsDead) return;

        // 1. Čtení vstupu a střelba přes PlayerActions
        inputH = 0f;
        if (playerActions != null)
        {
            // Horizontální pohyb (A / D)
            inputH = playerActions.Player.Move.ReadValue<float>();

            // Skok (Space)
            if (playerActions.Player.Jump.WasPressedThisFrame())
            {
                jumpBufferTimer = jumpBufferTime;
            }
            if (playerActions.Player.Jump.WasReleasedThisFrame())
            {
                jumpCancelled = true;
            }

            // Návrat do menu (Escape / Cancel)
            if (playerActions.UI.Cancel.WasPressedThisFrame())
            {
                SceneManager.LoadScene("SampleScene");
                return;
            }

            // Střelba přes PlayerActions.Player.Attack
            if (Time.time >= nextFireTime)
            {
                Vector2 aimScreenPos = playerActions.UI.Point.ReadValue<Vector2>();

                // Primární střelba (Attack: Left Mouse Button)
                if (playerActions.Player.Attack.WasPressedThisFrame())
                {
                    ShootAtMouse(aimScreenPos, false);
                    nextFireTime = Time.time + fireCooldown;
                }
                // Sekundární střelba (Right Mouse Button: obloukový lob)
                else if (playerActions.UI.RightClick.WasPressedThisFrame())
                {
                    ShootAtMouse(aimScreenPos, true);
                    nextFireTime = Time.time + fireCooldown;
                }
            }
        }

        // Doplňková podpora šipek a klávesy W pro skok
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
            {
                jumpBufferTimer = jumpBufferTime;
            }
            if (kb.wKey.wasReleasedThisFrame || kb.upArrowKey.wasReleasedThisFrame)
            {
                jumpCancelled = true;
            }
            if (kb.leftArrowKey.isPressed) inputH -= 1f;
            if (kb.rightArrowKey.isPressed) inputH += 1f;
            inputH = Mathf.Clamp(inputH, -1f, 1f);

            if (kb.escapeKey.wasPressedThisFrame)
            {
                SceneManager.LoadScene("SampleScene");
                return;
            }
        }

        // 2. Kontrola země pro skok a Coyote Time
        CheckGrounded();

        if (isGrounded)
        {
            coyoteTimer = coyoteTime;
        }
        else
        {
            coyoteTimer -= Time.deltaTime;
        }

        if (jumpBufferTimer > 0f)
        {
            jumpBufferTimer -= Time.deltaTime;
            // Pokud stiskneme skok na zemi nebo v coyote okně, zaregistrujeme skok
            if (coyoteTimer > 0f)
            {
                jumpRequested = true;
                jumpBufferTimer = 0f;
                coyoteTimer = 0f;
            }
        }

        // 3. Zpracování stavu knockbacku (po zásahu explozí)
        if (knockbackTimer > 0f)
        {
            knockbackTimer -= Time.deltaTime;

            if (!enableTumble && maxTiltAngle > 0.1f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.identity, 80f * Time.deltaTime);
            }

            if (knockbackTimer <= 0f || (isGrounded && rb.linearVelocity.magnitude < 1.2f))
            {
                knockbackTimer = 0f;
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                rb.angularVelocity = 0f;
                transform.rotation = Quaternion.identity;
            }
            else
            {
                // Během letu po zásahu má přednost fyzika
                return;
            }
        }

        // 4. Otočení spritu podle směru chůze
        if (sr != null && Mathf.Abs(inputH) > 0.05f)
        {
            sr.flipX = inputH < 0f;
        }
    }

    void FixedUpdate()
    {
        if (health != null && health.IsDead) return;

        CheckGrounded();

        // Během tvrdého knockbacku necháváme simulovat čistou fyziku rázové vlny
        if (knockbackTimer > 0f) return;

        // Horizontální fyzikální pohyb se zrychlením a brzděním
        float targetVx = inputH * moveSpeed;
        float currentVx = rb.linearVelocity.x;

        float accelRate;
        if (Mathf.Abs(targetVx) > 0.01f)
        {
            accelRate = isGrounded ? groundAcceleration : airAcceleration;
        }
        else
        {
            accelRate = groundDeceleration;
        }

        float newVx = Mathf.MoveTowards(currentVx, targetVx, accelRate * Time.fixedDeltaTime);
        float newVy = rb.linearVelocity.y;

        // Provedení skoku (Jump)
        if (jumpRequested)
        {
            newVy = jumpForce;
            jumpRequested = false;
            coyoteTimer = 0f;
            isGrounded = false;
        }
        // Variabilní výška skoku (při uvolnění klávesy se výskok zkrátí)
        else if (jumpCancelled && newVy > 0f)
        {
            newVy *= 0.5f;
            jumpCancelled = false;
        }

        rb.linearVelocity = new Vector2(newVx, newVy);
    }

    // Spolehlivá detekce země z paty kolideru (funguje na polygonovém zničitelném terénu i svazích)
    public void CheckGrounded()
    {
        if (col == null) return;

        Bounds b = col.bounds;
        float footY = b.min.y;
        float startY = footY + 0.12f;
        float castDist = 0.24f; // sahá 0.12f pod nohy hráče

        float leftX = b.min.x + 0.08f;
        float centerX = b.center.x;
        float rightX = b.max.x - 0.08f;

        RaycastHit2D hitL = Physics2D.Raycast(new Vector2(leftX, startY), Vector2.down, castDist, groundLayer);
        RaycastHit2D hitC = Physics2D.Raycast(new Vector2(centerX, startY), Vector2.down, castDist, groundLayer);
        RaycastHit2D hitR = Physics2D.Raycast(new Vector2(rightX, startY), Vector2.down, castDist, groundLayer);

        isGrounded = IsSolidGround(hitL) || IsSolidGround(hitC) || IsSolidGround(hitR);
    }

    bool IsSolidGround(RaycastHit2D hit)
    {
        if (hit.collider == null || hit.collider.isTrigger) return false;
        if (hit.collider.gameObject == gameObject || hit.collider.transform.IsChildOf(transform)) return false;
        return true;
    }

    // Aplikace rázové vlny po zásahu výbuchem
    public void ApplyKnockback(Vector2 impulse, float duration = 0.45f)
    {
        knockbackTimer = duration;

        if (rb != null)
        {
            rb.linearVelocity = impulse;

            if (enableTumble)
            {
                rb.constraints = RigidbodyConstraints2D.None;
                rb.angularVelocity = -Mathf.Sign(impulse.x) * tumbleAngularVelocity;
            }
            else
            {
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                rb.angularVelocity = 0f;

                if (maxTiltAngle > 0.1f)
                {
                    float tilt = -Mathf.Sign(impulse.x) * maxTiltAngle;
                    transform.rotation = Quaternion.Euler(0f, 0f, tilt);
                }
                else
                {
                    transform.rotation = Quaternion.identity;
                }
            }
        }
    }

    void ShootAtMouse(Vector2 mouseScreenPos, bool lobArc)
    {
        if (projectilePrefab == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // Střed výstřelu v úrovni hrudi/očí
        Vector2 shootOrigin = (Vector2)transform.position + Vector2.up * (col != null ? col.bounds.extents.y : 0.65f);

        Vector3 mouseScreen = new Vector3(mouseScreenPos.x, mouseScreenPos.y, -cam.transform.position.z);
        Vector2 mouseWorld = cam.ScreenToWorldPoint(mouseScreen);

        Vector2 dir = (mouseWorld - shootOrigin).normalized;
        if (dir.sqrMagnitude < 0.001f)
        {
            dir = (sr != null && sr.flipX) ? Vector2.left : Vector2.right;
        }

        // Ochrana: nezasekávat střelu přímo do nohou pod hráčem
        if (dir.y < -0.7f && Mathf.Abs(dir.x) < 0.35f)
        {
            float facing = (sr != null && sr.flipX) ? -1f : 1f;
            dir = new Vector2(facing * 0.45f, dir.y).normalized;
        }

        Vector2 spawnPos = shootOrigin + dir * 0.9f;

        // Kontrola, aby střela nevznikla uvnitř zdi/skály
        RaycastHit2D wallCheck = Physics2D.Raycast(shootOrigin, dir, 0.9f, groundLayer);
        if (wallCheck.collider != null && wallCheck.collider.gameObject != gameObject && !wallCheck.collider.transform.IsChildOf(transform))
        {
            spawnPos = wallCheck.point - dir * 0.12f;
        }

        GameObject bullet = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        // Nastavíme střelce (hráč je imunní vůči vlastním střelám i explozím)
        Projectile proj = bullet.GetComponent<Projectile>();
        if (proj != null)
        {
            proj.shooter = gameObject;
        }

        // Ignorujeme kolizi se VŠEMI collidery hráče
        Collider2D bulletCol = bullet.GetComponent<Collider2D>();
        if (bulletCol != null)
        {
            foreach (var pc in GetComponentsInChildren<Collider2D>())
            {
                Physics2D.IgnoreCollision(pc, bulletCol, true);
            }
        }

        Rigidbody2D bulletRb = bullet.GetComponent<Rigidbody2D>();
        if (bulletRb != null)
        {
            if (lobArc)
            {
                Vector2 lobVelocity = (dir + Vector2.up * 0.5f).normalized * (projectileSpeed * 0.9f);
                bulletRb.linearVelocity = lobVelocity;
            }
            else
            {
                bulletRb.linearVelocity = dir * projectileSpeed;
            }
        }
    }
}
