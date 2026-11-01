using UnityEngine;
using UnityEngine.UIElements;

// In-Space (World Space) UI Toolkit komponenta pro stylový ukazatel životů nad postavou.
// Inspirováno architekturou z MiskaandKyberkant s využitím PanelRenderer v Unity 6.
[RequireComponent(typeof(PanelRenderer))]
public class WorldSpaceHealthBar : MonoBehaviour
{
    private PanelRenderer panelRenderer;
    private VisualElement root;
    private VisualElement plate;
    private VisualElement teamPip;
    private Label nameLabel;
    private Label hpNumberLabel;
    private VisualElement mainBarFill;
    private VisualElement ghostBar;
    private ProgressBar legacyHealthBar;
    private int uiVersion = -1;

    [Header("Nastavení")]
    public string defaultName = "Bean";
    public float maxHealth = 100f;
    public float currentHealth = 100f;

    private float targetPct = 1f;
    private float ghostPct = 1f;
    private float ghostDelayTimer = 0f;
    private float punchScale = 1f;

    void Awake()
    {
        panelRenderer = GetComponent<PanelRenderer>();
    }

    void OnEnable()
    {
        if (panelRenderer != null)
        {
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
        }
    }

    void OnDisable()
    {
        if (panelRenderer != null)
        {
            panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        }
    }

    void Update()
    {
        // Plynulý pokles delayed ghost baru po zásahu (Worms / Fighting game efekt)
        if (ghostDelayTimer > 0f)
        {
            ghostDelayTimer -= Time.deltaTime;
        }
        else if (ghostPct > targetPct)
        {
            ghostPct = Mathf.MoveTowards(ghostPct, targetPct, 1.4f * Time.deltaTime);
            if (ghostBar != null)
            {
                ghostBar.style.width = Length.Percent(ghostPct * 100f);
            }
        }
        else if (ghostPct < targetPct)
        {
            ghostPct = targetPct;
            if (ghostBar != null)
            {
                ghostBar.style.width = Length.Percent(ghostPct * 100f);
            }
        }

        // Plynulé odeznění punch efektu (scale pop) po zásahu
        if (plate != null && punchScale > 1.001f)
        {
            punchScale = Mathf.MoveTowards(punchScale, 1f, 2.5f * Time.deltaTime);
            plate.style.scale = new Scale(new Vector3(punchScale, punchScale, 1f));
        }
    }

    void LateUpdate()
    {
        // Udržení ukazatele stále vodorovně bez rotace s postavou
        transform.rotation = Quaternion.identity;
    }

    private void OnUIReload(PanelRenderer pr, VisualElement rootElement, int version)
    {
        if (uiVersion == version) return;
        uiVersion = version;

        root = rootElement;
        plate = root.Q<VisualElement>("HealthBarPlate");
        teamPip = root.Q<VisualElement>("TeamPip");
        nameLabel = root.Q<Label>("NameLabel");
        hpNumberLabel = root.Q<Label>("HpNumberLabel");
        mainBarFill = root.Q<VisualElement>("MainBarFill");
        ghostBar = root.Q<VisualElement>("GhostBar");
        legacyHealthBar = root.Q<ProgressBar>("HealthBar");

        targetPct = maxHealth > 0 ? Mathf.Clamp01(currentHealth / maxHealth) : 1f;
        ghostPct = targetPct;

        UpdateUI();
    }

    public void SetHealth(float current, float max, string characterName = null)
    {
        float newMax = Mathf.Max(1f, max);
        float newCur = Mathf.Max(0f, current);
        float newTargetPct = Mathf.Clamp01(newCur / newMax);

        // Pokud došlo ke zranění, aktivujeme punch-pop a ghost delay
        if (newTargetPct < targetPct)
        {
            ghostDelayTimer = 0.32f;
            punchScale = 1.14f;
            if (plate != null)
            {
                plate.style.scale = new Scale(new Vector3(punchScale, punchScale, 1f));
            }
        }

        currentHealth = newCur;
        maxHealth = newMax;
        targetPct = newTargetPct;

        if (!string.IsNullOrEmpty(characterName))
        {
            defaultName = characterName;
        }

        UpdateUI();
    }

    public void UpdateUI()
    {
        // 1. Text jména a týmový indikátor
        if (nameLabel != null)
        {
            nameLabel.text = !string.IsNullOrEmpty(defaultName) ? defaultName.ToUpper() : "UNIT";
        }

        if (teamPip != null)
        {
            bool isEnemy = !string.IsNullOrEmpty(defaultName) && (defaultName.IndexOf("magician", System.StringComparison.OrdinalIgnoreCase) >= 0 || defaultName.IndexOf("enemy", System.StringComparison.OrdinalIgnoreCase) >= 0);
            teamPip.style.backgroundColor = isEnemy ? new StyleColor(new Color(0.92f, 0.28f, 0.42f)) : new StyleColor(new Color(0.18f, 0.85f, 0.45f));
        }

        // 2. Číselná hodnota HP
        if (hpNumberLabel != null)
        {
            hpNumberLabel.text = $"{Mathf.CeilToInt(currentHealth)} HP";
        }

        // 3. Šířka a barva hlavního pruhu zdraví
        if (mainBarFill != null)
        {
            mainBarFill.style.width = Length.Percent(targetPct * 100f);

            if (targetPct > 0.52f)
            {
                mainBarFill.style.backgroundColor = new StyleColor(new Color(0.18f, 0.82f, 0.44f)); // Emerald Green
            }
            else if (targetPct > 0.24f)
            {
                mainBarFill.style.backgroundColor = new StyleColor(new Color(0.96f, 0.65f, 0.12f)); // Amber Gold
            }
            else
            {
                mainBarFill.style.backgroundColor = new StyleColor(new Color(0.91f, 0.28f, 0.24f)); // Ruby Red
            }
        }

        // 4. Ghost bar
        if (ghostBar != null && ghostPct <= targetPct)
        {
            ghostBar.style.width = Length.Percent(targetPct * 100f);
        }

        // 5. Zpětná kompatibilita pro standardní ProgressBar
        if (legacyHealthBar != null)
        {
            legacyHealthBar.highValue = maxHealth;
            legacyHealthBar.value = currentHealth;
            legacyHealthBar.title = $"{Mathf.CeilToInt(currentHealth)}/{Mathf.CeilToInt(maxHealth)}";
        }
    }
}
