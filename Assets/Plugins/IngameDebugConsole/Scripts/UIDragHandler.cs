using UnityEngine;
using UnityEngine.EventSystems;

public class UIDragHandler : MonoBehaviour, IDragHandler
{
    [SerializeField]
    private RectTransform rectTransform;
    [SerializeField]
    private GameObject rootObject;

    public void OnDrag(PointerEventData eventData)
    {
        rectTransform.anchoredPosition += eventData.delta / rootObject.transform.localScale.magnitude;
    }
}