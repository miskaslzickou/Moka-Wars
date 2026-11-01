using System.Collections.Generic;
using UnityEngine;

// Zničitelný 2D terén rozdělený na chunky (čtverce chunkSize x chunkSize pixelů).
// Při výbuchu se přepočítají jen zasažené chunky, takže hra necuká.
public class DestructibleTerrain : MonoBehaviour
{
    [Header("Zdroj")]
    [Tooltip("Sprite celé mapy (vyžaduje v Import Settings: Read/Write ON, Filter Point, Compression None).")]
    public Sprite sourceSprite;
    [Min(16)] public int chunkSize = 128;

    [Header("Vykreslování a Fyzika")]
    public string sortingLayerName = "Default";
    public int sortingOrder = 5; // Terén se vykresluje před vodou na pozadí
    public PhysicsMaterial2D physicsMaterial;

    class Chunk
    {
        public GameObject go;
        public Texture2D tex;
        public Color32[] pixels;
        public Sprite sprite;
        public SpriteRenderer sr;
        public PolygonCollider2D col;
        public int opaque;   // počet neprůhledných pixelů, když dojde na 0, chunk se smaže
    }

    Chunk[,] chunks;
    int w, h, chunksX, chunksY;
    float ppu;
    readonly List<Vector2> shapeBuffer = new List<Vector2>();

    void Awake()
    {
        if (sourceSprite == null)
        {
            if (TryGetComponent<SpriteRenderer>(out var sr) && sr.sprite != null)
            {
                sourceSprite = sr.sprite;
                sr.enabled = false; // Vykreslování převezmou chunky
            }
            else
            {
                Debug.LogError("[DestructibleTerrain] Chybí Source Sprite!", this);
                return;
            }
        }

        Texture2D src = sourceSprite.texture;
        if (!src.isReadable)
        {
            Debug.LogError($"[DestructibleTerrain] Textura '{src.name}' nemá zapnuté Read/Write v Import Settings!", this);
            return;
        }

        ppu = sourceSprite.pixelsPerUnit;
        w = src.width;
        h = src.height;

        Color32[] all = src.GetPixels32();
        chunksX = Mathf.CeilToInt(w / (float)chunkSize);
        chunksY = Mathf.CeilToInt(h / (float)chunkSize);
        chunks = new Chunk[chunksX, chunksY];

        for (int cy = 0; cy < chunksY; cy++)
        {
            for (int cx = 0; cx < chunksX; cx++)
            {
                BuildChunk(all, cx, cy);
            }
        }

        Debug.Log($"[DestructibleTerrain] Vytvořeno {chunksX}x{chunksY} chunků pro terén velikosti {w}x{h} px.");
    }

    void BuildChunk(Color32[] all, int cx, int cy)
    {
        int cs = chunkSize;
        Color32[] pixels = new Color32[cs * cs];
        int opaque = 0;

        for (int ly = 0; ly < cs; ly++)
        {
            int gy = cy * cs + ly;
            if (gy >= h) break;
            for (int lx = 0; lx < cs; lx++)
            {
                int gx = cx * cs + lx;
                if (gx >= w) break;
                Color32 p = all[gy * w + gx];
                pixels[ly * cs + lx] = p;
                if (p.a != 0) opaque++;
            }
        }

        if (opaque == 0) return;   // Prázdný chunk nepotřebuje objekt

        Chunk c = new Chunk { pixels = pixels, opaque = opaque };

        c.go = new GameObject($"Chunk_{cx}_{cy}");
        c.go.layer = gameObject.layer;
        c.go.tag = gameObject.tag;
        c.go.transform.SetParent(transform, false);
        c.go.transform.localPosition = new Vector3(
            (cx * cs - w * 0.5f) / ppu,
            (cy * cs - h * 0.5f) / ppu,
            0f);

        c.tex = new Texture2D(cs, cs, TextureFormat.RGBA32, false);
        c.tex.filterMode = FilterMode.Point;
        c.tex.wrapMode = TextureWrapMode.Clamp;
        c.tex.SetPixels32(pixels);
        c.tex.Apply();

        c.sr = c.go.AddComponent<SpriteRenderer>();
        c.sr.sortingLayerName = sortingLayerName;
        c.sr.sortingOrder = sortingOrder;

        c.col = c.go.AddComponent<PolygonCollider2D>();
        if (physicsMaterial != null)
        {
            c.col.sharedMaterial = physicsMaterial;
        }

        Refresh(c);
        chunks[cx, cy] = c;
    }

    // worldPoint = střed výbuchu (světové souřadnice), radiusWorld = poloměr v jednotkách Unity
    public void Explode(Vector2 worldPoint, float radiusWorld)
    {
        if (chunks == null) return;

        Vector3 local = transform.InverseTransformPoint(worldPoint);
        int px = Mathf.RoundToInt(local.x * ppu + w * 0.5f);
        int py = Mathf.RoundToInt(local.y * ppu + h * 0.5f);
        float lossyScaleX = Mathf.Abs(transform.lossyScale.x);
        if (lossyScaleX < 0.0001f) lossyScaleX = 1f;

        int r = Mathf.RoundToInt(radiusWorld / lossyScaleX * ppu);
        if (r <= 0) return;

        int cs = chunkSize;
        int cx0 = Mathf.Max(0, Mathf.FloorToInt((px - r) / (float)cs));
        int cx1 = Mathf.Min(chunksX - 1, Mathf.FloorToInt((px + r) / (float)cs));
        int cy0 = Mathf.Max(0, Mathf.FloorToInt((py - r) / (float)cs));
        int cy1 = Mathf.Min(chunksY - 1, Mathf.FloorToInt((py + r) / (float)cs));

        for (int cy = cy0; cy <= cy1; cy++)
        {
            for (int cx = cx0; cx <= cx1; cx++)
            {
                Carve(cx, cy, px, py, r);
            }
        }
    }

    void Carve(int cx, int cy, int px, int py, int r)
    {
        Chunk c = chunks[cx, cy];
        if (c == null) return;

        int cs = chunkSize;
        int baseX = cx * cs;
        int baseY = cy * cs;

        int x0 = Mathf.Max(px - r - baseX, 0);
        int x1 = Mathf.Min(px + r - baseX, cs - 1);
        int y0 = Mathf.Max(py - r - baseY, 0);
        int y1 = Mathf.Min(py + r - baseY, cs - 1);
        int rr = r * r;
        bool changed = false;

        for (int ly = y0; ly <= y1; ly++)
        {
            int dy = baseY + ly - py;
            for (int lx = x0; lx <= x1; lx++)
            {
                int dx = baseX + lx - px;
                if (dx * dx + dy * dy > rr) continue;

                int i = ly * cs + lx;
                if (c.pixels[i].a == 0) continue;

                c.pixels[i] = new Color32(0, 0, 0, 0);
                c.opaque--;
                changed = true;
            }
        }

        if (!changed) return;

        if (c.opaque <= 0)
        {
            DestroyChunk(c);
            chunks[cx, cy] = null;
            return;
        }

        c.tex.SetPixels32(c.pixels);
        c.tex.Apply();
        Refresh(c);
    }

    // Nový sprite z upravené textury + nový tvar kolize jen pro tento chunk
    void Refresh(Chunk c)
    {
        Sprite old = c.sprite;
        c.sprite = Sprite.Create(
            c.tex,
            new Rect(0, 0, chunkSize, chunkSize),
            Vector2.zero,                // pivot vlevo dole odpovídá lokálnímu posunu
            ppu,
            0,
            SpriteMeshType.Tight,
            Vector4.zero,
            true);                       // vygeneruje tvar pro kolizi

        c.sr.sprite = c.sprite;
        if (old != null) Destroy(old);

        int n = c.sprite.GetPhysicsShapeCount();
        c.col.pathCount = n;
        for (int i = 0; i < n; i++)
        {
            c.sprite.GetPhysicsShape(i, shapeBuffer);
            c.col.SetPath(i, shapeBuffer);
        }
    }

    void DestroyChunk(Chunk c)
    {
        if (c.sprite != null) Destroy(c.sprite);
        if (c.tex != null) Destroy(c.tex);
        if (c.go != null) Destroy(c.go);
    }

    void OnDestroy()
    {
        if (chunks == null) return;
        foreach (Chunk c in chunks)
        {
            if (c == null) continue;
            if (c.sprite != null) Destroy(c.sprite);
            if (c.tex != null) Destroy(c.tex);
        }
    }
}
