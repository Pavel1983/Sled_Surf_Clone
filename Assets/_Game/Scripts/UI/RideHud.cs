using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen HUD for the slingshot and the ride. The hierarchy lives in the scene.
/// This class only shows and hides the screens and writes the numbers into them.
/// Aiming shows the pull tension. The ride shows distance, speed and progress to the finish.
/// The start screen sells upgrades, and the results card closes a run.
/// </summary>
[DisallowMultipleComponent]
public class RideHud : MonoBehaviour
{
    [Serializable]
    private class ShopRow
    {
        public UpgradeId id;
        public Text title;
        public Text rate;
        public Text cost;
        public Image[] pips;
        public Button button;
        public Image plate;
        public Image icon;
        [Tooltip("The coin next to the price. Hidden once the upgrade is maxed and there is no price.")]
        public GameObject costIcon;
        [Tooltip("One picture per tier, from the first to the best. Past the last one the best stays.")]
        public Sprite[] tiers;
    }

    private const float BarWidth = 22f;
    private const float BarHeight = 520f;
    private const float MetersPerSecondToKmh = 3.6f;

    private static readonly Color LaunchTint = new Color(0.93f, 0.27f, 0.34f, 1f);
    private static readonly Color SledTint = new Color(0.2f, 0.55f, 0.98f, 1f);
    private static readonly Color IncomeTint = new Color(0.98f, 0.74f, 0.12f, 1f);

    /// <summary>
    /// Raised when any button of the HUD is pressed.
    /// </summary>
    public event Action Clicked;

    [SerializeField] private Sled sled;
    [SerializeField] private SlingshotPull slingshot;
    [SerializeField] private PathCoins pathCoins;
    [SerializeField] private float speedGaugeMaxKmh = 180f;
    [SerializeField] private float speedSharpness = 9f;

    [Header("Layout")]
    [SerializeField] private RectTransform safeArea;
    [SerializeField] private GameObject tensionRoot;
    [SerializeField] private GameObject rideRoot;
    [SerializeField] private GameObject startRoot;
    [SerializeField] private GameObject resultsRoot;

    [Header("Aim")]
    [SerializeField] private UiArc tensionFill;
    [SerializeField] private Text tensionLabel;

    [Header("Ride")]
    [SerializeField] private Text distanceLabel;
    [SerializeField] private RectTransform distancePlate;
    [SerializeField] private RectTransform distanceShadow;
    [SerializeField] private UiArc speedFill;
    [SerializeField] private Text speedLabel;
    [SerializeField] private RectTransform progressFill;
    [SerializeField] private Text progressLabel;
    [SerializeField] private RectTransform bestRoot;
    [SerializeField] private Text coinLabel;
    [SerializeField] private Button restartButton;

    [Header("Start")]
    [SerializeField] private Button playButton;
    [SerializeField] private ShopRow[] shopRows;

    [Header("Results")]
    [SerializeField] private Text resultsDistance;
    [SerializeField] private Text resultsMoney;
    [SerializeField] private Button continueButton;

    private RunEconomy economy;
    private Rect appliedSafe;
    private float displayedKmh;

    private void Awake()
    {
        playButton.onClick.AddListener(PressPlay);
        restartButton.onClick.AddListener(PressRestart);
        continueButton.onClick.AddListener(PressRestart);
        for (int i = 0; i < shopRows.Length; i++)
        {
            UpgradeId id = shopRows[i].id;
            shopRows[i].button.onClick.AddListener(() => PressUpgrade(id));
        }
    }

    private void LateUpdate()
    {
        if (economy == null)
        {
            return;
        }

        ApplySafeArea();
        ShowCoins();

        bool aiming = slingshot.IsAiming;
        bool riding = sled.IsRiding;
        if (tensionRoot.activeSelf != aiming)
        {
            tensionRoot.SetActive(aiming);
        }

        if (rideRoot.activeSelf != riding)
        {
            rideRoot.SetActive(riding);
            if (!riding)
            {
                displayedKmh = 0f;
            }
        }

        if (aiming)
        {
            ShowTension();
        }

        if (riding)
        {
            ShowRide();
        }

        ShowScreens();
    }

    /// <summary>
    /// Hands over the economy the HUD reads prices and coins from. Called once by the composition root.
    /// </summary>
    public void Initialize(RunEconomy runEconomy)
    {
        economy = runEconomy;
    }

    private void PressPlay()
    {
        sled.ArmPlay();
        Clicked?.Invoke();
    }

    private void PressRestart()
    {
        sled.Restart();
        Clicked?.Invoke();
    }

    private void PressUpgrade(UpgradeId id)
    {
        economy.TryBuy(id);
        Clicked?.Invoke();
    }

    private void ShowCoins()
    {
        int coins = economy.DisplayCoins(pathCoins.Collected, sled.RunDistanceMeters);
        SetText(coinLabel, coins.ToString(CultureInfo.InvariantCulture));
    }

    private void ShowTension()
    {
        float tension = slingshot.Tension01;
        tensionFill.Fill = tension;
        SetText(tensionLabel, Percent(tension));
    }

    private void ShowRide()
    {
        float target = sled.SurfaceSpeed * MetersPerSecondToKmh;
        if (Mathf.Abs(displayedKmh - target) < 0.08f)
        {
            displayedKmh = target;
        }
        else
        {
            float blend = 1f - Mathf.Exp(-speedSharpness * Time.deltaTime);
            displayedKmh = Mathf.Lerp(displayedKmh, target, blend);
        }

        int speed = Mathf.Max(0, Mathf.RoundToInt(displayedKmh));
        SetText(speedLabel, speed.ToString(CultureInfo.InvariantCulture));
        speedFill.Fill = Mathf.Clamp01(displayedKmh / Mathf.Max(1f, speedGaugeMaxKmh));

        int meters = Mathf.Max(0, Mathf.RoundToInt(sled.RunDistanceMeters));
        SetText(distanceLabel, meters.ToString(CultureInfo.InvariantCulture) + "m");
        // The plate grows with the number, so a long distance still fits inside it.
        float plateWidth = Mathf.Max(168f, distanceLabel.preferredWidth + 72f);
        distancePlate.sizeDelta = new Vector2(plateWidth, 96f);
        distanceShadow.sizeDelta = new Vector2(plateWidth + 18f, 108f);

        float finishMeters = Mathf.Max(0.01f, sled.FinishMeters);
        float progress = Mathf.Clamp01(sled.RunDistanceMeters / finishMeters);
        SetText(progressLabel, Percent(progress));
        float fillHeight = BarHeight * progress;
        bool showFill = fillHeight > 2f;
        if (progressFill.gameObject.activeSelf != showFill)
        {
            progressFill.gameObject.SetActive(showFill);
        }

        if (showFill)
        {
            progressFill.sizeDelta = new Vector2(BarWidth, fillHeight);
        }

        bool showBest = sled.PreviousBestMeters > 1f;
        if (bestRoot.gameObject.activeSelf != showBest)
        {
            bestRoot.gameObject.SetActive(showBest);
        }

        if (showBest)
        {
            float along = Mathf.Clamp01(sled.PreviousBestMeters / finishMeters);
            bestRoot.anchoredPosition = new Vector2(16f, BarHeight * along);
        }
    }

    private void ShowScreens()
    {
        bool showStart = !sled.PlayArmed && !sled.IsRiding && !sled.IsStopped;
        bool showResults = sled.ResultsReady;
        if (startRoot.activeSelf != showStart)
        {
            startRoot.SetActive(showStart);
        }

        if (resultsRoot.activeSelf != showResults)
        {
            resultsRoot.SetActive(showResults);
        }

        if (showStart)
        {
            for (int i = 0; i < shopRows.Length; i++)
            {
                ShowRow(shopRows[i]);
            }
        }

        if (showResults)
        {
            SetText(resultsDistance, FormatDistance(sled.RunDistanceMeters));
            SetText(resultsMoney, "+" + economy.LastRunValue.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void ShowRow(ShopRow row)
    {
        int level = economy.Level(row.id);
        SetText(row.title, DisplayName(row.id) + "  " + level.ToString(CultureInfo.InvariantCulture));
        if (row.rate != null)
        {
            SetText(row.rate, economy.IncomePerKilometer.ToString(CultureInfo.InvariantCulture) + "/km");
        }

        bool maxed = economy.IsMaxed(row.id);
        SetText(row.cost, maxed ? "MAX" : FormatMoney(economy.NextCost(row.id)));
        if (row.costIcon.activeSelf == maxed)
        {
            row.costIcon.SetActive(!maxed);
        }

        Sprite picture = row.tiers[Mathf.Min(economy.Tier(row.id), row.tiers.Length - 1)];
        if (row.icon.sprite != picture)
        {
            row.icon.sprite = picture;
        }

        // The button and the pips take the upgrade's color, dimmed when it cannot be bought.
        Color theme = Theme(row.id);
        bool canBuy = economy.CanBuy(row.id);
        row.button.interactable = canBuy;
        row.plate.color = canBuy ? theme : new Color(theme.r * 0.55f, theme.g * 0.55f, theme.b * 0.55f, 1f);

        int filled = economy.PipCount(row.id);
        Color empty = new Color(theme.r * 0.35f, theme.g * 0.35f, theme.b * 0.35f, 0.4f);
        for (int i = 0; i < row.pips.Length; i++)
        {
            row.pips[i].color = i < filled ? theme : empty;
        }
    }

    /// <summary>
    /// Keeps the HUD inside the notch and the rounded corners of a phone screen.
    /// </summary>
    private void ApplySafeArea()
    {
        if (Screen.width < 2 || Screen.height < 2)
        {
            return;
        }

        Rect safe = Screen.safeArea;
        if (safe == appliedSafe)
        {
            return;
        }

        appliedSafe = safe;
        Vector2 min = safe.position;
        Vector2 max = safe.position + safe.size;
        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;
        safeArea.anchorMin = min;
        safeArea.anchorMax = max;
        safeArea.offsetMin = Vector2.zero;
        safeArea.offsetMax = Vector2.zero;
    }

    private static string DisplayName(UpgradeId id)
    {
        switch (id)
        {
            case UpgradeId.Launch:
                return "SLINGSHOT";
            case UpgradeId.Friction:
                return "SLED";
            default:
                return "INCOME";
        }
    }

    private static Color Theme(UpgradeId id)
    {
        switch (id)
        {
            case UpgradeId.Launch:
                return LaunchTint;
            case UpgradeId.Friction:
                return SledTint;
            default:
                return IncomeTint;
        }
    }

    private static string FormatDistance(float meters)
    {
        meters = Mathf.Max(0f, meters);
        if (meters >= 1000f)
        {
            return (meters / 1000f).ToString("0.00", CultureInfo.InvariantCulture) + " km";
        }

        return Mathf.RoundToInt(meters).ToString(CultureInfo.InvariantCulture) + " m";
    }

    private static string FormatMoney(int value)
    {
        if (value >= 1000000)
        {
            return (value / 1000000f).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        }

        if (value >= 1000)
        {
            return (value / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "K";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Percent(float unit)
    {
        int percent = Mathf.Clamp(Mathf.RoundToInt(unit * 100f), 0, 100);
        return percent.ToString(CultureInfo.InvariantCulture) + "%";
    }

    // Writing the same string again still rebuilds the text mesh, so skip it.
    private static void SetText(Text label, string value)
    {
        if (label.text != value)
        {
            label.text = value;
        }
    }
}
