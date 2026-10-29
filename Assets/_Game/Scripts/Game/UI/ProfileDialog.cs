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
    /// <summary>Basic profile and account details. Appearance editing opens a dedicated wardrobe page.</summary>
    public sealed class ProfileDialog
    {
        private readonly GameUi _ui;
        private readonly RestaurantSession _session;
        private readonly AvatarCatalog _catalog;
        private readonly AvatarPainter _painter;
        private readonly GameObject _root;
        private readonly Image _preview;
        private readonly Text _title, _name, _level, _settingsLabel, _wardrobeLabel;
        private readonly Text _artworkStatus;
        private InputField _nameInput;
        private Text _nameCaption, _nameHint, _changeLabel, _idCaption, _id, _copyLabel, _accountCaption, _account, _linkNote;
        private Button _signIn;
        private Text _signInLabel;
        private readonly List<(AccountProvider provider, Text label)> _linkButtons = new List<(AccountProvider, Text)>();
        private string _previewKey, _hintOverride;
        private bool _hintIsError;
        public event Action SettingsRequested;
        public bool IsShowing => _root.activeSelf;

        public ProfileDialog(GameUi ui, RectTransform canvas, AvatarPainter painter)
        {
            _ui=ui; _session=ui.Session; _catalog=ui.Content.Pack.Avatar; _painter=painter;
            var card=UiKit.Modal(canvas,"ProfileDialog",new Vector2(1320,800),out _root);
            _title=UiKit.Label(card,"Title",40,24,500,56,40,TextAnchor.MiddleLeft,UiKit.DarkText);
            UiKit.CloseButton(card,Close);
            var settings=UiKit.TextButton(card,"Settings",UiKit.WoodLight,26,()=>SettingsRequested?.Invoke(),out _settingsLabel);
            UiKit.Place((RectTransform)settings.transform,1014,20,210,60);
            var stage=UiKit.Box(card,"Preview",UiKit.CardRimSoft,UiKit.GoldFill,4);
            UiKit.Place(stage,30,96,360,460);
            var figure=UiKit.NewRect("Avatar",stage); UiKit.Stretch(figure,30,24,30,16);
            _preview=UiKit.AddImage(figure,null);
            _name=UiKit.Label(card,"Name",30,566,360,46,32,TextAnchor.MiddleCenter,UiKit.DarkText);
            _name.resizeTextForBestFit=true; _name.resizeTextMinSize=18; _name.resizeTextMaxSize=32;
            _level=UiKit.Label(card,"Level",30,612,360,34,24,TextAnchor.MiddleCenter,UiKit.SoftText);
            _artworkStatus=UiKit.Label(card,"ArtworkStatus",30,658,360,92,18,TextAnchor.MiddleCenter,UiKit.SoftText,wrap:true);
            var wardrobe=UiKit.TextButton(card,"OpenWardrobe",UiKit.Gold,28,_ui.OpenWardrobe,out _wardrobeLabel);
            UiKit.Place((RectTransform)wardrobe.transform,420,96,400,64); wardrobe.interactable=!_catalog.IsEmpty;
            BuildProfilePage(UiKit.Place(UiKit.NewRect("ProfileDetails",card),420,176,870,500));
            _session.Profile.Changed+=Refresh; Loc.LanguageChanged+=Refresh;
            Online.CatalogArtResources.StatusChanged+=Refresh;
        }
        public void Show()
        {
            _hintOverride=null; _nameInput.text=_session.Profile.DisplayName; Refresh();
            _root.transform.SetAsLastSibling(); _root.SetActive(true);
        }
        public void Close() { _root.SetActive(false); _painter.Release(_previewKey); _previewKey=null; }
        public void Update() { if(IsShowing && _hintOverride==null && Time.frameCount%30==0) RefreshNameHint(); }

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

        private void Refresh()
        {
            if (_root==null) return;
            var profile=_session.Profile;
            _title.text=Loc.Get("ui.profile.title"); _settingsLabel.text=Loc.Get("ui.settings.title");
            _wardrobeLabel.text=Loc.Get("ui.wardrobe.title");
            if (!_catalog.IsEmpty && _previewKey!=profile.Avatar.Key)
            {
                _painter.Release(_previewKey); _previewKey=_painter.Retain(profile.Avatar);
            }
            _preview.sprite=_ui.BakedOwnerFigure() ?? (_catalog.IsEmpty?null:_painter.Body(profile.Avatar));
            _preview.enabled=_preview.sprite!=null; _name.text=profile.DisplayName;
            _level.text=Loc.Format("ui.profile.level",_session.Progress.Level);
            _artworkStatus.text=$"App {Application.version} · build {Online.ClientBuild.Number}\n{Online.CatalogArtResources.Status}";
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
            _painter.Release(_previewKey); _previewKey=null;
            Loc.LanguageChanged-=Refresh; _session.Profile.Changed-=Refresh;
            Online.CatalogArtResources.StatusChanged-=Refresh;
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
