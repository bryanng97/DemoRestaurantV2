using System.Collections.Generic;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Progression;
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
        private Image _ownerPortrait, _itemIcon;
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

            // Energy item: count and a buy button. With several items (added in the BO) a tap on the icon switches.
            _item = Ui.Session.Content.Items.Count > 0 ? Ui.Session.Content.Items[0] : null;
            if (_item != null)
            {
                _itemIcon = UiKit.AddImage(UiKit.Place(UiKit.NewRect("ItemIcon", content), 0, 190, 48, 48), Ui.ItemIcon(_item));
                _itemIcon.raycastTarget = true;
                _itemIcon.gameObject.AddComponent<Button>().onClick.AddListener(NextItem);
                _itemLabel = UiKit.Label(content, "Item", 56, 190, 270, 48, 22, TextAnchor.MiddleLeft, UiKit.DarkText);
                _itemLabel.resizeTextForBestFit = true; _itemLabel.resizeTextMinSize = 11; _itemLabel.resizeTextMaxSize = 22;
                _itemLabel.horizontalOverflow = HorizontalWrapMode.Wrap; // a perk's effects take a second line
                // One tap: coffee for everyone who isn't full, most tired first; for a restaurant perk, use it.
                _refill = UiKit.TextButton(content, "RefillAll", UiKit.Gold, 22, RefillOrUse, out _refillLabel);
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
            // Stays left of the job buttons: long translations shrink instead of running underneath them.
            _ownerJob.horizontalOverflow = HorizontalWrapMode.Wrap;
            _ownerJob.verticalOverflow = VerticalWrapMode.Truncate;
            _ownerJob.resizeTextForBestFit = true;
            _ownerJob.resizeTextMinSize = 11;
            _ownerJob.resizeTextMaxSize = 17;
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
                _itemIcon.sprite = Ui.ItemIcon(_item);
                int owned = session.Items.Count(_item);
                _itemLabel.text = _item.EnergyRestore > 0
                    ? Loc.Format("ui.staff.item_count", Loc.Get(_item.NameKey), owned, _item.EnergyRestore.ToString("0"))
                    : Loc.Format("ui.staff.perk_count", Loc.Get(_item.NameKey), owned);
                if (_item.IsPerk) _itemLabel.text += "\n" + PerkText.For(_item);
                string restaurantPerks = RestaurantPerks();
                if (restaurantPerks.Length > 0) _itemLabel.text += "\n" + restaurantPerks;
                _buyLabel.text = session.CanBuyItem(_item) ? Loc.Format("ui.staff.buy", _item.Price)
                    : Loc.Format("ui.staff.unlocks_at", _item.UnlockLevel);
                if (!_item.Available || !_item.IsOnSale(System.DateTime.UtcNow)) _buyLabel.text = Loc.Get("ui.staff.item_not_sold");
                UiKit.SetButtonColor(_buy, session.CanBuyItem(_item) && session.Wallet.DisplayCoins >= _item.Price ? UiKit.Gold : UiKit.Disabled);
                if (_item.Target == ItemTarget.Restaurant)
                {
                    _refillLabel.text = Loc.Get("ui.staff.use_restaurant");
                    UiKit.SetButtonColor(_refill, owned > 0 && session.CheckPerk(_item, null) == PerkCheck.Ok ? UiKit.Gold : UiKit.Disabled);
                }
                else
                {
                    _refillLabel.text = Loc.Get("ui.staff.refill_all");
                    bool anyTired = false;
                    foreach (var s in session.Sim.Staff) anyTired |= s.Energy < StaffEnergyRules.Max;
                    UiKit.SetButtonColor(_refill, _item.EnergyRestore > 0 && anyTired && owned > 0 ? UiKit.Gold : UiKit.Disabled);
                }
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
            row.Status.text = Status(s) + StaffPerks(s);
            row.EnergyFill.fillAmount = s.Energy / StaffEnergyRules.Max;
            row.EnergyFill.color = s.IsResting ? new Color32(110, 160, 230, 255) : EnergyColor(s.Energy);
            row.EnergyValue.text = Mathf.FloorToInt(s.Energy) + "%";
            row.Speed.text = CurrentSpeed(s);
            row.Speed.color = s.SpeedMultiplier < 1f ? UiKit.Red : UiKit.SoftText;
            row.RestLabel.text = Loc.Get(s.IsResting || s.RestRequested ? "ui.staff.work" : "ui.staff.rest");
            UiKit.SetButtonColor(row.Rest, s.IsResting || s.RestRequested ? UiKit.Green : new Color32(110, 160, 230, 255));
            // Restaurant perks are used from the item row, not on one employee.
            bool onStaff = _item != null && _item.Target == ItemTarget.Employee;
            row.ItemLabel.text = onStaff ? Loc.Get(_item.NameKey) : "";
            row.ItemButton.gameObject.SetActive(onStaff);
            if (onStaff)
            {
                bool usable = _item.IsPerk ? session.CheckPerk(_item, s) == PerkCheck.Ok : s.Energy < StaffEnergyRules.Max;
                UiKit.SetButtonColor(row.ItemButton, session.Items.Count(_item) > 0 && usable ? UiKit.Gold : UiKit.Disabled);
            }
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
            if (!Ui.Session.CanBuyItem(_item))
                Ui.Toast(_item.Available ? Loc.Format("ui.staff.unlocks_at", _item.UnlockLevel) : Loc.Get("ui.staff.item_not_sold"));
            else if (!Ui.Session.BuyItem(_item))
                Ui.Toast(Loc.Get("ui.staff.no_coins"));
            Refresh();
        }

        /// <summary>Shows the next staff item that is on sale or still owned.</summary>
        private void NextItem()
        {
            var items = Ui.Session.Content.Items;
            int start = System.Math.Max(0, IndexOf(items, _item));
            for (int step = 1; step <= items.Count; step++)
            {
                var candidate = items[(start + step) % items.Count];
                if (candidate.Available || Ui.Session.Items.Count(candidate) > 0)
                {
                    _item = candidate;
                    break;
                }
            }
            Refresh();
        }

        private static int IndexOf(IReadOnlyList<ItemDef> items, ItemDef item)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] == item) return i;
            return -1;
        }

        /// <summary>" · perks 2 (12m)" for an employee with perks running, soonest to end first.</summary>
        private string StaffPerks(Staff s)
        {
            var now = System.DateTime.UtcNow;
            int n = 0;
            ActivePerk soonest = null;
            foreach (var p in Ui.Session.Perks.Active)
            {
                if (p.Staff != s || p.ExpiresUtc <= now) continue;
                n++;
                soonest ??= p;
            }
            return n == 0 ? "" : " · " + Loc.Format("ui.staff.perks_on", n, PerkText.Remaining(soonest.Remaining(now)));
        }

        /// <summary>The restaurant perks running now with their effects and time left, or "".</summary>
        private string RestaurantPerks()
        {
            var now = System.DateTime.UtcNow;
            var parts = new List<string>();
            foreach (var p in Ui.Session.Perks.Active)
                if (p.Staff == null && p.ExpiresUtc > now)
                    parts.Add(PerkText.Effects(p.Effects) + " (" + PerkText.Remaining(p.Remaining(now)) + ")");
            return parts.Count == 0 ? "" : Loc.Format("ui.staff.restaurant_perks", string.Join(", ", parts));
        }

        private void RefillOrUse()
        {
            if (_item == null) return;
            if (_item.Target == ItemTarget.Restaurant)
            {
                var check = Ui.Session.CheckPerk(_item, null);
                if (Ui.Session.Items.Count(_item) == 0) Ui.Toast(Loc.Format("ui.staff.no_items", Loc.Get(_item.NameKey)));
                else if (check != PerkCheck.Ok) Ui.Toast(PerkProblem(check));
                else if (Ui.Session.UseRestaurantItem(_item)) Ui.Toast(Loc.Format("ui.staff.perk_started", Loc.Get(_item.NameKey)));
                Refresh();
                return;
            }
            if (_item.EnergyRestore <= 0)
            {
                Ui.Toast(Loc.Get("ui.staff.perk_pick_staff"));
                return;
            }
            RefillAll();
        }

        private static string PerkProblem(PerkCheck check) =>
            check == PerkCheck.Disabled ? Loc.Get("ui.staff.perks_off") : Loc.Get("ui.staff.perk_slots_full");

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
            var check = Ui.Session.CheckPerk(_item, staff);
            if (!_item.IsPerk && staff.Energy >= StaffEnergyRules.Max)
                Ui.Toast(Loc.Format("ui.staff.full_energy", Loc.Get(staff.Type.NameKey)));
            else if (check != PerkCheck.Ok)
                Ui.Toast(PerkProblem(check));
            else if (!Ui.Session.UseItem(_item, staff))
                Ui.Toast(Loc.Format("ui.staff.no_items", Loc.Get(_item.NameKey)));
            else if (_item.IsPerk)
                Ui.Toast(Loc.Format("ui.staff.perk_started", Loc.Get(_item.NameKey)));
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
