using DemoRestaurant.Game.Content;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>A two-button question on top of everything (e.g. which restaurant to keep at first sign-in).</summary>
    public sealed class ChoiceDialog
    {
        private readonly GameObject _root;
        private readonly Text _title, _body, _yes, _no;
        private AwaitableCompletionSource<bool> _answer;

        public ChoiceDialog(RectTransform canvas)
        {
            var card = UiKit.Modal(canvas, "Choice", new Vector2(820f, 500f), out _root);
            _title = UiKit.Label(card, "Title", 0, 30, 820, 56, 34, TextAnchor.MiddleCenter, UiKit.DarkText);
            _body = UiKit.Label(card, "Body", 50, 100, 720, 230, 24, TextAnchor.UpperCenter, UiKit.DarkText, wrap: true);
            var no = UiKit.TextButton(card, "No", UiKit.WoodLight, 26, () => Answer(false), out _no);
            UiKit.Place((RectTransform)no.transform, 60, 370, 330, 84);
            var yes = UiKit.TextButton(card, "Yes", UiKit.Green, 26, () => Answer(true), out _yes);
            UiKit.Place((RectTransform)yes.transform, 430, 370, 330, 84);
            foreach (var label in new[] { _yes, _no })
            {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 16;
                label.resizeTextMaxSize = 26;
            }
        }

        /// <summary>Shows the question; completes with true for the yes button.</summary>
        public Awaitable<bool> Ask(string titleKey, string bodyKey, string yesKey, string noKey)
        {
            _answer?.TrySetResult(false);
            _answer = new AwaitableCompletionSource<bool>();
            _title.text = Loc.Get(titleKey);
            _body.text = Loc.Get(bodyKey);
            _yes.text = Loc.Get(yesKey);
            _no.text = Loc.Get(noKey);
            _root.transform.SetAsLastSibling();
            _root.SetActive(true);
            return _answer.Awaitable;
        }

        private void Answer(bool yes)
        {
            _root.SetActive(false);
            var answer = _answer;
            _answer = null;
            answer?.TrySetResult(yes);
        }
    }
}
