using System.Collections.Generic;
using UnityEngine;

// AI nepřítel pro Worms-style hru se zničitelným terénem:
// 1. Plně fyzikální reakce na zásahy (knockback, rotace/kutálení ve vzduchu, pád ze skály nebo do kráteru).
// 2. Počítá balistickou trajektorii k hráči (vysoký i nízký oblouk přes kopce a propasti).
// 3. Kontroluje trajektorii bez kolize s vlastním tělem.
// 4. Pokud spadne do hluboké vody, utone (zničí se), nikdy se neteleportuje.
public class BallisticShooterAI : MonoBehaviour
{
    [Header("Cíl a Střelba")]
    public Transform target;
    public GameObject projectilePrefab;
    public Transform firePoint;
    public float launchSpeed = 16f;
    public float fireRate = 2.5f;
    public bool preferHighArc = true;

    [Header("Detekce a Pohyb")]
    public float detectionRange = 30f;
    public float minRange = 4.5f;
    public float moveSpeed = 2.2f;
    public LayerMask obstacleLayer = ~0;

    [Header("Zdraví a Život")]
    public string enemyName = "Magician";
    public int maxHealth = 100;
    public int currentHealth = 100;
    public bool showFloatingHealth = true;
    public Vector3 healthBarOffset = new Vector3(0f, 1.4f, 0f);

    private WorldSpaceHealthBar worldHealthBar;
    private GameObject floatingUIRoot;
    private TextMesh hpTextMesh;
    private Transform barFillTransform;
    private SpriteRenderer barFillRenderer;

    [Header("Fyzikální Reakce na Zásah (Knockback)")]
    public float knockbackDuration = 0.5f;
    private float knockbackTimer = 0f;
    public bool isGrounded = true;

    [Header("Rotace a Kotrmelec")]
    [Tooltip("Povolit točení / kotrmelec při explozi. Při vypnutí (výchozí) zůstane postava vzpřímená.")]
    public bool enableTumble = false;
    [Tooltip("Maximální úhel náklonu (ve stupních) při zásahu, pokud je kotrmelec vypnutý.")]
    [Range(0f, 45f)] public float maxTiltAngle = 14f;
    [Tooltip("Rychlost úhlového točení (pokud je kotrmelec povolen).")]
    [Range(0f, 360f)] public float tumbleAngularVelocity = 140f;

    [Header("Trajektorie Kontrola")]
    [Range(5, 40)] public int trajectorySteps = 25;

    private float nextFireTime;
    private Rigidbody2D rb;
    private Collider2D col;
    private SpriteRenderer sr;
    private bool isDead = false;
    private Color originalColor = Color.white;
    private float hitFlashTimer = 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        if (sr != null) originalColor = sr.color;
        if (firePoint == null) firePoint = transform;

        if (col != null && col.sharedMaterial == null)
        {
            var mat = new PhysicsMaterial2D("SmoothEnemy") { friction = 0.05f, bounciness = 0.15f };
            col.sharedMaterial = mat;
        }

        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }
    }

    void Start()
    {
        FindPlayerTarget();
        nextFireTime = Time.time + 1.2f;

        // Přednostně vyhledáme nebo vytvoříme In-Space (World Space) UI Toolkit lištu
        worldHealthBar = GetComponentInChildren<WorldSpaceHealthBar>();
        if (worldHealthBar == null && showFloatingHealth)
        {
            var prefab = Resources.Load<GameObject>("Prefabs/InSpaceHealthBar");
            if (prefab != null)
            {
                var go = Instantiate(prefab, transform);
                go.transform.localPosition = healthBarOffset;
                worldHealthBar = go.GetComponent<WorldSpaceHealthBar>();
            }
        }

        if (worldHealthBar != null)
        {
            worldHealthBar.SetHealth(currentHealth, maxHealth, enemyName);
        }
        else if (showFloatingHealth)
        {
            CreateFloatingHealthUI();
        }
    }

    void LateUpdate()
    {
        if (floatingUIRoot != null)
        {
            floatingUIRoot.transform.position = transform.position + healthBarOffset;
            floatingUIRoot.transform.rotation = Quaternion.identity;
        }
    }

    void Update()
    {
        if (isDead) return;

        // Vizuální záblesk po zásahu
        if (hitFlashTimer > 0f)
        {
            hitFlashTimer -= Time.deltaTime;
            if (hitFlashTimer <= 0f && sr != null)
            {
                sr.color = originalColor;
            }
        }

        // Kontrola pádu do propasti s vodou (Worms mechanika: utopení bez teleportování!)
        if (transform.position.y < -6.8f)
        {
            Drown();
            return;
        }

        // Fyzikální stav po zásahu
        if (knockbackTimer > 0f)
        {
            knockbackTimer -= Time.deltaTime;
            CheckGrounded();

            if (!enableTumble && maxTiltAngle > 0.1f)
            {
                // Plynulé vyrovnání náklonu zpět do svislé polohy
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.identity, 80f * Time.deltaTime);
            }

            // Pokud vypršel čas nebo postava dopadla na zem a zpomalila
            if (knockbackTimer <= 0f || (isGrounded && rb != null && rb.linearVelocity.magnitude < 1.2f && knockbackTimer < knockbackDuration * 0.5f))
            {
                // Přistál: narovnáme postavu zpět na nohy
                knockbackTimer = 0f;
                if (rb != null)
                {
                    rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                    rb.angularVelocity = 0f;
                }
                transform.rotation = Quaternion.identity;
            }
            else
            {
                // Dokud je v knockbacku / letí vzduchem, plně vládne fyzika (AI neřídí pohyb ani střelbu)
                return;
            }
        }

        CheckGrounded();

        if (target == null)
        {
            FindPlayerTarget();
            if (target == null) return;
        }

        float dist = Vector2.Distance(transform.position, target.position);
        if (dist > detectionRange) return;

        // Otočení spritu podle pozice hráče
        if (sr != null)
        {
            sr.flipX = target.position.x < transform.position.x;
        }

        // 1. Pokusíme se spočítat přesný balistický úhel
        bool canHit = TryCalculateTrajectory(out Vector2 launchVelocity);

        if (canHit)
        {
            // Zastavíme chůzi a pálíme (pokud stojíme na zemi)
            if (rb != null && isGrounded)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            }

            if (Time.time >= nextFireTime)
            {
                Fire(launchVelocity);
                nextFireTime = Time.time + fireRate;
            }
        }
        else
        {
            // Pokud je přímý výstřel blokován kopcem, vystřelíme minometem obloukem
            if (Time.time >= nextFireTime && dist <= detectionRange)
            {
                if (CalculateBlindMortar(out Vector2 mortarVelocity))
                {
                    Fire(mortarVelocity);
                    nextFireTime = Time.time + fireRate * 1.25f;
                }
            }

            // Pohyb se zabezpečením proti dobrovolnému pádu z útesu do vody
            if (isGrounded)
            {
                MoveSafelyTowardsTarget(dist);
            }
        }
    }

    public void TakeKnockback(Vector2 impulse, int dmg = 35)
    {
        if (isDead) return;

        currentHealth = Mathf.Max(0, currentHealth - dmg);
        hitFlashTimer = 0.2f;
        if (sr != null) sr.color = new Color(1f, 0.35f, 0.35f, 1f);

        UpdateHealthUI();

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        knockbackTimer = knockbackDuration;

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

    void CheckGrounded()
    {
        if (col == null) return;
        Vector2 origin = (Vector2)transform.position;
        float radius = col.bounds.extents.x * 0.9f;
        RaycastHit2D hit = Physics2D.CircleCast(origin, radius, Vector2.down, col.bounds.extents.y * 0.7f, obstacleLayer);
        isGrounded = hit.collider != null && hit.collider.gameObject != gameObject;
    }

    void Drown()
    {
        if (isDead) return;
        isDead = true;
        if (worldHealthBar != null) worldHealthBar.gameObject.SetActive(false);
        if (floatingUIRoot != null) floatingUIRoot.SetActive(false);
        if (rb != null)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.3f, -2f);
            rb.linearDamping = 3f;
        }
        if (sr != null)
        {
            sr.color = new Color(0.5f, 0.7f, 0.9f, 0.7f);
        }
        Destroy(gameObject, 0.8f);
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;
        if (worldHealthBar != null) worldHealthBar.gameObject.SetActive(false);
        if (floatingUIRoot != null) floatingUIRoot.SetActive(false);
        if (sr != null) sr.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints2D.None;
            rb.linearVelocity = new Vector2(Random.Range(-3f, 3f), 4f);
            rb.angularVelocity = 360f;
        }
        Destroy(gameObject, 0.6f);
    }

    void FindPlayerTarget()
    {
        var player = GameObject.FindWithTag("Player");
        if (player != null) target = player.transform;
    }

    void MoveSafelyTowardsTarget(float dist)
    {
        if (rb == null) return;

        float dir = Mathf.Sign(target.position.x - transform.position.x);
        if (dist < minRange)
        {
            dir = -dir; // Příliš blízko, couvni
        }

        // Kontrola útesu: nespadnout ze skály do vody při chůzi
        if (IsCliffAhead(dir))
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            return;
        }

        rb.linearVelocity = new Vector2(dir * moveSpeed, rb.linearVelocity.y);
    }

    bool IsCliffAhead(float dir)
    {
        if (col == null) return false;

        // Vyšleme paprsek dolů kousek před nohama AI
        Vector2 checkOrigin = (Vector2)transform.position + new Vector2(dir * (col.bounds.extents.x + 0.35f), -col.bounds.extents.y);
        RaycastHit2D hit = Physics2D.Raycast(checkOrigin, Vector2.down, 1.8f, obstacleLayer);

        return hit.collider == null;
    }

    void Fire(Vector2 velocity)
    {
        if (projectilePrefab == null) return;

        Vector2 dir = velocity.normalized;
        // Výpočet pozice výstřelu od hrudi nepřítele a vysunutí ven ze středu
        Vector2 shootOrigin = (Vector2)transform.position + Vector2.up * (col != null ? col.bounds.extents.y : 0.8f);
        Vector3 spawnPos = (Vector3)(shootOrigin + dir * 0.9f);

        GameObject proj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

        // Nastavíme střelce (nepřítel je imunní vůči vlastním střelám i explozím)
        Projectile projComp = proj.GetComponent<Projectile>();
        if (projComp != null)
        {
            projComp.shooter = gameObject;
        }

        // Ignorujeme kolize se VŠEMI collidery tohoto nepřítele
        Collider2D projCol = proj.GetComponent<Collider2D>();
        if (projCol != null)
        {
            foreach (var c in GetComponentsInChildren<Collider2D>())
            {
                Physics2D.IgnoreCollision(c, projCol, true);
            }
        }

        Rigidbody2D projRb = proj.GetComponent<Rigidbody2D>();
        if (projRb != null)
        {
            projRb.linearVelocity = velocity;
        }
    }

    public bool TryCalculateTrajectory(out Vector2 launchVelocity)
    {
        launchVelocity = Vector2.zero;
        if (target == null) return false;

        Vector2 start = (Vector2)transform.position + Vector2.up * (col != null ? col.bounds.extents.y : 0.8f);
        Vector2 end = target.position;

        float dx = end.x - start.x;
        float dy = end.y - start.y;
        float v = launchSpeed;

        float g = Mathf.Abs(Physics2D.gravity.y * (projectilePrefab != null && projectilePrefab.TryGetComponent<Rigidbody2D>(out var prb) ? prb.gravityScale : 1f));
        if (g < 0.001f) g = 9.81f;

        float v2 = v * v;
        float v4 = v2 * v2;
        float x2 = dx * dx;

        float underSqrt = v4 - g * (g * x2 + 2f * dy * v2);
        if (underSqrt < 0f) return false;

        float sqrtD = Mathf.Sqrt(underSqrt);

        float angle1 = Mathf.Atan2(v2 + sqrtD, g * Mathf.Abs(dx));
        float angle2 = Mathf.Atan2(v2 - sqrtD, g * Mathf.Abs(dx));

        float chosenAngle = preferHighArc ? angle1 : angle2;
        float altAngle = preferHighArc ? angle2 : angle1;

        if (TestTrajectoryClear(start, end, chosenAngle, dx, v, g, out Vector2 vel1))
        {
            launchVelocity = vel1;
            return true;
        }

        if (TestTrajectoryClear(start, end, altAngle, dx, v, g, out Vector2 vel2))
        {
            launchVelocity = vel2;
            return true;
        }

        return false;
    }

    bool CalculateBlindMortar(out Vector2 launchVelocity)
    {
        launchVelocity = Vector2.zero;
        if (target == null) return false;

        Vector2 start = (Vector2)transform.position + Vector2.up * (col != null ? col.bounds.extents.y : 0.8f);
        Vector2 end = target.position;
        float dx = end.x - start.x;

        float signX = Mathf.Sign(dx);
        float angle = 58f * Mathf.Deg2Rad;
        float vx = signX * launchSpeed * Mathf.Cos(angle);
        float vy = launchSpeed * Mathf.Sin(angle);

        launchVelocity = new Vector2(vx, vy);
        return true;
    }

    bool TestTrajectoryClear(Vector2 start, Vector2 end, float angle, float dx, float v, float g, out Vector2 vel)
    {
        float signX = Mathf.Sign(dx);
        float vx = signX * v * Mathf.Cos(angle);
        float vy = v * Mathf.Sin(angle);
        vel = new Vector2(vx, vy);

        float totalTime = Mathf.Abs(dx / vx);
        Vector2 prev = start;

        for (int i = 1; i <= trajectorySteps; i++)
        {
            float t = (i / (float)trajectorySteps) * totalTime;
            Vector2 current = new Vector2(
                start.x + vx * t,
                start.y + vy * t - 0.5f * g * t * t
            );

            RaycastHit2D hit = Physics2D.Linecast(prev, current, obstacleLayer);
            if (hit.collider != null)
            {
                if (hit.collider.transform != transform && hit.collider.transform != target)
                {
                    if ((i / (float)trajectorySteps) < 0.8f)
                    {
                        return false;
                    }
                }
            }

            prev = current;
        }

        return true;
    }

    void CreateFloatingHealthUI()
    {
        floatingUIRoot = new GameObject($"{gameObject.name}_FloatingHP");
        floatingUIRoot.transform.position = transform.position + healthBarOffset;

        // Jméno (např. "Magician")
        GameObject nameObj = new GameObject("NameText");
        nameObj.transform.SetParent(floatingUIRoot.transform, false);
        nameObj.transform.localPosition = new Vector3(0f, 0.42f, 0f);
        var nameTm = nameObj.AddComponent<TextMesh>();
        nameTm.text = enemyName;
        nameTm.fontSize = 38;
        nameTm.characterSize = 0.045f;
        nameTm.alignment = TextAlignment.Center;
        nameTm.anchor = TextAnchor.MiddleCenter;
        nameTm.color = new Color(1f, 0.6f, 0.6f, 1f); // Červeno-růžová pro nepřítele

        var nameMr = nameObj.GetComponent<MeshRenderer>();
        if (nameMr != null) { nameMr.sortingLayerName = "Default"; nameMr.sortingOrder = 20; }

        // Číselné HP
        GameObject hpObj = new GameObject("HpText");
        hpObj.transform.SetParent(floatingUIRoot.transform, false);
        hpObj.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        hpTextMesh = hpObj.AddComponent<TextMesh>();
        hpTextMesh.text = $"{currentHealth} HP";
        hpTextMesh.fontSize = 44;
        hpTextMesh.characterSize = 0.052f;
        hpTextMesh.alignment = TextAlignment.Center;
        hpTextMesh.anchor = TextAnchor.MiddleCenter;
        hpTextMesh.color = Color.white;

        var hpMr = hpObj.GetComponent<MeshRenderer>();
        if (hpMr != null) { hpMr.sortingLayerName = "Default"; hpMr.sortingOrder = 20; }

        // Pozadí lišty
        GameObject bgObj = new GameObject("BarBackground");
        bgObj.transform.SetParent(floatingUIRoot.transform, false);
        bgObj.transform.localPosition = new Vector3(0f, -0.08f, 0f);
        var bgSr = bgObj.AddComponent<SpriteRenderer>();
        bgSr.sprite = CreateQuadSprite();
        bgSr.color = new Color(0.1f, 0.1f, 0.12f, 0.85f);
        bgSr.sortingLayerName = "Default";
        bgSr.sortingOrder = 18;
        bgObj.transform.localScale = new Vector3(1.1f, 0.16f, 1f);

        // Výplň lišty
        GameObject fillObj = new GameObject("BarFill");
        fillObj.transform.SetParent(floatingUIRoot.transform, false);
        fillObj.transform.localPosition = new Vector3(-0.52f, -0.08f, 0f);
        barFillRenderer = fillObj.AddComponent<SpriteRenderer>();
        barFillRenderer.sprite = CreateQuadSprite(new Vector2(0f, 0.5f));
        barFillRenderer.color = new Color(0.9f, 0.25f, 0.25f, 1f);
        barFillRenderer.sortingLayerName = "Default";
        barFillRenderer.sortingOrder = 19;
        barFillTransform = fillObj.transform;
        barFillTransform.localScale = new Vector3(1.04f, 0.12f, 1f);

        UpdateHealthUI();
    }

    void UpdateHealthUI()
    {
        if (worldHealthBar != null)
        {
            worldHealthBar.SetHealth(currentHealth, maxHealth, enemyName);
            return;
        }

        float ratio = Mathf.Clamp01((float)currentHealth / maxHealth);

        if (hpTextMesh != null)
        {
            hpTextMesh.text = $"{currentHealth} HP";
            if (ratio > 0.5f) hpTextMesh.color = Color.white;
            else if (ratio > 0.25f) hpTextMesh.color = new Color(1f, 0.8f, 0.2f);
            else hpTextMesh.color = new Color(1f, 0.35f, 0.35f);
        }

        if (barFillTransform != null)
        {
            barFillTransform.localScale = new Vector3(1.04f * ratio, 0.12f, 1f);
        }

        if (barFillRenderer != null)
        {
            barFillRenderer.color = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.9f, 0.6f, 0.2f), ratio);
        }
    }

    static Sprite CreateQuadSprite(Vector2? pivot = null)
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        Vector2 piv = pivot ?? new Vector2(0.5f, 0.5f);
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), piv, 1f);
    }

    void OnDestroy()
    {
        if (floatingUIRoot != null)
        {
            Destroy(floatingUIRoot);
        }
    }
}
