using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class MapPaperSync : MonoBehaviour
{
    [SerializeField] RawImage mapRawImage;

    RectTransform viewportRect;
    RectTransform mapImageRect;
    Material      mapMaterial;

    // The material actually used by the CanvasRenderer (accounts for masks etc.)
    CanvasRenderer bgCanvasRenderer;

    void Awake()
    {
        viewportRect     = GetComponent<RectTransform>();
        bgCanvasRenderer = GetComponent<CanvasRenderer>();

        if (mapRawImage == null)
        {
            Debug.LogWarning("[MapPaperSync] mapRawImage not assigned");
            enabled = false;
            return;
        }

        mapImageRect = mapRawImage.GetComponent<RectTransform>();
        mapMaterial  = mapRawImage.material;
    }

    void LateUpdate()
    {
        // materialForRendering is the live material the CanvasRenderer actually draws with
        var bgMat = GetComponent<Graphic>().materialForRendering;
        if (bgMat == null) return;

        Vector3[] corners = new Vector3[4];
        mapImageRect.GetWorldCorners(corners);

        Vector2 viewportBL = new Vector2(viewportRect.rect.xMin, viewportRect.rect.yMin);
        Vector2 mapBL   = (Vector2)viewportRect.InverseTransformPoint(corners[0]) - viewportBL;
        Vector2 mapTR   = (Vector2)viewportRect.InverseTransformPoint(corners[2]) - viewportBL;
        Vector2 mapSize = mapTR - mapBL;
        Vector2 bgSize  = viewportRect.rect.size;

        if (mapSize.x == 0 || mapSize.y == 0) return;

        Vector2 mapTiling = mapMaterial.GetTextureScale("_PaperTex");
        Vector2 mapOffset = mapMaterial.GetTextureOffset("_PaperTex");

        Vector2 bgTiling = bgSize / mapSize * mapTiling;
        Vector2 bgOffset = (-mapBL / mapSize) * mapTiling + mapOffset;

        bgMat.SetVector("_BgPaperST", new Vector4(bgTiling.x, bgTiling.y, bgOffset.x, bgOffset.y));
    }
}
