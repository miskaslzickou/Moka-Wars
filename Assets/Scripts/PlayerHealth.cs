using UnityEngine;

// Správa zdraví (HP), zranění, Worms-style plovoucího HP ukazatele a oživení hráče.
// Odděleno od pohybu (PlayerMovement.cs).
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerHealth : MonoBehaviour
{
    [Header("Identita a Životy (HP)")]
    public string characterName = "Bean";
    public int maxHealth = 100;
    public int currentHealth = 100;

    [Header("Ochrana před zraněním (i-Frames)")]
    [Tooltip("Doba imunity po zásahu, aby hráč nedostal vícenásobné poškození z jedné exploze.")]
    public float invulnerabilityDuration = 0.35f;
    private float iFrameTimer = 0f;

    [Header("Worms Plovoucí HP Ukazatel")]
    public bool showFloatingHealth = true;
    public Vector3 healthBarOffset = new Vector3(0f, 1.4f, 0f);

    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private PlayerMovement movement;
    private Color originalColor = Color.white;
    private float hitFlashTimer = 0f;
    private bool isDead = false;

    // Komponenty plovoucího HP ukazatele
    private WorldSpaceHealthBar worldHealthBar;
    private GameObject floatingUIRoot;
    private TextMesh nameTextMesh;
    private TextMesh hpTextMesh;
    private Transform barFillTransform;
    private SpriteRenderer barFillRenderer;

    public bool IsDead => isDead;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        movement = GetComponent<PlayerMovement>();

        if (sr != null) originalColor = sr.color;
        currentHealth = maxHealth;

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
            worldHealthBar.SetHealth(currentHealth, maxHealth, characterName);
        }
        else if (showFloatingHealth)
        {
            CreateFloatingHealthUI();
        }
    }

    void Update()
    {
        if (isDead) return;

        // Časovač imunity
        if (iFrameTimer > 0f)
        {
            iFrameTimer -= Time.deltaTime;
        }

        // Vizuální červený záblesk po zásahu
        if (hitFlashTimer > 0f)
        {
            hitFlashTimer -= Time.deltaTime;
            if (hitFlashTimer <= 0f && sr != null)
            {
                sr.color = originalColor;
            }
        }

        // Kontrola pádu do propasti s vodou
        if (transform.position.y < -6.8f)
        {
            Drown();
        }
    }

    void LateUpdate()
    {
        // Udržujeme plovoucí HP ukazatel vždy svisle nad hlavou hráče (nerotuje s postavou)
        if (floatingUIRoot != null)
        {
            floatingUIRoot.transform.position = transform.position + healthBarOffset;
            floatingUIRoot.transform.rotation = Quaternion.identity;
        }
    }

    // Aplikace poškození a knockbacku z exploze
    public void TakeDamage(int damage, Vector2 knockbackImpulse)
    {
        if (isDead) return;

        // Pokud je v okně imunity, ignorujeme další zásah
        if (iFrameTimer > 0f)
        {
            // Přesto předáme menší impuls pohybu, pokud je to nutné
            if (movement != null && knockbackImpulse.sqrMagnitude > 0.1f)
            {
                movement.ApplyKnockback(knockbackImpulse * 0.5f);
            }
            return;
        }

        iFrameTimer = invulnerabilityDuration;
        currentHealth = Mathf.Max(0, currentHealth - damage);

        // Zčervenání spritu
        hitFlashTimer = 0.2f;
        if (sr != null) sr.color = new Color(1f, 0.3f, 0.3f, 1f);

        // Předání fyzikálního knockbacku do PlayerMovement
        if (movement != null)
        {
            movement.ApplyKnockback(knockbackImpulse);
        }

        UpdateHealthUI();

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(int amount)
    {
        if (isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateHealthUI();
    }

    void Drown()
    {
        if (isDead) return;
        isDead = true;

        if (rb != null)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.25f, -2.5f);
            rb.linearDamping = 4f;
            rb.angularDamping = 3f;
        }

        if (sr != null)
        {
            sr.color = new Color(0.4f, 0.7f, 1f, 0.7f);
        }

        if (floatingUIRoot != null) floatingUIRoot.SetActive(false);
        if (worldHealthBar != null) worldHealthBar.gameObject.SetActive(false);

        Invoke(nameof(RespawnPhysically), 1.4f);
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;

        if (sr != null) sr.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints2D.None;
            rb.linearVelocity = new Vector2(Random.Range(-3f, 3f), 4.5f);
            rb.angularVelocity = 360f;
        }

        if (floatingUIRoot != null) floatingUIRoot.SetActive(false);
        if (worldHealthBar != null) worldHealthBar.gameObject.SetActive(false);

        Invoke(nameof(RespawnPhysically), 1.5f);
    }

    void RespawnPhysically()
    {
        isDead = false;
        currentHealth = maxHealth;
        if (sr != null) sr.color = originalColor;

        if (rb != null)
        {
            // Fyzikální výsadek z nebe nad levým ostrovem (Worms airdrop)
            rb.position = new Vector2(-8f, 5.5f);
            rb.linearVelocity = new Vector2(0f, -1.5f);
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.05f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.angularVelocity = 0f;
        }

        transform.rotation = Quaternion.identity;

        if (worldHealthBar != null)
        {
            worldHealthBar.gameObject.SetActive(true);
            worldHealthBar.SetHealth(currentHealth, maxHealth, characterName);
        }

        if (floatingUIRoot != null)
        {
            floatingUIRoot.SetActive(true);
            UpdateHealthUI();
        }
    }

    // Vytvoření plovoucího HP ukazatele (Worms styl: Jméno + HP číslo + Lišta)
    void CreateFloatingHealthUI()
    {
        floatingUIRoot = new GameObject($"{gameObject.name}_FloatingHP");
        floatingUIRoot.transform.position = transform.position + healthBarOffset;

        // 1. Text se jménem (např. "Bean")
        GameObject nameObj = new GameObject("NameText");
        nameObj.transform.SetParent(floatingUIRoot.transform, false);
        nameObj.transform.localPosition = new Vector3(0f, 0.42f, 0f);
        nameTextMesh = nameObj.AddComponent<TextMesh>();
        nameTextMesh.text = characterName;
        nameTextMesh.fontSize = 38;
        nameTextMesh.characterSize = 0.045f;
        nameTextMesh.alignment = TextAlignment.Center;
        nameTextMesh.anchor = TextAnchor.MiddleCenter;
        nameTextMesh.color = new Color(1f, 0.95f, 0.6f, 1f); // Světle žlutá jako ve Worms

        var nameMr = nameObj.GetComponent<MeshRenderer>();
        if (nameMr != null)
        {
            nameMr.sortingLayerName = "Default";
            nameMr.sortingOrder = 20;
        }

        // 2. Číselný údaj HP (např. "100")
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
        if (hpMr != null)
        {
            hpMr.sortingLayerName = "Default";
            hpMr.sortingOrder = 20;
        }

        // 3. Pozadí lišty zdraví
        GameObject bgObj = new GameObject("BarBackground");
        bgObj.transform.SetParent(floatingUIRoot.transform, false);
        bgObj.transform.localPosition = new Vector3(0f, -0.08f, 0f);
        var bgSr = bgObj.AddComponent<SpriteRenderer>();
        bgSr.sprite = CreateQuadSprite();
        bgSr.color = new Color(0.1f, 0.1f, 0.12f, 0.85f);
        bgSr.sortingLayerName = "Default";
        bgSr.sortingOrder = 18;
        bgObj.transform.localScale = new Vector3(1.1f, 0.16f, 1f);

        // 4. Výplň lišty zdraví
        GameObject fillObj = new GameObject("BarFill");
        fillObj.transform.SetParent(floatingUIRoot.transform, false);
        fillObj.transform.localPosition = new Vector3(-0.52f, -0.08f, 0f); // Pivot vlevo
        barFillRenderer = fillObj.AddComponent<SpriteRenderer>();
        barFillRenderer.sprite = CreateQuadSprite(new Vector2(0f, 0.5f));
        barFillRenderer.color = new Color(0.25f, 0.85f, 0.35f, 1f); // Zelená
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
            worldHealthBar.SetHealth(currentHealth, maxHealth, characterName);
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
            if (ratio > 0.5f)
                barFillRenderer.color = Color.Lerp(new Color(0.85f, 0.85f, 0.2f), new Color(0.25f, 0.85f, 0.35f), (ratio - 0.5f) * 2f);
            else
                barFillRenderer.color = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.85f, 0.85f, 0.2f), ratio * 2f);
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
