using System;
using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Game settings: music volume, language, frame rate (30 saves battery), help and legal links, version and player
    /// id (for support), and "Delete account and data" behind a confirmation (Google Play and the App Store require
    /// in-app deletion once accounts exist). Preferences are per device (<see cref="GameSettings"/>).
    /// </summary>
    public sealed class SettingsDialog
    {
        private const float LabelX = 40f, ControlX = 330f;

        private readonly GameUi _ui;
        private readonly GameObject _root, _confirm;
        private readonly Text _title, _music, _musicValue, _language, _fps, _help, _version, _delete, _deleteNote;
        private readonly Text _confirmTitle, _confirmBody, _confirmCancel, _confirmDelete;
        private readonly Slider _musicSlider;
        private readonly List<(Button button, string language)> _languageButtons = new List<(Button, string)>();
        private readonly List<(Button button, Text label, int fps)> _fpsButtons = new List<(Button, Text, int)>();
        private readonly List<(Text label, string key, Func<string> url)> _links = new List<(Text, string, Func<string>)>();
        private readonly int _defaultFrameRate;

        /// <summary>Wipes every save and preference on this device and restarts (set by the bootstrap).</summary>
        public Action DeleteAllData { get; set; }
        public bool IsShowing => _root.activeSelf;

        public SettingsDialog(GameUi ui, RectTransform canvas, int defaultFrameRate)
        {
            _ui = ui;
            _defaultFrameRate = defaultFrameRate;
            var theme = ui.Content.Theme;

            var card = UiKit.Modal(canvas, "SettingsDialog", new Vector2(1000f, 800f), out _root);
            _title = UiKit.Label(card, "Title", 40, 24, 600, 56, 40, TextAnchor.MiddleLeft, UiKit.DarkText);
            UiKit.CloseButton(card, Close);

            // Music.
            _music = RowLabel(card, 110);
            _musicSlider = UiKit.Slider(card, "Music", ControlX, 112, 480, 52, v =>
            {
                GameSettings.SetMusicVolume(v);
                _musicValue.text = Mathf.RoundToInt(v * 100f) + "%";
            });
            _musicValue = UiKit.Label(card, "MusicValue", ControlX + 500, 110, 120, 56, 26, TextAnchor.MiddleLeft, UiKit.SoftText);

            // Language: each button is written in its own language so anyone can find theirs.
            _language = RowLabel(card, 200);
            var names = new Dictionary<string, string> { { "en", "English" }, { "ms", "Bahasa Melayu" }, { "zh", "中文" } };
            for (int i = 0; i < Loc.SupportedLanguages.Length; i++)
            {
                string lang = Loc.SupportedLanguages[i];
                var button = UiKit.TextButton(card, "Lang" + lang, UiKit.WoodLight, 24, () => GameSettings.SetLanguage(lang), out var label);
                label.text = names.TryGetValue(lang, out var n) ? n : lang;
                UiKit.Place((RectTransform)button.transform, ControlX + i * 210, 196, 200, 64);
                _languageButtons.Add((button, lang));
            }

            // Frame rate.
            _fps = RowLabel(card, 290);
            for (int i = 0; i < GameSettings.FrameRates.Length; i++)
            {
                int fps = GameSettings.FrameRates[i];
                var button = UiKit.TextButton(card, "Fps" + fps, UiKit.WoodLight, 24, () => { GameSettings.SetFrameRate(fps); Refresh(); }, out var label);
                UiKit.Place((RectTransform)button.transform, ControlX + i * 315, 286, 305, 64);
                _fpsButtons.Add((button, label, fps));
            }

            // Help and legal.
            _help = RowLabel(card, 380);
            var links = new (string key, Func<string> url)[]
            {
                ("ui.settings.support", () => theme.supportUrl),
                ("ui.settings.privacy", () => theme.privacyUrl),
                ("ui.settings.terms", () => theme.termsUrl),
            };
            for (int i = 0; i < links.Length; i++)
            {
                var link = links[i];
                var button = UiKit.TextButton(card, link.key, UiKit.WoodLight, 22, () => Open(link.url()), out var label);
                UiKit.Place((RectTransform)button.transform, ControlX + i * 210, 376, 200, 64);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 14;
                label.resizeTextMaxSize = 22;
                _links.Add((label, link.key, link.url));
            }

            _version = UiKit.Label(card, "Version", LabelX, 470, 920, 40, 21, TextAnchor.MiddleLeft, UiKit.SoftText);

            // Danger zone.
            var delete = UiKit.TextButton(card, "Delete", UiKit.Red, 26, () => ShowConfirm(true), out _delete);
            UiKit.Place((RectTransform)delete.transform, LabelX, 560, 440, 72);
            _deleteNote = UiKit.Label(card, "DeleteNote", LabelX, 644, 920, 100, 20, TextAnchor.UpperLeft, UiKit.SoftText, wrap: true);

            // Confirmation, on top of the settings card.
            var confirmCard = UiKit.Modal((RectTransform)_root.transform, "Confirm", new Vector2(760f, 440f), out _confirm);
            _confirmTitle = UiKit.Label(confirmCard, "Title", 0, 30, 760, 56, 36, TextAnchor.MiddleCenter, UiKit.Red);
            _confirmBody = UiKit.Label(confirmCard, "Body", 50, 100, 660, 180, 24, TextAnchor.UpperCenter, UiKit.DarkText, wrap: true);
            var cancel = UiKit.TextButton(confirmCard, "Cancel", UiKit.WoodLight, 28, () => ShowConfirm(false), out _confirmCancel);
            UiKit.Place((RectTransform)cancel.transform, 70, 320, 290, 76);
            var yes = UiKit.TextButton(confirmCard, "Delete", UiKit.Red, 28, ConfirmDelete, out _confirmDelete);
            UiKit.Place((RectTransform)yes.transform, 400, 320, 290, 76);

            Loc.LanguageChanged += Refresh;
            GameSettings.Changed += Refresh;
        }

        public void Show()
        {
            _musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
            _confirm.SetActive(false);
            Refresh();
            _root.transform.SetAsLastSibling();
            _root.SetActive(true);
        }

        public void Close()
        {
            GameSettings.Flush();
            _root.SetActive(false);
        }

        private static Text RowLabel(RectTransform card, float y)
        {
            var label = UiKit.Label(card, "Label", LabelX, y, ControlX - LabelX - 10, 60, 26, TextAnchor.MiddleLeft, UiKit.DarkText);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 16;
            label.resizeTextMaxSize = 26;
            return label;
        }

        private void Open(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                _ui.Toast(Loc.Get("ui.coming_soon"), 1.6f);
            else
                Application.OpenURL(url);
        }

        private void ShowConfirm(bool show)
        {
            _confirm.SetActive(show);
            if (show) _confirm.transform.SetAsLastSibling();
        }

        private void ConfirmDelete()
        {
            _confirm.SetActive(false);
            _root.SetActive(false);
            DeleteAllData?.Invoke();
        }

        private void Refresh()
        {
            if (_root == null) { Loc.LanguageChanged -= Refresh; return; }
            _title.text = Loc.Get("ui.settings.title");
            _music.text = Loc.Get("ui.settings.music");
            _musicValue.text = Mathf.RoundToInt(GameSettings.MusicVolume * 100f) + "%";
            _language.text = Loc.Get("ui.settings.language");
            foreach (var (button, lang) in _languageButtons)
                UiKit.SetButtonColor(button, lang == Loc.Language ? UiKit.Green : UiKit.WoodLight);

            _fps.text = Loc.Get("ui.settings.fps");
            int current = GameSettings.FrameRate > 0 ? GameSettings.FrameRate : _defaultFrameRate;
            foreach (var (button, label, fps) in _fpsButtons)
            {
                UiKit.SetButtonColor(button, fps == current ? UiKit.Green : UiKit.WoodLight);
                label.text = Loc.Format(fps <= 30 ? "ui.settings.fps.saver" : "ui.settings.fps.smooth", fps);
            }

            _help.text = Loc.Get("ui.settings.help");
            foreach (var (label, key, _) in _links)
                label.text = Loc.Get(key);
            _version.text = Loc.Format("ui.settings.version", Application.version, _ui.Session.Profile.PlayerId);
            _delete.text = Loc.Get("ui.settings.delete");
            _deleteNote.text = Loc.Get("ui.settings.delete.note");
            _confirmTitle.text = Loc.Get("ui.settings.delete.confirm.title");
            _confirmBody.text = Loc.Get("ui.settings.delete.confirm.body");
            _confirmCancel.text = Loc.Get("ui.profile.cancel");
            _confirmDelete.text = Loc.Get("ui.settings.delete.confirm.yes");
        }
        public void Dispose()
        {
            Loc.LanguageChanged -= Refresh;
            GameSettings.Changed -= Refresh;
        }
    }
}
