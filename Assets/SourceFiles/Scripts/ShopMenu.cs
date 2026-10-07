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
        if (_contractPanel != null) _contractPanel.SetActive(false);
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
    }

    // ---------------------------------------------------------------- F87: the contract board's panel

    private GameObject _contractPanel;
    private readonly TextMeshProUGUI[] _offerName = new TextMeshProUGUI[2];
    private readonly TextMeshProUGUI[] _offerText = new TextMeshProUGUI[2];
    private readonly TextMeshProUGUI[] _offerReward = new TextMeshProUGUI[2];
    private readonly Button[] _offerButton = new Button[2];
    private readonly TextMeshProUGUI[] _offerButtonLabel = new TextMeshProUGUI[2];
    private TextMeshProUGUI _contractStatus;
    private Button _declineButton;

    /// <summary>
    /// F87: E at the contract board. The same panel machinery as the shop (IsOpen / ClosedThisFrame, so PauseMenu and
    /// TouchControls already treat it as a menu): two offers for the next floor, TAKE one, DECLINE, or BACK.
    /// </summary>
    public void OpenContracts()
    {
        if (IsOpen) return;
        IsOpen = true;

        if (_contractPanel == null) BuildContractPanel();
        if (_contractPanel != null)
        {
            _contractPanel.SetActive(true);
            _contractPanel.transform.SetAsLastSibling();
        }
        RefreshContracts();

        PlayerLock.Freeze(_player, true);
        PlayerLock.SetCursorFree(true);
    }

    private void BuildContractPanel()
    {
        RuntimeUi.EnsureEventSystem();
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _contractPanel = RuntimeUi.CreatePanel(canvas.transform, "ContractScreen", new Color(0.02f, 0.02f, 0.03f, 0.94f));

        TextMeshProUGUI title = RuntimeUi.CreateText(_contractPanel.transform, "Title", "CONTRACTS", 64f, Amber);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1400f, 90f));

        TextMeshProUGUI tip = RuntimeUi.CreateText(_contractPanel.transform, "Tip", "Optional work for the next floor. One at a time. Paid on the ledger when you clear it.", 26f, Grey);
        RuntimeUi.Place(tip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(1500f, 44f));

        float[] xs = { -380f, 380f };
        for (int i = 0; i < 2; i++)
        {
            GameObject card = RuntimeUi.CreatePanel(_contractPanel.transform, "Offer" + i, CardColor);
            RuntimeUi.Place(card.GetComponent<Image>().rectTransform, new Vector2(0.5f, 0.5f), new Vector2(xs[i], 60f), new Vector2(680f, 380f));

            _offerName[i] = RuntimeUi.CreateText(card.transform, "Name", "", 40f, Color.white);
            _offerName[i].fontStyle = FontStyles.Bold;
            RuntimeUi.Place(_offerName[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(640f, 56f));

            _offerText[i] = RuntimeUi.CreateText(card.transform, "Text", "", 28f, Grey);
            RuntimeUi.Place(_offerText[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(600f, 110f));

            _offerReward[i] = RuntimeUi.CreateText(card.transform, "Reward", "", 32f, WalletColor);
            RuntimeUi.Place(_offerReward[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(600f, 44f));

            int slot = i;
            Button take = RuntimeUi.CreateButton(card.transform, "TAKE", new Vector2(0f, 50f), new Vector2(260f, 62f), 30f, ButtonIdle, ButtonHover, Color.white);
            take.onClick.AddListener(() => TakeContract(slot));
            _offerButton[i] = take;
            _offerButtonLabel[i] = take.GetComponentInChildren<TextMeshProUGUI>();
        }

        _contractStatus = RuntimeUi.CreateText(_contractPanel.transform, "Status", "", 32f, Amber);
        RuntimeUi.Place(_contractStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(1500f, 50f));

        _declineButton = RuntimeUi.CreateButton(_contractPanel.transform, "DECLINE", new Vector2(-230f, 40f), new Vector2(380f, 70f), 34f, ButtonIdle, ButtonHover, Color.white);
        _declineButton.onClick.AddListener(DeclineContract);

        Button back = RuntimeUi.CreateButton(_contractPanel.transform, "BACK", new Vector2(230f, 40f), new Vector2(380f, 70f), 34f, ButtonIdle, ButtonHover, Color.white);
        back.onClick.AddListener(Close);
    }

    private void RefreshContracts()
    {
        if (_contractPanel == null) return;

        int[] offers = { ContractState.OfferA, ContractState.OfferB };
        int rewardFloor = GameFlow.CurrentFloor + 1;
        for (int i = 0; i < 2; i++)
        {
            bool has = offers[i] >= 0;
            _offerName[i].transform.parent.gameObject.SetActive(has);
            if (!has) continue;

            ContractDef def = ContractState.Def(offers[i]);
            _offerName[i].text = def.Name;
            _offerText[i].text = def.Text;
            _offerReward[i].text = $"REWARD  {ContractState.Reward(offers[i], rewardFloor)}  {PlayerWallet.CurrencyName}";

            bool taken = ContractState.ActiveId == offers[i];
            _offerButtonLabel[i].text = taken ? "TAKEN" : "TAKE";
            _offerButton[i].interactable = !ContractState.HasActive;
        }

        if (!ContractState.HasOffers) _contractStatus.text = "Nothing on the board.";
        else if (ContractState.HasActive) _contractStatus.text = $"You took: {ContractState.Def(ContractState.ActiveId).Name}  Floor {ContractState.ActiveFloor}.";
        else _contractStatus.text = "";

        TextMeshProUGUI declineLabel = _declineButton.GetComponentInChildren<TextMeshProUGUI>();
        if (declineLabel != null) declineLabel.text = ContractState.HasActive ? "DROP IT" : "DECLINE";
    }

    private void TakeContract(int slot)
    {
        int id = slot == 0 ? ContractState.OfferA : ContractState.OfferB;
        if (id < 0 || ContractState.HasActive) return;

        ContractState.Take(id, GameFlow.CurrentFloor + 1);
        SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, true); // like a purchase: the taken contract survives a resume
        if (_hud != null) _hud.ShowSubtitle("Bring it back in one piece.", 3f);
        Close();
    }

    private void DeclineContract()
    {
        if (ContractState.HasActive)
        {
            ContractState.ClearActive();
            SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, true);
        }
        if (_hud != null) _hud.ShowSubtitle("Suit yourself.", 2.5f);
        Close();
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
        // T11: the panel already has an on-screen BACK button (below) that Close()s on tap - this OR
        // is for parity with the other 7 direct readers, and covers PAUSE overlapping the shop somehow.
        if (TouchInput.Pressed(TouchInput.TouchAction.Pause)) pressed = true;
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

        // F75: grew from 3x3 to 4x3 (12 items). Card width dropped from 540 to 420 so four columns still
        // fit inside the 1920-wide reference canvas with margin either side; every child offset inside
        // BuildCard/PlaceTopLeft was rescaled to match. Rows (ys) are untouched.
        float[] xs = { -660f, -220f, 220f, 660f };
        float[] ys = { 150f, -80f, -310f };
        for (int i = 0; i < ShopCatalogue.Items.Length && i < 12; i++)
        {
            BuildCard(ShopCatalogue.Items[i], new Vector2(xs[i % 4], ys[i / 4]));
        }

        Button back = RuntimeUi.CreateButton(_panel.transform, "BACK", new Vector2(0f, 40f), new Vector2(420f, 70f), 34f, ButtonIdle, ButtonHover, Color.white);
        back.onClick.AddListener(Close);
    }

    private void BuildCard(ShopItemDef def, Vector2 position)
    {
        GameObject cardObj = RuntimeUi.CreatePanel(_panel.transform, def.Name + " Card", CardColor);
        RuntimeUi.Place(cardObj.GetComponent<Image>().rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(420f, 210f));

        TextMeshProUGUI nameText = RuntimeUi.CreateText(cardObj.transform, "Name", def.Name, 26f, Color.white);
        nameText.fontStyle = FontStyles.Bold;
        nameText.alignment = TextAlignmentOptions.TopLeft;
        PlaceTopLeft(nameText.rectTransform, new Vector2(16f, -16f), new Vector2(340f, 34f));

        TextMeshProUGUI blurbText = RuntimeUi.CreateText(cardObj.transform, "Blurb", def.Blurb, 19f, Grey);
        blurbText.alignment = TextAlignmentOptions.TopLeft;
        PlaceTopLeft(blurbText.rectTransform, new Vector2(16f, -52f), new Vector2(388f, 60f));

        TextMeshProUGUI statusText = RuntimeUi.CreateText(cardObj.transform, "Status", "", 19f, Grey);
        statusText.alignment = TextAlignmentOptions.TopLeft;
        PlaceTopLeft(statusText.rectTransform, new Vector2(16f, -116f), new Vector2(340f, 30f));

        ShopCard card = new ShopCard { Def = def, Status = statusText };

        if (def.Kind == ItemKind.Cosmetic)
        {
            (string Name, Color Colour)[] palette = def.Id == ShopItem.TorchColour ? ShopCatalogue.TorchColours : ShopCatalogue.HudTints;
            card.SwatchButtons = new Button[3];
            card.SwatchLabels = new TextMeshProUGUI[3];
            float[] xs = { -132f, 0f, 132f };

            for (int i = 0; i < 3; i++)
            {
                // Palette index 0 is the default look and is never for sale - swatches map to indices 1..3.
                int paletteIndex = i + 1;
                Color swatch = palette[paletteIndex].Colour;
                Button button = RuntimeUi.CreateButton(cardObj.transform, palette[paletteIndex].Name.ToUpperInvariant(),
                    new Vector2(xs[i], 18f), new Vector2(118f, 42f), 17f,
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
            Button buy = RuntimeUi.CreateButton(cardObj.transform, "BUY", new Vector2(130f, 18f), new Vector2(160f, 46f), 22f, ButtonIdle, ButtonHover, Color.white);
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

        SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, true); // F83: purchases are kept (AdvanceFloor has not run yet)
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

        SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, true); // F83
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
