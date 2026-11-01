using UnityEngine;

// Zpětná kompatibilita: dřívější monolitický skript hráče.
// Nyní je logika rozdělena do dvou specializovaných komponent:
// 1. PlayerMovement.cs (pohyb, skok, střelba, fyzika)
// 2. PlayerHealth.cs (životy, zranění, i-frames, Worms floating HP ukazatel)
public class SimplePlayerController : PlayerMovement
{
    // Přesměrování na novou komponentu PlayerHealth
    public void TakeKnockback(Vector2 impulse, int damage = 25)
    {
        if (TryGetComponent<PlayerHealth>(out var health))
        {
            health.TakeDamage(damage, impulse);
        }
        else
        {
            ApplyKnockback(impulse);
        }
    }
}
