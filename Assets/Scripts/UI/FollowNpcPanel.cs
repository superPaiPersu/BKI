using System.Collections.Generic;
using CityStateSim.Behavior;
using UnityEngine;

public class FollowNpcPanel : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public List<FollowNpcItem> npcLt=new List<FollowNpcItem>();
    public Transform itemsFather;
    void Start()
    {
        for(int i=0;i<itemsFather.childCount;i++)
        {
            npcLt.Add(itemsFather.GetChild(i).GetComponent<FollowNpcItem>());
        }


    }
    public bool CheckFull()
    {
        EnsureNpcList();
        foreach(var i in npcLt)
        {
            if(!i.isActive)
                return false;
        }
        return true;
    }

    public bool TryAdd(NpcActionExecutor npc)
    {
        if(npc == null)
            return false;

        EnsureNpcList();

        foreach(var i in npcLt)
        {
            if(i != null && i.isActive && i.inpc == npc)
                return true;
        }

        if(CheckFull())
            return false;

        foreach(var i in npcLt)
        {
            if(i != null && !i.isActive)
            {
                i.Add(npc);
                return true;
            }
        }

        return false;
    }

    private void EnsureNpcList()
    {
        if(npcLt.Count > 0)
            return;

        for(int i=0;i<itemsFather.childCount;i++)
        {
            FollowNpcItem item = itemsFather.GetChild(i).GetComponent<FollowNpcItem>();
            if(item != null)
                npcLt.Add(item);
        }
    }

    public void CheckClicked()
    {
        if(Input.GetMouseButtonDown(0))
        {
            for(int i=0;i<transform.childCount;i++)
            {
                if(npcLt[i].isHoverd)
                {
                    
                    npcLt[i].Clear();
                    ;
                }
            }
        }
        

    }
    // Update is called once per frame
    void Update()
    {
        CheckClicked();
    }
}
