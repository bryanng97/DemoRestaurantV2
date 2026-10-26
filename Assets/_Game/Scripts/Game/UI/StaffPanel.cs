using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace DemoRestaurant.Game.UI
{
    /// <summary>
    /// Staff management:
    /// - hire chefs, waiters and cleaners: they share the level's staff slots, cost more the bigger the team,
    ///   and cleaners unlock at a higher level (economy sheet);
    /// - give the player's own avatar a job (chef, waiter or cleaner);
    /// - a scrolling list of everyone working: what they're doing, energy, their actual speed right now (base speed
    ///   times the energy factor), rest / back to work, an energy item, and dismiss (not for the owner).
    /// </summary>
    public sealed class StaffPanel : SlidePanel
    {
        private const int MaxRows = 16;
        private const float RowHeight = 74f;
        private const float RowGap = 6f;
        private const float ListTop = 330f;
        private static readonly StaffRole[] Roles = { StaffRole.Chef, StaffRole.Waiter, StaffRole.Cleaner };

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<HireButton> _hireButtons = new List<HireButton>();
        private readonly List<(StaffRole role, Text label)> _roleCounts = new List<(StaffRole, Text)>();
        private readonly List<(StaffRole role, Button button, Text label)> _jobButtons = new List<(StaffRole, Button, Text)>();
        private Text _itemLabel, _buyLabel, _ownerName, _ownerJob;
        private Image _ownerPortrait;
        private Button _buy, _refill;
        private Text _refillLabel;
        private ItemDef _item;
        private RectTransform _listContent;

        protected override string TitleKey => "ui.staff.title";

        protected override void BuildContent(RectTransform content)
        {
            for (int i = 0; i < Roles.Length; i++)
            {
                var label = UiKit.Label(content, "Count" + Roles[i], 0, i * 62, 184, 56, 26, TextAnchor.MiddleLeft, UiKit.DarkText);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 16;
                label.resizeTextMaxSize = 26;
                _roleCounts.Add((Roles[i], label));
                BuildHireRow(content, Roles[i], i * 62);
            }

            // Energy item: count and a buy button.
            _item = Ui.Session.Content.Items.Count > 0 ? Ui.Session.Content.Items[0] : null;
            if (_item != null)
            {
                UiKit.AddImage(UiKit.Place(UiKit.NewRect("ItemIcon", content), 0, 190, 48, 48), Ui.ItemIcon(_item));
                _itemLabel = UiKit.Label(content, "Item", 56, 190, 270, 48, 22, TextAnchor.MiddleLeft, UiKit.DarkText);
                _itemLabel.resizeTextForBestFit = true; _itemLabel.resizeTextMinSize = 14; _itemLabel.resizeTextMaxSize = 22;
                // One tap: coffee for everyone who isn't full, most tired first.
                _refill = UiKit.TextButton(content, "RefillAll", UiKit.Gold, 22, RefillAll, out _refillLabel);
                UiKit.Place((RectTransform)_refill.transform, 335, 190, 215, 48);
                _buy = UiKit.TextButton(content, "Buy", UiKit.Gold, 22, BuyItem, out _buyLabel);
                UiKit.Place((RectTransform)_buy.transform, 560, 190, 220, 48);
            }

            BuildOwnerCard(content, 248);
            BuildList(content);
        }

        /// <summary>"You": the player's avatar and which job they do.</summary>
        private void BuildOwnerCard(RectTransform content, float y)
        {
            var card = UiKit.Box(content, "Owner", UiKit.Gold, UiKit.GoldFill, 4f);
            UiKit.Place(card, 0, y, Width - 60, 72);
            _ownerPortrait = UiKit.AddImage(UiKit.Place(UiKit.NewRect("Portrait", card), 8, 4, 64, 64), null);
            _ownerName = UiKit.Label(card, "Name", 80, 4, 250, 34, 22, TextAnchor.MiddleLeft, UiKit.DarkText);
            _ownerName.resizeTextForBestFit = true;
            _ownerName.resizeTextMinSize = 14;
            _ownerName.resizeTextMaxSize = 22;
            _ownerJob = UiKit.Label(card, "Job", 80, 36, 250, 30, 17, TextAnchor.MiddleLeft, UiKit.SoftText);
            for (int i = 0; i < Roles.Length; i++)
            {
                var role = Roles[i];
                var button = UiKit.TextButton(card, "Job" + role, UiKit.WoodLight, 20, () => AssignOwner(role), out var label);
                UiKit.Place((RectTransform)button.transform, 336 + i * 142, 8, 136, 56);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = 20;
                _jobButtons.Add((role, button, label));
            }
        }

        /// <summary>Everyone working, in a vertical scroll list (up to <see cref="MaxRows"/>).</summary>
        private void BuildList(RectTransform content)
        {
            float height = Height - 130f - ListTop;
            var viewport = UiKit.Place(UiKit.NewRect("List", content), 0, ListTop, Width - 60, height);
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0); // catches drags between rows
            viewport.gameObject.AddComponent<RectMask2D>();

            _listContent = UiKit.NewRect("Rows", viewport);
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = new Vector2(0f, 0f);

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _listContent;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            for (int i = 0; i < MaxRows; i++)
                _rows.Add(BuildRow(_listContent, i * (RowHeight + RowGap)));
        }

        protected override void Refresh()
        {
            if (_listContent == null)
                return;
            var session = Ui.Session;
            foreach (var (role, label) in _roleCounts)
                label.text = Loc.Format("ui.staff.count." + role, session.StaffCount(role));

            foreach (var h in _hireButtons)
            {
                var check = Ui.Staff.Check(h.Type.Role);
                bool canHire = check == HireResult.Hired;
                UiKit.SetButtonColor(h.Button, canHire ? UiKit.Green : UiKit.Disabled);
                string price = check == HireResult.RoleLocked
                    ? Loc.Format("ui.staff.unlocks_at", session.RoleUnlockLevel(h.Type.Role))
                    : Loc.Format("ui.staff.cost", session.NextHireCost);
                h.Label.text = Loc.Format("ui.staff.hire_row", Loc.Get(h.Type.NameKey), price,
                    session.HiredCount, session.StaffSlots, BaseSpeed(h.Type));
            }

            if (_item != null)
            {
                _itemLabel.text = Loc.Format("ui.staff.item_count", Loc.Get(_item.NameKey), session.Items.Count(_item), _item.EnergyRestore.ToString("0"));
                _buyLabel.text = Loc.Format("ui.staff.buy", _item.Price);
                UiKit.SetButtonColor(_buy, session.Wallet.DisplayCoins >= _item.Price ? UiKit.Gold : UiKit.Disabled);
                _refillLabel.text = Loc.Get("ui.staff.refill_all");
                bool anyTired = false;
                foreach (var s in session.Sim.Staff) anyTired |= s.Energy < StaffEnergyRules.Max;
                UiKit.SetButtonColor(_refill, anyTired && session.Items.Count(_item) > 0 ? UiKit.Gold : UiKit.Disabled);
            }

            var owner = session.Owner;
            _ownerName.text = Loc.Format("ui.staff.owner", session.Profile.DisplayName);
            _ownerJob.text = owner != null ? Loc.Format("ui.staff.owner_job", Loc.Get("ui.staff.job." + owner.Role)) : "";
            _ownerPortrait.sprite = owner != null ? Ui.PortraitFor(owner) : null;
            _ownerPortrait.enabled = _ownerPortrait.sprite != null;
            foreach (var (role, button, label) in _jobButtons)
            {
                label.text = Loc.Get("ui.staff.job." + role);
                button.gameObject.SetActive(session.Content.OwnerType(role) != null);
                UiKit.SetButtonColor(button, owner != null && owner.Role == role ? UiKit.Green : UiKit.WoodLight);
            }

            var staff = session.Sim.Staff;
            int shown = 0;
            // The owner first, then everyone in hiring order.
            if (owner != null) FillRow(_rows[shown++], owner);
            foreach (var s in staff)
            {
                if (s.IsOwner || shown >= _rows.Count) continue;
                FillRow(_rows[shown++], s);
            }
            for (int i = shown; i < _rows.Count; i++)
                _rows[i].Root.SetActive(false);
            _listContent.sizeDelta = new Vector2(0f, shown * (RowHeight + RowGap));
        }

        private void FillRow(Row row, Staff s)
        {
            var session = Ui.Session;
            row.Root.SetActive(true);
            row.Staff = s;
            row.Portrait.sprite = Ui.PortraitFor(s);
            row.Portrait.enabled = row.Portrait.sprite != null;
            row.Name.text = s.IsOwner ? Loc.Format("ui.staff.owner", session.Profile.DisplayName) : $"{Loc.Get(s.Type.NameKey)} #{s.Id}";
            row.Status.text = Status(s);
            row.EnergyFill.fillAmount = s.Energy / StaffEnergyRules.Max;
            row.EnergyFill.color = s.IsResting ? new Color32(110, 160, 230, 255) : EnergyColor(s.Energy);
            row.EnergyValue.text = Mathf.FloorToInt(s.Energy) + "%";
            row.Speed.text = CurrentSpeed(s);
            row.Speed.color = s.SpeedMultiplier < 1f ? UiKit.Red : UiKit.SoftText;
            row.RestLabel.text = Loc.Get(s.IsResting || s.RestRequested ? "ui.staff.work" : "ui.staff.rest");
            UiKit.SetButtonColor(row.Rest, s.IsResting || s.RestRequested ? UiKit.Green : new Color32(110, 160, 230, 255));
            row.ItemLabel.text = _item != null ? Loc.Get(_item.NameKey) : "";
            row.ItemButton.gameObject.SetActive(_item != null);
            if (_item != null)
                UiKit.SetButtonColor(row.ItemButton, session.Items.Count(_item) > 0 && s.Energy < StaffEnergyRules.Max ? UiKit.Gold : UiKit.Disabled);
            row.Dismiss.gameObject.SetActive(!s.IsOwner);
            row.Box.color = s.IsOwner ? UiKit.Gold : UiKit.CardRim;
        }

        /// <summary>A tier's speed at full energy: cooking (chefs), walking (waiters) or cleaning (cleaners).</summary>
        private static string BaseSpeed(StaffTypeDef t)
        {
            switch (t.Role)
            {
                case StaffRole.Chef: return Loc.Format("ui.staff.stat.cook", Number(t.CookSpeed));
                case StaffRole.Waiter: return Loc.Format("ui.staff.stat.walk", Number(t.WalkSpeed));
                default: return Loc.Format("ui.staff.stat.clean", Number(t.CookSpeed));
            }
        }

        /// <summary>Speeds right now: base speed times the energy factor (tired staff are slower).</summary>
        private static string CurrentSpeed(Staff s)
        {
            float k = s.SpeedMultiplier;
            string walk = Loc.Format("ui.staff.stat.walk", Number(s.Type.WalkSpeed * k));
            switch (s.Role)
            {
                case StaffRole.Chef: return Loc.Format("ui.staff.stat.cook", Number(s.Type.CookSpeed * k)) + " · " + walk;
                case StaffRole.Cleaner: return Loc.Format("ui.staff.stat.clean", Number(s.Type.CookSpeed * k)) + " · " + walk;
                default: return walk;
            }
        }

        private static string Number(float v) => v.ToString("0.0#");

        private static Color EnergyColor(float energy)
        {
            if (energy >= 80f) return new Color32(86, 180, 74, 255);
            if (energy >= 60f) return new Color32(160, 200, 70, 255);
            if (energy >= 40f) return new Color32(235, 200, 60, 255);
            if (energy >= 20f) return new Color32(240, 140, 50, 255);
            return UiKit.Red;
        }

        private static string Status(Staff s)
        {
            if (s.IsResting) return Loc.Get("ui.staff.resting");
            string dish = s.Ticket != null ? Loc.Get(s.Ticket.Dish.NameKey) : "";
            string status;
            switch (s.State)
            {
                case StaffState.WalkingToStove:
                case StaffState.Cooking: status = Loc.Format("ui.staff.cooking", dish); break;
                case StaffState.WalkingToDrinks:
                case StaffState.Pouring:
                case StaffState.WalkingToPickup:
                case StaffState.WalkingToCustomer: status = Loc.Format("ui.staff.serving", dish); break;
                case StaffState.WalkingToFix:
                case StaffState.Fixing: status = Loc.Get("ui.staff.fixing"); break;
                case StaffState.WalkingToClean:
                case StaffState.Cleaning: status = Loc.Get("ui.staff.cleaning"); break;
                default: status = Loc.Get("ui.staff.idle"); break;
            }
            return s.RestRequested ? $"{status} · {Loc.Get("ui.staff.rest_after")}" : status;
        }

        private void BuildHireRow(RectTransform content, StaffRole role, float y)
        {
            var type = Ui.Staff.TypeFor(role);
            if (type == null)
                return;
            var button = UiKit.TextButton(content, "Hire_" + type.Id, UiKit.Green, 18, () => Hire(role), out var label);
            UiKit.Place((RectTransform)button.transform, 190, y, 590, 56);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.lineSpacing = 0.9f;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 11;
            label.resizeTextMaxSize = 18;
            _hireButtons.Add(new HireButton { Type = type, Button = button, Label = label });
        }

        private void Hire(StaffRole role)
        {
            switch (Ui.Staff.Hire(role))
            {
                case HireResult.NoSlot: Ui.Toast(Loc.Get("ui.staff.cap")); break;
                case HireResult.RoleLocked: Ui.Toast(Loc.Format("ui.staff.unlocks_at", Ui.Session.RoleUnlockLevel(role))); break;
                case HireResult.NotEnoughCoins: Ui.Toast(Loc.Get("ui.staff.no_coins")); break;
            }
            Refresh();
        }

        private void AssignOwner(StaffRole role)
        {
            if (!Ui.Staff.AssignOwner(role))
                Ui.Toast(Loc.Get("ui.staff.owner_no_room"));
            else
                Ui.Toast(Loc.Format("ui.staff.owner_now", Loc.Get("ui.staff.job." + role)), 1.6f);
            Refresh();
        }

        private void BuyItem()
        {
            if (!Ui.Session.BuyItem(_item))
                Ui.Toast(Loc.Get("ui.staff.no_coins"));
            Refresh();
        }

        private void RefillAll()
        {
            if (_item == null) return;
            bool anyTired = false;
            foreach (var s in Ui.Session.Sim.Staff) anyTired |= s.Energy < StaffEnergyRules.Max;
            if (!anyTired) Ui.Toast(Loc.Get("ui.staff.all_full"));
            else
            {
                int used = Ui.Session.RefillAll(_item);
                Ui.Toast(used > 0 ? Loc.Format("ui.staff.refilled", used, Loc.Get(_item.NameKey))
                    : Loc.Format("ui.staff.no_items", Loc.Get(_item.NameKey)));
            }
            Refresh();
        }

        private void UseItem(Staff staff)
        {
            if (staff == null || _item == null)
                return;
            if (staff.Energy >= StaffEnergyRules.Max)
                Ui.Toast(Loc.Format("ui.staff.full_energy", Loc.Get(staff.Type.NameKey)));
            else if (!Ui.Session.UseItem(_item, staff))
                Ui.Toast(Loc.Format("ui.staff.no_items", Loc.Get(_item.NameKey)));
            Refresh();
        }

        private Row BuildRow(RectTransform content, float y)
        {
            var row = new Row();
            var rect = UiKit.Box(content, "Row", UiKit.CardRim, UiKit.CardFill, 4f, out row.Box, out _);
            UiKit.Place(rect, 0, y, Width - 60, RowHeight);
            row.Root = rect.gameObject;

            row.Portrait = UiKit.AddImage(UiKit.Place(UiKit.NewRect("Portrait", rect), 8, 5, 60, 64), null);
            row.Name = UiKit.Label(rect, "Name", 72, 4, 236, 32, 21, TextAnchor.MiddleLeft, UiKit.DarkText);
            row.Name.resizeTextForBestFit = true;
            row.Name.resizeTextMinSize = 14;
            row.Name.resizeTextMaxSize = 21;
            row.Status = UiKit.Label(rect, "Status", 72, 38, 236, 30, 17, TextAnchor.MiddleLeft, UiKit.SoftText);
            row.Status.resizeTextForBestFit = true;
            row.Status.resizeTextMinSize = 11;
            row.Status.resizeTextMaxSize = 17;

            // Energy bar with its percentage, and the speeds right now underneath.
            row.EnergyFill = UiKit.Bar(rect, "Energy", 312, 8, 186, 28, UiKit.Green, UiKit.CardRimSoft);
            row.EnergyValue = UiKit.Label(rect, "EnergyValue", 312, 8, 186, 28, 16, TextAnchor.MiddleCenter, Color.white);
            UiKit.AddOutline(row.EnergyValue);
            row.Speed = UiKit.Label(rect, "Speed", 312, 40, 190, 28, 16, TextAnchor.MiddleLeft, UiKit.SoftText);
            row.Speed.resizeTextForBestFit = true;
            row.Speed.resizeTextMinSize = 10;
            row.Speed.resizeTextMaxSize = 16;

            row.ItemButton = UiKit.TextButton(rect, "Item", UiKit.Gold, 16, () => UseItem(row.Staff), out row.ItemLabel);
            UiKit.Place((RectTransform)row.ItemButton.transform, 506, 9, 92, 56);
            row.ItemLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            row.Rest = UiKit.TextButton(rect, "Rest", new Color32(110, 160, 230, 255), 20, () =>
            {
                if (row.Staff != null) Ui.Staff.ToggleRest(row.Staff);
                Refresh();
            }, out row.RestLabel);
            UiKit.Place((RectTransform)row.Rest.transform, 604, 9, 104, 56);

            row.Dismiss = UiKit.TextButton(rect, "Dismiss", UiKit.Red, 22, () =>
            {
                if (row.Staff != null) Ui.Staff.Dismiss(row.Staff);
                Refresh();
            }, out var x);
            x.text = "X";
            UiKit.Place((RectTransform)row.Dismiss.transform, 714, 9, 52, 56);
            return row;
        }

        private sealed class Row
        {
            public GameObject Root;
            public Image Box, Portrait, EnergyFill;
            public Text Name, Status, EnergyValue, Speed, RestLabel, ItemLabel;
            public Button Rest, ItemButton, Dismiss;
            public Staff Staff;
        }

        private sealed class HireButton
        {
            public StaffTypeDef Type;
            public Button Button;
            public Text Label;
        }
    }
}
