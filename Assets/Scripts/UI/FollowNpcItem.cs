using CityStateSim.Behavior;
using UnityEngine;
using UnityEngine.UI;

public class FollowNpcItem : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    RectTransform rt;
    Canvas parentCanvas;
    public GameObject deleteImg;
    public Image npcImg;

    public NpcActionExecutor inpc;
    public bool isActive,isHoverd;
    void Start()
    {
        rt=transform.GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
    }

    public bool GetClickArea_pos(Vector3 pos)
    {
        if (rt == null)
        {
            rt = transform.GetComponent<RectTransform>();
        }

        if (parentCanvas == null)
        {
            parentCanvas = GetComponentInParent<Canvas>();
        }

        return rt != null
            && RectTransformUtility.RectangleContainsScreenPoint(
                rt,
                pos,
                UiScreenPositionUtility.GetCanvasCamera(parentCanvas));
    }
    public void Add(NpcActionExecutor npc)
    {
        inpc=npc;
        
        npcImg.color=new Color(1,1,1,1);
        npcImg.sprite=npc.GetHeadIcon();
        isActive=true;
    }
    public void Clear()
    {
        inpc.StopFollowingPlayer("玩家停止了带路。");
        inpc=null;
        npcImg.color=new Color(0,0,0,0);
        isActive=false;
        isHoverd=false;
        deleteImg.SetActive(false);
        
    }
    
    // Update is called once per frame
    void Update()
    {
        if(!isActive)
            return;
        if(GetClickArea_pos(Input.mousePosition))
        {
            isHoverd=true;
            
        }
        else
        {
            isHoverd=false;
            
        }
        deleteImg.SetActive(isHoverd);
    }
}
