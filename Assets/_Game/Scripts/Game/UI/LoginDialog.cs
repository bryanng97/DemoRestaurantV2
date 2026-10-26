using System;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Game.Online;
using DemoRestaurant.Logic.Online;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Email sign-in for the online backend, shown at start-up while signed out and from the profile card.
    /// Pages: sign in, create account, forgot password, and "check your email" while the address is unverified.
    /// "Play offline" always works; the restaurant keeps running behind the dialog.
    /// </summary>
    public sealed class LoginDialog
    {
        private enum Page { SignIn, SignUp, Forgot, Verify }

        private readonly GameUi _ui;
        private readonly OnlineSession _online;
        private readonly GameObject _root;
        private readonly Text _title, _message, _status, _primaryLabel, _switchLabel, _forgotLabel, _offlineLabel,
            _emailCaption, _passwordCaption;
        private readonly InputField _email, _password;
        private readonly Button _primary, _switch, _forgot, _offline;
        private Page _page = Page.SignIn;
        private string _statusKey;
        private object[] _statusArgs;
        private bool _statusIsError;
        private bool _busy;
        private string _pendingEmail, _pendingPassword;

        public bool IsShowing => _root.activeSelf;

        public LoginDialog(GameUi ui, RectTransform canvas, OnlineSession online)
        {
            _ui = ui;
            _online = online;

            var card = UiKit.Modal(canvas, "LoginDialog", new Vector2(960f, 760f), out _root);
            _title = UiKit.Label(card, "Title", 40, 24, 760, 56, 40, TextAnchor.MiddleLeft, UiKit.DarkText);
            UiKit.CloseButton(card, Close);
            _message = UiKit.Label(card, "Message", 44, 92, 872, 80, 23, TextAnchor.UpperLeft, UiKit.SoftText, wrap: true);

            _emailCaption = UiKit.Label(card, "EmailCaption", 44, 180, 600, 36, 26, TextAnchor.MiddleLeft, UiKit.DarkText);
            _email = UiKit.TextField(card, "Email", 40, 218, 880, 72, 30, 254);
            _email.contentType = InputField.ContentType.EmailAddress;
            _passwordCaption = UiKit.Label(card, "PasswordCaption", 44, 304, 600, 36, 26, TextAnchor.MiddleLeft, UiKit.DarkText);
            _password = UiKit.TextField(card, "Password", 40, 342, 880, 72, 30, 72);
            _password.contentType = InputField.ContentType.Password;
            _password.onEndEdit.AddListener(_ => { if (Keyboard.SubmitPressed()) Primary(); });

            _status = UiKit.Label(card, "Status", 44, 424, 872, 64, 22, TextAnchor.MiddleLeft, UiKit.SoftText, wrap: true);

            _primary = UiKit.TextButton(card, "Primary", UiKit.Green, 30, Primary, out _primaryLabel);
            UiKit.Place((RectTransform)_primary.transform, 40, 496, 880, 84);
            _switch = UiKit.TextButton(card, "Switch", UiKit.WoodLight, 24, Switch, out _switchLabel);
            UiKit.Place((RectTransform)_switch.transform, 40, 600, 430, 64);
            _forgot = UiKit.TextButton(card, "Forgot", UiKit.WoodLight, 24, Forgot, out _forgotLabel);
            UiKit.Place((RectTransform)_forgot.transform, 490, 600, 430, 64);
            _offline = UiKit.TextButton(card, "Offline", UiKit.Disabled, 24, PlayOffline, out _offlineLabel);
            UiKit.Place((RectTransform)_offline.transform, 40, 676, 880, 60);
            foreach (var label in new[] { _switchLabel, _forgotLabel, _offlineLabel })
            {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 14;
                label.resizeTextMaxSize = 24;
            }

            _online.StateChanged += OnStateChanged;
            Loc.LanguageChanged += Refresh;
        }

        public void Show()
        {
            _page = _online.State == OnlineState.NeedsVerification ? Page.Verify : Page.SignIn;
            if (_online.Auth?.Email != null) _email.text = _online.Auth.Email;
            _password.text = "";
            SetStatus(null);
            _root.transform.SetAsLastSibling();
            _root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            ForgetPending();
            _password.text = "";
            _root.SetActive(false);
        }

        // ---------------------------------------------------------------------------------------------------------

        private async void Primary()
        {
            if (_busy) return;
            switch (_page)
            {
                case Page.SignIn: await SignIn(); break;
                case Page.SignUp: await SignUp(); break;
                case Page.Forgot: await SendReset(); break;
                case Page.Verify: await CheckVerified(); break;
            }
        }

        private async Awaitable SignIn()
        {
            var check = CredentialRules.Check(_email.text, _password.text, newAccount: false);
            if (check != CredentialCheck.Ok) { ShowCheck(check); return; }
            SetBusy(true);
            var result = await _online.SignIn(_email.text, _password.text);
            SetBusy(false);
            if (result.Status == AuthStatus.EmailNotVerified)
            {
                WaitForConfirmation();
                GoTo(Page.Verify, "ui.login.verify.not_yet");
            }
            else if (!result.Ok) SetStatus(AuthMessage(result.Status), error: true); // keep the password to fix a typo
            else AfterSignedIn();
        }

        private async Awaitable SignUp()
        {
            var check = CredentialRules.Check(_email.text, _password.text, newAccount: true);
            if (check != CredentialCheck.Ok) { ShowCheck(check); return; }
            SetBusy(true);
            var result = await _online.SignUp(_email.text, _password.text);
            SetBusy(false);
            if (!result.Ok) { SetStatus(AuthMessage(result.Status), error: true); return; }
            if (_online.Auth.IsSignedIn && _online.Auth.IsEmailVerified) { AfterSignedIn(); return; }
            WaitForConfirmation();
            GoTo(Page.Verify);
        }

        /// <summary>
        /// Supabase returns no session until the email link is opened, so "Continue" signs in again with the
        /// credentials just typed. They stay in memory only while this dialog is open, never on disk.
        /// </summary>
        private void WaitForConfirmation()
        {
            _pendingEmail = _email.text.Trim();
            _pendingPassword = _password.text;
            _password.text = "";
        }

        private void ForgetPending()
        {
            _pendingEmail = null;
            _pendingPassword = null;
        }

        private async Awaitable SendReset()
        {
            if (!CredentialRules.LooksLikeEmail(_email.text)) { ShowCheck(CredentialCheck.EmailInvalid); return; }
            SetBusy(true);
            var result = await _online.Auth.RecoverPassword(_email.text.Trim());
            SetBusy(false);
            // Same message whether or not the address has an account.
            if (result.Ok || result.Status == AuthStatus.InvalidCredentials) SetStatus("ui.login.reset.sent");
            else SetStatus(AuthMessage(result.Status), error: true);
        }

        private async Awaitable CheckVerified()
        {
            SetBusy(true);
            if (_online.Auth.IsSignedIn)
            {
                bool verified = await _online.CheckVerified();
                SetBusy(false);
                if (verified) AfterSignedIn();
                else SetStatus("ui.login.verify.not_yet", error: true);
                return;
            }
            if (_pendingPassword == null)
            {
                // Typed in an earlier session of the dialog: ask for the password again.
                SetBusy(false);
                GoTo(Page.SignIn, "ui.login.verify.then_sign_in");
                return;
            }
            var result = await _online.SignIn(_pendingEmail, _pendingPassword);
            SetBusy(false);
            if (result.Ok) AfterSignedIn();
            else if (result.Status == AuthStatus.EmailNotVerified) SetStatus("ui.login.verify.not_yet", error: true);
            else
            {
                ForgetPending();
                GoTo(Page.SignIn);
                SetStatus(AuthMessage(result.Status), error: true);
            }
        }

        private async void Resend()
        {
            if (_busy || !CredentialRules.LooksLikeEmail(_email.text)) return;
            SetBusy(true);
            var result = await _online.Auth.ResendVerification(_email.text.Trim());
            SetBusy(false);
            SetStatus(result.Ok ? "ui.login.verify.resent" : AuthMessage(result.Status), error: !result.Ok);
        }

        private void AfterSignedIn()
        {
            Close();
            _ui.Toast(Loc.Format("ui.login.signed_in", _online.Auth.Email), 2.4f);
        }

        private void Switch()
        {
            if (_busy) return;
            if (_page == Page.Verify) { Resend(); return; }
            GoTo(_page == Page.SignIn ? Page.SignUp : Page.SignIn);
        }

        private async void Forgot()
        {
            if (_busy) return;
            if (_page == Page.Verify)
            {
                // "Use another email": drop the unverified session.
                ForgetPending();
                await _online.SignOut();
                GoTo(Page.SignIn);
                return;
            }
            GoTo(_page == Page.Forgot ? Page.SignIn : Page.Forgot);
        }

        private void PlayOffline()
        {
            _online.ChooseOffline();
            Close();
            _ui.Toast(Loc.Get("ui.login.offline.toast"), 2.6f);
        }

        private void OnStateChanged()
        {
            if (!IsShowing) return;
            if (_online.State == OnlineState.NeedsVerification && _page != Page.Verify) GoTo(Page.Verify);
            else if (_online.State == OnlineState.Unreachable) SetStatus("ui.login.error.network", error: true);
        }

        // ---------------------------------------------------------------------------------------------------------

        private void GoTo(Page page, string statusKey = null)
        {
            _page = page;
            SetStatus(statusKey);
            Refresh();
        }

        private void ShowCheck(CredentialCheck check)
        {
            if (check == CredentialCheck.EmailInvalid) SetStatus("ui.login.error.email", error: true);
            else SetStatus("ui.login.error.password", true, CredentialRules.MinPasswordLength);
        }

        private static string AuthMessage(AuthStatus status)
        {
            switch (status)
            {
                case AuthStatus.InvalidCredentials: return "ui.login.error.credentials";
                case AuthStatus.AlreadyRegistered: return "ui.login.error.exists";
                case AuthStatus.WeakPassword: return "ui.login.error.weak";
                case AuthStatus.RateLimited: return "ui.login.error.rate";
                case AuthStatus.Network: return "ui.login.error.network";
                case AuthStatus.EmailNotVerified: return "ui.login.verify.not_yet";
                default: return "ui.login.error.generic";
            }
        }

        private void SetStatus(string key, bool error = false, params object[] args)
        {
            _statusKey = key;
            _statusArgs = args;
            _statusIsError = error;
            RefreshStatus();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            foreach (var b in new[] { _primary, _switch, _forgot, _offline }) b.interactable = !busy;
            _email.interactable = _password.interactable = !busy;
            if (busy) SetStatus("ui.login.busy");
            else if (_statusKey == "ui.login.busy") SetStatus(null);
        }

        private void Refresh()
        {
            if (_root == null) { Loc.LanguageChanged -= Refresh; return; }
            bool verify = _page == Page.Verify;
            bool forgot = _page == Page.Forgot;
            string p = _page.ToString().ToLowerInvariant();
            _title.text = Loc.Get("ui.login.title." + p);
            _message.text = verify ? Loc.Format("ui.login.message.verify", _online.Auth?.Email ?? _email.text.Trim())
                : Loc.Get("ui.login.message." + p);
            _emailCaption.text = Loc.Get("ui.login.email");
            _passwordCaption.text = Loc.Get("ui.login.password");
            ((Text)_email.placeholder).text = "name@example.com";
            ((Text)_password.placeholder).text = _page == Page.SignUp
                ? Loc.Format("ui.login.password.hint", CredentialRules.MinPasswordLength) : "";

            _email.gameObject.SetActive(!verify);
            _emailCaption.gameObject.SetActive(!verify);
            _password.gameObject.SetActive(!verify && !forgot);
            _passwordCaption.gameObject.SetActive(!verify && !forgot);

            _primaryLabel.text = Loc.Get("ui.login.primary." + p);
            _switchLabel.text = Loc.Get(verify ? "ui.login.verify.resend"
                : _page == Page.SignUp ? "ui.login.switch.sign_in" : "ui.login.switch.sign_up");
            _forgotLabel.text = Loc.Get(verify ? "ui.login.verify.other_email"
                : forgot ? "ui.login.back" : "ui.login.forgot");
            _switch.gameObject.SetActive(!forgot);
            _offlineLabel.text = Loc.Get("ui.login.offline");
            RefreshStatus();
        }
        public void Dispose()
        {
            Loc.LanguageChanged -= Refresh;
            _online.StateChanged -= OnStateChanged;
        }

        private void RefreshStatus()
        {
            _status.text = _statusKey == null ? "" : Loc.Format(_statusKey, _statusArgs ?? Array.Empty<object>());
            _status.color = _statusIsError ? UiKit.Red : UiKit.SoftText;
        }

        /// <summary>Enter/Return on a hardware keyboard submits; phones use the button.</summary>
        private static class Keyboard
        {
            public static bool SubmitPressed()
            {
                var k = UnityEngine.InputSystem.Keyboard.current;
                return k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame);
            }
        }
    }
}
