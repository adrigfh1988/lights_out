using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The 3x3 item panel opened by talking to the salesman (E at the counter). Lazily built on first
/// Open(), like the pause menu builds its panel on first pause.
/// </summary>
public class ShopMenu : MonoBehaviour
{
    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);
    private static readonly Color CardColor = new Color(0.08f, 0.08f, 0.10f, 0.96f);
    private static readonly Color Grey = new Color(0.75f, 0.75f, 0.78f);
    private static readonly Color Amber = new Color(1f, 0.72f, 0.42f);
    private static readonly Color WalletColor = new Color(0.7f, 0.9f, 1f);

    private sealed class ShopCard
    {
        public ShopItemDef Def;
        public TextMeshProUGUI Status;
        public Button BuyButton;
        public TextMeshProUGUI BuyLabel;
        public Button[] SwatchButtons;
        public TextMeshProUGUI[] SwatchLabels;
    }

    private Transform _player;
    private PlayerHud _hud;
    private Flashlight _flashlight;

    private GameObject _panel;
    private TextMeshProUGUI _walletText;
    private readonly List<ShopCard> _cards = new List<ShopCard>();
    private int _closedFrame = -1;

    private AudioSource _tickSource;
    private AudioClip _tickClip;

    public bool IsOpen { get; private set; }

    /// <summary>True on the exact frame Close() ran. PauseMenu uses this to swallow the Esc that closed the shop panel.</summary>
    public bool ClosedThisFrame => Time.frameCount == _closedFrame;

    public void Configure(Transform player, PlayerHud hud, Flashlight flashlight)
    {
        _player = player;
        _hud = hud;
        _flashlight = flashlight;
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;

        if (_panel == null) BuildPanel();
        if (_panel != null)
        {
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
        }
        Refresh();

        PlayerLock.Freeze(_player, true);
        PlayerLock.SetCursorFree(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _closedFrame = Time.frameCount;

        if (_panel != null) _panel.SetActive(false);
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
    }

    private void Update()
    {
        if (!IsOpen) return;

        // Held every frame: StarterAssetsInputs re-locks the cursor on every focus change.
        PlayerLock.SetCursorFree(true);

        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) pressed = true;
        if (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame) pressed = true;
#endif
        if (pressed) Close();
    }

    // ---------------------------------------------------------------- panel

    private void BuildPanel()
    {
        RuntimeUi.EnsureEventSystem();
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _panel = RuntimeUi.CreatePanel(canvas.transform, "ShopScreen", new Color(0.02f, 0.02f, 0.03f, 0.94f));

        TextMeshProUGUI title = RuntimeUi.CreateText(_panel.transform, "Title", "BETWEEN FLOORS", 64f, Amber);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1400f, 90f));

        _walletText = RuntimeUi.CreateText(_panel.transform, "Wallet", "SHARDS  0", 38f, WalletColor);
        RuntimeUi.Place(_walletText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(900f, 50f));

        TextMeshProUGUI tip = RuntimeUi.CreateText(_panel.transform, "Tip", "Prices rise the deeper you go.", 24f, Grey);
        RuntimeUi.Place(tip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(900f, 40f));

        float[] xs = { -575f, 0f, 575f };
        float[] ys = { 150f, -80f, -310f };
        for (int i = 0; i < ShopCatalogue.Items.Length && i < 9; i++)
        {
            BuildCard(ShopCatalogue.Items[i], new Vector2(xs[i % 3], ys[i / 3]));
        }

        Button back = RuntimeUi.CreateButton(_panel.transform, "BACK", new Vector2(0f, 40f), new Vector2(420f, 70f), 34f, ButtonIdle, ButtonHover, Color.white);
        back.onClick.AddListener(Close);
    }

    private void BuildCard(ShopItemDef def, Vector2 position)
    {
        GameObject cardObj = RuntimeUi.CreatePanel(_panel.transform, def.Name + " Card", CardColor);
        RuntimeUi.Place(cardObj.GetComponent<Image>().rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(540f, 210f));

        TextMeshProUGUI nameText = RuntimeUi.CreateText(cardObj.transform, "Name", def.Name, 30f, Color.white);
        nameText.fontStyle = FontStyles.Bold;
        nameText.alignment = TextAlignmentOptions.TopLeft;
        PlaceTopLeft(nameText.rectTransform, new Vector2(20f, -18f), new Vector2(400f, 40f));

        TextMeshProUGUI blurbText = RuntimeUi.CreateText(cardObj.transform, "Blurb", def.Blurb, 22f, Grey);
        blurbText.alignment = TextAlignmentOptions.TopLeft;
        PlaceTopLeft(blurbText.rectTransform, new Vector2(20f, -56f), new Vector2(500f, 60f));

        TextMeshProUGUI statusText = RuntimeUi.CreateText(cardObj.transform, "Status", "", 22f, Grey);
        statusText.alignment = TextAlignmentOptions.TopLeft;
        PlaceTopLeft(statusText.rectTransform, new Vector2(20f, -120f), new Vector2(400f, 34f));

        ShopCard card = new ShopCard { Def = def, Status = statusText };

        if (def.Kind == ItemKind.Cosmetic)
        {
            (string Name, Color Colour)[] palette = def.Id == ShopItem.TorchColour ? ShopCatalogue.TorchColours : ShopCatalogue.HudTints;
            card.SwatchButtons = new Button[3];
            card.SwatchLabels = new TextMeshProUGUI[3];
            float[] xs = { -170f, 0f, 170f };

            for (int i = 0; i < 3; i++)
            {
                // Palette index 0 is the default look and is never for sale - swatches map to indices 1..3.
                int paletteIndex = i + 1;
                Color swatch = palette[paletteIndex].Colour;
                Button button = RuntimeUi.CreateButton(cardObj.transform, palette[paletteIndex].Name.ToUpperInvariant(),
                    new Vector2(xs[i], 22f), new Vector2(150f, 48f), 22f,
                    new Color(swatch.r, swatch.g, swatch.b, 0.35f), new Color(swatch.r, swatch.g, swatch.b, 0.6f), Color.white);

                int capturedIndex = paletteIndex;
                ShopItemDef capturedDef = def;
                button.onClick.AddListener(() => TrySelectCosmetic(capturedDef, capturedIndex));

                card.SwatchButtons[i] = button;
                card.SwatchLabels[i] = button.GetComponentInChildren<TextMeshProUGUI>();
            }
        }
        else
        {
            Button buy = RuntimeUi.CreateButton(cardObj.transform, "BUY", new Vector2(170f, 22f), new Vector2(200f, 56f), 26f, ButtonIdle, ButtonHover, Color.white);
            ShopItemDef capturedDef = def;
            buy.onClick.AddListener(() => TryBuy(capturedDef));
            card.BuyButton = buy;
            card.BuyLabel = buy.GetComponentInChildren<TextMeshProUGUI>();
        }

        _cards.Add(card);
    }

    private static void PlaceTopLeft(RectTransform rect, Vector2 offset, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }

    /// <summary>Updates every card's text and interactability in place - never rebuilds them.</summary>
    private void Refresh()
    {
        if (_walletText != null) _walletText.text = $"{PlayerWallet.CurrencyName}  {PlayerWallet.Shards}";

        int shopFloor = GameFlow.CurrentFloor;
        foreach (ShopCard card in _cards)
        {
            ShopItemDef def = card.Def;
            int price = ShopCatalogue.PriceFor(def, shopFloor);

            switch (def.Kind)
            {
                case ItemKind.Consumable:
                {
                    int count = PlayerInventory.Count(def.Id);
                    int cap = PlayerInventory.CapFor(def.Id);
                    card.Status.text = $"HELD  x{count} / {cap}";
                    bool canBuy = count < cap && PlayerWallet.Shards >= price;
                    SetBuyButton(card, canBuy, count >= cap ? "HELD MAX" : $"BUY  {price}");
                    break;
                }
                case ItemKind.SecondWind:
                {
                    bool held = PlayerInventory.Count(def.Id) > 0;
                    card.Status.text = held ? "HELD" : "NOT HELD";
                    bool canBuy = !held && PlayerWallet.Shards >= price;
                    SetBuyButton(card, canBuy, held ? "HELD MAX" : $"BUY  {price}");
                    break;
                }
                case ItemKind.FloorModifier:
                {
                    bool pending = PlayerInventory.HasPending(def.Id);
                    card.Status.text = pending ? $"READY FOR FLOOR {GameFlow.CurrentFloor + 1}" : "NEXT FLOOR ONLY";
                    bool canBuy = !pending && PlayerWallet.Shards >= price;
                    SetBuyButton(card, canBuy, pending ? "BOUGHT" : $"BUY  {price}");
                    break;
                }
                case ItemKind.Cosmetic:
                {
                    bool isTorch = def.Id == ShopItem.TorchColour;
                    int selected = isTorch ? PlayerInventory.TorchColourIndex : PlayerInventory.HudTintIndex;
                    (string Name, Color Colour)[] palette = isTorch ? ShopCatalogue.TorchColours : ShopCatalogue.HudTints;
                    card.Status.text = $"IN USE: {palette[selected].Name.ToUpperInvariant()}";

                    for (int i = 0; i < card.SwatchButtons.Length; i++)
                    {
                        int paletteIndex = i + 1;
                        bool owned = isTorch ? PlayerInventory.OwnsTorchColour(paletteIndex) : PlayerInventory.OwnsHudTint(paletteIndex);
                        bool isSelected = selected == paletteIndex;
                        string label = isSelected ? "IN USE" : owned ? "USE" : $"{palette[paletteIndex].Name.ToUpperInvariant()}  {price}";
                        card.SwatchLabels[i].text = label;
                        card.SwatchButtons[i].interactable = !isSelected && (owned || PlayerWallet.Shards >= price);
                    }
                    break;
                }
            }
        }
    }

    private static void SetBuyButton(ShopCard card, bool canBuy, string label)
    {
        if (card.BuyButton == null) return;
        card.BuyButton.interactable = canBuy;
        if (card.BuyLabel != null) card.BuyLabel.text = label;
    }

    // ---------------------------------------------------------------- purchases

    private void TryBuy(ShopItemDef def)
    {
        int price = ShopCatalogue.PriceFor(def, GameFlow.CurrentFloor);

        switch (def.Kind)
        {
            case ItemKind.Consumable:
                if (!PlayerInventory.CanHoldMore(def.Id) || !PlayerWallet.TrySpend(price)) return;
                PlayerInventory.Grant(def.Id);
                break;
            case ItemKind.SecondWind:
                if (PlayerInventory.Count(def.Id) > 0 || !PlayerWallet.TrySpend(price)) return;
                PlayerInventory.Grant(def.Id);
                break;
            case ItemKind.FloorModifier:
                if (PlayerInventory.HasPending(def.Id) || !PlayerWallet.TrySpend(price)) return;
                PlayerInventory.PendingModifiers.Add(def.Id);
                break;
            default:
                return; // cosmetics go through TrySelectCosmetic
        }

        PlayTick();
        Refresh();
    }

    /// <summary>Buying an unowned swatch spends shards; selecting an already-owned one is free.</summary>
    private void TrySelectCosmetic(ShopItemDef def, int paletteIndex)
    {
        bool isTorch = def.Id == ShopItem.TorchColour;
        bool owned = isTorch ? PlayerInventory.OwnsTorchColour(paletteIndex) : PlayerInventory.OwnsHudTint(paletteIndex);

        if (!owned)
        {
            int price = ShopCatalogue.PriceFor(def, GameFlow.CurrentFloor);
            if (!PlayerWallet.TrySpend(price)) return;

            if (isTorch) PlayerInventory.GrantTorchColour(paletteIndex);
            else PlayerInventory.GrantHudTint(paletteIndex);
        }
        else
        {
            if (isTorch) PlayerInventory.SelectTorchColour(paletteIndex);
            else PlayerInventory.SelectHudTint(paletteIndex);
        }

        if (isTorch)
        {
            if (_flashlight != null) _flashlight.SetBeamColor(ShopCatalogue.TorchColours[PlayerInventory.TorchColourIndex].Colour);
        }
        else
        {
            Color tint = ShopCatalogue.HudTints[PlayerInventory.HudTintIndex].Colour;
            if (_hud != null) _hud.SetTint(tint);
            TensionDirector tension = FindAnyObjectByType<TensionDirector>();
            if (tension != null) tension.SetCalmColor(tint);
        }

        PlayTick();
        Refresh();
    }

    private void PlayTick()
    {
        if (_tickSource == null)
        {
            _tickSource = gameObject.AddComponent<AudioSource>();
            _tickSource.playOnAwake = false;
            _tickSource.spatialBlend = 0f;
        }
        if (_tickClip == null) _tickClip = BuildTickClip();
        _tickSource.PlayOneShot(_tickClip, 0.5f);
    }

    /// <summary>A short 1 kHz tick - the purchase confirmation.</summary>
    private static AudioClip BuildTickClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.06f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t / 0.02f);
            envelope *= Mathf.Clamp01(t / 0.002f);
            samples[i] = Mathf.Sin(2f * Mathf.PI * 1000f * t) * envelope;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.8f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("ShopTick", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
