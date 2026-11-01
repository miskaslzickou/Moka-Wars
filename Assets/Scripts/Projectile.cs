using System.Collections.Generic;
using UnityEngine;

// Střela: po nárazu vytvoří explozi, vyhloubí kráter do zničitelného terénu
// a udělí fyzikální rázovou vlnu (knockback) všem cizím Rigidbody2D v dosahu.
// Střelec (shooter) je imunní vůči vlastním střelám i vlastním explozím (nemůže se střelit).
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    [Header("Shooter / Owner")]
    public GameObject shooter;

    [Header("Explosion Settings")]
    public float explosionRadius = 1.6f;
    public float explosionForce = 16f;
    public int damage = 25;
    public GameObject explosionEffect;   // volitelné: prefab s částicemi
    public float lifetime = 8f;

    private bool hasExploded = false;
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Start()
    {
        if (lifetime > 0f)
        {
            Destroy(gameObject, lifetime);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 1. Zabraň jakékoliv kolizi se střelcem (hráč se nemůže sám střelit)
        if (shooter != null)
        {
            if (collision.gameObject == shooter || collision.transform.IsChildOf(shooter.transform))
            {
                return;
            }
        }

        Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2)transform.position;
        ExplodeAt(point, collision.collider);
    }

    public void ExplodeAt(Vector2 point, Collider2D hitCollider = null)
    {
        if (hasExploded) return;
        hasExploded = true;

        Vector2 bulletDir = (rb != null && rb.linearVelocity.sqrMagnitude > 0.1f) 
            ? rb.linearVelocity.normalized 
            : Vector2.right;

        // 1. Zničíme terén kolem místa výbuchu (kráter)
        var terrain = FindAnyObjectByType<DestructibleTerrain>();
        if (terrain != null)
        {
            terrain.Explode(point, explosionRadius);
        }

        // Jemný a decentní otřes kamery (Screen Shake)
        CameraController.Instance?.Shake(0.14f, 0.10f);

        // 2. Fyzikální rázová vlna (Worms knockback) na všechny objekty v okolí
        float blastRadius = explosionRadius * 1.65f;
        Collider2D[] colliders = Physics2D.OverlapCircleAll(point, blastRadius);
        HashSet<Rigidbody2D> pushedBodies = new HashSet<Rigidbody2D>();

        foreach (var col in colliders)
        {
            if (col == null) continue;
            Rigidbody2D targetRb = col.attachedRigidbody;
            if (targetRb == null || pushedBodies.Contains(targetRb)) continue;

            // STŘELEC NIKDY NEDOSTANE DAMAGE ANI KNOCKBACK ZE SVÉ VLASTNÍ STŘELY!
            if (shooter != null)
            {
                if (targetRb.gameObject == shooter || targetRb.transform.IsChildOf(shooter.transform))
                {
                    continue;
                }
            }

            // Kontrola odstínění skálou (Line of Sight):
            // Pokud je mezi epicentrem výbuchu a cílem pevná skála/terén (např. zásah zespodu útesu),
            // tlaková vlna přes neprobitou skálu neprojde!
            if (!HasLineOfSightToTarget(point, targetRb))
            {
                continue;
            }

            pushedBodies.Add(targetRb);

            // Výpočet směru od středu exploze k těžišti objektu
            Vector2 diff = targetRb.worldCenterOfMass - point;
            float dist = diff.magnitude;

            Vector2 dir;
            if (dist < 0.2f)
            {
                // Přímý zásah těla: směr je dán letem střely s výrazným vyhozením nahoru
                dir = (bulletDir * 0.7f + Vector2.up * 0.9f).normalized;
            }
            else
            {
                // Výbuch v blízkosti: tlaková vlna se šíří radiálně + nahoru
                dir = (diff.normalized + Vector2.up * 0.75f).normalized;
            }

            // Síla i zranění klesají se vzdáleností od středu výbuchu (Worms styl)
            float falloff = Mathf.Clamp01(1f - (dist / blastRadius));
            float force = explosionForce * (0.45f + 0.55f * falloff);
            Vector2 impulse = dir * force;
            int actualDamage = Mathf.Max(6, Mathf.RoundToInt(damage * (0.35f + 0.65f * falloff)));

            // Zásah hráče (PlayerHealth - oddělená logika životů)
            if (targetRb.TryGetComponent<PlayerHealth>(out var pHealth))
            {
                pHealth.TakeDamage(actualDamage, impulse);
            }
            // Zásah AI nepřítele (BallisticShooterAI)
            else if (targetRb.TryGetComponent<BallisticShooterAI>(out var ai))
            {
                ai.TakeKnockback(impulse, actualDamage);
            }
            // Zpětná kompatibilita pro SimplePlayerController
            else if (targetRb.TryGetComponent<SimplePlayerController>(out var player))
            {
                player.TakeKnockback(impulse, actualDamage);
            }
            else
            {
                targetRb.linearVelocity = impulse;
            }
        }

        // 3. Částicový efekt výbuchu (pokud je přiřazen)
        if (explosionEffect != null)
        {
            Instantiate(explosionEffect, point, Quaternion.identity);
        }

        Destroy(gameObject);
    }

    private bool HasLineOfSightToTarget(Vector2 explosionPoint, Rigidbody2D targetRb)
    {
        if (targetRb == null) return false;

        Vector2 targetCenter = targetRb.worldCenterOfMass;
        Vector2 targetTop = targetCenter + Vector2.up * 0.35f;

        // Pokud má přímou viditelnost střed těla nebo horní část těla, tlaková vlna projde
        return CheckRayClear(explosionPoint, targetCenter, targetRb) || 
               CheckRayClear(explosionPoint, targetTop, targetRb);
    }

    private bool CheckRayClear(Vector2 from, Vector2 to, Rigidbody2D targetRb)
    {
        Vector2 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.05f) return true;

        RaycastHit2D[] hits = Physics2D.RaycastAll(from, dir.normalized, dist);
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger) continue;

            // Ignorujeme kolidery samotného cíle
            if (hit.collider.attachedRigidbody == targetRb) continue;

            // Ignorujeme kolidery střelce
            if (shooter != null && (hit.collider.gameObject == shooter || hit.collider.transform.IsChildOf(shooter.transform)))
                continue;

            // Pokud paprsek narazí na zničitelný terén, skála blokuje výbuch
            if (hit.collider.GetComponentInParent<DestructibleTerrain>() != null)
            {
                return false;
            }
        }

        return true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Dopad do vody (Cainos Interactive Pixel Water)
        if (other.name.Contains("Water") || other.GetComponentInParent<Cainos.InteractivePixelWater.PixelWater>() != null)
        {
            // Zpomalíme střelu a necháme ji potopit ke dnu
            if (rb != null)
            {
                rb.linearVelocity *= 0.35f;
            }
            Destroy(gameObject, 1.2f);
        }
    }
}
