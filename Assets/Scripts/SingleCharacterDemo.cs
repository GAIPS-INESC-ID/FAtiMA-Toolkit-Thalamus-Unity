using AssetManagerPackage;
using Assets.Scripts;
using IntegratedAuthoringTool;
using IntegratedAuthoringTool.DTOs;
using RolePlayCharacter;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Utilities;
using WellFormedNames;

public class SingleCharacterDemo : MonoBehaviour
{
    [SerializeField]
    private Text AgentUtterance;

    [SerializeField]
    private List<Button> DialogueButtons;

    private IntegratedAuthoringToolAsset iat;
    private RolePlayCharacterAsset rpc;

    private string CurrentDialogueState;

    private ThalamusConnector TUC;

    // Use this for initialization
    private void Start()
    {
        TUC = new ThalamusConnector(this);

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
    }

    // Update is called once per frame
    private void Update()
    {
        var state = rpc.GetBeliefValue(String.Format(IATConsts.DIALOGUE_STATE_PROPERTY, IATConsts.PLAYER));
        if (state != CurrentDialogueState)
        {
            CurrentDialogueState = state;
            AgentUtterance.text = DetermineAgentDialogue();
            UpdatePlayerDialogOptions();
        }
    }

    private string DetermineAgentDialogue()
    {
        var action = rpc.Decide().FirstOrDefault();
        if (action != null && action.Key.ToString().Equals(IATConsts.DIALOG_ACTION_KEY))
        {
            Name cs = action.Parameters[0];
            Name ns = action.Parameters[1];
            Name m = action.Parameters[2];
            Name s = action.Parameters[3];
            var dialogs = iat.GetDialogueActions(IATConsts.AGENT, cs, ns, m, s);
            var dialog = dialogs.Shuffle().FirstOrDefault();

            HandleSpeakAction(rpc.CharacterName.ToString(), dialog.Id, IATConsts.PLAYER);

            CurrentDialogueState = ns.ToString();
            
            return dialog.Utterance;
        }
        else
        {
            return String.Empty;
        }
    }

    private void UpdatePlayerDialogOptions()
    {
        var dOpt = iat.GetDialogueActionsByState(IATConsts.PLAYER, CurrentDialogueState);
        for (int i = 0; i < DialogueButtons.Count(); i++)
        {
            if (i >= dOpt.Count())
            {
                DialogueButtons[i].gameObject.SetActive(false);
            }
            else
            {
                DialogueButtons[i].gameObject.SetActive(true);
                DialogueButtons[i].GetComponentInChildren<Text>().text = dOpt.ElementAt(i).Utterance;
                var id = dOpt.ElementAt(i).Id;
                DialogueButtons[i].onClick.AddListener(() => OnDialogueSelected(id));
            }
        }
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
        }
        else
        {
            d = iat.GetDialogActionById(IATConsts.AGENT, id);
            TUC.PerformUtterance("", d.Utterance, "");
        }

        var dAct = string.Format("Speak({0},{1},{2},{3})", d.CurrentState, d.NextState, d.GetMeaningName(), d.GetStylesName());
        var dStateProperty = string.Format(IATConsts.DIALOGUE_STATE_PROPERTY, IATConsts.PLAYER);

        rpc.Perceive(EventHelper.ActionEnd(s, dAct, t));
        rpc.Perceive(EventHelper.PropertyChange(dStateProperty, d.NextState, s));
    }
}