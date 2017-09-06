using AssetManagerPackage;
using Assets.Scripts;
using IntegratedAuthoringTool;
using IntegratedAuthoringTool.DTOs;
using RolePlayCharacter;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Utilities;
using WellFormedNames;
using System.Text.RegularExpressions;
using UnityEngine.SceneManagement;

public class SingleCharacterDemo : MonoBehaviour
{
    [SerializeField]
    private Text AgentUtterance;

    [SerializeField]
    private Text AgentEmotionalStateText;

    [SerializeField]
    private Button PreviousPageButton;

    [SerializeField]
    private Button NextPageButton;

    [SerializeField]
    private List<Button> DialogueButtons;

    private IntegratedAuthoringToolAsset iat;
    private RolePlayCharacterAsset rpc;

    private string CurrentDialogueState;
    private int currentPageNumber = 0; //Dialogue options

    private ThalamusConnector TUC;

    private bool configConnectThalamus;
    private float configurationUpdateFrequency;
    private bool configResetEmotions;
    private bool configLog;

    private void LoadConfiguration(RolePlayCharacterAsset rpc)
    {
        bool.TryParse(rpc.GetBeliefValue("Configuration(ConnectThalamus)"), out configConnectThalamus);
        float.TryParse(rpc.GetBeliefValue("Configuration(UpdateFrequency)"), out configurationUpdateFrequency);
        bool.TryParse(rpc.GetBeliefValue("Configuration(ResetEmotions)"), out configResetEmotions);
        bool.TryParse(rpc.GetBeliefValue("Configuration(Log)"), out configLog);
    }

    // Use this for initialization
    private void Start()
    {
        AssetManager.Instance.Bridge = new AssetManagerBridge();

        var streamingAssetsPath = Application.streamingAssetsPath;

#if UNITY_EDITOR || UNITY_STANDALONE
        streamingAssetsPath = "file://" + streamingAssetsPath;
#endif
        iat = IntegratedAuthoringToolAsset.LoadFromFile("ConversationDemo.iat");
        var characterSources = iat.GetAllCharacterSources().ToList();

        rpc = RolePlayCharacterAsset.LoadFromFile(characterSources[0].Source);
        rpc.LoadAssociatedAssets();
        iat.BindToRegistry(rpc.DynamicPropertiesRegistry);

        this.LoadConfiguration(rpc);
        if (configConnectThalamus)
        {
            TUC = new ThalamusConnector(this);
        }

        StartCoroutine(UpdateEmotionalState(configurationUpdateFrequency));
    }

    // Update is called once per frame
    private void Update()
    {
        var state = rpc.GetBeliefValue(String.Format(IATConsts.DIALOGUE_STATE_PROPERTY, IATConsts.PLAYER));

        if (state != CurrentDialogueState)
        {
            currentPageNumber = 0;
            CurrentDialogueState = state;
            var agentDialogue = DetermineAgentDialogue(); 
            if (TUC != null && !string.IsNullOrEmpty(agentDialogue))
            {
                TUC.PerformUtterance("", agentDialogue, "");
            }
            AgentUtterance.text = agentDialogue;
            UpdatePlayerDialogOptions();
        }
    }


    private IEnumerator UpdateEmotionalState(float updateTime)
    {
        while (true)
        {
            var SIPlayerAgent = rpc.GetBeliefValue("ToM(Player, SI(SELF))");
            var SIAgentPlayer = rpc.GetBeliefValue("SI(Player)");
            
            if (!rpc.GetAllActiveEmotions().Any())
            {
                this.AgentEmotionalStateText.text = "Mood: " + rpc.Mood + ", Emotions: [], SI(A,P): " + SIAgentPlayer + " SI(P,A): " + SIPlayerAgent;
            }
            else
            {
                var aux = "Mood: " + rpc.Mood + ", Emotions: [";

                StringBuilder builder = new StringBuilder();

                var query = rpc.GetAllActiveEmotions().OrderByDescending(e => e.Intensity);

                foreach (var emt in query)
                {
                    builder.AppendFormat("{0}: {1:N2}, ", emt.Type, emt.Intensity);
                }
                aux += builder.Remove(builder.Length - 2, 2);
                this.AgentEmotionalStateText.text = aux + "], SI(A,P): " + SIAgentPlayer + " SI(P,A): " + SIPlayerAgent;
            }
             
            rpc.Update();

            //Change Posture
            var actions = rpc.Decide().ToArray();
            var action = actions.Where(a => a.Key.ToString().Equals("ChangePosture")).FirstOrDefault();

            if(action != null)
            {
                var posture = action.Parameters[0];
                if (TUC != null)
                {
                    TUC.SetPosture("", posture.ToString(), 1, 0);
                }
            }
            yield return new WaitForSeconds(updateTime);
        }
    }


    private string DetermineAgentDialogue()
    {
        var actions = rpc.Decide().ToArray();
        var action = actions.Where(a => a.Key.ToString().Equals(IATConsts.DIALOG_ACTION_KEY)).FirstOrDefault();

        if (action != null)
        {
            Name cs = action.Parameters[0];
            Name ns = action.Parameters[1];
            Name m = action.Parameters[2];
            Name s = action.Parameters[3];
            var dialogs = iat.GetDialogueActions(IATConsts.AGENT, cs, ns, m, s);
            var dialog = dialogs.Shuffle().FirstOrDefault();

            HandleSpeakAction(rpc.CharacterName.ToString(), dialog.Id, IATConsts.PLAYER);

            CurrentDialogueState = ns.ToString();
            var processed = this.ReplaceVariablesInDialogue(dialog.Utterance);
            return processed;
        }
        else
        {
            return String.Empty;
        }
    }

    //This method will replace every belief within [[ ]] by its value
    private string ReplaceVariablesInDialogue(string dialog)
    {
        var tokens = Regex.Split(dialog, @"\[|\]\]");
       
        var result = string.Empty;
        bool process = false;
        foreach (var t in tokens)
        {
            if (process)
            {
                var beliefValue = rpc.GetBeliefValue(t);
                result += beliefValue;
                process = false;
            }else if (t == string.Empty)
            {
                process = true;
                continue;
            }
            else
            {
                result += t;
            }
        }
        return result;
    }

    private void UpdatePlayerDialogOptions()
    {
        var dOpt = iat.GetDialogueActionsByState(IATConsts.PLAYER, CurrentDialogueState);
        var additional = iat.GetDialogueActionsByState(IATConsts.PLAYER, "Any");

        dOpt = dOpt.Concat(additional);

        var pageSize = DialogueButtons.Count();

        this.UpdatePageButtons(dOpt.Count(), pageSize);

        var aux = currentPageNumber * pageSize;

        for (int i = 0; i < DialogueButtons.Count(); i++)
        {
            if (i + aux >= dOpt.Count())
            {
                DialogueButtons[i].gameObject.SetActive(false);
            }
            else
            {
                DialogueButtons[i].gameObject.SetActive(true);
                DialogueButtons[i].GetComponentInChildren<Text>().text = dOpt.ElementAt(i + aux).Utterance;
                var id = dOpt.ElementAt(i+ aux).Id;
                DialogueButtons[i].onClick.RemoveAllListeners();
                DialogueButtons[i].onClick.AddListener(() => OnDialogueSelected(id));
            }
        }
    }

    private void UpdatePageButtons(int numOfOptions, int pageSize)
    {
        if (numOfOptions > pageSize * (currentPageNumber+1))
        {
            this.NextPageButton.gameObject.SetActive(true);
        }
        else
        {
            this.NextPageButton.gameObject.SetActive(false);
        }

        if (currentPageNumber > 0)
        {
            this.PreviousPageButton.gameObject.SetActive(true);
        }
        else
        {
            this.PreviousPageButton.gameObject.SetActive(false);
        }
    }

    public void OnNextPage()
    {
        currentPageNumber++;
        UpdatePlayerDialogOptions();
    }

    public void OnPreviousPage()
    {
        currentPageNumber--;
        UpdatePlayerDialogOptions();
    }


    public void OnDialogueSelected(Guid dialogId)
    {
        HandleSpeakAction(IATConsts.PLAYER, dialogId, rpc.CharacterName.ToString());
    }

    private void HandleSpeakAction(string s, Guid id, string t)
    {
        DialogueStateActionDTO d;
        if (s.Equals(IATConsts.PLAYER))
        {
            d = iat.GetDialogActionById(IATConsts.PLAYER, id);

            if (d.Utterance.Contains("Restart"))
            {
                StopAllCoroutines();
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            if (configResetEmotions)
            {
                var currentMood = rpc.Mood;
                rpc.ResetEmotionalState();
                rpc.Mood = currentMood;
            }
        }
        else
        {
            d = iat.GetDialogActionById(IATConsts.AGENT, id);
        }

        var dAct = string.Format("Speak({0},{1},{2},{3})", d.CurrentState, d.NextState, d.GetMeaningName(), d.GetStylesName());
        var dStateProperty = string.Format(IATConsts.DIALOGUE_STATE_PROPERTY, IATConsts.PLAYER);

        rpc.Perceive(EventHelper.ActionEnd(s, dAct, t));
        rpc.Perceive(EventHelper.PropertyChange(dStateProperty, d.NextState, s));

        if (configLog)
        {
            this.SaveState();
        }
    }

    private void SaveState()
    {
        const string datePattern = "dd-MM-yyyy-H-mm-ss";
        rpc.SaveToFile(Application.streamingAssetsPath
            + "\\Logs\\" + rpc.CharacterName
            + "-" + DateTime.Now.ToString(datePattern) + ".rpc");
    }
}