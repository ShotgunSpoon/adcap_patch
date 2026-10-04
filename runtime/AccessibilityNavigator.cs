using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace AdCapUnityMCP
{
    internal static partial class AccessibilityNavigator
    {
        private static readonly List<Selectable> Controls = new List<Selectable>();
        private static int _index = -1;
        private static int _pendingDialogAnnouncementFrames;
        private static string _lastInfoDialogSignature;
        private static string _lastAnnouncedDialogMessage;
        private static readonly List<string> ScreenText = new List<string>();
        private static int _textIndex;
        private static bool _textMode;
        private static int _businessIndex;
        private static int _businessRow = 2;
        private static int _topTabIndex;
        private static float _topTabIntentUntil;
        private static int _pendingBusinessFrames;
        private static BusinessState _pendingBusinessBefore;
        private static string _pendingBusinessAction;
        private static string _pendingDescription;
        private static int _panelItemIndex;
        private static int _panelRow;
        private static int _pendingUpgradeScrollFrames;
        private static int _pendingUpgradeScrollDirection;
        private static bool _pendingManagerScroll;
        private static string _pendingPanelTargetId;
        private static double _pendingPanelTargetCost = double.NaN;
        private static int _pendingPanelTargetRow;
        private static int _managerCursorPanelId;
        private static string _managerCursorId;
        private static int _pendingManagerFocusFrames;
        private static int _pendingManagerFocusAttempts;
        private static string _lastWelcomeBackPath;
        private static int _pendingWelcomeBackFrames;
        private static bool _enterTopTabFromEnd;
        private static float _musicVolume = -1f;
        private static bool _gildMode;
        private static int _gildIndex;
        private static int _pendingGildEntryFrames;

        private static readonly string[] TopTabs = { "Businesses", "Managers", "Upgrades", "Unlocks", "Investors", "Shop", "Career", "Adventures", "Connect" };

        private sealed class BusinessState
        {
            public Transform Root;
            public string Name;
            public string Owned;
            public string Earnings;
            public string EarningsDenomination;
            public string Rate;
            public string Timer;
            public string BuyCost;
            public string BuyCostDenomination;
            public string PurchaseCost;
            public double ProductionSeconds;
            public double OwnedValue;
            public double EarningsValue;
            public double RateValue;
            public double BuyCostValue;
            public double PurchaseCostValue;
            public double BuyQuantityValue;
            public Button Production;
            public Button Buy;
            public Button Purchase;
            public Button Gild;
            public bool IsBoosted;
            public bool IsOwned { get { return !double.IsNaN(OwnedValue) && !double.IsInfinity(OwnedValue) ? OwnedValue > 0d : !string.IsNullOrEmpty(Owned); } }
        }

        private sealed class PanelItem
        {
            public string Name;
            public string Description;
            public string Cost;
            public Selectable Action;
            public string ActionName;
            public string UnavailableReason;
            public object Model;
            public string StableId;
            public double NumericCost = double.NaN;
        }

        public static void Initialize() { Refresh(false); }

        public static void UpdateKeyboard()
        {
            EventAccessibility.Update();
            MaintainMusicVolume();
            DetectWelcomeBackDialog();
            DetectInfoDialog();
            if (_pendingWelcomeBackFrames > 0 && --_pendingWelcomeBackFrames == 0) AnnounceWelcomeBackDialog();
            if (_pendingDialogAnnouncementFrames > 0 && --_pendingDialogAnnouncementFrames == 0) AnnounceSmallDialog();
            if (_pendingBusinessFrames > 0 && --_pendingBusinessFrames == 0) AnnounceBusinessResult();
            if (_pendingUpgradeScrollFrames > 0 && --_pendingUpgradeScrollFrames == 0) CompleteUpgradeScroll();
            if (_pendingManagerFocusFrames > 0 && --_pendingManagerFocusFrames == 0) CompleteManagerInitialFocus();
            if (_pendingGildEntryFrames > 0 && --_pendingGildEntryFrames == 0) CompleteGildModeEntry();
            if (EventAccessibility.HandleReturnShortcut()) return;
            if (Input.GetKeyDown(KeyCode.W))
            {
                var shifted = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shifted) UseTimeWarpExpress(true);
                else ReadTimeWarpExpressCount(true);
                return;
            }
            if (Input.GetKeyDown(KeyCode.F8)) { ToggleTextMode(); return; }
            if (_textMode)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow)) ReadText(-1);
                if (Input.GetKeyDown(KeyCode.DownArrow)) ReadText(1);
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape)) { _textMode = false; Speech.Write("Control navigation"); }
                return;
            }
            if (_gildMode)
            {
                if (Input.GetKeyDown(KeyCode.G)) { ToggleGildMode(true); return; }
                if (!GildModeAvailable()) { ExitGildMode("Gilding mode ended because it is unavailable on the current planet", true); return; }
                if (_pendingGildEntryFrames > 0) return;
                if (Input.GetKeyDown(KeyCode.Escape)) { ExitGildMode("Gilding mode closed", true); return; }
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow)) { MoveGildInvestment(-1, true); return; }
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.DownArrow)) { MoveGildInvestment(1, true); return; }
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) { ActivateGildInvestment(true); return; }
                if (Input.GetKeyDown(KeyCode.F6)) { Speak(CurrentGildLabel(), true); return; }
                return;
            }
            if (EventAccessibility.HandleKeyboard()) return;
            if (Input.GetKeyDown(KeyCode.G)) { ToggleGildMode(true); return; }
            if (Input.GetKeyDown(KeyCode.C)) { Speech.Write(Money()); return; }
            if (Input.GetKeyDown(KeyCode.Home)) { AdjustMusicVolume(0.1f, true); return; }
            if (Input.GetKeyDown(KeyCode.End)) { AdjustMusicVolume(-0.1f, true); return; }
            if (Input.GetKeyDown(KeyCode.LeftBracket)) { CycleAccessibleTab(-1, true); return; }
            if (Input.GetKeyDown(KeyCode.RightBracket)) { CycleAccessibleTab(1, true); return; }
            if (!HasModal())
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) MoveBusiness(-1, true);
                if (Input.GetKeyDown(KeyCode.RightArrow)) MoveBusiness(1, true);
                if (Input.GetKeyDown(KeyCode.UpArrow)) MoveBusinessRow(-1, true);
                if (Input.GetKeyDown(KeyCode.DownArrow)) MoveBusinessRow(1, true);
                var activatePressed = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
                if (activatePressed)
                {
                    ActivateBusiness(true);
                    return;
                }
                var purchaseHeld = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.Space);
                if (purchaseHeld && TryRapidBusinessPurchase(false)) return;
                if (Input.GetKeyDown(KeyCode.F6)) Speech.Write(CurrentBusinessLabel());
                return;
            }
            if (Input.GetKeyDown(KeyCode.F6)) Speech.Write(CurrentPanelLabel());
            if (Input.GetKeyDown(KeyCode.F7)) Speech.Write(Status());
            if (Input.GetKeyDown(KeyCode.LeftArrow)) MovePanelItem(-1, true);
            if (Input.GetKeyDown(KeyCode.RightArrow)) MovePanelItem(1, true);
            if (Input.GetKeyDown(KeyCode.UpArrow)) MovePanelRow(-1, true);
            if (Input.GetKeyDown(KeyCode.DownArrow)) MovePanelRow(1, true);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) ActivatePanelItem(true);
        }

        public static string Refresh(bool announce)
        {
            Controls.Clear();
            var candidates = Resources.FindObjectsOfTypeAll<Selectable>()
                .Where(s => (s is Button || s is Toggle) && IsPresent(s) && !IsPlayFabControl(s)).ToList();
            var modal = candidates.Where(s => CanvasOrder(s) >= 20).ToList();
            if (modal.Count > 0)
            {
                var top = modal.Max(CanvasOrder);
                candidates = modal.Where(s => CanvasOrder(s) == top).ToList();
            }
            Controls.AddRange(candidates.OrderBy(s => -CanvasOrder(s))
                .ThenByDescending(s => s.transform.position.y).ThenBy(s => s.transform.position.x));
            if (Controls.Count == 0) _index = -1;
            else if (_index < 0 || _index >= Controls.Count) _index = 0;
            var message = "Found " + Controls.Count + " controls";
            if (announce) Speech.Write(message + ". " + Current(false));
            return message;
        }

        public static string Current(bool announce)
        {
            EnsureCurrent();
            var message = _index < 0 ? "No controls" : (_index + 1) + " of " + Controls.Count + ", " + Label(Controls[_index]);
            if (announce) Speech.Write(message);
            return message;
        }

        public static string Next(bool announce)
        {
            Refresh(false);
            if (Controls.Count > 0) _index = (_index + 1) % Controls.Count;
            return Current(announce);
        }

        public static string Previous(bool announce)
        {
            Refresh(false);
            if (Controls.Count > 0) _index = (_index - 1 + Controls.Count) % Controls.Count;
            return Current(announce);
        }

        public static string Activate(bool announce)
        {
            EnsureCurrent();
            if (_index < 0) return Current(announce);
            var control = Controls[_index];
            var label = Label(control);
            if (!control.interactable)
            {
                var unavailable = "Unavailable. " + label;
                if (announce) Speech.Write(unavailable);
                return unavailable;
            }
            var button = control as Button;
            if (button != null)
            {
                var purchaseDescription = PurchaseLabel(control);
                if (!string.IsNullOrEmpty(purchaseDescription))
                {
                    var affected = Businesses().FirstOrDefault(b => purchaseDescription.IndexOf(b.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (affected != null)
                    {
                        _pendingBusinessBefore = affected;
                        _pendingBusinessAction = "upgrade";
                        _pendingDescription = purchaseDescription;
                        _pendingBusinessFrames = 2;
                    }
                }
                button.onClick.Invoke();
            }
            var toggle = control as Toggle;
            if (toggle != null) toggle.isOn = true;
            var message = "Activated " + label;
            if (announce) Speech.Write(message);
            return message;
        }

        public static string ListControls()
        {
            Refresh(false);
            var b = new StringBuilder();
            for (var i = 0; i < Controls.Count; i++)
                b.Append(i + 1).Append(": ").Append(Label(Controls[i])).Append(" [").Append(Path(Controls[i].transform)).AppendLine("]");
            return b.ToString();
        }

        public static string CycleTopTab(int direction, bool announce)
        {
            _enterTopTabFromEnd = direction < 0;
            // A modal title can remain visible for several frames after its close button is
            // invoked.  During a rapid sequence, keep advancing the intended destination
            // instead of snapping back to that stale title on every key press.
            if (Time.unscaledTime > _topTabIntentUntil) _topTabIndex = CurrentTopTabIndex();
            _topTabIndex = (_topTabIndex + direction + TopTabs.Length) % TopTabs.Length;
            _topTabIntentUntil = Time.unscaledTime + 0.75f;
            return ActivateTopTab(announce);
        }

        public static string CycleAccessibleTab(int direction, bool announce)
        {
            return EventAccessibility.IsActive ? EventAccessibility.CycleTab(direction, announce) : CycleTopTab(direction, announce);
        }

        private static string ToggleGildMode(bool announce)
        {
            if (_gildMode) return ExitGildMode("Gilding mode closed", announce);
            var button = GildModeButton();
            if (button == null || !button.interactable)
                return Speak("Gilding with Mega Tickets is unavailable on this planet", announce);
            button.onClick.Invoke();
            _gildMode = true;
            _gildIndex = 0;
            _pendingGildEntryFrames = 3;
            return Speak("Opening Mega Ticket gilding mode", announce);
        }

        private static void CompleteGildModeEntry()
        {
            if (!_gildMode) return;
            var investments = GildableBusinesses();
            if (investments.Count == 0)
            {
                ExitGildMode("No owned, ungilded investments are currently eligible for a Mega Ticket", true);
                return;
            }
            Speech.Write("Gilding mode. Mega Tickets " + MegaTicketCount() + ". " + investments.Count + " eligible investments. " + CurrentGildLabel(investments) + ". Use the arrow keys, then Enter or Space to choose. Press G or Escape to cancel");
        }

        private static string ExitGildMode(string message, bool announce)
        {
            if (_gildMode)
            {
                var button = GildModeButton();
                if (button != null && button.interactable) button.onClick.Invoke();
            }
            _gildMode = false;
            _gildIndex = 0;
            _pendingGildEntryFrames = 0;
            return Speak(message, announce);
        }

        private static string MoveGildInvestment(int direction, bool announce)
        {
            var investments = GildableBusinesses();
            if (investments.Count == 0) return ExitGildMode("No investments are eligible for a Mega Ticket", announce);
            _gildIndex = (_gildIndex + direction + investments.Count) % investments.Count;
            return Speak(CurrentGildLabel(investments), announce);
        }

        private static string ActivateGildInvestment(bool announce)
        {
            var investments = GildableBusinesses();
            if (investments.Count == 0) return ExitGildMode("No investments are eligible for a Mega Ticket", announce);
            if (_gildIndex >= investments.Count) _gildIndex = 0;
            var investment = investments[_gildIndex];
            if (investment.Gild == null || !investment.Gild.interactable)
                return Speak(investment.Name + " cannot be gilded", announce);
            investment.Gild.onClick.Invoke();
            _gildMode = false;
            _gildIndex = 0;
            return Speak("Opening gild confirmation for " + investment.Name + ". Cost 1 Mega Ticket", announce);
        }

        private static string CurrentGildLabel() { return CurrentGildLabel(GildableBusinesses()); }

        private static string CurrentGildLabel(List<BusinessState> investments)
        {
            if (investments.Count == 0) return "No eligible investments";
            if (_gildIndex >= investments.Count) _gildIndex = 0;
            var investment = investments[_gildIndex];
            return (_gildIndex + 1) + " of " + investments.Count + ", " + investment.Name + ". Owned " + CountExact(investment.OwnedValue, investment.Owned) + ". Earns " + ProductionMoney(investment) + " per production. Gild using 1 Mega Ticket";
        }

        private static List<BusinessState> GildableBusinesses()
        {
            return Businesses().Where(b => b.IsOwned && !b.IsBoosted && b.Gild != null && IsPresent(b.Gild)).ToList();
        }

        private static Button GildModeButton()
        {
            // btn_goldGilding is the serialized TopPanel field name, but Unity does not
            // require the referenced GameObject to share that name. Resolve the field
            // first and retain name/path matching only as a fallback for UI variants.
            var topPanel = Resources.FindObjectsOfTypeAll<Component>().FirstOrDefault(c => c != null &&
                string.Equals(c.GetType().Name, "TopPanel", StringComparison.Ordinal));
            var fieldButton = ObjectMember(topPanel, "btn_goldGilding") as Button;
            if (fieldButton != null && IsPresent(fieldButton)) return fieldButton;
            return Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b => b != null && IsPresent(b) &&
                (string.Equals(b.gameObject.name, "btn_goldGilding", StringComparison.OrdinalIgnoreCase) ||
                 Path(b.transform).IndexOf("Gild", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 ComponentAncestor(b.transform, "TopPanel") != null));
        }

        private static bool GildModeAvailable() { return GildModeButton() != null; }

        private static string MegaTicketCount()
        {
            var button = GildModeButton();
            var value = button == null ? "" : ChildText(button.transform);
            var match = Regex.Match(value, "[0-9][0-9,]*");
            return match.Success ? match.Value : "unknown";
        }

        public static string ActivateTopTab(bool announce)
        {
            var name = TopTabs[_topTabIndex];
            if (name == "Businesses")
            {
                CloseTopModal();
                _businessRow = 2;
                _enterTopTabFromEnd = false;
                var message = "Businesses tab. " + CurrentBusinessLabel();
                if (announce) Speech.Write(message);
                return message;
            }
            var button = FindTopTabButton(name);
            if (button == null)
            {
                _enterTopTabFromEnd = false;
                var missing = name + " tab is unavailable";
                if (announce) Speech.Write(missing);
                return missing;
            }
            CloseTopModal();
            button.onClick.Invoke();
            Refresh(false);
            _panelItemIndex = 0;
            _panelRow = 0;
            if (name == "Managers")
            {
                _pendingManagerFocusAttempts = 0;
                _pendingManagerFocusFrames = 1;
            }
            var selected = name + " tab";
            _enterTopTabFromEnd = false;
            if (announce) Speech.Write(selected);
            return selected;
        }

        public static string OpenEventSelector(bool announce)
        {
            var button = FindTopTabButton("Adventures");
            if (button == null || !button.interactable)
                return Speak("Multiple events are active, but the Adventures event selector is unavailable", announce);
            CloseTopModal();
            button.onClick.Invoke();
            var controller = Resources.FindObjectsOfTypeAll<Component>().FirstOrDefault(component => component != null &&
                component.gameObject != null && component.gameObject.activeInHierarchy &&
                string.Equals(component.GetType().Name, "AdventuresModalController", StringComparison.Ordinal));
            if (controller == null)
                return Speak("Adventures opened. Select the Events section to choose an active event", announce);
            try
            {
                var showEvents = controller.GetType().GetMethod("ShowEventPanel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (showEvents == null)
                    return Speak("Adventures opened. Select the Events section to choose an active event", announce);
                showEvents.Invoke(controller, null);
            }
            catch
            {
                return Speak("Adventures opened. Select the Events section to choose an active event", announce);
            }
            _topTabIndex = Array.IndexOf(TopTabs, "Adventures");
            _panelItemIndex = 0;
            _panelRow = 0;
            _enterTopTabFromEnd = false;
            Refresh(false);
            return Speak("Multiple events are active. Events selector opened. Use the arrow keys to choose an event and its Go action, then press Enter or Space", announce);
        }

        public static string MoveBusiness(int direction, bool announce)
        {
            var businesses = Businesses();
            if (businesses.Count == 0) return Speak("No businesses found", announce);
            _businessIndex = (_businessIndex + direction + businesses.Count) % businesses.Count;
            if (!businesses[_businessIndex].IsOwned) _businessRow = 2;
            return Speak(CurrentBusinessLabel(businesses), announce);
        }

        public static string MoveBusinessRow(int direction, bool announce)
        {
            var businesses = Businesses();
            if (businesses.Count == 0) return Speak("No businesses found", announce);
            if (_businessIndex >= businesses.Count) _businessIndex = 0;
            if (!businesses[_businessIndex].IsOwned) _businessRow = 2;
            else _businessRow = (_businessRow + direction + 4) % 4;
            return Speak(CurrentBusinessLabel(businesses), announce);
        }

        public static string ActivateBusiness(bool announce)
        {
            var businesses = Businesses();
            if (businesses.Count == 0) return Speak("No businesses found", announce);
            if (_businessIndex >= businesses.Count) _businessIndex = 0;
            var business = businesses[_businessIndex];
            if (!business.IsOwned)
            {
                if (business.Purchase == null || !business.Purchase.interactable)
                    return Speak(business.Name + " cannot be purchased yet. Cost " + MoneyExact(business.PurchaseCostValue, business.PurchaseCost) + ". " + Money(), announce);
                _pendingBusinessBefore = business;
                _pendingBusinessAction = "purchase";
                business.Purchase.onClick.Invoke();
                _pendingBusinessFrames = 2;
                return Speak("Purchasing " + business.Name, announce);
            }
            if (_businessRow == 0 || _businessRow == 1) return Speak(BusinessDetails(business), announce);
            if (_businessRow == 2)
            {
                if (business.Production == null) return Speak("Production is automated", announce);
                if (!business.Production.interactable)
                    return Speak("Production in progress. " + Remaining(business.Timer) + " remaining", announce);
                _pendingBusinessBefore = business;
                _pendingBusinessAction = "production";
                business.Production.onClick.Invoke();
                _pendingBusinessFrames = 2;
                return Speak("Starting " + business.Name + " production", announce);
            }
            if (business.Buy == null || !business.Buy.interactable)
                return Speak("Cannot buy another " + business.Name + ". Cost " + BuyMoney(business) + ". " + Money(), announce);
            _pendingBusinessBefore = business;
            _pendingBusinessAction = "buy";
            business.Buy.onClick.Invoke();
            _pendingBusinessFrames = 2;
            return Speak("Buying one " + business.Name, announce);
        }

        private static bool TryRapidBusinessPurchase(bool announce)
        {
            var businesses = Businesses();
            if (businesses.Count == 0) return false;
            if (_businessIndex >= businesses.Count) _businessIndex = 0;
            var business = businesses[_businessIndex];
            Button purchaseButton;
            if (!business.IsOwned) purchaseButton = business.Purchase;
            else
            {
                if (_businessRow != 3) return false;
                purchaseButton = business.Buy;
            }

            // This is a purchase row, so consume the held key even when the next
            // purchase is unaffordable. Do not repeatedly announce that state.
            if (purchaseButton == null || !purchaseButton.interactable) return true;
            if (_pendingBusinessBefore == null)
            {
                _pendingBusinessBefore = business;
                _pendingBusinessAction = business.IsOwned ? "buy" : "purchase";
            }
            purchaseButton.onClick.Invoke();
            // Keep postponing the existing before/after announcement while the
            // key is held. Once purchasing stops it reports the combined owned,
            // earnings, production-time, and automatic-unlock changes once.
            _pendingBusinessFrames = 2;
            if (announce) Speech.Write("Buying " + business.Name);
            return true;
        }

        public static string CurrentBusinessLabel()
        {
            return CurrentBusinessLabel(Businesses());
        }

        public static string Money()
        {
            var cash = FindActiveText("Txt_CashOnHand");
            return "Money " + MoneyValue(cash) + ". Gold " + FindActiveText("Txt_Gold") + ". Mega Bucks " + FindActiveText("Txt_MegaBucks");
        }

        public static string MovePanelItem(int direction, bool announce)
        {
            var items = PanelItems();
            if (items.Count == 0) return Speak("No items in this tab", announce);
            string managerResult;
            if (TryMoveManager(items, direction, out managerResult)) return Speak(managerResult, announce);
            if (TryEnterManagerList(items, direction, out managerResult)) return Speak(managerResult, announce);
            if (TryScrollVirtualList(items, direction))
                return Speak(_pendingManagerScroll ? "Moving to more managers" : "Moving to more upgrades", announce);
            _panelItemIndex = (_panelItemIndex + direction + items.Count) % items.Count;
            if (!PanelRowExists(items[_panelItemIndex], _panelRow)) _panelRow = 0;
            return Speak(CurrentPanelLabel(items), announce);
        }

        private static bool TryScrollVirtualList(List<PanelItem> items, int direction)
        {
            if (_panelItemIndex < 0 || _panelItemIndex >= items.Count) return false;
            var current = items[_panelItemIndex];
            var manager = IsManagerItem(current);
            var virtualItems = items.Select((item, index) => new { item, index })
                .Where(x => manager ? IsManagerItem(x.item) : IsUpgradeItem(x.item)).ToList();
            if (virtualItems.Count == 0 || (!manager && !IsUpgradeItem(current))) return false;
            var atLowerEdge = direction > 0 && _panelItemIndex == virtualItems[virtualItems.Count - 1].index;
            var atUpperEdge = direction < 0 && _panelItemIndex == virtualItems[0].index;
            if (!atLowerEdge && !atUpperEdge) return false;
            var scroll = current.Action.GetComponentInParent<ScrollRect>();
            if (scroll == null) return false;
            var oldPosition = scroll.verticalNormalizedPosition;
            var viewport = scroll.viewport != null ? scroll.viewport : scroll.GetComponent<RectTransform>();
            var scrollableHeight = Mathf.Max(1f, scroll.content.rect.height - viewport.rect.height);
            var page = Mathf.Clamp(viewport.rect.height * 0.8f / scrollableHeight, 0.01f, 1f);
            var newPosition = Mathf.Clamp01(oldPosition + (direction < 0 ? page : -page));
            if (Mathf.Abs(newPosition - oldPosition) < 0.01f) return false;
            scroll.verticalNormalizedPosition = newPosition;
            _pendingManagerScroll = manager;
            _pendingUpgradeScrollDirection = direction;
            _pendingUpgradeScrollFrames = 6;
            return true;
        }

        private static void CompleteUpgradeScroll()
        {
            var items = PanelItems();
            var upgrades = items.Select((item, index) => new { item, index })
                .Where(x => _pendingManagerScroll ? IsManagerItem(x.item) : IsUpgradeItem(x.item)).ToList();
            if (upgrades.Count == 0) { Speech.Write(_pendingManagerScroll ? "No more managers" : "No more upgrades"); return; }
            if (_pendingManagerScroll && !string.IsNullOrEmpty(_pendingPanelTargetId))
            {
                var exact = upgrades.FirstOrDefault(x => string.Equals(x.item.StableId, _pendingPanelTargetId, StringComparison.Ordinal));
                if (exact == null && !double.IsNaN(_pendingPanelTargetCost))
                    exact = upgrades.OrderBy(x => Math.Abs(ManagerSortKey(x.item) - _pendingPanelTargetCost)).FirstOrDefault();
                _panelItemIndex = exact == null ? upgrades[0].index : exact.index;
                _panelRow = PanelRowExists(items[_panelItemIndex], _pendingPanelTargetRow) ? _pendingPanelTargetRow : 0;
                _pendingPanelTargetId = null;
                _pendingPanelTargetCost = double.NaN;
            }
            else
            {
                _panelItemIndex = _pendingUpgradeScrollDirection < 0
                    ? upgrades[upgrades.Count - 1].index : upgrades[0].index;
                _panelRow = 0;
            }
            Speech.Write(CurrentPanelLabel(items));
        }

        private static bool TryMoveManager(List<PanelItem> items, int direction, out string result)
        {
            result = null;
            if (_panelItemIndex < 0 || _panelItemIndex >= items.Count) return false;
            var current = items[_panelItemIndex];
            if (!IsManagerItem(current) || current.Model == null || string.IsNullOrEmpty(current.StableId)) return false;
            var models = ManagerModels(current);
            if (models.Count == 0) return false;
            var panel = ComponentAncestor(current.Action.transform, "UpgradePanel");
            var panelId = panel == null ? 0 : panel.GetInstanceID();
            var sourceId = panelId != 0 && panelId == _managerCursorPanelId &&
                models.Any(model => string.Equals(ManagerModelId(model), _managerCursorId, StringComparison.Ordinal))
                ? _managerCursorId : current.StableId;
            var currentIndex = models.FindIndex(model => string.Equals(ManagerModelId(model), sourceId, StringComparison.Ordinal));
            if (currentIndex < 0) return false;
            var targetIndex = currentIndex + (direction < 0 ? -1 : 1);
            // At a true logical edge, move out of the manager list here. Returning
            // false would let the generic virtual-list scroller run afterward and
            // recycle focus back into old rows, creating an endless loop.
            if (targetIndex < 0 || targetIndex >= models.Count)
            {
                _pendingUpgradeScrollFrames = 0;
                _pendingPanelTargetId = null;
                _pendingPanelTargetCost = double.NaN;
                _managerCursorPanelId = 0;
                _managerCursorId = null;
                if (targetIndex < 0)
                {
                    var tabIndex = items.FindLastIndex(item => item.Action is Toggle && item.ActionName == "select");
                    _panelItemIndex = tabIndex >= 0 ? tabIndex : (_panelItemIndex - 1 + items.Count) % items.Count;
                }
                else
                {
                    var lastManagerIndex = items.FindLastIndex(IsManagerItem);
                    _panelItemIndex = lastManagerIndex >= 0 ? (lastManagerIndex + 1) % items.Count : (_panelItemIndex + 1) % items.Count;
                }
                _panelRow = 0;
                result = CurrentPanelLabel(items);
                return true;
            }
            var target = models[targetIndex];
            var targetId = ManagerModelId(target);
            _managerCursorPanelId = panelId;
            _managerCursorId = targetId;
            var visible = items.Select((item, index) => new { item, index })
                .FirstOrDefault(x => IsManagerItem(x.item) && string.Equals(x.item.StableId, targetId, StringComparison.Ordinal));
            if (visible != null)
            {
                _pendingUpgradeScrollFrames = 0;
                _pendingPanelTargetId = null;
                _pendingPanelTargetCost = double.NaN;
                _panelItemIndex = visible.index;
                if (!PanelRowExists(visible.item, _panelRow)) _panelRow = 0;
                result = CurrentPanelLabel(items);
                return true;
            }
            if (!ScrollToManager(current, models, target, direction))
            {
                result = "Manager list could not scroll to " + ManagerModelName(target);
                return true;
            }
            result = "Moving to " + ManagerModelName(target);
            return true;
        }

        private static bool TryEnterManagerList(List<PanelItem> items, int direction, out string result)
        {
            result = null;
            if (_panelItemIndex < 0 || _panelItemIndex >= items.Count || IsManagerItem(items[_panelItemIndex])) return false;
            var candidateIndex = (_panelItemIndex + direction + items.Count) % items.Count;
            var candidate = items[candidateIndex];
            if (!IsManagerItem(candidate)) return false;
            var models = ManagerModels(candidate);
            if (models.Count == 0) return false;
            var target = direction < 0 ? models[models.Count - 1] : models[0];
            var targetId = ManagerModelId(target);
            var panel = ComponentAncestor(candidate.Action.transform, "UpgradePanel");
            _managerCursorPanelId = panel == null ? 0 : panel.GetInstanceID();
            _managerCursorId = targetId;
            var visible = items.Select((item, index) => new { item, index })
                .FirstOrDefault(x => IsManagerItem(x.item) && string.Equals(x.item.StableId, targetId, StringComparison.Ordinal));
            if (visible != null)
            {
                _panelItemIndex = visible.index;
                _panelRow = 0;
                result = CurrentPanelLabel(items);
                return true;
            }
            if (!ScrollToManager(candidate, models, target, direction)) return false;
            result = "Moving to " + ManagerModelName(target);
            return true;
        }

        private static bool IsUpgradeItem(PanelItem item)
        {
            return item != null && item.Action != null &&
                (ComponentAncestor(item.Action.transform, "UpgradeView") != null ||
                 Path(item.Action.transform).IndexOf("/UpgradeContent/UpgradeGridRowView", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsManagerItem(PanelItem item)
        {
            return item != null && item.Action != null &&
                (ComponentAncestor(item.Action.transform, "ManagerView") != null ||
                 Path(item.Action.transform).IndexOf("/ManagerContent/UpgradeGridRowView", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static List<object> ManagerModels(PanelItem current)
        {
            var result = new List<object>();
            if (current == null || current.Action == null) return result;
            var panel = ComponentAncestor(current.Action.transform, "UpgradePanel");
            var available = ObjectMember(panel, "availableUpgrades") as System.Collections.IEnumerable;
            if (available == null) return result;
            foreach (var model in available)
                if (model != null && !string.IsNullOrEmpty(ManagerModelId(model))) result.Add(model);
            return result.OrderBy(ManagerModelCost).ThenBy(ManagerModelName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string ManagerModelId(object model) { return ObjectTextMember(model, "id"); }
        private static string ManagerModelName(object model) { return ObjectTextMember(model, "name"); }
        private static double ManagerModelCost(object model) { return ObjectDoubleMember(model, "cost"); }
        private static double ManagerModelOrder(object model) { return ObjectDoubleMember(model, "order"); }
        private static double ManagerModelSortKey(object model)
        {
            var cost = ManagerModelCost(model);
            return cost > 0 && !double.IsNaN(cost) ? Math.Log10(cost) : double.MaxValue;
        }

        private static bool ScrollToManager(PanelItem current, List<object> logicalModels, object target, int direction)
        {
            if (current == null || current.Action == null || target == null) return false;
            var scroll = current.Action.GetComponentInParent<ScrollRect>();
            if (scroll == null) return false;
            var nativeModels = logicalModels.OrderBy(ManagerModelOrder).ThenBy(ManagerModelCost).ToList();
            var targetId = ManagerModelId(target);
            var nativeIndex = nativeModels.FindIndex(model => string.Equals(ManagerModelId(model), targetId, StringComparison.Ordinal));
            if (nativeIndex < 0) return false;
            var position = nativeModels.Count <= 1 ? 1f : 1f - (float)nativeIndex / (nativeModels.Count - 1);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(position);
            Canvas.ForceUpdateCanvases();
            _pendingManagerScroll = true;
            var panel = ComponentAncestor(current.Action.transform, "UpgradePanel");
            _managerCursorPanelId = panel == null ? 0 : panel.GetInstanceID();
            _managerCursorId = targetId;
            _pendingUpgradeScrollDirection = direction;
            _pendingPanelTargetId = targetId;
            _pendingPanelTargetCost = ManagerModelSortKey(target);
            _pendingPanelTargetRow = _panelRow;
            _pendingUpgradeScrollFrames = 6;
            return true;
        }

        private static void CompleteManagerInitialFocus()
        {
            var items = PanelItems();
            var current = items.FirstOrDefault(IsManagerItem);
            if (current == null)
            {
                RetryManagerInitialFocus();
                return;
            }
            var models = ManagerModels(current);
            if (models.Count == 0)
            {
                RetryManagerInitialFocus();
                return;
            }
            _pendingManagerFocusAttempts = 0;
            var first = models[0];
            var targetId = ManagerModelId(first);
            var panel = ComponentAncestor(current.Action.transform, "UpgradePanel");
            _managerCursorPanelId = panel == null ? 0 : panel.GetInstanceID();
            _managerCursorId = targetId;
            var visible = items.Select((item, index) => new { item, index })
                .FirstOrDefault(x => IsManagerItem(x.item) && string.Equals(x.item.StableId, targetId, StringComparison.Ordinal));
            _panelRow = 2;
            if (visible != null)
            {
                _panelItemIndex = visible.index;
                Speech.Write(CurrentPanelLabel(items));
                return;
            }
            _panelItemIndex = items.IndexOf(current);
            ScrollToManager(current, models, first, -1);
        }

        private static void RetryManagerInitialFocus()
        {
            if (++_pendingManagerFocusAttempts <= 60) _pendingManagerFocusFrames = 1;
        }

        public static string MovePanelRow(int direction, bool announce)
        {
            var items = PanelItems();
            if (items.Count == 0) return Speak("No items in this tab", announce);
            if (_panelItemIndex >= items.Count) _panelItemIndex = 0;
            var item = items[_panelItemIndex];
            for (var i = 0; i < 4; i++)
            {
                _panelRow = (_panelRow + direction + 4) % 4;
                if (PanelRowExists(item, _panelRow)) break;
            }
            return Speak(CurrentPanelLabel(items), announce);
        }

        public static string ActivatePanelItem(bool announce)
        {
            var items = PanelItems();
            if (items.Count == 0) return Speak("No items in this tab", announce);
            if (_panelItemIndex >= items.Count) _panelItemIndex = 0;
            var item = items[_panelItemIndex];
            var actionOnly = IsActionOnly(item);
            if ((!actionOnly && _panelRow != 3) || item.Action == null) return Speak(PanelDetails(item), announce);
            if (!string.IsNullOrEmpty(item.UnavailableReason)) return Speak(item.Name + " is unavailable. " + item.UnavailableReason, announce);
            if (!item.Action.interactable) return Speak(item.Name + " is unavailable" + (string.IsNullOrEmpty(item.Cost) ? "" : ". Cost " + item.Cost), announce);
            var button = item.Action as Button;
            if (button != null)
            {
                var affected = IsUpgradeItem(item) ? Businesses().FirstOrDefault(b => !string.IsNullOrEmpty(item.Description) && item.Description.IndexOf(b.Name, StringComparison.OrdinalIgnoreCase) >= 0) : null;
                if (affected != null)
                {
                    _pendingBusinessBefore = affected;
                    _pendingBusinessAction = "upgrade";
                    _pendingDescription = PanelDetails(item);
                    _pendingBusinessFrames = 2;
                }
                button.onClick.Invoke();
            }
            var toggle = item.Action as Toggle;
            if (toggle != null)
            {
                toggle.isOn = true;
                // Manager panels rebuild their virtualized rows after the Cash/Angels
                // toggle changes. Wait for that rebuild before choosing and announcing
                // the first manager; otherwise the accessibility list can still reflect
                // the panel that was just hidden and report the Angels section as empty.
                if (ComponentAncestor(toggle.transform, "ManagerModalController") != null)
                {
                    _pendingManagerFocusAttempts = 0;
                    _pendingManagerFocusFrames = 2;
                }
            }
            _panelItemIndex = 0;
            _panelRow = 0;
            return Speak("Activated " + item.Name, announce);
        }

        public static string CurrentPanelLabel() { return CurrentPanelLabel(PanelItems()); }

        private static string CurrentPanelLabel(List<PanelItem> items)
        {
            if (items.Count == 0) return "No items in this tab";
            if (_panelItemIndex >= items.Count) _panelItemIndex = 0;
            var item = items[_panelItemIndex];
            if (!PanelRowExists(item, _panelRow)) _panelRow = 0;
            if (_panelRow == 0) return IsActionOnly(item) ? item.Name + "; " + (string.IsNullOrEmpty(item.ActionName) ? "activate" : item.ActionName) + " button" + (item.Action.interactable && string.IsNullOrEmpty(item.UnavailableReason) ? "" : ". Unavailable") : item.Name;
            if (_panelRow == 1) return item.Name + "; " + item.Description;
            if (_panelRow == 2) return item.Name + "; cost " + AccessibleCost(item.Cost);
            return item.Name + "; " + (string.IsNullOrEmpty(item.ActionName) ? "activate button" : item.ActionName + " button") + (item.Action != null && (!item.Action.interactable || !string.IsNullOrEmpty(item.UnavailableReason)) ? ". Unavailable" : "");
        }

        private static string PanelDetails(PanelItem item)
        {
            var parts = new List<string> { item.Name };
            if (!string.IsNullOrEmpty(item.Description)) parts.Add(item.Description);
            if (!string.IsNullOrEmpty(item.Cost)) parts.Add("Cost " + AccessibleCost(item.Cost));
            return string.Join(". ", parts.ToArray());
        }

        private static bool PanelRowExists(PanelItem item, int row)
        {
            if (IsActionOnly(item)) return row == 0;
            return row == 0 || (row == 1 && !string.IsNullOrEmpty(item.Description)) || (row == 2 && !string.IsNullOrEmpty(item.Cost)) || (row == 3 && item.Action != null);
        }

        private static bool IsActionOnly(PanelItem item)
        {
            return item != null && item.Action != null && string.IsNullOrEmpty(item.Description) && string.IsNullOrEmpty(item.Cost);
        }

        private static string CurrentBusinessLabel(List<BusinessState> businesses)
        {
            if (businesses.Count == 0) return "No businesses found";
            if (_businessIndex >= businesses.Count) _businessIndex = 0;
            var b = businesses[_businessIndex];
            if (!b.IsOwned) return b.Name + "; purchase button. Cost " + MoneyExact(b.PurchaseCostValue, b.PurchaseCost) + (b.Purchase != null && b.Purchase.interactable ? "" : ". Unavailable");
            if (_businessRow == 0) return b.Name + "; owned " + CountExact(b.OwnedValue, b.Owned);
            if (_businessRow == 1) return b.Name + "; earns " + ProductionMoney(b) + " per production";
            if (_businessRow == 2) return b.Name + "; " + (b.Production != null && b.Production.interactable ? "start production button" : "production in progress. " + Remaining(b.Timer) + " remaining");
            var quantity = CountExact(b.BuyQuantityValue, "1");
            return b.Name + "; buy upgrade button. Adds " + quantity + (quantity == "1" ? " business" : " businesses") + ". Cost " + BuyMoney(b) + (b.Buy != null && b.Buy.interactable ? "" : ". Unavailable");
        }

        private static string BusinessDetails(BusinessState b)
        {
            return b.Name + ". Owned " + CountExact(b.OwnedValue, b.Owned) + ". Earns " + ProductionMoney(b) + " per production. Rate " + MoneyExact(b.RateValue, b.Rate) + " per second. " + (b.Production != null && b.Production.interactable ? "Ready" : "Production in progress, " + Remaining(b.Timer) + " remaining") + ". Next purchase " + BuyMoney(b);
        }

        private static void AnnounceBusinessResult()
        {
            if (_pendingBusinessBefore == null) return;
            var businesses = Businesses();
            var after = businesses.FirstOrDefault(b => b.Name == _pendingBusinessBefore.Name);
            if (after == null) return;
            string message;
            if (_pendingBusinessAction == "purchase" && !_pendingBusinessBefore.IsOwned && after.IsOwned)
                message = after.Name + " purchased. Owned " + CountExact(after.OwnedValue, after.Owned) + ". Start production when ready";
            else if (_pendingBusinessAction == "buy" && _pendingBusinessBefore.Owned != after.Owned)
            {
                var changes = new List<string> { "Upgrade purchased", "owned " + CountExact(after.OwnedValue, after.Owned) };
                if (Different(_pendingBusinessBefore.EarningsValue, after.EarningsValue)) changes.Add("earnings increased from " + ProductionMoney(_pendingBusinessBefore) + " to " + ProductionMoney(after));
                if (!double.IsNaN(_pendingBusinessBefore.ProductionSeconds) && !double.IsNaN(after.ProductionSeconds) && after.ProductionSeconds < _pendingBusinessBefore.ProductionSeconds - 0.0001)
                {
                    var reductionSeconds = _pendingBusinessBefore.ProductionSeconds - after.ProductionSeconds;
                    var reduction = FormatSeconds(reductionSeconds);
                    changes.Add(after.Name + " automatic unlock reached at " + CountExact(after.OwnedValue, after.Owned) + " owned");
                    changes.Add("production time decreased by " + reduction + ", from " + FormatSeconds(_pendingBusinessBefore.ProductionSeconds) + " to " + FormatSeconds(after.ProductionSeconds));
                }
                message = string.Join(". ", changes.ToArray());
            }
            else if (_pendingBusinessAction == "upgrade")
            {
                var changes = new List<string> { "Upgrade purchased" };
                if (Different(_pendingBusinessBefore.EarningsValue, after.EarningsValue)) changes.Add("earnings increased from " + ProductionMoney(_pendingBusinessBefore) + " to " + ProductionMoney(after));
                if (_pendingBusinessBefore.Timer != after.Timer) changes.Add("production time changed from " + Remaining(_pendingBusinessBefore.Timer) + " to " + Remaining(after.Timer));
                if (changes.Count == 1 && !string.IsNullOrEmpty(_pendingDescription)) changes.Add(_pendingDescription);
                message = string.Join(". ", changes.ToArray());
            }
            else message = "Production started. Time until completed: " + Remaining(after.Timer);
            _pendingBusinessBefore = null;
            _pendingBusinessAction = null;
            _pendingDescription = null;
            Speech.Write(message);
        }

        public static string Status()
        {
            Refresh(false);
            var parts = new List<string>();
            AddValue(parts, "Cash", MoneyValue(FindActiveText("Txt_CashOnHand")));
            AddValue(parts, "Gold", FindActiveText("Txt_Gold"));
            AddValue(parts, "Mega Bucks", FindActiveText("Txt_MegaBucks"));
            var modal = Controls.FirstOrDefault(s => CanvasOrder(s) >= 20);
            if (modal != null)
            {
                var canvas = modal.GetComponentInParent<Canvas>();
                var title = canvas == null ? "" : TextNamed(canvas.transform, "Txt_Title");
                if (!string.IsNullOrEmpty(title)) parts.Insert(0, title + " screen");
            }
            return parts.Count == 0 ? "No status available" : string.Join(", ", parts.ToArray());
        }

        public static string ListVisibleText()
        {
            var b = new StringBuilder();
            var texts = Resources.FindObjectsOfTypeAll<Text>().Where(t => t != null && IsVisible(t))
                .Select(t => new { Value = Clean(t.text), Text = t }).Where(x => !string.IsNullOrEmpty(x.Value))
                .OrderByDescending(x => CanvasOrder(x.Text)).ThenByDescending(x => x.Text.transform.position.y)
                .ThenBy(x => x.Text.transform.position.x).ToList();
            for (var i = 0; i < texts.Count; i++)
                b.Append(i + 1).Append(": ").Append(texts[i].Value).Append(" [canvas ")
                    .Append(CanvasOrder(texts[i].Text)).Append("; ").Append(Path(texts[i].Text.transform)).AppendLine("]");
            return b.ToString();
        }

        private static void EnsureCurrent()
        {
            if (_index < 0 || _index >= Controls.Count || !IsPresent(Controls[_index])) Refresh(false);
        }

        private static bool IsPresent(Selectable s) { return s != null && s.IsActive() && IsVisible(s); }

        private static bool IsVisible(Component component)
        {
            if (component == null || !component.gameObject.activeInHierarchy) return false;
            return component.GetComponentsInParent<CanvasGroup>(true).All(group => group.alpha > 0.01f);
        }

        private static int CanvasOrder(Component c)
        {
            var canvas = c.GetComponentInParent<Canvas>();
            return canvas == null ? 0 : canvas.sortingOrder;
        }

        private static string Label(Selectable s)
        {
            var toggle = s as Toggle;
            if (toggle != null)
            {
                var text = ChildText(toggle.transform);
                if (string.IsNullOrEmpty(text)) text = ContextName(toggle.transform);
                return text + (toggle.isOn ? " subtab, selected" : " subtab") + (toggle.interactable ? "" : ", unavailable");
            }
            var venture = ComponentAncestor(s.transform, "VentureView");
            if (venture != null) return VentureLabel(s, venture.transform);
            var purchase = PurchaseLabel(s);
            if (!string.IsNullOrEmpty(purchase)) return purchase + (s.interactable ? "" : ", unavailable");
            var label = ChildText(s.transform);
            if (string.IsNullOrEmpty(label)) label = Clean(s.gameObject.name);
            if (!label.Any(char.IsLetter) || label.StartsWith("Img ", StringComparison.OrdinalIgnoreCase) || s.gameObject.name == "ProgressBar")
            {
                var context = ContextName(s.transform);
                if (!string.IsNullOrEmpty(context) && label.IndexOf(context, StringComparison.OrdinalIgnoreCase) < 0)
                    label = context + (string.IsNullOrEmpty(label) ? "" : ", " + label);
            }
            return label + (s.interactable ? "" : ", unavailable");
        }

        private static string VentureLabel(Selectable s, Transform venture)
        {
            var name = VentureName(venture);
            var path = Path(s.transform);
            var quantity = TextNamed(venture, "Txt_Qty");
            var profit = TextNamed(venture, "Profit Label");
            var rate = TextNamed(venture, "Txt_CashPerSecond");
            var earningsDenomination = TextNamed(venture, "Txt_CashString");
            var timer = TextNamed(venture, "Txt_Timer");
            var cost = TextNamed(venture, "Txt_Cost");
            var costDenomination = TextNamed(venture, "Txt_Denomination");
            var lockedName = TextNamed(venture, "Txt_VentureName");
            var costPer = TextNamed(venture, "Txt_CostPer");
            var ownedValue = VentureModelValue(venture, "TotalOwned");
            var profitValue = ApplyDenomination(VentureModelValue(venture, "ProfitOnNext"), earningsDenomination);
            var rateValue = ApplyDenomination(VentureModelValue(venture, "CashPerSec"), earningsDenomination);
            var buyCostValue = VentureModelValue(venture, "CostForNext");
            var purchaseCostValue = VentureModelValue(venture, "CostPer");
            var buyQuantityValue = VentureModelValue(venture, "CanAfford");
            string label;
            if (path.IndexOf("Buy Button", StringComparison.OrdinalIgnoreCase) >= 0)
                label = name + ", buy x" + CountExact(buyQuantityValue, "1") + ", cost " + MoneyWithDenomination(cost, costDenomination, buyCostValue) + ", owned " + CountExact(ownedValue, quantity);
            else if (!string.IsNullOrEmpty(lockedName) && string.IsNullOrEmpty(quantity))
                label = lockedName + ", purchase business, cost " + MoneyExact(purchaseCostValue, costPer);
            else
                label = name + ", " + (s.interactable ? "start production" : "production running")
                    + ", owned " + CountExact(ownedValue, quantity) + ", earns " + MoneyExact(profitValue, profit) + ", rate " + MoneyExact(rateValue, rate) + " per second" + Part("timer", timer);
            var cash = FindActiveText("Txt_CashOnHand");
            if (!string.IsNullOrEmpty(cash)) label += ", cash " + cash;
            return label + (s.interactable ? "" : ", unavailable");
        }

        private static string PurchaseLabel(Selectable s)
        {
            if (s.gameObject.name.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) < 0 && s.gameObject.name.IndexOf("Purchase", StringComparison.OrdinalIgnoreCase) < 0) return "";
            var t = s.transform;
            for (var i = 0; i < 6 && t != null; i++, t = t.parent)
            {
                var name = TextNamed(t, "Txt_Name");
                var description = TextNamed(t, "Txt_Description");
                var price = TextNamed(t, "Txt_Price");
                if (!string.IsNullOrEmpty(name) && (!string.IsNullOrEmpty(description) || !string.IsNullOrEmpty(price)))
                    return name + ". " + description + ". " + price;
            }
            return "";
        }

        public static string ToggleTextMode()
        {
            if (_textMode) { _textMode = false; Speech.Write("Control navigation"); return "Control navigation"; }
            ScreenText.Clear();
            var texts = Resources.FindObjectsOfTypeAll<Text>().Where(t => t != null && IsVisible(t)).ToList();
            var modalTexts = texts.Where(t => CanvasOrder(t) >= 20).ToList();
            if (modalTexts.Count > 0)
            {
                var top = modalTexts.Max(CanvasOrder);
                texts = modalTexts.Where(t => CanvasOrder(t) == top).ToList();
            }
            var unlockRows = new HashSet<Transform>();
            foreach (var text in texts)
            {
                var row = Ancestor(text.transform, "UnlockItemView");
                if (row == null || !unlockRows.Add(row)) continue;
                var unlockItem = UnlockViewItem(row);
                ScreenText.Add(unlockItem.Name + ". " + unlockItem.Description);
            }
            ScreenText.AddRange(texts.Where(t => Ancestor(t.transform, "UnlockItemView") == null)
                .OrderByDescending(t => t.transform.position.y).ThenBy(t => t.transform.position.x)
                .Select(t => Clean(t.text)).Where(t => !string.IsNullOrEmpty(t) && t != "Text" && t != "Badge Name" && !t.StartsWith("Lorem ipsum", StringComparison.OrdinalIgnoreCase)).Distinct());
            _textIndex = 0;
            _textMode = true;
            var message = ScreenText.Count == 0 ? "No screen text. Press F8 to return" : "Screen text mode, " + ScreenText.Count + " items. 1 of " + ScreenText.Count + ", " + ScreenText[0] + ". Use Up and Down. Press Enter, Escape, or F8 to return";
            Speech.Write(message);
            return message;
        }

        public static string ReadText(int direction)
        {
            if (ScreenText.Count == 0) { Speech.Write("No screen text"); return "No screen text"; }
            _textIndex = (_textIndex + direction + ScreenText.Count) % ScreenText.Count;
            var message = (_textIndex + 1) + " of " + ScreenText.Count + ", " + ScreenText[_textIndex];
            Speech.Write(message);
            return message;
        }

        private static List<BusinessState> Businesses()
        {
            var result = new List<BusinessState>();
            foreach (var ventureView in BusinessViews())
            {
                var root = ventureView.transform;
                var buttons = root.GetComponentsInChildren<Button>(true).Where(IsPresent).ToList();
                var ventureModel = ObjectMember(ventureView, "venture");
                var gild = ObjectMember(ventureView, "BuyBoostBannerButton") as Button;
                if (gild == null || !IsPresent(gild)) gild = ObjectMember(ventureView, "BuyBoostCertificateButton") as Button;
                var purchase = ObjectMember(ventureView, "UnpurchasedStateButton") as Button;
                if (purchase == null || !IsPresent(purchase)) purchase = buttons.FirstOrDefault(b => Path(b.transform).IndexOf("UnlockedState", StringComparison.OrdinalIgnoreCase) >= 0);
                var earningsDenomination = TextNamed(root, "Txt_CashString");
                result.Add(new BusinessState
                {
                    Root = root,
                    Name = VentureName(root),
                    Owned = TextNamed(root, "Txt_Qty"),
                    Earnings = TextNamed(root, "Profit Label"),
                    EarningsDenomination = earningsDenomination,
                    Rate = TextNamed(root, "Txt_CashPerSecond"),
                    Timer = TextNamed(root, "Txt_Timer"),
                    BuyCost = TextNamed(root, "Txt_Cost"),
                    BuyCostDenomination = TextNamed(root, "Txt_Denomination"),
                    PurchaseCost = ViewText(ventureView, "UnpurchasedCostPer", TextNamed(root, "Txt_CostPer")),
                    ProductionSeconds = ModelProductionSeconds(root),
                    OwnedValue = VentureModelValue(root, "TotalOwned"),
                    EarningsValue = ApplyDenomination(VentureModelValue(root, "ProfitOnNext"), earningsDenomination),
                    RateValue = ApplyDenomination(VentureModelValue(root, "CashPerSec"), earningsDenomination),
                    BuyCostValue = VentureModelValue(root, "CostForNext"),
                    PurchaseCostValue = VentureModelValue(root, "CostForNext"),
                    BuyQuantityValue = VentureModelValue(root, "CanAfford"),
                    Production = PresentViewButton(ventureView, "RunBarButton") ?? PresentViewButton(ventureView, "RunIconButton"),
                    Buy = PresentViewButton(ventureView, "BuyButton"),
                    Purchase = purchase,
                    Gild = gild,
                    IsBoosted = string.Equals(ReactiveValueText(ObjectMember(ventureModel, "IsBoosted")), "True", StringComparison.OrdinalIgnoreCase)
                });
            }
            return result;
        }

        private static Button PresentViewButton(Component view, string member)
        {
            var button = ObjectMember(view, member) as Button;
            return button != null && IsPresent(button) ? button : null;
        }

        private static List<Component> BusinessViews()
        {
            // The game reparents the same views when orientation changes. Landscape's
            // VentureParent containers are empty in portrait, so use the model/view map
            // and the planet's model order, never container names or screen coordinates.
            var result = new List<Component>();
            var seen = new HashSet<object>();
            foreach (var panel in Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(c => c != null && c.gameObject.activeInHierarchy && c.GetType().Name == "VenturesPanel"))
            {
                var models = ObjectMember(ObjectMember(panel, "state"), "VentureModels") as System.Collections.IEnumerable;
                var views = ObjectMember(panel, "VentureModelViewMap") as System.Collections.IDictionary;
                if (models == null || views == null) continue;
                foreach (var model in models)
                {
                    if (model == null || !views.Contains(model)) continue;
                    var view = views[model] as Component;
                    if (view == null || !view.gameObject.activeInHierarchy ||
                        !ReferenceEquals(ObjectMember(view, "venture"), model) || !seen.Add(model)) continue;
                    result.Add(view);
                }
            }
            return result;
        }

        private static List<PanelItem> PanelItems()
        {
            var result = new List<PanelItem>();
            var controls = Resources.FindObjectsOfTypeAll<Selectable>()
                .Where(s => (s is Button || s is Toggle) && IsPresent(s) && CanvasOrder(s) >= 20).ToList();
            if (controls.Count == 0) return result;
            var top = controls.Max(CanvasOrder);
            controls = controls.Where(s => CanvasOrder(s) == top).ToList();

            foreach (var button in controls.OfType<Button>())
            {
                if (ComponentAncestor(button.transform, "PlanetMissionPanel") != null) continue;
                if (button.gameObject.name.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) < 0 && button.gameObject.name.IndexOf("Purchase", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var t = button.transform;
                string name = "", description = "", price = "";
                for (var i = 0; i < 6 && t != null; i++, t = t.parent)
                {
                    name = TextNamed(t, "Txt_Name");
                    description = TextNamed(t, "Txt_Description");
                    price = TextNamed(t, "Txt_Price");
                    if (!string.IsNullOrEmpty(name)) break;
                }
                if (string.IsNullOrEmpty(name)) continue;
                var upgradeView = ComponentAncestor(button.transform, "UpgradeView");
                var upgradeModel = ObjectMember(upgradeView, "Upgrade") ?? ObjectMember(upgradeView, "upgrade");
                var storeView = ComponentAncestorWithMember(button.transform, "Item", "AdCapStoreItem");
                var storeItem = ObjectMember(storeView, "Item");
                if (storeItem != null)
                {
                    name = ObjectTextMember(storeItem, "DisplayName");
                    description = ObjectTextMember(storeItem, "Description");
                    var cost = ObjectMember(storeItem, "Cost");
                    var amount = ObjectTextMember(cost, "Price");
                    var discount = ObjectTextMember(cost, "Discount");
                    var currency = ObjectTextMember(cost, "Currency");
                    double discountValue;
                    if (double.TryParse(discount, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out discountValue) && discountValue > 0) amount = discount;
                    if (string.Equals(currency, "MegaBuck", StringComparison.OrdinalIgnoreCase)) price = amount + " Mega Bucks";
                    else if (string.Equals(currency, "Gold", StringComparison.OrdinalIgnoreCase)) price = amount + " Gold";
                    else if (string.Equals(currency, "MegaTicket", StringComparison.OrdinalIgnoreCase)) price = amount + " Mega Tickets";
                    else if (string.Equals(currency, "Cash", StringComparison.OrdinalIgnoreCase)) price = ObjectTextMember(cost, "LocalizedPriceString");
                }
                result.Add(new PanelItem
                {
                    Name = name,
                    Description = description,
                    Cost = price,
                    Action = button,
                    ActionName = "buy",
                    Model = upgradeModel,
                    StableId = ObjectTextMember(upgradeModel, "id"),
                    NumericCost = ObjectDoubleMember(upgradeModel, "cost")
                });
            }

            var modalRoot = controls[0].GetComponentInParent<Canvas>();
            var modalTitle = modalRoot == null ? "" : modalRoot.GetComponentsInChildren<Text>(true)
                .Where(IsVisible).Where(t => t.gameObject.name == "Txt_Title").Select(t => Clean(t.text)).FirstOrDefault();
            var galleryItems = GalleryModelItems(modalRoot);
            if (galleryItems.Count > 0) result.AddRange(galleryItems);
            if (modalRoot != null)
            {
                var stats = modalRoot.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null && string.Equals(c.GetType().Name, "StatsFieldUI", StringComparison.Ordinal));
                foreach (var stat in stats)
                {
                    var titleText = ObjectMember(stat, "titleText") as Text;
                    var infoText = ObjectMember(stat, "infoText") as Text;
                    if (titleText == null || infoText == null || !IsVisible(titleText) || !IsVisible(infoText)) continue;
                    var title = Clean(titleText.text);
                    var value = Clean(infoText.text);
                    if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(value)) continue;
                    if (title.IndexOf("Cash", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("Earnings", StringComparison.OrdinalIgnoreCase) >= 0)
                        value = MoneyValue(value);
                    result.Add(new PanelItem { Name = title, Description = value });
                }
                var articles = modalRoot.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null && string.Equals(c.GetType().Name, "NewsArticleUI", StringComparison.Ordinal));
                foreach (var article in articles)
                {
                    var titleText = ObjectMember(article, "articleTitle") as Text;
                    var bodyText = ObjectMember(article, "articleBody") as Text;
                    var title = titleText == null ? "" : Clean(titleText.text);
                    var body = bodyText == null ? "" : Clean(bodyText.text);
                    if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(body))
                        result.Add(new PanelItem { Name = string.IsNullOrEmpty(title) ? "News article" : title, Description = body });
                }
            }
            if (string.Equals(modalTitle, "Investors", StringComparison.OrdinalIgnoreCase))
            {
                var pending = TextNamed(modalRoot.transform, "Txt_PendingAngels");
                var owned = TextNamed(modalRoot.transform, "Txt_AngelsOnHand");
                var bonus = TextNamed(modalRoot.transform, "Txt_BonusAmount");
                result.Add(new PanelItem
                {
                    Name = "Angel investors",
                    Description = pending + " available; " + owned + " owned; each Angel adds " + bonus + " profit"
                });
                var claim = controls.OfType<Button>().FirstOrDefault(b => ChildText(b.transform).IndexOf("Claim", StringComparison.OrdinalIgnoreCase) >= 0);
                if (claim != null)
                {
                    result.Add(new PanelItem
                    {
                        Name = "Claim " + pending + " Angels",
                        Description = "Restart all businesses to add these Angels",
                        Action = claim,
                        ActionName = "claim and restart"
                    });
                }
            }
            if (modalRoot != null && result.Count <= controls.OfType<Toggle>().Count())
            {
                var rows = new HashSet<Transform>();
                foreach (var text in modalRoot.GetComponentsInChildren<Text>(true).Where(IsVisible))
                {
                    var row = Ancestor(text.transform, "UnlockItemView");
                    if (row == null || !rows.Add(row)) continue;
                    result.Add(UnlockViewItem(row));
                }
            }

            foreach (var button in controls.OfType<Button>()
                .OrderBy(PanelButtonCategory)
                .ThenByDescending(b => b.transform.position.y).ThenBy(b => b.transform.position.x))
            {
                if (IsPlayFabControl(button)) continue;
                if (galleryItems.Count > 0 && ComponentAncestor(button.transform, "UnlockDetailedItemView") != null) continue;
                if (result.Any(i => i.Action == button)) continue;
                var special = SpecialPanelItem(button);
                if (special != null)
                {
                    result.Add(special);
                    continue;
                }
                var name = ChildText(button.transform);
                if (string.IsNullOrEmpty(name)) name = ContextName(button.transform);
                if (string.IsNullOrEmpty(name)) continue;
                result.Add(new PanelItem { Name = name, Action = button, ActionName = name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0 ? "close" : "activate" });
            }
            var subtabItems = controls.OfType<Toggle>().OrderBy(t => t.transform.position.x).Select(t => new PanelItem
            {
                Name = SubtabName(t) + " section" + (t.isOn ? ", selected" : ""),
                Action = t,
                ActionName = "select"
            }).ToList();
            if (subtabItems.Count > 1)
            {
                var insertAt = result.FindIndex(i => i.Action != null &&
                    (i.ActionName == "open" || i.ActionName == "close" || i.Name.IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0));
                if (insertAt < 0) insertAt = result.Count;
                result.InsertRange(insertAt, subtabItems);
            }
            if (string.Equals(modalTitle, "Managers", StringComparison.OrdinalIgnoreCase))
            {
                var purchases = result.Where(i => i.Action != null && i.ActionName == "buy")
                    .OrderBy(ManagerSortKey).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var tabs = result.Where(i => i.Action is Toggle && i.ActionName == "select").ToList();
                var other = result.Where(i => (i.Action == null || i.ActionName != "buy") &&
                    !(i.Action is Toggle && i.ActionName == "select")).ToList();
                result.Clear();
                result.AddRange(tabs);
                result.AddRange(purchases);
                result.AddRange(other);
            }
            return result;
        }

        private static List<PanelItem> GalleryModelItems(Canvas modalRoot)
        {
            var result = new List<PanelItem>();
            if (modalRoot == null) return result;
            var galleryPanel = modalRoot.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null &&
                string.Equals(c.GetType().Name, "UnlockGalleryPanel", StringComparison.Ordinal) &&
                c.GetComponentsInChildren<Text>(true).Any(IsVisible));
            if (galleryPanel == null) return result;
            var gameState = PrivateField(galleryPanel, "gameState");
            var unlocks = ObjectMember(gameState, "Unlocks") as System.Collections.IEnumerable;
            if (unlocks == null) return result;
            var ordered = new List<object>();
            foreach (var unlock in unlocks) if (unlock != null) ordered.Add(unlock);
            foreach (var unlock in ordered.OrderBy(u => MemberDouble(u, "order")))
            {
                var name = ObjectTextMember(unlock, "name");
                var description = InvokeTextMethod(unlock, "GetDescription");
                if (string.IsNullOrEmpty(name)) continue;
                result.Add(new PanelItem { Name = name, Description = JoinDetails(description, UnlockStateDescription(unlock)) });
            }
            return result;
        }

        private static string UnlockStateDescription(object unlock)
        {
            var earned = string.Equals(ReactiveValueText(ObjectMember(unlock, "Earned")), "True", StringComparison.OrdinalIgnoreCase);
            var claimed = string.Equals(ReactiveValueText(ObjectMember(unlock, "Claimed")), "True", StringComparison.OrdinalIgnoreCase);
            if (claimed) return "Automatically earned, claimed, and active";
            if (earned) return "Earned but not yet claimed. Viewing Gallery details does not claim it";
            return "Locked. It is earned automatically when its requirement is reached";
        }

        private static double MemberDouble(object instance, string memberName)
        {
            var value = ObjectMember(instance, memberName);
            try { return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return double.MaxValue; }
        }

        private static int PanelButtonCategory(Button button)
        {
            if (button == null) return 3;
            var name = ChildText(button.transform) + " " + button.gameObject.name;
            if (name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (ComponentAncestor(button.transform, "InfoButtonUI") != null ||
                name.IndexOf("Info Button", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(Clean(name), "Info", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 0;
        }

        private static List<Toggle> ActiveSubtabs()
        {
            var toggles = Resources.FindObjectsOfTypeAll<Toggle>()
                .Where(t => t != null && IsPresent(t) && CanvasOrder(t) >= 20).ToList();
            if (toggles.Count == 0) return toggles;
            var top = toggles.Max(CanvasOrder);
            return toggles.Where(t => CanvasOrder(t) == top)
                .OrderByDescending(t => t.transform.position.y).ThenBy(t => t.transform.position.x)
                .GroupBy(SubtabName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(t => t.isOn).ThenByDescending(t => t.interactable).First()).ToList();
        }

        private static string SubtabName(Toggle toggle)
        {
            if (toggle == null) return "";
            var name = ChildText(toggle.transform);
            return string.IsNullOrEmpty(name) ? ContextName(toggle.transform) : name;
        }

        private static bool HasModal()
        {
            return Resources.FindObjectsOfTypeAll<Selectable>().Any(s => s != null && IsPresent(s) && CanvasOrder(s) >= 20);
        }

        private static int CurrentTopTabIndex()
        {
            if (!HasModal()) return 0;
            var title = Resources.FindObjectsOfTypeAll<Text>().Where(t => t != null && IsVisible(t) && CanvasOrder(t) >= 20 && t.gameObject.name == "Txt_Title").OrderByDescending(CanvasOrder).Select(t => Clean(t.text)).FirstOrDefault();
            if (string.IsNullOrEmpty(title)) return _topTabIndex;
            for (var i = 0; i < TopTabs.Length; i++) if (title.IndexOf(TopTabs[i], StringComparison.OrdinalIgnoreCase) >= 0) return i;
            return _topTabIndex;
        }

        private static Button FindTopTabButton(string name)
        {
            var expectedObjectName = "btn_" + name;
            return Resources.FindObjectsOfTypeAll<Button>()
                .Where(b => b != null && b.gameObject.activeInHierarchy && Path(b.transform).IndexOf("MenuPanelController", StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(b => string.Equals(b.gameObject.name, expectedObjectName, StringComparison.OrdinalIgnoreCase) || ChildText(b.transform).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || b.gameObject.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(b => string.Equals(b.gameObject.name, expectedObjectName, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(b => b.interactable)
                .FirstOrDefault();
        }

        private static void CloseTopModal()
        {
            var close = Resources.FindObjectsOfTypeAll<Button>().Where(b => b != null && IsPresent(b) && CanvasOrder(b) >= 20 && b.gameObject.name.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0).OrderByDescending(CanvasOrder).FirstOrDefault();
            if (close != null && close.interactable) close.onClick.Invoke();
        }

        private static string Remaining(string timer)
        {
            return string.IsNullOrEmpty(timer) ? "unknown" : timer;
        }

        private static string FormatSeconds(double seconds)
        {
            return seconds.ToString(seconds == Math.Floor(seconds) ? "0" : "0.###", System.Globalization.CultureInfo.InvariantCulture) + (seconds == 1 ? " second" : " seconds");
        }

        private static string MoneyValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            if (string.IsNullOrEmpty(value)) return "unknown";
            value = Clean(value);
            if (!value.StartsWith("$")) value = "$" + value;
            return AccessibleCost(value);
        }

        private static string AccessibleCost(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var cleaned = Clean(value).Replace(",", "");
            var match = Regex.Match(cleaned, "^\\$\\s*([0-9]+(?:\\.[0-9]+)?)\\s*([A-Za-z]+)?$", RegexOptions.IgnoreCase);
            double number;
            if (!match.Success || !double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number)) return Clean(value);
            var suffix = match.Groups[2].Value.ToLowerInvariant();
            var exponent = DenominationExponent(suffix);
            if (exponent < 0) return Clean(value);
            return "$" + AccessibleNumber(number * Math.Pow(10d, exponent));
        }

        private static string MoneyExact(double value, string fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return MoneyValue(fallback);
            return "$" + AccessibleNumber(value);
        }

        private static string ProductionMoney(BusinessState business)
        {
            if (business == null) return "unknown";
            if (!string.IsNullOrEmpty(business.Earnings) && !string.IsNullOrEmpty(business.EarningsDenomination))
                return MoneyValue(business.Earnings + " " + business.EarningsDenomination);
            return MoneyExact(business.EarningsValue, business.Earnings);
        }

        private static string BuyMoney(BusinessState business)
        {
            if (business == null) return "unknown";
            return MoneyWithDenomination(business.BuyCost, business.BuyCostDenomination, business.BuyCostValue);
        }

        private static string MoneyWithDenomination(string amount, string denomination, double fallbackValue)
        {
            if (!string.IsNullOrEmpty(amount) && !string.IsNullOrEmpty(denomination))
                return MoneyValue(amount + " " + denomination);
            return MoneyExact(fallbackValue, amount);
        }

        private static string CountExact(double value, string fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return string.IsNullOrEmpty(fallback) ? "unknown" : fallback;
            return AccessibleNumber(value);
        }

        private static string AccessibleNumber(double value)
        {
            var absolute = Math.Abs(value);
            if (absolute < 1000000000000000d)
                return value.ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture);
            var exponent = (int)Math.Floor(Math.Log10(absolute) / 3d) * 3;
            var names = new Dictionary<int, string>
            {
                { 15, "quadrillion" }, { 18, "quintillion" }, { 21, "sextillion" }, { 24, "septillion" },
                { 27, "octillion" }, { 30, "nonillion" }, { 33, "decillion" }, { 36, "undecillion" },
                { 39, "duodecillion" }, { 42, "tredecillion" }, { 45, "quattuordecillion" },
                { 48, "quindecillion" }, { 51, "sexdecillion" }, { 54, "septendecillion" },
                { 57, "octodecillion" }, { 60, "novemdecillion" }, { 63, "vigintillion" }
            };
            string name;
            if (names.TryGetValue(exponent, out name))
                return (value / Math.Pow(10d, exponent)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " " + name;
            return value.ToString("0.###E+0", System.Globalization.CultureInfo.InvariantCulture).Replace("E+", " times ten to the ");
        }

        private static bool Different(double before, double after)
        {
            return !double.IsNaN(before) && !double.IsNaN(after) && Math.Abs(before - after) > Math.Max(0.0001, Math.Abs(before) * 0.000000001);
        }

        private static string Speak(string message, bool announce)
        {
            if (announce) Speech.Write(message);
            return message;
        }

        private static string ReadTimeWarpExpressCount(bool announce)
        {
            var button = TimeWarpExpressButton();
            if (button == null) return Speak("Time Warp Express is unavailable on the current screen", announce);
            var count = TimeWarpExpressCount(button);
            if (count < 0) return Speak("The Time Warp Express count is unavailable", announce);
            return Speak("You have " + count + (count == 1 ? " Time Warp Express" : " Time Warp Expresses"), announce);
        }

        private static string UseTimeWarpExpress(bool announce)
        {
            var button = TimeWarpExpressButton();
            if (button == null || !button.interactable)
                return Speak("Time Warp Express is unavailable on the current screen", announce);
            var count = TimeWarpExpressCount(button);
            if (count <= 0)
                return Speak(count == 0 ? "You have no Time Warp Expresses" : "The Time Warp Express count is unavailable, so none was used", announce);
            button.onClick.Invoke();
            var remaining = count - 1;
            return Speak("Used one Time Warp Express on the current planet. " + remaining + (remaining == 1 ? " remains" : " remain"), announce);
        }

        private static Button TimeWarpExpressButton()
        {
            return Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(button => button != null && button.gameObject != null &&
                button.gameObject.activeInHierarchy && string.Equals(button.gameObject.name, "Btn_TimeWarpExpress", StringComparison.Ordinal));
        }

        private static int TimeWarpExpressCount(Button button)
        {
            var match = Regex.Match(Label(button), @"\d+");
            int count;
            return match.Success && int.TryParse(match.Value, out count) ? count : -1;
        }

        private static void MaintainMusicVolume()
        {
            var source = BackgroundMusicSource();
            if (source == null) return;
            if (_musicVolume < 0f)
                _musicVolume = PlayerPrefs.HasKey("AdCapAccessMusicVolume") ? PlayerPrefs.GetFloat("AdCapAccessMusicVolume") : source.volume;
            _musicVolume = Mathf.Clamp01(_musicVolume);
            if (Math.Abs(source.volume - _musicVolume) > 0.001f) source.volume = _musicVolume;
        }

        private static string AdjustMusicVolume(float change, bool announce)
        {
            var source = BackgroundMusicSource();
            if (source == null) return Speak("Music control is unavailable", announce);
            MaintainMusicVolume();
            _musicVolume = Mathf.Clamp01(_musicVolume + change);
            source.volume = _musicVolume;
            PlayerPrefs.SetFloat("AdCapAccessMusicVolume", _musicVolume);
            PlayerPrefs.Save();
            return Speak("Music volume " + Mathf.RoundToInt(_musicVolume * 100f) + " percent", announce);
        }

        private static AudioSource BackgroundMusicSource()
        {
            var controller = Resources.FindObjectsOfTypeAll<Component>().FirstOrDefault(c => c != null && c.gameObject.activeInHierarchy &&
                string.Equals(c.GetType().Name, "AudioController", StringComparison.Ordinal));
            return ObjectMember(controller, "backgroundMusic") as AudioSource;
        }

        private static string VentureName(Transform venture)
        {
            var modelName = ObjectTextMember(VentureModel(venture), "Name");
            if (!string.IsNullOrEmpty(modelName)) return modelName;
            var explicitName = TextNamed(venture, "Txt_VentureName");
            if (!string.IsNullOrEmpty(explicitName)) return explicitName;
            var match = Regex.Match(venture.name, "(\\d+)$");
            var names = new[] { "Lemonade Stand", "Newspaper Delivery", "Car Wash", "Pizza Delivery", "Donut Shop", "Shrimp Boat", "Hockey Team", "Movie Studio", "Bank", "Oil Company" };
            int n;
            return match.Success && int.TryParse(match.Value, out n) && n > 0 && n <= names.Length ? names[n - 1] : Clean(venture.name);
        }

        private static string ChildText(Transform t)
        {
            return string.Join(", ", t.GetComponentsInChildren<Text>(true).Where(IsVisible)
                .Where(x => !IsPlayFabText(x)).Select(x => Clean(x.text)).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToArray());
        }

        private static string TextNamed(Transform root, string name)
        {
            if (root == null) return "";
            var text = root.GetComponentsInChildren<Text>(true)
                .FirstOrDefault(t => IsVisible(t) && string.Equals(t.gameObject.name, name, StringComparison.OrdinalIgnoreCase));
            return text == null ? "" : Clean(text.text);
        }

        private static string FindActiveText(string name)
        {
            var text = Resources.FindObjectsOfTypeAll<Text>()
                .FirstOrDefault(t => t != null && IsVisible(t) && string.Equals(t.gameObject.name, name, StringComparison.OrdinalIgnoreCase));
            return text == null ? "" : Clean(text.text);
        }

        private static Transform Ancestor(Transform t, string prefix)
        {
            while (t != null) { if (t.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return t; t = t.parent; }
            return null;
        }

        private static string Part(string label, string value) { return string.IsNullOrEmpty(value) ? "" : ", " + label + " " + value; }
        private static void AddValue(List<string> parts, string label, string value) { if (!string.IsNullOrEmpty(value)) parts.Add(label + " " + value); }

        private static string ContextName(Transform t)
        {
            var ignored = new[] { "Button", "Buy Button", "Progress Bar", "Info Container", "Purchased State", "Icon Container", "Content", "Currencies", "Currency Container", "Top Panel", "Panel Canvases", "Main UI Canvas" };
            while (t != null)
            {
                var candidate = Clean(t.name);
                if (!string.IsNullOrEmpty(candidate) && !ignored.Contains(candidate) && !candidate.StartsWith("VentureParent", StringComparison.OrdinalIgnoreCase) && !candidate.StartsWith("Img ", StringComparison.OrdinalIgnoreCase)) return candidate;
                t = t.parent;
            }
            return "";
        }

        private static bool IsPlayFabText(Text text)
        {
            if (text == null) return false;
            if (text.gameObject.name.IndexOf("Playfab", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var value = Clean(text.text);
            return Regex.IsMatch(value, "^[0-9A-F]{16}$", RegexOptions.IgnoreCase);
        }

        private static bool IsPlayFabControl(Selectable control)
        {
            if (control == null) return false;
            var value = Path(control.transform) + " " + ChildText(control.transform);
            return value.IndexOf("PlayFab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("Play Fab ID", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static double CostSortKey(string cost)
        {
            if (string.IsNullOrEmpty(cost)) return double.MaxValue;
            var cleaned = Clean(cost).Replace(",", "");
            var scientific = Regex.Match(cleaned, "([0-9]+(?:\\.[0-9]+)?)\\s*(?:[eE]\\s*\\+?|times\\s+ten\\s+to\\s+the)\\s*([+-]?[0-9]+)", RegexOptions.IgnoreCase);
            double number;
            int scientificExponent;
            if (scientific.Success &&
                double.TryParse(scientific.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) &&
                int.TryParse(scientific.Groups[2].Value, out scientificExponent))
                return Math.Log10(Math.Max(number, double.Epsilon)) + scientificExponent;
            var match = Regex.Match(cleaned, "([0-9]+(?:\\.[0-9]+)?)\\s*([A-Za-z]+)?", RegexOptions.IgnoreCase);
            if (!match.Success || !double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number)) return double.MaxValue;
            var suffix = match.Groups[2].Value.ToLowerInvariant();
            var exponent = DenominationExponent(suffix);
            if (exponent < 0) return double.MaxValue;
            return Math.Log10(Math.Max(number, double.Epsilon)) + exponent;
        }

        private static double ManagerSortKey(PanelItem item)
        {
            if (item != null && !double.IsNaN(item.NumericCost) && !double.IsInfinity(item.NumericCost) && item.NumericCost >= 0d)
                return Math.Log10(Math.Max(item.NumericCost, double.Epsilon));
            return item == null ? double.MaxValue : CostSortKey(item.Cost);
        }

        private static int DenominationExponent(string suffix)
        {
            var names = new[] { "", "", "", "million", "billion", "trillion", "quadrillion", "quintillion", "sextillion", "septillion", "octillion", "nonillion", "decillion", "undecillion", "duodecillion", "tredecillion", "quattuordecillion", "quindecillion", "sexdecillion", "septendecillion", "octodecillion", "novemdecillion", "vigintillion", "unvigintillion", "duovigintillion", "trevigintillion", "quattuorvigintillion", "quinvigintillion", "sexvigintillion", "septenvigintillion", "octovigintillion", "novemvigintillion", "trigintillion", "untrigintillion", "duotrigintillion", "tretrigintillion", "quattuortrigintillion", "quintrigintillion", "sextrigintillion", "septentrigintillion", "octotrigintillion", "novemtrigintillion" };
            if (string.IsNullOrEmpty(suffix)) return 0;
            if (suffix == "hundred") return 2;
            if (suffix == "thousand") return 3;
            var index = Array.IndexOf(names, suffix);
            return index < 3 ? -1 : index * 3 - 3;
        }

        private static PanelItem SpecialPanelItem(Button button)
        {
            var infoPanel = ComponentAncestor(button.transform, "InfoPanel");
            if (infoPanel != null)
            {
                var objectName = button.gameObject.name;
                var network = objectName.IndexOf("Facebook", StringComparison.OrdinalIgnoreCase) >= 0 ? "Facebook" :
                    objectName.IndexOf("Twitter", StringComparison.OrdinalIgnoreCase) >= 0 ? "Twitter" :
                    objectName.IndexOf("Instagram", StringComparison.OrdinalIgnoreCase) >= 0 ? "Instagram" : "";
                if (!string.IsNullOrEmpty(network))
                {
                    var rewardMember = network == "Facebook" ? "fbReward" : network.ToLowerInvariant() + "Reward";
                    var rewardObject = ObjectMember(infoPanel, rewardMember) as GameObject;
                    var reward = rewardObject != null && rewardObject.activeInHierarchy ? "First visit reward: 10 Gold" : "First visit reward already claimed";
                    return new PanelItem { Name = network + " social media page", Description = "Opens the official AdVenture Capitalist " + network + " page. " + reward, Action = button, ActionName = "open" };
                }
                if (objectName.IndexOf("Hippo", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new PanelItem { Name = "Hyper Hippo website", Description = "Opens the game developer's website", Action = button, ActionName = "open" };
                if (objectName.IndexOf("Support", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new PanelItem { Name = "AdVenture Capitalist support", Description = "Opens the official support website", Action = button, ActionName = "open" };
            }

            var exchange = ComponentAncestor(button.transform, "CurrencyExchange");
            if (exchange != null && (button.gameObject.name.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0 ||
                button.gameObject.name.IndexOf("Exchange", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var result = TextObjectMember(exchange, "megaCashResultsText");
                var cost = TextObjectMember(exchange, "sourceCashText");
                var source = TextObjectMember(exchange, "fromCurrencyText");
                var balance = TextObjectMember(exchange, "megaCashText");
                var details = "Receive " + result + " Mega Bucks for " + cost + (string.IsNullOrEmpty(source) ? "" : " " + source.ToLowerInvariant()) +
                    (string.IsNullOrEmpty(balance) ? "" : ". Current Mega Bucks " + balance);
                if (button.gameObject.name.IndexOf("Up", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new PanelItem { Name = "Increase Mega Bucks received", Description = details, Action = button, ActionName = "increase" };
                if (button.gameObject.name.IndexOf("Down", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new PanelItem { Name = "Decrease Mega Bucks received", Description = details, Action = button, ActionName = "decrease" };
                return new PanelItem { Name = "Exchange for " + result + " Mega Bucks", Description = details, Action = button, ActionName = "exchange" };
            }

            var tickets = ComponentAncestor(button.transform, "MegaTicketsPanel");
            if (tickets != null && button.gameObject.name.IndexOf("buy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var buttonName = button.gameObject.name;
                var card = Ancestor(button.transform, "Platform Store Item");
                var cardTitle = TextNamed(card, "title");
                var ten = cardTitle.StartsWith("10", StringComparison.OrdinalIgnoreCase);
                var gold = buttonName.IndexOf("Gold", StringComparison.OrdinalIgnoreCase) >= 0;
                var quantity = ten ? "10" : "1";
                var price = ChildText(button.transform);
                var currency = gold ? "Gold" : "Mega Bucks";
                return new PanelItem
                {
                    Name = "Buy " + quantity + (ten ? " Mega Tickets" : " Mega Ticket"),
                    Description = "Mega Tickets permanently gild businesses for a profit boost",
                    Cost = price + " " + currency,
                    Action = button,
                    ActionName = "buy"
                };
            }

            var infoButton = ComponentAncestor(button.transform, "InfoButtonUI");
            if (infoButton != null)
            {
                var title = ObjectTextMember(infoButton, "titleText");
                return new PanelItem { Name = string.IsNullOrEmpty(title) ? "Information" : title + " information", Action = button, ActionName = "open" };
            }

            var itemDetail = ComponentAncestor(button.transform, "ItemDetailView");
            if (itemDetail != null)
            {
                var item = PrivateField(itemDetail, "Item");
                if (item != null)
                {
                    var name = ObjectTextMember(item, "ItemName");
                    var description = ItemDescription(item);
                    var rarity = InvokeTextMethod(item, "GetRarityName");
                    var action = button.gameObject.name.IndexOf("Unequip", StringComparison.OrdinalIgnoreCase) >= 0 ? "unequip" : "equip";
                    var owned = ReactiveValueText(ObjectMember(item, "Owned"));
                    return new PanelItem { Name = name, Description = JoinDetails(description, rarity, "Owned " + owned), Action = button, ActionName = action,
                        UnavailableReason = ItemActionNeedsOwnership(action) && !HasOwnedItem(item) ? "You do not own this item" : "" };
                }
            }

            var itemIcon = ComponentAncestor(button.transform, "ItemIconView");
            if (itemIcon != null)
            {
                var item = PrivateField(itemIcon, "Item");
                if (item != null)
                {
                    var name = ObjectTextMember(item, "ItemName");
                    var description = ItemDescription(item);
                    var rarity = InvokeTextMethod(item, "GetRarityName");
                    var owned = ReactiveValueText(ObjectMember(item, "Owned"));
                    var equipped = string.Equals(ObjectTextMember(item, "IsEquipped"), "True", StringComparison.OrdinalIgnoreCase) ? "Equipped" : "Not equipped";
                    var action = "open details";
                    if (button.gameObject.name.IndexOf("Replace", StringComparison.OrdinalIgnoreCase) >= 0) action = "replace";
                    else if (button.gameObject.name.IndexOf("Equip", StringComparison.OrdinalIgnoreCase) >= 0) action = "equip";
                    return new PanelItem
                    {
                        Name = name,
                        Description = JoinDetails(description, rarity, string.IsNullOrEmpty(owned) ? "" : "Level or quantity " + owned, equipped),
                        Action = button,
                        ActionName = action,
                        UnavailableReason = ItemActionNeedsOwnership(action) && !HasOwnedItem(item) ? "You do not own this item" : ""
                    };
                }
            }

            var gallery = ComponentAncestor(button.transform, "UnlockDetailedItemView");
            if (gallery != null)
            {
                var unlock = PrivateField(gallery, "unlock");
                var name = ObjectTextMember(unlock, "name");
                var description = InvokeTextMethod(unlock, "GetDescription");
                var amount = TextNamed(gallery.transform, "txt_value");
                if (string.IsNullOrEmpty(name)) name = "Unlock at " + amount;
                if (!string.IsNullOrEmpty(amount) && description.IndexOf(amount, StringComparison.OrdinalIgnoreCase) < 0)
                    description = "Requires " + amount + " owned. " + description;
                return new PanelItem { Name = name, Description = JoinDetails(description, UnlockStateDescription(unlock)) };
            }

            var mission = ComponentAncestor(button.transform, "PlanetMissionPanel");
            if (mission != null)
            {
                var planetData = PrivateField(mission, "planetData");
                var storeItem = PrivateField(mission, "storeItem");
                var planet = ObjectTextMember(planetData, "DisplayName");
                if (string.IsNullOrEmpty(planet)) planet = TextNamed(mission.transform, "txt_Banner");
                if (string.IsNullOrEmpty(planet)) planet = Clean(mission.gameObject.name);
                var price = TextNamed(mission.transform, "txt_Price");
                var isPurchase = button.gameObject.name.IndexOf("Purchase", StringComparison.OrdinalIgnoreCase) >= 0;
                var planetInfo = ObjectTextMember(planetData, "PlanetInfo");
                var unlockMessage = ObjectTextMember(planetData, "UnlockMessage");
                var storeDescription = ObjectTextMember(storeItem, "Description");
                if (isPurchase)
                {
                    var cost = ObjectMember(storeItem, "Cost");
                    var currency = ObjectTextMember(cost, "Currency");
                    if (string.Equals(currency, "MegaBuck", StringComparison.OrdinalIgnoreCase)) price += " Mega Bucks";
                    else if (string.Equals(currency, "MegaTicket", StringComparison.OrdinalIgnoreCase)) price += " Mega Tickets";
                    else if (string.Equals(currency, "Gold", StringComparison.OrdinalIgnoreCase)) price += " Gold";
                    else if (string.Equals(currency, "Cash", StringComparison.OrdinalIgnoreCase)) price += " cash";
                    else if (string.Equals(currency, "Kreds", StringComparison.OrdinalIgnoreCase)) price += " Kreds";
                    else if (!price.StartsWith("$", StringComparison.Ordinal)) price += " Mega Bucks";
                }
                return new PanelItem
                {
                    Name = (isPurchase ? "Unlock " : "Travel to ") + planet,
                    Description = JoinDetails(planetInfo, unlockMessage, storeDescription, isPurchase ? "Unlock this adventure" : "Open this adventure"),
                    Cost = isPurchase ? price : "",
                    Action = button,
                    ActionName = isPurchase ? "purchase" : "go"
                };
            }

            var eventItem = ComponentAncestor(button.transform, "EventPanelItemUI");
            if (eventItem != null)
            {
                var values = eventItem.GetComponentsInChildren<Text>(true).Where(IsVisible).Where(t => !IsPlayFabText(t))
                    .Select(t => Clean(t.text)).Where(t => !string.IsNullOrEmpty(t) && !string.Equals(t, "Go", StringComparison.OrdinalIgnoreCase) && !string.Equals(t, "Go!", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
                var title = values.FirstOrDefault() ?? "Event adventure";
                var details = string.Join(". ", values.Skip(1).ToArray());
                return new PanelItem { Name = title, Description = details, Action = button, ActionName = "go" };
            }
            return null;
        }

        private static Component ComponentAncestor(Transform transform, string typeName)
        {
            while (transform != null)
            {
                var match = transform.GetComponents<Component>().FirstOrDefault(c => c != null && string.Equals(c.GetType().Name, typeName, StringComparison.Ordinal));
                if (match != null) return match;
                transform = transform.parent;
            }
            return null;
        }

        private static Component ComponentAncestorWithMember(Transform transform, string memberName, string memberTypeName)
        {
            while (transform != null)
            {
                var match = transform.GetComponents<Component>().FirstOrDefault(c =>
                {
                    var value = ObjectMember(c, memberName);
                    return value != null && string.Equals(value.GetType().Name, memberTypeName, StringComparison.Ordinal);
                });
                if (match != null) return match;
                transform = transform.parent;
            }
            return null;
        }

        private static object PrivateField(object instance, string fieldName)
        {
            if (instance == null) return null;
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(instance);
        }

        private static object ObjectMember(object instance, string memberName)
        {
            if (instance == null) return null;
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(instance);
                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null) return property.GetValue(instance, null);
            }
            return null;
        }

        private static double ObjectDoubleMember(object instance, string memberName)
        {
            var member = ObjectMember(instance, memberName);
            if (member == null) return double.NaN;
            var value = ObjectMember(member, "Value") ?? member;
            try { return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return double.NaN; }
        }

        private static bool ItemActionNeedsOwnership(string action)
        {
            return string.Equals(action, "equip", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "replace", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "unequip", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasOwnedItem(object item)
        {
            int owned;
            return int.TryParse(ReactiveValueText(ObjectMember(item, "Owned")), out owned) && owned > 0;
        }

        private static string TextObjectMember(object instance, string memberName)
        {
            var text = ObjectMember(instance, memberName) as Text;
            return text == null ? "" : Clean(text.text);
        }

        private static string ObjectTextMember(object instance, string memberName)
        {
            if (instance == null) return "";
            var type = instance.GetType();
            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return Clean(Convert.ToString(field.GetValue(instance)));
            var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property == null ? "" : Clean(Convert.ToString(property.GetValue(instance, null)));
        }

        private static string InvokeTextMethod(object instance, string methodName)
        {
            if (instance == null) return "";
            var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            return method == null ? "" : Clean(Convert.ToString(method.Invoke(instance, null)));
        }

        private static string ItemDescription(object item)
        {
            if (item == null) return "";
            var method = item.GetType().GetMethod("GetFilledItemDescription", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(string) }, null);
            if (method == null) return ObjectTextMember(item, "Description");
            try { return Clean(Convert.ToString(method.Invoke(item, new object[] { 0, "" }))); }
            catch { return ObjectTextMember(item, "Description"); }
        }

        private static string ReactiveValueText(object reactive)
        {
            if (reactive == null) return "";
            var property = reactive.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property == null ? "" : Clean(Convert.ToString(property.GetValue(reactive, null)));
        }

        private static string JoinDetails(params string[] values)
        {
            return string.Join(". ", values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        private static double ModelProductionSeconds(Transform businessRoot)
        {
            var model = VentureModel(businessRoot);
            if (model == null) return double.NaN;
            var durationProperty = model.GetType().GetProperty("EffectiveCoolDownTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var reactiveDuration = durationProperty == null ? null : durationProperty.GetValue(model, null);
            if (reactiveDuration == null) return double.NaN;
            var valueProperty = reactiveDuration.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (valueProperty == null) return double.NaN;
            try { return Convert.ToDouble(valueProperty.GetValue(reactiveDuration, null), System.Globalization.CultureInfo.InvariantCulture); }
            catch { return double.NaN; }
        }

        private static object VentureModel(Transform businessRoot)
        {
            return ObjectMember(VentureView(businessRoot), "venture");
        }

        private static Component VentureView(Transform businessRoot)
        {
            return businessRoot == null ? null : businessRoot.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(c => c != null && string.Equals(c.GetType().Name, "VentureView", StringComparison.Ordinal));
        }

        private static string ViewText(object ventureView, string memberName, string fallback)
        {
            var text = ObjectMember(ventureView, memberName) as Text;
            var value = text == null ? "" : Clean(text.text);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static double VentureModelValue(Transform businessRoot, string memberName)
        {
            var model = VentureModel(businessRoot);
            if (model == null) return double.NaN;
            object member = null;
            var property = model.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null) member = property.GetValue(model, null);
            if (member == null)
            {
                var field = model.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) member = field.GetValue(model);
            }
            if (member == null) return double.NaN;
            var valueProperty = member.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var value = valueProperty == null ? member : valueProperty.GetValue(member, null);
            try { return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return double.NaN; }
        }

        private static double ApplyDenomination(double value, string denomination)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || string.IsNullOrEmpty(denomination)) return value;
            var names = new[] { "", "thousand", "million", "billion", "trillion", "quadrillion", "quintillion", "sextillion", "septillion", "octillion", "nonillion", "decillion", "undecillion", "duodecillion", "tredecillion", "quattuordecillion", "quindecillion", "sexdecillion", "septendecillion", "octodecillion", "novemdecillion", "vigintillion" };
            var index = Array.IndexOf(names, Clean(denomination).ToLowerInvariant());
            return index <= 0 ? value : value * Math.Pow(1000d, index);
        }

        private static void DetectWelcomeBackDialog()
        {
            var button = Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b => b != null && IsPresent(b) && Path(b.transform).IndexOf("WelcomeBack", StringComparison.OrdinalIgnoreCase) >= 0);
            if (button == null)
            {
                _lastWelcomeBackPath = null;
                _pendingWelcomeBackFrames = 0;
                return;
            }
            var path = Path(button.transform);
            if (string.Equals(path, _lastWelcomeBackPath, StringComparison.Ordinal)) return;
            _lastWelcomeBackPath = path;
            _pendingWelcomeBackFrames = 3;
        }

        private static void DetectInfoDialog()
        {
            var popupButtons = Resources.FindObjectsOfTypeAll<Button>()
                .Where(b => b != null && IsPresent(b) && Path(b.transform).IndexOf("WelcomeBack", StringComparison.OrdinalIgnoreCase) < 0 && PopupAncestor(b.transform) != null).ToList();
            if (popupButtons.Count == 0)
            {
                _lastInfoDialogSignature = null;
                _lastAnnouncedDialogMessage = null;
                return;
            }
            var topOrder = popupButtons.Max(CanvasOrder);
            var topButtons = popupButtons.Where(b => CanvasOrder(b) == topOrder).ToList();
            var popup = PopupAncestor(topButtons[0].transform);
            if (popup == null) return;
            var eventSignature = EventAccessibility.EventDialogKey(popup);
            // Dialog values such as projected Angels can update every frame. Treat the
            // popup instance, rather than its mutable text, as the dialog identity so
            // live values do not repeatedly interrupt screen-reader speech.
            var signature = eventSignature ?? popup.GetInstanceID().ToString();
            if (string.Equals(signature, _lastInfoDialogSignature, StringComparison.Ordinal)) return;
            _lastInfoDialogSignature = signature;
            _pendingDialogAnnouncementFrames = 3;
        }

        private static Transform PopupAncestor(Transform transform)
        {
            while (transform != null)
            {
                if (transform.GetComponents<Component>().Any(c => c != null &&
                    (c.GetType().Name.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     c.GetType().Name.EndsWith("Modal", StringComparison.OrdinalIgnoreCase) ||
                     c.GetType().Name.IndexOf("Celebration", StringComparison.OrdinalIgnoreCase) >= 0)))
                    return transform;
                transform = transform.parent;
            }
            return null;
        }

        private static void AnnounceWelcomeBackDialog()
        {
            var buttons = Resources.FindObjectsOfTypeAll<Button>().Where(b => b != null && IsPresent(b) && Path(b.transform).IndexOf("WelcomeBack", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (buttons.Count == 0) return;
            var root = buttons[0].GetComponentInParent<Canvas>();
            if (root == null) return;
            var values = root.GetComponentsInChildren<Text>(true).Where(IsVisible).Where(t => !IsPlayFabText(t) && t.GetComponentInParent<Button>() == null)
                .OrderByDescending(t => t.transform.position.y).ThenBy(t => t.transform.position.x)
                .Select(t => DialogText(t.text)).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
            var actions = buttons.Select(b => ChildText(b.transform)).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
            var message = "Welcome back dialog";
            if (values.Count > 0) message += ". " + string.Join(" ", values.ToArray());
            if (actions.Count > 0) message += ". " + string.Join(". ", actions.Select(a => a + " button").ToArray());
            Speech.Write(message);
        }

        private static string DialogText(string value)
        {
            value = Clean(value);
            value = Regex.Replace(value, "(?<!\\d)(\\d+):(\\d{1,2}):(\\d{1,2}):(\\d{1,2})(?!\\d)", m =>
            {
                return SpokenDuration(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value));
            });
            value = Regex.Replace(value, "(?<![\\d:])(\\d+):(\\d{1,2}):(\\d{1,2})(?![:\\d])", m =>
            {
                var totalHours = int.Parse(m.Groups[1].Value);
                return SpokenDuration(totalHours / 24, totalHours % 24, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            });
            value = Regex.Replace(value, "\\$\\s*[0-9]+(?:\\.[0-9]+)?\\s*[A-Za-z]*", m => AccessibleCost(m.Value), RegexOptions.IgnoreCase);
            return Regex.Replace(value, "[\\s\\.]+$", "");
        }

        private static string SpokenDuration(int days, int hours, int minutes, int seconds)
        {
            var parts = new List<string>();
            if (days != 0) parts.Add(days + (days == 1 ? " day" : " days"));
            if (hours != 0) parts.Add(hours + (hours == 1 ? " hour" : " hours"));
            if (minutes != 0) parts.Add(minutes + (minutes == 1 ? " minute" : " minutes"));
            if (seconds != 0 || parts.Count == 0) parts.Add(seconds + (seconds == 1 ? " second" : " seconds"));
            return string.Join(", ", parts.ToArray());
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("_", " ").Replace("Btn ", "").Replace("btn ", "").Replace("\n", " ").Trim();
            value = Regex.Replace(value, "<[^>]+>", "");
            value = Regex.Replace(value, "\\s+", " ");
            return Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
        }

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static void AnnounceSmallDialog()
        {
            var popupButtons = Resources.FindObjectsOfTypeAll<Button>()
                .Where(b => b != null && IsPresent(b) && Path(b.transform).IndexOf("WelcomeBack", StringComparison.OrdinalIgnoreCase) < 0 && PopupAncestor(b.transform) != null).ToList();
            if (popupButtons.Count > 0)
            {
                var topButtons = popupButtons.Where(b => CanvasOrder(b) == popupButtons.Max(CanvasOrder)).ToList();
                var popup = topButtons.Count == 0 ? null : PopupAncestor(topButtons[0].transform);
                var eventMessage = EventAccessibility.EventDialogAnnouncement(popup);
                if (!string.IsNullOrEmpty(eventMessage))
                {
                    if (!string.Equals(eventMessage, _lastAnnouncedDialogMessage, StringComparison.Ordinal))
                    {
                        _lastAnnouncedDialogMessage = eventMessage;
                        Speech.Write(eventMessage);
                    }
                    return;
                }
            }
            var texts = Resources.FindObjectsOfTypeAll<Text>().Where(t => t != null && IsVisible(t) && CanvasOrder(t) >= 20).ToList();
            if (texts.Count == 0) return;
            var topOrder = texts.Max(CanvasOrder);
            var values = texts.Where(t => CanvasOrder(t) == topOrder && t.GetComponentInParent<Button>() == null && !IsPlayFabText(t))
                .OrderByDescending(t => t.transform.position.y).ThenBy(t => t.transform.position.x)
                .Select(t => DialogText(t.text)).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToArray();
            if (values.Length == 0) return;
            var message = "Dialog. " + string.Join(" ", values);
            if (message.Length > 1600) message = message.Substring(0, 1600);
            if (string.Equals(message, _lastAnnouncedDialogMessage, StringComparison.Ordinal)) return;
            _lastAnnouncedDialogMessage = message;
            Speech.Write(message);
        }
    }
}
