using System.Collections;
using TMPro;
using UnityEngine;

public class DayOverCheck : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public TMP_Text waitingTxt;

    public GameObject nextDayButton;

    FlyPanel fly;
    bool isAICompleted;
    public float waitingPointUpdateTime;
    string waitingTxtPoint;

    public static bool IsUserInputLocked { get; private set; }

    void Awake()
    {
        UnlockUserInput();
    }

    void Start()
    {
        fly = GetComponent<FlyPanel>();
    }

    public void SetUserInputLocked(bool locked)
    {
        IsUserInputLocked = locked;
    }

    public void LockUserInput()
    {
        SetUserInputLocked(true);
    }

    public void UnlockUserInput()
    {
        SetUserInputLocked(false);
    }

    IEnumerator WaitForAI()
    {
        int cnt = 1;
        while (!isAICompleted)
        {
            yield return new WaitForSeconds(waitingPointUpdateTime);
            cnt++;
            if (cnt > 6)
            {
                cnt = 1;
                waitingTxtPoint = ".";
            }
            else
            {
                waitingTxtPoint += ".";
            }
        }
    }

    void ShowStatusChanged()
    {
        // 显示玩家的各种属性变化结算，比如声望，金钱。
        // 目前不实现。
        ;
    }

    void ShowWaitingUI()
    {
        // 显示等待 UI，可以是文字，等待结算中。
        waitingTxt.text = "正在结算中" + waitingTxtPoint;
    }

    public void SettlementStarted()
    {
        LockUserInput();
        isAICompleted = false;

        StartCoroutine(WaitForAI());
        nextDayButton.SetActive(false);
        fly.Show();
    }

    public void Close()
    {
        UnlockUserInput();
        fly.Hide();
    }

    public void SettlementFinished()
    {
        isAICompleted = true;
        waitingTxt.text = "";
        ShowStatusChanged();
    }

    // Update is called once per frame
    void Update()
    {
        if (!isAICompleted)
        {
            ShowWaitingUI();
        }
        else
        {
            nextDayButton.SetActive(true);
        }
    }
}
