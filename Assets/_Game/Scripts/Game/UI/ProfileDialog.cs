using System;
using System.Collections.Generic;
using System.Linq;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Render;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Session;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Opened by tapping the top-left profile card. A live avatar preview on the left and three tabs on the right:
    /// - Profile: change name (rules + cooldown), player id, guest status and account-linking buttons for this platform;
    /// - Look: gender, skin, eyes, eyebrows, hair style and colour;
    /// - Outfit: hat, shirt, trousers and their colours.
    /// Look/outfit edits are a draft until Save (Cancel or closing discards them). Parts above the player's level
    /// show "Lv.N" and can't be picked. The Settings button opens the game settings on top.
    /// </summary>
    public sealed class ProfileDialog
    {
        private enum Tab { Profile, Look, Outfit }

        private static readonly AvatarSlot[] LookSlots =
            { AvatarSlot.Gender, AvatarSlot.SkinTone, AvatarSlot.Eyes, AvatarSlot.Eyebrows, AvatarSlot.HairStyle, AvatarSlot.HairColor };
        private static readonly AvatarSlot[] OutfitSlots =
            { AvatarSlot.Hat, AvatarSlot.Shirt, AvatarSlot.ShirtColor, AvatarSlot.Pants, AvatarSlot.PantsColor };

        private sealed class Option
        {
            public AvatarPartDef Part;
            public Image Rim, Fill;
            public Text Label;
            public bool IsColor;
        }

        private readonly GameUi _ui;
        private readonly RestaurantSession _session;
        private readonly AvatarCatalog _catalog;
        private readonly AvatarPainter _painter;
        private readonly GameObject _root;
        private readonly Image _preview;
        private readonly Text _title, _name, _level, _settingsLabel, _randomLabel, _cancelLabel, _saveLabel;
        private readonly Button _save, _cancel, _random;
        private readonly Dictionary<Tab, (Button button, Text label, GameObject page)> _tabs =
            new Dictionary<Tab, (Button, Text, GameObject)>();
        private readonly List<Option> _options = new List<Option>();
        private readonly List<(Text label, AvatarSlot slot)> _rowLabels = new List<(Text, AvatarSlot)>();
        private readonly System.Random _rng = new System.Random();

        // Profile tab.
        private InputField _nameInput;
        private Text _nameCaption, _nameHint, _changeLabel, _idCaption, _id, _copyLabel, _accountCaption, _account, _linkNote;
        private Button _signIn;
        private Text _signInLabel;
        private readonly List<(AccountProvider provider, Text label)> _linkButtons = new List<(AccountProvider, Text)>();

        private AvatarLook _draft;
        private AvatarPartDef _selectedPart;
        private readonly Button _buy, _sell, _filter;
        private readonly Text _buyLabel, _sellLabel, _filterLabel;
        private readonly Text _wardrobeNote;
        private bool _ownedOnly;
        private string _previewKey;
        private Tab _tab = Tab.Profile;
        private string _hintOverride;
        private bool _hintIsError;

        public event Action SettingsRequested;
        public bool IsShowing => _root.activeSelf;

        public ProfileDialog(GameUi ui, RectTransform canvas, AvatarPainter painter)
        {
            _ui = ui;
            _session = ui.Session;
            _catalog = ui.Content.Pack.Avatar;
            _painter = painter;

            var card = UiKit.Modal(canvas, "ProfileDialog", new Vector2(1320f, 800f), out _root);
            _title = UiKit.Label(card, "Title", 40, 24, 500, 56, 40, TextAnchor.MiddleLeft, UiKit.DarkText);
            UiKit.CloseButton(card, Close);
            var settings = UiKit.TextButton(card, "Settings", UiKit.WoodLight, 26, () => SettingsRequested?.Invoke(), out _settingsLabel);
            UiKit.Place((RectTransform)settings.transform, 1320 - 96 - 210, 20, 210, 60);

            // Left: preview, name, level, randomize.
            var stage = UiKit.Box(card, "Preview", UiKit.CardRimSoft, UiKit.GoldFill, 4f);
            UiKit.Place(stage, 30, 96, 360, 460);
            var figure = UiKit.NewRect("Avatar", stage);
            UiKit.Stretch(figure, 30, 24, 30, 16);
            _preview = UiKit.AddImage(figure, null);
            _name = UiKit.Label(card, "Name", 30, 566, 360, 46, 32, TextAnchor.MiddleCenter, UiKit.DarkText);
            _name.horizontalOverflow = HorizontalWrapMode.Wrap;
            _name.resizeTextForBestFit = true;
            _name.resizeTextMinSize = 18;
            _name.resizeTextMaxSize = 32;
            _level = UiKit.Label(card, "Level", 30, 612, 360, 34, 24, TextAnchor.MiddleCenter, UiKit.SoftText);
            _random = UiKit.TextButton(card, "Randomize", UiKit.Gold, 26, Randomize, out _randomLabel);
            UiKit.Place((RectTransform)_random.transform, 70, 680, 280, 72);

            // Right: tabs and pages.
            var tabOrder = _catalog.IsEmpty ? new[] { Tab.Profile } : new[] { Tab.Profile, Tab.Look, Tab.Outfit };
            for (int i = 0; i < tabOrder.Length; i++)
            {
                var tab = tabOrder[i];
                var button = UiKit.TextButton(card, "Tab" + tab, UiKit.WoodLight, 26, () => SelectTab(tab), out var label);
                UiKit.Place((RectTransform)button.transform, 420 + i * 210, 96, 200, 60);
                var page = UiKit.Place(UiKit.NewRect("Page" + tab, card), 420, 176, 870, 500);
                _tabs[tab] = (button, label, page.gameObject);
                if (tab == Tab.Profile) BuildProfilePage(page);
                else BuildPartsPage(page, tab == Tab.Look ? LookSlots : OutfitSlots);
            }

            _cancel = UiKit.TextButton(card, "Cancel", UiKit.Disabled, 28, Revert, out _cancelLabel);
            UiKit.Place((RectTransform)_cancel.transform, 820, 704, 220, 72);
            _save = UiKit.TextButton(card, "Save", UiKit.Green, 28, SaveLook, out _saveLabel);
            UiKit.Place((RectTransform)_save.transform, 1060, 704, 230, 72);
            _buy = UiKit.TextButton(card, "BuyOutfit", UiKit.Gold, 22, Buy, out _buyLabel);
            UiKit.Place((RectTransform)_buy.transform, 420, 704, 180, 72);
            _sell = UiKit.TextButton(card, "SellOutfit", UiKit.Red, 22, Sell, out _sellLabel);
            UiKit.Place((RectTransform)_sell.transform, 610, 704, 190, 72);
            _filter = UiKit.TextButton(card, "WardrobeFilter", UiKit.WoodLight, 22, () => { _ownedOnly = !_ownedOnly; Refresh(); }, out _filterLabel);
            UiKit.Place((RectTransform)_filter.transform, 1060, 96, 230, 60);
            _wardrobeNote = UiKit.Label(card, "WardrobeNote", 420, 675, 870, 26, 18, TextAnchor.MiddleLeft, UiKit.SoftText);

            _session.Profile.Changed += Refresh;
            _session.Wallet.Changed += Refresh;
            Loc.LanguageChanged += Refresh;
        }

        public void Show()
        {
            _draft = _session.Profile.Avatar.Clone();
            _selectedPart = null;
            _hintOverride = null;
            _nameInput.text = _session.Profile.DisplayName;
            SelectTab(Tab.Profile);
            _root.transform.SetAsLastSibling();
            _root.SetActive(true);
        }

        /// <summary>Closing discards an unsaved look, like Cancel.</summary>
        public void Close()
        {
            _root.SetActive(false);
            _painter.Release(_previewKey); _previewKey = null;
        }

        public void Update()
        {
            // Keeps the rename countdown current while the dialog is open.
            if (IsShowing && _tab == Tab.Profile && _hintOverride == null && Time.frameCount % 30 == 0)
                RefreshNameHint();
        }

        // ---------------------------------------------------------------------------------------------------------

        private void BuildProfilePage(RectTransform page)
        {
            _nameCaption = UiKit.Label(page, "NameCaption", 0, 0, 600, 36, 26, TextAnchor.MiddleLeft, UiKit.DarkText);
            _nameInput = UiKit.TextField(page, "NameInput", 0, 40, 580, 72, 30, 24);
            var change = UiKit.TextButton(page, "Change", UiKit.Green, 26, Rename, out _changeLabel);
            UiKit.Place((RectTransform)change.transform, 600, 40, 270, 72);
            _nameInput.onValueChanged.AddListener(_ => { _hintOverride = null; RefreshNameHint(); });
            _nameInput.onEndEdit.AddListener(_ => RefreshNameHint());
            _nameHint = UiKit.Label(page, "NameHint", 4, 118, 866, 40, 21, TextAnchor.MiddleLeft, UiKit.SoftText, wrap: true);

            _idCaption = UiKit.Label(page, "IdCaption", 0, 172, 600, 36, 26, TextAnchor.MiddleLeft, UiKit.DarkText);
            _id = UiKit.Label(page, "Id", 4, 210, 600, 40, 24, TextAnchor.MiddleLeft, UiKit.SoftText);
            var copy = UiKit.TextButton(page, "Copy", UiKit.WoodLight, 24, CopyId, out _copyLabel);
            UiKit.Place((RectTransform)copy.transform, 690, 204, 180, 56);

            _accountCaption = UiKit.Label(page, "AccountCaption", 0, 276, 600, 36, 26, TextAnchor.MiddleLeft, UiKit.DarkText);
            _account = UiKit.Label(page, "Account", 4, 312, 866, 56, 21, TextAnchor.UpperLeft, UiKit.SoftText, wrap: true);
            _signIn = UiKit.TextButton(page, "SignIn", UiKit.Gold, 22, ToggleSignIn, out _signInLabel);
            UiKit.Place((RectTransform)_signIn.transform, 600, 266, 270, 50);

            var providers = ProvidersForPlatform();
            for (int i = 0; i < providers.Length; i++)
            {
                var provider = providers[i];
                var button = UiKit.TextButton(page, "Link" + provider, ProviderColor(provider), 22, () => Link(provider), out var label);
                UiKit.Place((RectTransform)button.transform, i * 290, 376, 280, 72);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 14;
                label.resizeTextMaxSize = 22;
                _linkButtons.Add((provider, label));
            }
            _linkNote = UiKit.Label(page, "LinkNote", 4, 456, 866, 40, 19, TextAnchor.MiddleLeft, UiKit.SoftText, wrap: true);
        }

        /// <summary>
        /// Store rules: Android offers Google Play Games; iOS offers Sign in with Apple (required next to any other
        /// login, App Store guideline 4.8) and Game Center. The editor shows all of them for testing the layout.
        /// </summary>
        private static AccountProvider[] ProvidersForPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android:
                    return new[] { AccountProvider.GooglePlayGames };
                case RuntimePlatform.IPhonePlayer:
                    return new[] { AccountProvider.SignInWithApple, AccountProvider.GameCenter };
                default:
                    return new[] { AccountProvider.GooglePlayGames, AccountProvider.SignInWithApple, AccountProvider.GameCenter };
            }
        }

        private static Color ProviderColor(AccountProvider provider)
        {
            switch (provider)
            {
                case AccountProvider.GooglePlayGames: return new Color32(52, 140, 84, 255);
                case AccountProvider.SignInWithApple: return new Color32(40, 40, 44, 255);
                default: return new Color32(98, 92, 190, 255);
            }
        }

        private void BuildPartsPage(RectTransform page, AvatarSlot[] slots)
        {
            // Styles are buttons (6 per line), colours are swatches (8 per line); longer lists wrap onto another line.
            const float rowHeight = 82f, labelWidth = 176f, step = 114f, swatchStep = 86f;
            int row = 0;
            foreach (var slot in slots)
            {
                var parts = _catalog.PartsFor(slot);
                if (parts.Count == 0) continue;
                float y = row * rowHeight;
                bool colors = !string.IsNullOrEmpty(parts[0].Color);
                int perLine = colors ? 8 : 6;
                row += (parts.Count + perLine - 1) / perLine;
                var caption = UiKit.Label(page, "Caption" + slot, 0, y, labelWidth, 64, 23, TextAnchor.MiddleLeft, UiKit.DarkText);
                caption.resizeTextForBestFit = true;
                caption.resizeTextMinSize = 14;
                caption.resizeTextMaxSize = 23;
                _rowLabels.Add((caption, slot));

                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    var option = new Option { Part = part, IsColor = !string.IsNullOrEmpty(part.Color) };
                    float x = labelWidth + (i % perLine) * (colors ? swatchStep : step);
                    float lineY = y + (i / perLine) * rowHeight;
                    if (option.IsColor)
                    {
                        var swatch = UiKit.NewRect(part.Id, page);
                        UiKit.Place(swatch, x + 12, lineY + 2, 60, 60);
                        option.Rim = swatch.gameObject.AddComponent<Image>();
                        option.Rim.sprite = UiArt.Circle();
                        var inner = UiKit.NewRect("Fill", swatch);
                        UiKit.Stretch(inner, 6, 6, 6, 6);
                        option.Fill = inner.gameObject.AddComponent<Image>();
                        option.Fill.sprite = UiArt.Circle();
                        option.Fill.raycastTarget = false;
                        ColorUtility.TryParseHtmlString(part.Color, out var c);
                        option.Fill.color = c;
                        UiKit.AddButton(swatch, () => Pick(part));
                        option.Label = UiKit.AddText(UiKit.NewRect("Lock", swatch), 17, TextAnchor.MiddleCenter, Color.white);
                        UiKit.Stretch(option.Label.rectTransform);
                        UiKit.AddOutline(option.Label);
                    }
                    else
                    {
                        var button = UiKit.TextButton(page, part.Id, UiKit.WoodLight, 20, () => Pick(part), out option.Label);
                        UiKit.Place((RectTransform)button.transform, x, lineY + 2, step - 8, 60);
                        option.Rim = button.GetComponent<Image>();
                        option.Fill = button.transform.Find("Fill").GetComponent<Image>();
                        option.Label.resizeTextForBestFit = true;
                        option.Label.horizontalOverflow = HorizontalWrapMode.Wrap;
                        option.Label.verticalOverflow = VerticalWrapMode.Truncate;
                        option.Label.resizeTextMinSize = 12;
                        option.Label.resizeTextMaxSize = 20;
                    }
                    _options.Add(option);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------

        private void SelectTab(Tab tab)
        {
            _tab = tab;
            foreach (var pair in _tabs)
            {
                bool on = pair.Key == tab;
                pair.Value.page.SetActive(on);
                UiKit.SetButtonColor(pair.Value.button, on ? UiKit.Gold : UiKit.WoodLight);
            }
            Refresh();
        }

        private void Pick(AvatarPartDef part)
        {
            if (!_ui.CanCustomizeAvatar) { _ui.Toast(Loc.Get("ui.wardrobe.native_pending")); return; }
            if (part.UnlockLevel > _session.Progress.Level)
            {
                _ui.Toast(Loc.Format("ui.profile.locked", part.UnlockLevel), 1.6f);
                return;
            }
            _draft.TrySet(_catalog, part.Slot, part.Id, _session.Progress.Level);
            _selectedPart = part;
            Refresh();
        }

        private void Buy()
        {
            if (!_ui.CanCustomizeAvatar) return;
            var result = _session.BuyAvatarPart(_selectedPart);
            _ui.Toast(Loc.Get("ui.wardrobe.result." + result.ToString().ToLowerInvariant()));
            Refresh();
        }

        private void Sell()
        {
            if (!_ui.CanCustomizeAvatar) return;
            var result = _session.SellAvatarPart(_selectedPart);
            if (result == WardrobeResult.Success)
                _draft.TrySet(_catalog, _selectedPart.Slot, _session.Profile.Avatar[_selectedPart.Slot], int.MaxValue);
            _ui.Toast(Loc.Get("ui.wardrobe.result." + result.ToString().ToLowerInvariant()));
            Refresh();
        }

        private void Randomize()
        {
            if (!_ui.CanCustomizeAvatar) return;
            _draft = _session.RandomOwnedAvatar(_rng);
            _selectedPart = null;
            Refresh();
        }

        private void Revert()
        {
            _draft = _session.Profile.Avatar.Clone();
            _selectedPart = null;
            Refresh();
        }

        private void SaveLook()
        {
            if (!_ui.CanCustomizeAvatar) return;
            if (HasUnownedDraft()) { _ui.Toast(Loc.Get("ui.wardrobe.buy_first")); return; }
            _session.SaveAvatar(_draft);
            _draft = _session.Profile.Avatar.Clone();
            _ui.Toast(Loc.Get("ui.profile.saved"), 1.6f);
            Refresh();
        }

        private void Rename()
        {
            var result = _session.Rename(_nameInput.text);
            if (result == NameCheck.Ok)
            {
                _hintOverride = Loc.Get("ui.profile.name.changed");
                _hintIsError = false;
                _ui.Toast(Loc.Get("ui.profile.name.changed"), 1.6f);
            }
            else
            {
                _hintOverride = NameMessage(result);
                _hintIsError = result != NameCheck.Unchanged;
            }
            Refresh();
        }

        private void CopyId()
        {
            GUIUtility.systemCopyBuffer = _session.Profile.PlayerId;
            _ui.Toast(Loc.Get("ui.profile.copied"), 1.4f);
        }

        /// <summary>Email account: opens the sign-in screen, or signs out (local progress stays on the device).</summary>
        private async void ToggleSignIn()
        {
            var online = _ui.Online;
            if (online == null || !online.IsConfigured) return;
            if (!online.Auth.IsSignedIn)
            {
                _ui.OpenLogin();
                return;
            }
            await online.SignOut();
            _ui.Toast(Loc.Get("ui.login.signed_out"), 2f);
            Refresh();
        }

        /// <summary>Platform linking needs the platform SDKs and backend support; until then it explains that.</summary>
        private void Link(AccountProvider provider)
        {
            if (_session.Profile.LinkedAccounts.Contains(provider))
                return;
            _ui.Toast(Loc.Get("ui.profile.link.soon"), 2.4f);
        }

        // ---------------------------------------------------------------------------------------------------------

        private void Refresh()
        {
            if (_root == null) { Loc.LanguageChanged -= Refresh; return; }
            if (_draft == null) return;
            var profile = _session.Profile;
            bool editingLook = _tab != Tab.Profile;
            bool canEdit = _ui.CanCustomizeAvatar;
            bool dirty = _draft.Key != profile.Avatar.Key;

            _title.text = Loc.Get("ui.profile.title");
            _settingsLabel.text = Loc.Get("ui.settings.title");
            // The same figure as in the restaurant: the skin's baked owner when it has one, else the drawn avatar.
            if (canEdit && _previewKey != _draft.Key)
            {
                _painter.Release(_previewKey);
                _previewKey = _catalog.IsEmpty ? null : _painter.Retain(_draft);
            }
            _preview.sprite = (!editingLook?_ui.BakedOwnerFigure():null) ?? (_catalog.IsEmpty ? null : _painter.Body(_draft));
            _preview.enabled = _preview.sprite != null;
            _name.text = profile.DisplayName;
            _level.text = Loc.Format("ui.profile.level", _session.Progress.Level);

            foreach (var pair in _tabs)
                pair.Value.label.text = Loc.Get("ui.profile.tab." + pair.Key.ToString().ToLowerInvariant());
            _random.gameObject.SetActive(editingLook);
            _randomLabel.text = Loc.Get("ui.profile.randomize");
            _save.gameObject.SetActive(editingLook);
            _cancel.gameObject.SetActive(editingLook);
            _save.interactable = canEdit && dirty && !HasUnownedDraft();
            _random.interactable = canEdit;
            _cancel.interactable = dirty;
            bool selectedPaid = canEdit && editingLook && _selectedPart != null && _selectedPart.Price > 0;
            bool owned = _session.OwnsAvatarPart(_selectedPart);
            _buy.gameObject.SetActive(selectedPaid && !owned);
            _sell.gameObject.SetActive(selectedPaid && owned);
            _buy.interactable = selectedPaid && _session.LocalWardrobeEconomy && _selectedPart.Price <= _session.Wallet.DisplayCoins;
            _sell.interactable = selectedPaid && _session.LocalWardrobeEconomy && _session.Profile.Avatar[_selectedPart.Slot] != _selectedPart.Id;
            _buyLabel.text = Loc.Format("ui.wardrobe.buy", _selectedPart?.Price ?? 0);
            _sellLabel.text = Loc.Format("ui.wardrobe.sell", (_selectedPart?.Price ?? 0) / 2);
            _filter.gameObject.SetActive(editingLook);
            _filterLabel.text = Loc.Get(_ownedOnly ? "ui.wardrobe.owned" : "ui.wardrobe.shop");
            _wardrobeNote.text = !editingLook ? "" : !canEdit ? Loc.Get("ui.wardrobe.native_pending") : !_session.LocalWardrobeEconomy
                ? Loc.Get("ui.wardrobe.online_note") : HasUnownedDraft() ? Loc.Get("ui.wardrobe.buy_first") : "";
            _saveLabel.text = Loc.Get("ui.profile.save");
            _cancelLabel.text = Loc.Get("ui.profile.cancel");

            foreach (var (label, slot) in _rowLabels)
                label.text = Loc.Get(_painter.NativeAvailable && slot==AvatarSlot.Gender
                    ? "ui.avatar.slot.mouth" : "ui.avatar.slot." + slot.ToString().ToLowerInvariant());
            int level = _session.Progress.Level;
            foreach (var o in _options)
            {
                bool partOwned = _session.OwnsAvatarPart(o.Part);
                o.Rim.gameObject.SetActive(!_ownedOnly || partOwned);
                bool locked = o.Part.UnlockLevel > level;
                var optionButton = o.Rim.GetComponent<Button>();
                if (optionButton != null) optionButton.interactable = canEdit && !locked;
                bool selected = _draft[o.Part.Slot] == o.Part.Id;
                if (o.IsColor)
                {
                    o.Rim.color = selected ? UiKit.Gold : UiKit.CardRimSoft;
                    o.Rim.rectTransform.localScale = selected ? new Vector3(1.12f, 1.12f, 1f) : Vector3.one;
                    var c = o.Fill.color;
                    c.a = locked ? 0.35f : 1f;
                    o.Fill.color = c;
                    o.Label.text = locked ? Loc.Format("ui.profile.lv", o.Part.UnlockLevel) : "";
                }
                else
                {
                    var color = locked ? UiKit.Disabled : selected ? UiKit.Green : UiKit.WoodLight;
                    o.Rim.color = UiKit.Darken(color, 0.75f);
                    o.Fill.color = color;
                    string nameKey=_painter.NativeAvailable
                        ? "avatar.native."+o.Part.Slot.ToString().ToLowerInvariant()+"."+o.Part.Id : o.Part.NameKey;
                    o.Label.text = locked ? Loc.Format("ui.profile.lv", o.Part.UnlockLevel) : Loc.Get(nameKey)
                        + (partOwned ? "" : "\n" + Loc.Format("ui.wardrobe.price", o.Part.Price));
                }
            }

            if (_nameCaption == null) return;
            _nameCaption.text = Loc.Get("ui.profile.name");
            ((Text)_nameInput.placeholder).text = Loc.Get("ui.profile.name.placeholder");
            _changeLabel.text = Loc.Get("ui.profile.name.change");
            RefreshNameHint();
            _idCaption.text = Loc.Get("ui.profile.id");
            _id.text = profile.PlayerId;
            _copyLabel.text = Loc.Get("ui.profile.copy");
            _accountCaption.text = Loc.Get("ui.profile.account");
            var online = _ui.Online;
            bool canSignIn = online != null && online.IsConfigured;
            bool signedIn = canSignIn && online.Auth.IsSignedIn;
            _account.text = signedIn ? Loc.Format("ui.profile.account.email", online.Auth.Email)
                : Loc.Get(profile.IsGuest ? "ui.profile.account.guest" : "ui.profile.account.linked");
            _signIn.gameObject.SetActive(canSignIn);
            _signInLabel.text = Loc.Get(signedIn ? "ui.login.sign_out" : "ui.login.sign_in");
            foreach (var (provider, label) in _linkButtons)
            {
                string name = Loc.Get("ui.profile.link." + provider);
                label.text = profile.LinkedAccounts.Contains(provider) ? Loc.Format("ui.profile.link.done", name) : name;
            }
            _linkNote.text = Loc.Get("ui.profile.link.note");
        }
        public void Dispose()
        {
            _painter.Release(_previewKey); _previewKey = null;
            Loc.LanguageChanged -= Refresh;
            _session.Profile.Changed -= Refresh;
            _session.Wallet.Changed -= Refresh;
        }

        private bool HasUnownedDraft()
        {
            foreach (var pair in _draft.Parts)
                if (!_session.OwnsAvatarPart(_catalog.Find(pair.Key, pair.Value))) return true;
            return false;
        }

        private void RefreshNameHint()
        {
            if (_nameHint == null) return;
            if (_hintOverride != null)
            {
                _nameHint.text = _hintOverride;
                _nameHint.color = _hintIsError ? UiKit.Red : UiKit.Green;
                return;
            }
            _nameHint.color = UiKit.SoftText;
            var profile = _session.Profile;
            var next = profile.NextRenameUtc(_session.RenameCooldown);
            var now = _session.UtcNow;
            string rules = Loc.Format("ui.profile.name.rules", NameRules.MinLength, NameRules.MaxLength);
            _nameHint.text = next.HasValue && next.Value > now
                ? rules + " " + Loc.Format("ui.profile.name.wait", Duration(next.Value - now))
                : rules + " " + Loc.Get(profile.LastRenameUtc.HasValue ? "ui.profile.name.ready" : "ui.profile.name.free");
        }

        private string NameMessage(NameCheck check)
        {
            if (check == NameCheck.Cooldown)
            {
                var next = _session.Profile.NextRenameUtc(_session.RenameCooldown) ?? _session.UtcNow;
                return Loc.Format("ui.profile.name.wait", Duration(next - _session.UtcNow));
            }
            if (check == NameCheck.TooShort || check == NameCheck.TooLong)
                return Loc.Format("ui.profile.name." + check, NameRules.MinLength, NameRules.MaxLength);
            return Loc.Get("ui.profile.name." + check);
        }

        private static string Duration(TimeSpan t)
        {
            if (t.TotalHours >= 1)
                return Loc.Format("ui.time.hm", (int)t.TotalHours, t.Minutes);
            return Loc.Format("ui.time.m", Math.Max(1, (int)Math.Ceiling(t.TotalMinutes)));
        }
    }
}
