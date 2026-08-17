using UnityEngine;

public class FlyPanel : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public KeyCode toggleKey;
    public bool hideOnInit;
    bool ishidden;
    Vector3 originPos;
    bool initialized;

    void Awake()
    {
        originPos=transform.position;
        initialized=true;
    }

    void Start()
    {
        if(hideOnInit)
        {
            Hide();
        }
    }
    public void Hide()
    {
        EnsureInitialized();
        transform.position=new Vector3(-2000,0,0);
        ishidden=true;
    }
    public void Show()
    {
        EnsureInitialized();
        transform.position=originPos;
        ishidden=false;
    }
    void EnsureInitialized()
    {
        if(initialized)
        {
            return;
        }
        originPos=transform.position;
        initialized=true;
    }
    // Update is called once per frame
    void Update()
    {
        
        if(Input.GetKeyDown(toggleKey))
        {
            if(CityStateSim.UI.StorageChestSession.TryConsumeFlyPanelToggle(this, toggleKey))
            {
                return;
            }

            if(ishidden)
            {
                Show();
            }
            else
            {
                Hide();
            }
        }
    }
}
