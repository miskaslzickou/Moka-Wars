using UnityEngine;
using UnityEngine.InputSystem;

// Plynulá dynamická kamera s předsazením podle míření myší, ohraničením mapy a otřesem (Screen Shake).
public class CameraController : MonoBehaviour
{
    public static CameraController Instance;

    [Header("Cíl")]
    public Transform target;
    public Vector3 baseOffset = new Vector3(0f, 1.5f, -10f);
    public float smoothSpeed = 6.5f;
    public float mouseLookAhead = 2.5f;

    [Header("Ohraničení velké mapy")]
    public bool clampToBounds = true;
    public Vector2 minBounds = new Vector2(-22f, -2f);
    public Vector2 maxBounds = new Vector2(22f, 8f);

    [Header("Otřesy kamery (Screen Shake)")]
    [Range(0f, 2f)]
    public float shakeIntensityMultiplier = 0.5f;

    private float shakeTimer = 0f;
    private float totalShakeDuration = 0.15f;
    private float currentShakeMagnitude = 0f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(this);
    }

    void Start()
    {
        if (target == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) target = p.transform;
        }
    }

    void LateUpdate()
    {
        if (target == null)
        {
            var p = GameObject.FindWithTag("Player");
            if (p != null) target = p.transform;
            if (target == null) return;
        }

        // 1. Výpočet cílové pozice s předsazením ve směru míření
        Vector3 desiredPos = target.position + baseOffset;

        var mouse = Mouse.current;
        if (mouse != null && Camera.main != null)
        {
            Vector3 mouseScreen = mouse.position.ReadValue();
            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(mouseScreen);
            Vector2 lookDir = (Vector2)(mouseWorld - target.position);
            lookDir = Vector2.ClampMagnitude(lookDir * 0.25f, mouseLookAhead);
            desiredPos += new Vector3(lookDir.x, lookDir.y, 0f);
        }

        // 2. Ohraničení kamery podle rozměrů bojiště
        if (clampToBounds)
        {
            desiredPos.x = Mathf.Clamp(desiredPos.x, minBounds.x, maxBounds.x);
            desiredPos.y = Mathf.Clamp(desiredPos.y, minBounds.y, maxBounds.y);
        }

        // 3. Plynulý pohyb (Smooth Lerp)
        Vector3 smoothed = Vector3.Lerp(transform.position, desiredPos, smoothSpeed * Time.deltaTime);

        // 4. Efekt otřesu obrazovky (Screen Shake s plynulým kvadratickým dozníváním)
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            float progress = totalShakeDuration > 0.0001f ? Mathf.Clamp01(shakeTimer / totalShakeDuration) : 0f;
            float dampedMagnitude = currentShakeMagnitude * (progress * progress);
            Vector2 offset = Random.insideUnitCircle * dampedMagnitude;
            smoothed += new Vector3(offset.x, offset.y, 0f);
        }
        else
        {
            currentShakeMagnitude = 0f;
        }

        transform.position = new Vector3(smoothed.x, smoothed.y, -10f);
    }

    public void Shake(float duration, float magnitude)
    {
        totalShakeDuration = Mathf.Max(duration, 0.01f);
        shakeTimer = totalShakeDuration;
        currentShakeMagnitude = Mathf.Max(currentShakeMagnitude, magnitude * shakeIntensityMultiplier);
    }
}
