using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class BattleHudRig : MonoBehaviour
{
    [Header("Wild UI")]
    public GameObject wildPanel;
    public Slider wildHPBar;
    public Image wildIcon;
    public TextMeshProUGUI wildNameText;
    public TextMeshProUGUI wildLevelText;
    public TextMeshProUGUI wildIdText;
    public TextMeshProUGUI wildTypeText;
    public TextMeshProUGUI wildRarityText;
    public TextMeshProUGUI wildHPText;
    public TextMeshProUGUI wildATKText;
    public TextMeshProUGUI wildDEFText;
    public TextMeshProUGUI wildSPDText;

    [Header("Player UI")]
    public GameObject playerPanel;
    public Slider playerHPBar;
    public Image playerIcon;
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI playerLevelText;
    public TextMeshProUGUI playerIdText;
    public TextMeshProUGUI playerTypeText;
    public TextMeshProUGUI playerRarityText;
    public TextMeshProUGUI playerHPText;
    public TextMeshProUGUI playerATKText;
    public TextMeshProUGUI playerDEFText;
    public TextMeshProUGUI playerSPDText;

    [Header("Bench UI")]
    public Button benchBtn1;
    public Button benchBtn2;
    public Image benchImg1;
    public Image benchImg2;
    public TextMeshProUGUI benchHPText1;
    public TextMeshProUGUI benchHPText2;

    [Header("Battle UX")]
    public BattleFeedbackManager feedback;
    public BattleTextBoxUI battleTextBox;
    public BattleSwitchToggle bottomToggle;
}