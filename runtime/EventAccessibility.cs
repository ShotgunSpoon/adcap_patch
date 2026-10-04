using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace AdCapUnityMCP
{
    internal static class EventAccessibility
    {
        private static readonly string[] Tabs = { "Details", "Goals", "Rewards", "Leaderboard" };
        private static readonly int[] ItemIndices = new int[Tabs.Length];
        private static readonly int[] RowIndices = new int[Tabs.Length];
        private static readonly object LeaderboardLock = new object();
        private static readonly object TypeCacheLock = new object();
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly List<LeaderboardEntry> NearbyLeaderboard = new List<LeaderboardEntry>();
        private static readonly List<LeaderboardEntry> TopLeaderboard = new List<LeaderboardEntry>();
        private static readonly List<IDisposable> Subscriptions = new List<IDisposable>();

        private static bool _active;
        private static int _tabIndex;
        private static string _eventId;
        private static string _leaderboardEventId;
        private static string _leaderboardState = "not loaded";
        private static string _leaderboardError;
        private static int _leaderboardRequestsPending;
        private static int _sessionGeneration;
        private static int _leaderboardRequestGeneration;

        private sealed class EventItem
        {
            public string Key;
            public string State;
            public string Name;
            public string Description;
            public string Progress;
            public string ActionName;
            public Func<string> Action;
        }

        private sealed class LeaderboardEntry
        {
            public string Name;
            public int Rank;
            public double Score;
            public bool IsPlayer;
        }

        private sealed class ObserverProxy<T> : IObserver<T>
        {
            private readonly Action<object> _next;
            private readonly Action<Exception> _error;
            private readonly Action _completed;

            public ObserverProxy(Action<object> next, Action<Exception> error, Action completed)
            {
                _next = next;
                _error = error;
                _completed = completed;
            }

            public void OnNext(T value) { if (_next != null) _next(value); }
            public void OnError(Exception error) { if (_error != null) _error(error); }
            public void OnCompleted() { if (_completed != null) _completed(); }
        }

        public static bool IsActive { get { return _active; } }

        public static bool IsEventPlanet()
        {
            return BoolMember(GameState(), "IsEventPlanet");
        }

        public static bool HandleReturnShortcut()
        {
            if (!Input.GetKeyDown(KeyCode.E) ||
                (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))) return false;
            if (!IsEventPlanet()) return false;
            ReturnToLastNormalPlanet(true);
            return true;
        }

        public static string ReturnToLastNormalPlanet(bool announce)
        {
            var controller = GameController();
            if (controller == null) return Speak("The game controller is unavailable", announce);
            if (!IsEventPlanet()) return Speak("Shift E returns from an event planet only", announce);
            if (Member(controller, "IsLoadingPlanet") == null || BoolMember(controller, "IsLoadingPlanet"))
                return Speak("A planet is already loading", announce);

            var destination = Clean(ObjectText(controller, "lastPlanetName"));
            if (destination != "Earth" && destination != "Moon" && destination != "Mars") destination = "Earth";
            try
            {
                Invoke(controller, "LoadPlanetScene", destination);
            }
            catch (Exception exception)
            {
                return Speak("Could not return to " + destination + ". " + exception.GetBaseException().Message, announce);
            }
            if (!BoolMember(controller, "IsLoadingPlanet"))
                return Speak("The game did not start returning to " + destination, announce);
            _active = false;
            ResetForEvent(null);
            return Speak("Returning to " + destination, announce);
        }

        public static void Update()
        {
            if (!_active) return;
            object controller;
            object model;
            string currentId;
            string reason = null;
            if (!TryGetActiveEvent(out controller, out model, out currentId, out reason))
            {
                InvalidateSession("Event interface closed. " + reason, true);
                return;
            }
            if (!string.Equals(currentId, _eventId, StringComparison.Ordinal))
            {
                _active = false;
                ResetForEvent(null);
                Speech.Write("Event interface closed because the active event changed. Press E to open the new event");
            }
        }

        public static bool HandleKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                Speak(EventTimingMessage(), true);
                return true;
            }

            if (!_active)
            {
                if (Input.GetKeyDown(KeyCode.E))
                {
                    if (IsEventPlanet())
                    {
                        if (HasBlockingOverlay()) Speak("Complete or close the current dialog before opening the event interface", true);
                        else Open(true);
                    }
                    else EnterActiveEvent(true);
                    return true;
                }
                return false;
            }

            if (HasBlockingOverlay())
            {
                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.RightBracket) || Input.GetKeyDown(KeyCode.G))
                {
                    Speak("Complete or close the current dialog before returning to the event interface", true);
                    return true;
                }
                return false;
            }

            if (Input.GetKeyDown(KeyCode.LeftBracket)) { CycleTab(-1, true); return true; }
            if (Input.GetKeyDown(KeyCode.RightBracket)) { CycleTab(1, true); return true; }
            if (Input.GetKeyDown(KeyCode.E)) { Exit("Event interface closed. Business navigation", true); return true; }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { MoveItem(-1, true); return true; }
            if (Input.GetKeyDown(KeyCode.RightArrow)) { MoveItem(1, true); return true; }
            if (Input.GetKeyDown(KeyCode.UpArrow)) { MoveRow(-1, true); return true; }
            if (Input.GetKeyDown(KeyCode.DownArrow)) { MoveRow(1, true); return true; }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) { Activate(true); return true; }
            if (Input.GetKeyDown(KeyCode.F6)) { Current(true); return true; }
            if (Input.GetKeyDown(KeyCode.F7)) { Status(true); return true; }
            if (Input.GetKeyDown(KeyCode.G)) { Speak("Mega Ticket gilding is unavailable while the event interface is open", true); return true; }
            return false;
        }

        public static string EnterActiveEvent(bool announce)
        {
            if (IsEventPlanet())
            {
                if (HasBlockingOverlay()) return Speak("Complete or close the current dialog before opening the event interface", announce);
                return Open(announce);
            }
            if (HasBlockingEntryOverlay())
                return Speak("Close the current dialog before entering an event", announce);

            object controller;
            object model;
            string eventId;
            string reason;
            if (!TryGetEnterableEvent(out controller, out model, out eventId, out reason))
            {
                if (string.Equals(reason, "No event is currently active", StringComparison.Ordinal) ||
                    string.Equals(reason, "The event has ended", StringComparison.Ordinal))
                    return Speak(NoActiveEventMessage(), announce);
                return Speak(reason, announce);
            }
            if (EnterableEventCount(controller) > 1)
                return AccessibilityNavigator.OpenEventSelector(announce);

            // Re-resolve immediately before invoking the game's own event button. The
            // native handler owns event ordering, analytics, saving, and planet loading.
            object currentController;
            object currentModel;
            string currentId;
            if (!TryGetEnterableEvent(out currentController, out currentModel, out currentId, out reason) ||
                !ReferenceEquals(controller, currentController) ||
                !string.Equals(eventId, currentId, StringComparison.Ordinal))
                return Speak("The active event changed, so travel was cancelled", announce);
            if (EnterableEventCount(currentController) > 1)
                return AccessibilityNavigator.OpenEventSelector(announce);

            var quickButtons = ActiveComponent("QuickButtonController");
            var goToEvent = Member(quickButtons, "GoToEventButton") as Button;
            if (!NativeEventButtonEligible(quickButtons) || goToEvent == null || goToEvent.gameObject == null ||
                !goToEvent.enabled || !goToEvent.interactable)
                return Speak("The game's Go to Event control is not ready", announce);

            var title = EventTitleForModel(currentModel);
            try
            {
                goToEvent.onClick.Invoke();
            }
            catch (Exception exception)
            {
                return Speak("Could not enter " + title + ". " + exception.Message, announce);
            }
            if (!BoolMember(controller, "IsLoadingPlanet"))
                return Speak("The game's Go to Event control did not start travel", announce);
            return Speak("Entering " + title + " event", announce);
        }

        public static string EntryStatus()
        {
            if (IsEventPlanet())
            {
                object loadedController;
                object loadedModel;
                string loadedId;
                string loadedReason;
                var loaded = TryGetActiveEvent(out loadedController, out loadedModel, out loadedId, out loadedReason);
                var interfaceBlocked = HasBlockingOverlay();
                return "eventPlanet=true; eventAvailable=" + loaded.ToString().ToLowerInvariant() +
                    "; entryAvailable=" + (loaded && !interfaceBlocked).ToString().ToLowerInvariant() +
                    "; blocked=" + interfaceBlocked.ToString().ToLowerInvariant() +
                    "; eventId=" + Clean(loadedId) + "; title=" + Clean(EventTitleForModel(loadedModel)) +
                    "; action=" + (interfaceBlocked ? "complete or close dialog" : "open interface") +
                    "; reason=" + Clean(interfaceBlocked ? "A blocking dialog is open" : loadedReason);
            }

            object controller;
            object model;
            string eventId;
            string reason;
            var available = TryGetEnterableEvent(out controller, out model, out eventId, out reason);
            var entryBlocked = HasBlockingEntryOverlay();
            var activeCount = available ? EnterableEventCount(controller) : 0;
            var quickButtons = available ? ActiveComponent("QuickButtonController") : null;
            var button = Member(quickButtons, "GoToEventButton") as Button;
            var ready = !entryBlocked && activeCount == 1 && NativeEventButtonEligible(quickButtons) && button != null && button.gameObject != null && button.enabled && button.interactable;
            var entryAvailable = available && !entryBlocked && (activeCount > 1 || ready);
            return "eventPlanet=false; eventAvailable=" + available.ToString().ToLowerInvariant() +
                "; entryAvailable=" + entryAvailable.ToString().ToLowerInvariant() +
                "; blocked=" + entryBlocked.ToString().ToLowerInvariant() +
                "; activeEventCount=" + activeCount +
                "; nativeActionReady=" + ready.ToString().ToLowerInvariant() +
                "; eventId=" + Clean(eventId) + "; title=" + Clean(EventTitleForModel(model)) +
                "; remaining=" + Clean(EventTime(model)) +
                "; action=" + (entryBlocked ? "complete or close dialog" : activeCount > 1 ? "select event" : ready ? "enter event" : "unavailable") +
                "; reason=" + Clean(entryBlocked ? "A blocking dialog is open" : reason);
        }

        public static string Open(bool announce)
        {
            object controller;
            object model;
            string id;
            string reason;
            if (!TryGetActiveEvent(out controller, out model, out id, out reason)) return Speak(reason, announce);
            if (!_active || !string.Equals(id, _eventId, StringComparison.Ordinal)) ResetForEvent(id);
            _active = true;
            _tabIndex = 0;
            return Speak("Event interface opened. Use left and right bracket for Details, Goals, Rewards, and Leaderboard. Press E to close it. " + Current(false), announce);
        }

        public static string Exit(string message, bool announce)
        {
            _active = false;
            return Speak(message, announce);
        }

        private static string InvalidateSession(string message, bool announce)
        {
            _active = false;
            ResetForEvent(null);
            return Speak(message, announce);
        }

        public static string CycleTab(int direction, bool announce)
        {
            if (!_active) return Speak("The event interface is closed. Press E to open it", announce);
            string reason;
            if (!ValidateSession(out reason)) return InvalidateSession("Event interface closed. " + reason, announce);
            _tabIndex = (_tabIndex + direction + Tabs.Length) % Tabs.Length;
            if (_tabIndex == 3) EnsureLeaderboardRequested(false);
            return Speak("Event " + Tabs[_tabIndex] + " tab. " + Current(false), announce);
        }

        public static string MoveItem(int direction, bool announce)
        {
            if (!EnsureOpen(announce)) return "Event interface unavailable";
            var items = CurrentItems();
            if (items.Count == 0) return Speak("No items are available in Event " + Tabs[_tabIndex], announce);
            ItemIndices[_tabIndex] = (ItemIndices[_tabIndex] + direction + items.Count) % items.Count;
            if (!RowExists(items[ItemIndices[_tabIndex]], RowIndices[_tabIndex])) RowIndices[_tabIndex] = 0;
            return Speak(Current(false), announce);
        }

        public static string MoveRow(int direction, bool announce)
        {
            if (!EnsureOpen(announce)) return "Event interface unavailable";
            var items = CurrentItems();
            if (items.Count == 0) return Speak("No items are available in Event " + Tabs[_tabIndex], announce);
            ClampItemIndex(items);
            var item = items[ItemIndices[_tabIndex]];
            for (var i = 0; i < 4; i++)
            {
                RowIndices[_tabIndex] = (RowIndices[_tabIndex] + direction + 4) % 4;
                if (RowExists(item, RowIndices[_tabIndex])) break;
            }
            return Speak(Current(false), announce);
        }

        public static string Current(bool announce)
        {
            if (!EnsureOpen(announce)) return "Event interface unavailable";
            var items = CurrentItems();
            if (items.Count == 0) return Speak("Event " + Tabs[_tabIndex] + " tab. No items available", announce);
            ClampItemIndex(items);
            var item = items[ItemIndices[_tabIndex]];
            if (!RowExists(item, RowIndices[_tabIndex])) RowIndices[_tabIndex] = 0;
            var prefix = "Event " + Tabs[_tabIndex] + ", " + (ItemIndices[_tabIndex] + 1) + " of " + items.Count + ". ";
            string message;
            switch (RowIndices[_tabIndex])
            {
                case 1: message = prefix + item.Name + ". " + item.Description; break;
                case 2: message = prefix + item.Name + ". " + item.Progress; break;
                case 3: message = prefix + item.Name + ". " + item.ActionName + " button"; break;
                default: message = prefix + item.Name; break;
            }
            return Speak(message, announce);
        }

        public static string Activate(bool announce)
        {
            if (!EnsureOpen(announce)) return "Event interface unavailable";
            var items = CurrentItems();
            if (items.Count == 0) return Speak("There is nothing to activate in Event " + Tabs[_tabIndex], announce);
            ClampItemIndex(items);
            var item = items[ItemIndices[_tabIndex]];
            if (item.Action == null) return Speak(ItemDetails(item), announce);
            if (RowIndices[_tabIndex] != 3)
                return Speak(ItemDetails(item) + ". Move to the " + item.ActionName + " button before activating", announce);
            try
            {
                var result = item.Action();
                if (!_active) return Speak(result, announce);
                ItemIndices[_tabIndex] = Math.Max(0, Math.Min(ItemIndices[_tabIndex], Math.Max(0, CurrentItems().Count - 1)));
                RowIndices[_tabIndex] = 0;
                return Speak(result, announce);
            }
            catch (Exception error)
            {
                return Speak("Event action failed safely. " + Clean(error.GetBaseException().Message), announce);
            }
        }

        public static string Status(bool announce)
        {
            if (!EnsureOpen(announce)) return "Event interface unavailable";
            var model = CurrentEventModel();
            var title = EventTitle(model);
            var remaining = EventTime(model);
            var goals = GoalObjects();
            var claimable = goals.Count(g => string.Equals(ReactiveText(Member(g, "State")), "COMPLETE", StringComparison.OrdinalIgnoreCase));
            var expired = goals.Count(g => string.Equals(ReactiveText(Member(g, "State")), "EXPIRED", StringComparison.OrdinalIgnoreCase));
            var score = CurrentEventScore();
            var rank = CurrentPlayerRank();
            var parts = new List<string> { title + " event", "Event " + Tabs[_tabIndex] + " tab" };
            if (!string.IsNullOrEmpty(remaining)) parts.Add(remaining + " remaining");
            parts.Add("Score " + FormatNumber(score));
            if (rank > 0) parts.Add("Rank " + rank);
            parts.Add(goals.Count + " goals");
            if (claimable > 0) parts.Add(claimable + " ready to claim");
            if (expired > 0) parts.Add(expired + " expired");
            return Speak(string.Join(". ", parts.ToArray()), announce);
        }

        public static string ListItems()
        {
            if (!EnsureOpen(false)) return "Event interface unavailable";
            var items = CurrentItems();
            return string.Join("\n", items.Select((item, index) => (index + 1) + ": " + ItemDetails(item)).ToArray());
        }

        public static bool IsEventIntroVisible()
        {
            return ActiveComponent("EventIntroModal") != null;
        }

        public static string EventIntroAnnouncement()
        {
            if (!IsEventPlanet()) return "Event introduction";
            var model = CurrentEventModel();
            var title = EventTitle(model);
            var remaining = EventTime(model);
            var progression = ObjectText(GameState(), "progressionType");
            string steps;
            if (string.Equals(progression, "Missions", StringComparison.OrdinalIgnoreCase))
                steps = "Step 1, complete event goals. Step 2, claim goal points to climb the leaderboard. Step 3, claim point milestone rewards during the event and receive a final leaderboard reward after it ends";
            else if (string.Equals(progression, "Angels", StringComparison.OrdinalIgnoreCase))
                steps = "Build event businesses, then claim Angels to advance event reward milestones and the leaderboard. Milestone rewards are claimed during the event and final leaderboard rewards are received after it ends";
            else
                steps = "This event's score method is not identified. Details and available rewards can be reviewed in the accessible event interface";
            return title + " event introduction. " + (string.IsNullOrEmpty(remaining) ? "" : "Ends in " + remaining + ". ") +
                steps + ". Continue button. " +
                "Press E to open the accessible event interface";
        }

        private static void ResetForEvent(string eventId)
        {
            _eventId = eventId;
            _sessionGeneration++;
            _leaderboardRequestGeneration++;
            _tabIndex = 0;
            for (var i = 0; i < Tabs.Length; i++) { ItemIndices[i] = 0; RowIndices[i] = 0; }
            lock (LeaderboardLock)
            {
                NearbyLeaderboard.Clear();
                TopLeaderboard.Clear();
                _leaderboardEventId = null;
                _leaderboardState = "not loaded";
                _leaderboardError = null;
                _leaderboardRequestsPending = 0;
                foreach (var subscription in Subscriptions) try { subscription.Dispose(); } catch { }
                Subscriptions.Clear();
            }
        }

        private static bool EnsureOpen(bool announce)
        {
            string reason = null;
            if (_active && ValidateSession(out reason)) return true;
            if (_active)
            {
                InvalidateSession("Event interface closed. " + reason, announce);
                return false;
            }
            Speak("The event interface is closed. Press E to open it", announce);
            return false;
        }

        private static List<EventItem> CurrentItems()
        {
            switch (_tabIndex)
            {
                case 1: return GoalItems();
                case 2: return RewardItems();
                case 3: return LeaderboardItems();
                default: return DetailItems();
            }
        }

        private static List<EventItem> DetailItems()
        {
            var result = new List<EventItem>();
            var model = CurrentEventModel();
            var title = EventTitle(model);
            var state = ReactiveText(Member(model, "State"));
            var remaining = EventTime(model);
            var promo = Join(ObjectText(model, "PromoTitle"), ObjectText(model, "PromoBody"), ObjectText(model, "FeatureTutorialText"));
            result.Add(new EventItem
            {
                Key = "event:" + CurrentEventId(),
                State = state,
                Name = title + " event, " + (string.IsNullOrEmpty(state) ? "current" : state.ToLowerInvariant()),
                Description = string.IsNullOrEmpty(promo) ? "A temporary event planet with its own businesses, goals, rewards, and leaderboard" : promo,
                Progress = string.IsNullOrEmpty(remaining) ? "Event time is unavailable" : remaining + " remaining"
            });
            var progression = ObjectText(GameState(), "progressionType");
            var howItWorks = string.Equals(progression, "Missions", StringComparison.OrdinalIgnoreCase)
                ? "Complete goals, then claim each completed goal to add its points. Points advance event reward milestones and your leaderboard score"
                : string.Equals(progression, "Angels", StringComparison.OrdinalIgnoreCase)
                    ? "Claim Angels during the event to increase your event score. That score advances event reward milestones and the leaderboard"
                    : "The game did not publish a recognized mission or Angel score method for this event";
            result.Add(new EventItem
            {
                Key = "how-it-works",
                State = progression,
                Name = "How this event works",
                Description = howItWorks,
                Progress = "Milestone rewards are claimed during the event. Final leaderboard rewards are awarded after the event ends"
            });
            var rank = CurrentPlayerRank();
            result.Add(new EventItem
            {
                Key = "standing",
                State = rank > 0 ? "ranked" : "unranked",
                Name = "Current event standing",
                Description = "Score " + FormatNumber(CurrentEventScore()) + (rank > 0 ? ". Rank " + rank : ". A rank is not available yet"),
                Progress = GoalObjects().Count + " current goals"
            });
            var continueButton = Member(ActiveComponent("EventIntroModal"), "btn_continue") as Button;
            if (continueButton != null && IsPresent(continueButton))
            {
                result.Add(new EventItem
                {
                    Key = "continue-intro",
                    State = continueButton.interactable ? "available" : "unavailable",
                    Name = "Continue into " + title,
                    Description = "Close the event introduction and enter the event planet",
                    ActionName = "continue",
                    Action = delegate { return ContinueEventIntro(title); }
                });
            }
            return result;
        }

        private static List<object> GoalObjects()
        {
            return Enumerate(Member(EventMissionService(), "currentMissions"));
        }

        private static List<EventItem> GoalItems()
        {
            var result = new List<EventItem>();
            var progression = ObjectText(GameState(), "progressionType");
            if (!string.Equals(progression, "Missions", StringComparison.OrdinalIgnoreCase))
            {
                var isAngels = string.Equals(progression, "Angels", StringComparison.OrdinalIgnoreCase);
                result.Add(new EventItem
                {
                    Key = isAngels ? "angel-progression" : "unknown-progression",
                    State = progression,
                    Name = isAngels ? "This event uses Angel progression" : "Goal progression is unavailable",
                    Description = isAngels
                        ? "Claim Angels on the event planet to increase the score used by rewards and the leaderboard"
                        : "This event does not expose mission goals or a recognized Angel progression method",
                    Progress = "Current score " + FormatNumber(CurrentEventScore())
                });
                return result;
            }
            var service = EventMissionService();
            var goals = GoalObjects();
            for (var index = 0; index < goals.Count; index++)
            {
                var goal = goals[index];
                var description = Clean(Convert.ToString(Invoke(service, "GetMissionDescription", goal)));
                if (string.IsNullOrEmpty(description)) description = Clean(ObjectText(goal, "Type").Replace("_", " ").ToLowerInvariant());
                var state = ReactiveText(Member(goal, "State"));
                var current = DoubleReactive(Member(goal, "CurrentCount"));
                var target = DoubleMember(goal, "TargetAmount");
                var points = IntMember(goal, "RewardAmount");
                var missionId = IntMember(goal, "ID");
                var remaining = DoubleReactive(Member(goal, "TimeRemaining"));
                var item = new EventItem
                {
                    Key = "goal:" + missionId,
                    State = state,
                    Name = "Goal " + (index + 1) + ", " + description + ". " + FriendlyState(state),
                    Description = "Progress " + FormatNumber(current) + " of " + FormatNumber(target) + ". Reward " + points + " points",
                    Progress = double.IsNaN(remaining) ? "No goal expiration timer" : FormatDuration(remaining) + " remaining"
                };
                if (string.Equals(state, "COMPLETE", StringComparison.OrdinalIgnoreCase))
                {
                    item.ActionName = "claim " + points + " points";
                    item.Action = delegate { return ClaimGoal(missionId); };
                }
                else if (string.Equals(state, "EXPIRED", StringComparison.OrdinalIgnoreCase))
                {
                    item.ActionName = "clear expired goal";
                    item.Action = delegate { return ClearGoal(missionId); };
                }
                result.Add(item);
            }
            return result;
        }

        private static string ClaimGoal(int missionId)
        {
            string reason;
            if (!ValidateSession(out reason)) return InvalidateSession(reason, false);
            var goal = GoalObjects().FirstOrDefault(candidate => IntMember(candidate, "ID") == missionId);
            if (goal == null) return "This goal was replaced before it could be claimed";
            if (!string.Equals(ReactiveText(Member(goal, "State")), "COMPLETE", StringComparison.OrdinalIgnoreCase))
                return "This goal is no longer ready to claim";
            var points = IntMember(goal, "RewardAmount");
            var claimed = Invoke(EventMissionService(), "ClaimMissionReward", goal);
            if (!(claimed is bool) || !(bool)claimed) return "The game did not claim this goal, so no points were changed";
            return "Goal claimed. Added " + points + " points. New event score " + FormatNumber(CurrentEventScore());
        }

        private static string ClearGoal(int missionId)
        {
            string reason;
            if (!ValidateSession(out reason)) return InvalidateSession(reason, false);
            var goal = GoalObjects().FirstOrDefault(candidate => IntMember(candidate, "ID") == missionId);
            if (goal == null) return "This expired goal was already replaced";
            if (!string.Equals(ReactiveText(Member(goal, "State")), "EXPIRED", StringComparison.OrdinalIgnoreCase))
                return "This goal is no longer expired";
            var cleared = Invoke(EventMissionService(), "ClearMission", goal);
            if (!(cleared is bool) || !(bool)cleared) return "The game did not clear this goal";
            return "Expired goal cleared and replaced. No points were awarded";
        }

        private static string ContinueEventIntro(string title)
        {
            string reason;
            if (!ValidateSession(out reason)) return InvalidateSession(reason, false);
            var button = Member(ActiveComponent("EventIntroModal"), "btn_continue") as Button;
            if (button == null || !IsPresent(button) || !button.interactable) return "Continue is no longer available";
            button.onClick.Invoke();
            return "Continuing into " + title;
        }

        private static List<EventItem> RewardItems()
        {
            var result = new List<EventItem>();
            var controller = GameController();
            var milestoneService = Member(controller, "PlanetMilestoneService");
            var milestones = Enumerate(Invoke(milestoneService, "GetUserMilestonesForCurrentPlanet"));
            var configured = Enumerate(Member(GameState(), "PlanetMilestones"));
            for (var index = 0; index < milestones.Count; index++)
            {
                var milestone = milestones[index];
                var milestoneId = ObjectText(milestone, "MilestoneId");
                var state = ReactiveText(Member(milestone, "State"));
                var current = DoubleReactive(Member(milestone, "CurrentCount"));
                var target = DoubleMember(milestone, "TargetAmount");
                var source = configured.FirstOrDefault(candidate => string.Equals(ObjectText(candidate, "Id"), milestoneId, StringComparison.Ordinal));
                var rewardText = MilestoneRewardText(source, state);
                var displayName = Clean(ObjectText(milestone, "DisplayName"));
                if (string.IsNullOrEmpty(displayName)) displayName = "Milestone " + (index + 1);
                var item = new EventItem
                {
                    Key = "milestone:" + milestoneId,
                    State = state,
                    Name = displayName + ". " + FriendlyState(state),
                    Description = "Reward " + rewardText,
                    Progress = "Event score " + FormatNumber(current) + " of " + FormatNumber(target)
                };
                if (string.Equals(state, "COMPLETE", StringComparison.OrdinalIgnoreCase))
                {
                    item.ActionName = "claim " + rewardText;
                    item.Action = delegate { return ClaimMilestone(milestoneId); };
                }
                result.Add(item);
            }

            var tiers = RewardTiers();
            for (var index = 0; index < tiers.Count; index++)
            {
                var tier = tiers[index];
                var tierId = ObjectText(tier, "tierId");
                var tierName = Clean(ObjectText(tier, "tierName"));
                if (string.IsNullOrEmpty(tierName)) tierName = "Leaderboard reward tier " + (index + 1);
                var top = IntMember(tier, "topRank");
                var bottom = IntMember(tier, "bottomRank");
                var rewards = Enumerate(Member(tier, "leaderboardRewardItems"));
                var rewardNames = rewards.Select(LeaderboardTierRewardText).Where(value => !string.IsNullOrEmpty(value)).ToArray();
                result.Add(new EventItem
                {
                    Key = "leaderboard-tier:" + tierId,
                    State = RankRange(top, bottom),
                    Name = tierName + " leaderboard reward",
                    Description = rewardNames.Length == 0 ? "Reward details unavailable" : string.Join(", ", rewardNames),
                    Progress = "Awarded for " + RankRange(top, bottom) + " after the event ends"
                });
            }

            if (result.Count == 0)
            {
                result.Add(new EventItem
                {
                    Key = "no-rewards",
                    State = "unavailable",
                    Name = "No event rewards are available",
                    Description = "The current event did not provide milestone or leaderboard reward data",
                    Progress = "No claim action is available"
                });
            }
            return result;
        }

        private static string ClaimMilestone(string milestoneId)
        {
            string reason;
            if (!ValidateSession(out reason)) return InvalidateSession(reason, false);
            var service = Member(GameController(), "PlanetMilestoneService");
            var milestone = Enumerate(Invoke(service, "GetUserMilestonesForCurrentPlanet"))
                .FirstOrDefault(candidate => string.Equals(ObjectText(candidate, "MilestoneId"), milestoneId, StringComparison.Ordinal));
            if (milestone == null) return "This milestone was replaced before it could be claimed";
            if (!string.Equals(ReactiveText(Member(milestone, "State")), "COMPLETE", StringComparison.OrdinalIgnoreCase))
                return "This milestone is no longer ready to claim";
            var observable = Invoke(service, "ClaimMilestoneRewardsForCurrentPlanet", milestoneId);
            if (observable == null) return "The game did not provide a milestone claim operation";
            var eventId = _eventId;
            var generation = _sessionGeneration;
            string subscribeError;
            var subscribed = SubscribeObservable(observable,
                delegate(object value)
                {
                    MainThreadQueue.Post(delegate
                    {
                        if (!IsCurrentSession(eventId, generation)) return;
                        var names = Enumerate(value).Select(RewardDataText).Where(text => !string.IsNullOrEmpty(text)).ToArray();
                        Speech.Write(names.Length == 0 ? "Milestone reward claimed" : "Milestone reward claimed. " + string.Join(", ", names));
                    });
                },
                delegate(Exception error)
                {
                    MainThreadQueue.Post(delegate
                    {
                        if (IsCurrentSession(eventId, generation)) Speech.Write("Milestone reward was not claimed. " + Clean(error.Message));
                    });
                }, null, out subscribeError);
            if (!subscribed) return "Milestone reward was not claimed. " + subscribeError;
            return "Claiming milestone reward";
        }

        private static List<EventItem> LeaderboardItems()
        {
            var result = new List<EventItem>();
            var model = CurrentEventModel();
            if (!BoolMember(model, "HasLeaderboard"))
            {
                result.Add(new EventItem
                {
                    Key = "no-leaderboard",
                    State = "unavailable",
                    Name = "This event has no leaderboard",
                    Description = "Details, goals, and milestone rewards remain available",
                    Progress = "No rank data or leaderboard reward tiers are available"
                });
                return result;
            }

            EnsureLeaderboardRequested(false);
            List<LeaderboardEntry> nearby;
            List<LeaderboardEntry> top;
            string state;
            string error;
            lock (LeaderboardLock)
            {
                nearby = NearbyLeaderboard.ToList();
                top = TopLeaderboard.ToList();
                state = _leaderboardState;
                error = _leaderboardError;
            }
            var rank = CurrentPlayerRank();
            var enrolled = IsEnrolledInEvent();
            result.Add(new EventItem
            {
                Key = "leaderboard-summary",
                State = state,
                Name = rank > 0 ? "Your leaderboard rank is " + rank : enrolled ? "Your current rank is not available" : "You are not ranked yet",
                Description = enrolled
                    ? "Current event score " + FormatNumber(CurrentEventScore()) + ". " + state
                    : LeaderboardEntryInstruction(),
                Progress = string.IsNullOrEmpty(error) ? nearby.Count + " nearby entries and " + top.Count + " top entries loaded" : "Leaderboard notice. " + error,
                ActionName = "refresh leaderboard",
                Action = delegate { return RefreshLeaderboard(false); }
            });

            foreach (var entry in nearby)
            {
                result.Add(new EventItem
                {
                    Key = "nearby:" + entry.Rank + ":" + (entry.IsPlayer ? "me" : entry.Name),
                    State = entry.IsPlayer ? "you" : "nearby player",
                    Name = (entry.IsPlayer ? "You, " : "") + entry.Name + ", rank " + entry.Rank,
                    Description = "Nearby leaderboard entry",
                    Progress = "Event score " + FormatNumber(entry.Score)
                });
            }
            foreach (var entry in top)
            {
                result.Add(new EventItem
                {
                    Key = "top:" + entry.Rank + ":" + (entry.IsPlayer ? "me" : entry.Name),
                    State = entry.IsPlayer ? "you" : "top player",
                    Name = (entry.IsPlayer ? "You, " : "") + entry.Name + ", rank " + entry.Rank,
                    Description = "Top leaderboard entry",
                    Progress = "Event score " + FormatNumber(entry.Score)
                });
            }
            return result;
        }

        public static string RefreshLeaderboard(bool announce)
        {
            if (!EnsureOpen(announce)) return "Event interface unavailable";
            var model = CurrentEventModel();
            if (!BoolMember(model, "HasLeaderboard")) return Speak("This event has no leaderboard", announce);
            var controller = GameController();
            var service = Member(controller, "LeaderboardService");
            var eventData = CurrentEventData();
            var leaderboardType = Member(eventData, "leaderboardType") ?? Member(model, "LeaderboardType");
            if (service == null || leaderboardType == null) return Speak("Leaderboard service is unavailable", announce);

            var eventId = _eventId;
            var sessionGeneration = _sessionGeneration;
            var requestGeneration = ++_leaderboardRequestGeneration;
            var enrolled = IsEnrolledInEvent();
            lock (LeaderboardLock)
            {
                NearbyLeaderboard.Clear();
                TopLeaderboard.Clear();
                _leaderboardEventId = eventId;
                _leaderboardState = "loading leaderboard";
                _leaderboardError = null;
                _leaderboardRequestsPending = enrolled ? 2 : 1;
            }

            StartLeaderboardRequest(Invoke(service, "GetLeaderboardTop100", eventId, leaderboardType), false,
                eventId, sessionGeneration, requestGeneration);
            if (enrolled)
            {
                StartLeaderboardRequest(Invoke(service, "GetLeaderboardAroundPlayer", eventId, leaderboardType, 25), true,
                    eventId, sessionGeneration, requestGeneration);
            }
            var message = enrolled ? "Refreshing top and nearby leaderboard entries" : "Refreshing the top leaderboard. You will receive a personal rank after entering the event leaderboard";
            return Speak(message, announce);
        }

        private static void EnsureLeaderboardRequested(bool announce)
        {
            var id = CurrentEventId();
            lock (LeaderboardLock)
            {
                if (string.Equals(_leaderboardEventId, id, StringComparison.Ordinal) &&
                    !string.Equals(_leaderboardState, "not loaded", StringComparison.Ordinal)) return;
            }
            RefreshLeaderboard(announce);
        }

        private static void StartLeaderboardRequest(object observable, bool nearby, string eventId, int sessionGeneration, int requestGeneration)
        {
            if (observable == null)
            {
                QueueLeaderboardFinished(eventId, sessionGeneration, requestGeneration, "Leaderboard request was unavailable");
                return;
            }
            string subscribeError;
            if (!SubscribeObservable(observable,
                delegate(object value) { QueueLeaderboardResult(value, nearby, eventId, sessionGeneration, requestGeneration); },
                delegate(Exception error) { QueueLeaderboardFinished(eventId, sessionGeneration, requestGeneration, Clean(error.Message)); },
                delegate { QueueLeaderboardFinished(eventId, sessionGeneration, requestGeneration, null); }, out subscribeError))
                QueueLeaderboardFinished(eventId, sessionGeneration, requestGeneration, subscribeError);
        }

        private static void QueueLeaderboardResult(object value, bool nearby, string eventId, int sessionGeneration, int requestGeneration)
        {
            MainThreadQueue.Post(delegate
            {
                if (!IsCurrentLeaderboardRequest(eventId, sessionGeneration, requestGeneration)) return;
                var entries = ParseLeaderboardEntries(value);
                lock (LeaderboardLock)
                {
                    var target = nearby ? NearbyLeaderboard : TopLeaderboard;
                    target.Clear();
                    target.AddRange(entries);
                }
            });
        }

        private static void QueueLeaderboardFinished(string eventId, int sessionGeneration, int requestGeneration, string error)
        {
            MainThreadQueue.Post(delegate
            {
                if (!IsCurrentLeaderboardRequest(eventId, sessionGeneration, requestGeneration)) return;
                var announce = false;
                lock (LeaderboardLock)
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        _leaderboardError = string.IsNullOrEmpty(_leaderboardError) ? error : _leaderboardError + ". " + error;
                    }
                    if (_leaderboardRequestsPending > 0) _leaderboardRequestsPending--;
                    if (_leaderboardRequestsPending == 0)
                    {
                        _leaderboardState = string.IsNullOrEmpty(_leaderboardError) ? "leaderboard loaded" : "leaderboard loaded with a network notice";
                        announce = _active && _tabIndex == 3;
                    }
                }
                if (announce) Speech.Write("Leaderboard updated. " + LeaderboardItems()[0].Name);
            });
        }

        private static List<LeaderboardEntry> ParseLeaderboardEntries(object value)
        {
            var result = new List<LeaderboardEntry>();
            foreach (var item in Enumerate(value))
            {
                var rank = IntMember(item, "position");
                if (rank <= 0) continue;
                var name = Clean(ObjectText(item, "name"));
                if (string.IsNullOrEmpty(name)) name = "Anonymous player";
                else
                {
                    try { name = Clean(Uri.UnescapeDataString(name.Replace("+", " "))); } catch { }
                    if (string.IsNullOrEmpty(name)) name = "Anonymous player";
                }
                result.Add(new LeaderboardEntry
                {
                    Name = name,
                    Rank = rank,
                    Score = LeaderboardDisplayScore(DoubleMember(item, "val")),
                    IsPlayer = BoolMember(item, "me")
                });
            }
            return result.OrderBy(entry => entry.Rank <= 0 ? int.MaxValue : entry.Rank).ToList();
        }

        private static bool IsCurrentLeaderboardRequest(string eventId, int sessionGeneration, int requestGeneration)
        {
            return IsCurrentSession(eventId, sessionGeneration) && requestGeneration == _leaderboardRequestGeneration;
        }

        private static bool IsCurrentSession(string eventId, int generation)
        {
            return generation == _sessionGeneration && string.Equals(eventId, _eventId, StringComparison.Ordinal);
        }

        private static bool SubscribeObservable(object observable, Action<object> next, Action<Exception> error, Action completed, out string failure)
        {
            failure = null;
            try
            {
                var observableInterface = observable.GetType().GetInterfaces().FirstOrDefault(candidate =>
                    candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IObservable<>));
                if (observableInterface == null && observable.GetType().IsGenericType &&
                    observable.GetType().GetGenericTypeDefinition() == typeof(IObservable<>)) observableInterface = observable.GetType();
                if (observableInterface == null)
                {
                    failure = "The game returned an unsupported asynchronous operation";
                    return false;
                }
                var valueType = observableInterface.GetGenericArguments()[0];
                var observerType = typeof(ObserverProxy<>).MakeGenericType(valueType);
                var observer = Activator.CreateInstance(observerType, next, error, completed);
                var subscription = observableInterface.GetMethod("Subscribe").Invoke(observable, new[] { observer }) as IDisposable;
                if (subscription != null)
                {
                    lock (LeaderboardLock)
                    {
                        Subscriptions.Add(subscription);
                        while (Subscriptions.Count > 32)
                        {
                            try { Subscriptions[0].Dispose(); } catch { }
                            Subscriptions.RemoveAt(0);
                        }
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                failure = Clean(exception.GetBaseException().Message);
                return false;
            }
        }

        public static string DumpState()
        {
            if (!_active)
            {
                object controller;
                object model;
                string eventId;
                string reason;
                var available = TryGetActiveEvent(out controller, out model, out eventId, out reason);
                return "active=false\neventAvailable=" + available.ToString().ToLowerInvariant() +
                    "\neventId=" + (available ? eventId : "") + "\nreason=" + (available ? "event interface is closed" : Clean(reason));
            }
            var items = CurrentItems();
            ClampItemIndex(items);
            var item = items.Count == 0 ? null : items[ItemIndices[_tabIndex]];
            var values = new List<string>
            {
                "active=true",
                "eventId=" + _eventId,
                "tab=" + Tabs[_tabIndex],
                "tabIndex=" + _tabIndex,
                "itemIndex=" + (items.Count == 0 ? -1 : ItemIndices[_tabIndex]),
                "itemCount=" + items.Count,
                "rowIndex=" + RowIndices[_tabIndex]
            };
            if (item != null)
            {
                values.Add("itemKey=" + Clean(item.Key));
                values.Add("state=" + Clean(item.State));
                values.Add("name=" + Clean(item.Name));
                values.Add("description=" + Clean(item.Description));
                values.Add("progress=" + Clean(item.Progress));
                values.Add("action=" + Clean(item.ActionName));
            }
            return string.Join("\n", values.ToArray());
        }

        public static string EventDialogKey(Transform root)
        {
            var typeName = EventDialogType(root);
            return string.IsNullOrEmpty(typeName) || root == null ? null : root.GetInstanceID() + ":" + typeName;
        }

        public static string EventDialogAnnouncement(Transform root)
        {
            var typeName = EventDialogType(root);
            if (string.IsNullOrEmpty(typeName)) return null;
            var title = EventTitle(CurrentEventModel());
            var remaining = EventTime(CurrentEventModel());
            var timing = string.IsNullOrEmpty(remaining) ? "" : ". " + remaining + " remaining";
            if (typeName == "EventIntroModal") return EventIntroAnnouncement();
            if (typeName == "EventMissionsModal" || typeName == "PlanetMilestoneRewardPanel")
                return title + " event goals and milestone rewards dialog" + timing + ". Press E for the accessible Details, Goals, Rewards, and Leaderboard interface";
            if (typeName == "LeaderboardModalController")
                return title + " event leaderboard dialog" + timing + ". Press E for the accessible event interface";
            if (typeName == "LeaderboardRewardModal")
                return title + " final leaderboard reward dialog. Use the available claim button to collect the displayed reward";
            if (typeName == "LeaderboardRewardInfoModal")
                return title + " leaderboard reward information dialog. Use the available controls, then close this dialog to return to the event interface";
            var promotionText = root.GetComponentsInChildren<Text>(true).Where(text => text != null && IsPresent(text))
                .Select(text => Clean(text.text)).Where(text => !string.IsNullOrEmpty(text)).Distinct().ToArray();
            var promotion = "Event promotion dialog" + (promotionText.Length == 0 ? timing : ". " + string.Join(". ", promotionText));
            if (promotion.Length > 1600) promotion = promotion.Substring(0, 1600);
            return promotion + ". Press E after entering the event planet to open the accessible event interface";
        }

        private static string EventDialogType(Transform root)
        {
            if (root == null) return null;
            var priorities = new[]
            {
                "EventIntroModal", "LeaderboardRewardModal", "LeaderboardRewardInfoModal",
                "LeaderboardModalController", "EventMissionsModal", "PlanetMilestoneRewardPanel", "EventPromoModal"
            };
            var names = new HashSet<string>(root.GetComponentsInChildren<Component>(true)
                .Where(component => component != null).Select(component => component.GetType().Name), StringComparer.Ordinal);
            return priorities.FirstOrDefault(names.Contains);
        }

        private static bool HasBlockingOverlay()
        {
            try
            {
                var buttons = Resources.FindObjectsOfTypeAll<Button>().Where(button => button != null && IsPresent(button))
                    .Where(button => ModalAncestor(button.transform) != null).ToList();
                if (buttons.Count == 0) return false;
                var topOrder = buttons.Max(CanvasOrder);
                var top = buttons.FirstOrDefault(button => CanvasOrder(button) == topOrder);
                var root = top == null ? null : ModalAncestor(top.transform);
                if (root == null) return true;
                var type = EventDialogType(root);
                return type != "EventIntroModal" && type != "EventMissionsModal" &&
                    type != "PlanetMilestoneRewardPanel" && type != "LeaderboardModalController";
            }
            catch { return true; }
        }

        private static bool HasBlockingEntryOverlay()
        {
            try
            {
                var controls = Resources.FindObjectsOfTypeAll<Selectable>()
                    .Where(control => control != null && IsPresent(control) && CanvasOrder(control) >= 20).ToList();
                if (controls.Count == 0) return false;
                return true;
            }
            catch { return true; }
        }

        private static bool NativeEventButtonEligible(Component quickButtons)
        {
            foreach (var pair in Enumerate(Member(quickButtons, "buttonVisibility")))
            {
                if (!string.Equals(ObjectText(pair, "Key"), "GoToEvent", StringComparison.Ordinal)) continue;
                return BoolMember(Member(pair, "Value"), "Visible");
            }
            return false;
        }

        private static Transform ModalAncestor(Transform transform)
        {
            while (transform != null)
            {
                if (transform.GetComponents<Component>().Any(component => component != null &&
                    (component.GetType().Name.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     component.GetType().Name.EndsWith("Modal", StringComparison.OrdinalIgnoreCase) ||
                     component.GetType().Name.IndexOf("Celebration", StringComparison.OrdinalIgnoreCase) >= 0))) return transform;
                transform = transform.parent;
            }
            return null;
        }

        private static int CanvasOrder(Component component)
        {
            var canvas = component == null ? null : component.GetComponentInParent<Canvas>();
            return canvas == null ? 0 : canvas.sortingOrder;
        }

        private static bool ValidateSession(out string reason)
        {
            object controller;
            object model;
            string eventId;
            if (!TryGetActiveEvent(out controller, out model, out eventId, out reason)) return false;
            if (!_active || string.IsNullOrEmpty(_eventId) || !string.Equals(eventId, _eventId, StringComparison.Ordinal))
            {
                reason = "The event changed, so the requested action was cancelled";
                return false;
            }
            return true;
        }

        private static bool TryGetEnterableEvent(out object controller, out object model, out string eventId, out string reason)
        {
            controller = GameController();
            model = null;
            eventId = null;
            reason = null;
            if (controller == null)
            {
                reason = "The game controller is unavailable";
                return false;
            }
            if (Member(controller, "IsLoadingPlanet") == null || BoolMember(controller, "IsLoadingPlanet"))
            {
                reason = "A planet is still loading";
                return false;
            }
            var state = Member(controller, "game");
            if (state == null)
            {
                reason = "The current planet is unavailable";
                return false;
            }
            if (BoolMember(state, "IsEventPlanet"))
            {
                reason = "The event planet is already loaded. Press E to open its accessible interface";
                return false;
            }

            var eventService = Member(controller, "EventService");
            if (eventService == null || !BoolMember(eventService, "EventUnlocked"))
            {
                reason = "Event travel is not unlocked";
                return false;
            }
            var dataService = Member(controller, "EventDataService");
            if (dataService == null || !BoolMember(dataService, "WasEventScheduleDownloadedSuccessfully"))
            {
                reason = "The event schedule is unavailable. Check the internet connection and game version";
                return false;
            }
            var catalog = Member(dataService, "CatalogEntry");
            if (catalog == null)
            {
                reason = "The event catalog is unavailable";
                return false;
            }
            var compatible = InvokeStatic(FindType("LoadScene"), "MeetsVersionRequirement", ObjectText(catalog, "MinClientVersion"));
            if (!(compatible is bool) || !(bool)compatible)
            {
                reason = "The game must be updated before entering this event";
                return false;
            }

            model = Enumerate(Member(eventService, "ActiveEvents")).FirstOrDefault();
            if (model == null)
            {
                reason = "No event is currently active";
                return false;
            }
            eventId = ObjectText(model, "Id");
            if (string.IsNullOrEmpty(eventId) ||
                !string.Equals(ReactiveText(Member(model, "State")), "current", StringComparison.OrdinalIgnoreCase))
            {
                reason = "No event is currently active";
                return false;
            }
            var endValue = Member(model, "EndDate");
            var nowValue = Member(Member(controller, "DateTimeService"), "UtcNow");
            if (!(endValue is DateTime) || !(nowValue is DateTime))
            {
                reason = "The event expiration time could not be verified";
                return false;
            }
            if ((DateTime)endValue <= (DateTime)nowValue)
            {
                reason = "The event has ended";
                return false;
            }
            return true;
        }

        private static int EnterableEventCount(object controller)
        {
            var eventService = Member(controller, "EventService");
            var nowValue = Member(Member(controller, "DateTimeService"), "UtcNow");
            if (!(nowValue is DateTime)) return 0;
            var now = (DateTime)nowValue;
            return Enumerate(Member(eventService, "ActiveEvents")).Count(candidate =>
            {
                var endValue = Member(candidate, "EndDate");
                return !string.IsNullOrEmpty(ObjectText(candidate, "Id")) &&
                    string.Equals(ReactiveText(Member(candidate, "State")), "current", StringComparison.OrdinalIgnoreCase) &&
                    endValue is DateTime && (DateTime)endValue > now;
            });
        }

        private static string NoActiveEventMessage()
        {
            return "No event is currently active";
        }

        public static string EventTimingMessage()
        {
            var controller = GameController();
            if (controller == null) return "Event timing is unavailable";
            var eventService = Member(controller, "EventService");
            var nowValue = Member(Member(controller, "DateTimeService"), "UtcNow");
            if (eventService == null || !(nowValue is DateTime))
                return "Event timing is unavailable";

            var now = (DateTime)nowValue;
            var active = Enumerate(Member(eventService, "ActiveEvents"))
                .Where(candidate =>
                {
                    var endValue = Member(candidate, "EndDate");
                    return string.Equals(ReactiveText(Member(candidate, "State")), "current", StringComparison.OrdinalIgnoreCase) &&
                        endValue is DateTime && (DateTime)endValue > now;
                })
                .OrderBy(candidate => (DateTime)Member(candidate, "EndDate"))
                .FirstOrDefault();
            if (active != null)
            {
                var end = (DateTime)Member(active, "EndDate");
                return "The event ends in " + FormatDuration((end - now).TotalSeconds);
            }

            var next = Enumerate(Member(eventService, "FutureEvents"))
                .Where(candidate => Member(candidate, "StartDate") is DateTime && (DateTime)Member(candidate, "StartDate") > now)
                .OrderBy(candidate => (DateTime)Member(candidate, "StartDate"))
                .FirstOrDefault();
            if (next == null)
                return "No event is currently active. The next event start time is unavailable";

            var start = (DateTime)Member(next, "StartDate");
            return "The next event starts in " + FormatEventStartDuration(start - now);
        }

        private static string FormatEventStartDuration(TimeSpan remaining)
        {
            // Round up because this announcement intentionally omits seconds. That
            // avoids saying "0 minutes" while an event is still about to start.
            var totalMinutes = Math.Max(0L, (long)Math.Ceiling(remaining.TotalMinutes));
            var weeks = totalMinutes / (7L * 24L * 60L);
            totalMinutes %= 7L * 24L * 60L;
            var days = totalMinutes / (24L * 60L);
            totalMinutes %= 24L * 60L;
            var hours = totalMinutes / 60L;
            var minutes = totalMinutes % 60L;
            var parts = new List<string>();
            if (weeks > 0) parts.Add(weeks + (weeks == 1 ? " week" : " weeks"));
            if (days > 0) parts.Add(days + (days == 1 ? " day" : " days"));
            if (hours > 0) parts.Add(hours + (hours == 1 ? " hour" : " hours"));
            if (minutes > 0 || parts.Count == 0) parts.Add(minutes + (minutes == 1 ? " minute" : " minutes"));
            return string.Join(", ", parts.ToArray());
        }

        private static bool TryGetActiveEvent(out object controller, out object model, out string eventId, out string reason)
        {
            controller = GameController();
            model = null;
            eventId = null;
            reason = null;
            if (controller == null)
            {
                reason = "The game controller is unavailable";
                return false;
            }
            if (Member(controller, "IsLoadingPlanet") == null || BoolMember(controller, "IsLoadingPlanet"))
            {
                reason = "The event planet is still loading";
                return false;
            }
            var state = Member(controller, "game");
            if (state == null || !BoolMember(state, "IsEventPlanet"))
            {
                reason = "The event interface is available only while an active event planet is loaded";
                return false;
            }
            eventId = ObjectText(state, "planetName");
            if (string.IsNullOrEmpty(eventId))
            {
                reason = "The active event could not be identified";
                return false;
            }
            var eventService = Member(controller, "EventService");
            var loadedEventId = eventId;
            model = Enumerate(Member(eventService, "ActiveEvents")).FirstOrDefault(candidate =>
                string.Equals(ObjectText(candidate, "Id"), loadedEventId, StringComparison.Ordinal));
            if (model == null || !string.Equals(ReactiveText(Member(model, "State")), "current", StringComparison.OrdinalIgnoreCase))
            {
                reason = "The loaded event is no longer active";
                return false;
            }
            var endValue = Member(model, "EndDate");
            var nowValue = Member(Member(controller, "DateTimeService"), "UtcNow");
            if (!(endValue is DateTime) || !(nowValue is DateTime))
            {
                reason = "The event expiration time could not be verified";
                return false;
            }
            if ((DateTime)endValue <= (DateTime)nowValue)
            {
                reason = "The event has ended";
                return false;
            }
            return true;
        }

        private static object CurrentEventModel()
        {
            object controller;
            object model;
            string eventId;
            string reason;
            return TryGetActiveEvent(out controller, out model, out eventId, out reason) ? model : null;
        }

        private static object CurrentEventData()
        {
            var id = CurrentEventId();
            return Enumerate(Member(Member(GameController(), "EventDataService"), "EventDataList"))
                .FirstOrDefault(candidate => string.Equals(ObjectText(candidate, "id"), id, StringComparison.Ordinal));
        }

        private static List<object> RewardTiers()
        {
            var service = Member(GameController(), "EventDataService");
            return Enumerate(Invoke(service, "GetLeaderBoardTiersForEvent", CurrentEventId()));
        }

        private static object EventMissionService()
        {
            return Member(GameController(), "EventMissionsService");
        }

        private static object GameState()
        {
            return Member(GameController(), "game");
        }

        private static object GameController()
        {
            return StaticMember(FindType("GameController"), "Instance");
        }

        private static string CurrentEventId()
        {
            return ObjectText(GameState(), "planetName");
        }

        private static string EventTitle(object model)
        {
            var title = Clean(ObjectText(model, "Name"));
            if (string.IsNullOrEmpty(title)) title = Clean(ObjectText(GameState(), "planetTitle"));
            if (string.IsNullOrEmpty(title)) title = Clean(ObjectText(Member(GameState(), "PlanetData"), "DisplayName"));
            return string.IsNullOrEmpty(title) ? "Current" : title;
        }

        private static string EventTitleForModel(object model)
        {
            var title = Clean(ObjectText(model, "Name"));
            return string.IsNullOrEmpty(title) ? "current" : title;
        }

        private static string EventTime(object model)
        {
            var endValue = Member(model, "EndDate");
            var nowValue = Member(Member(GameController(), "DateTimeService"), "UtcNow");
            if (endValue is DateTime && nowValue is DateTime)
                return FormatDuration(Math.Max(0d, ((DateTime)endValue - (DateTime)nowValue).TotalSeconds));
            var seconds = DoubleReactive(Member(model, "TimeRemaining"));
            return double.IsNaN(seconds) ? "" : FormatDuration(Math.Max(0d, seconds));
        }

        private static double CurrentEventScore()
        {
            var progression = ObjectText(GameState(), "progressionType");
            if (string.Equals(progression, "Missions", StringComparison.OrdinalIgnoreCase))
                return DoubleReactive(Member(EventMissionService(), "CurentScore"));
            if (string.Equals(progression, "Angels", StringComparison.OrdinalIgnoreCase))
                return DoubleReactive(Member(Member(GameController(), "PlanetMilestoneService"), "CurrentScore"));
            return double.NaN;
        }

        private static int CurrentPlayerRank()
        {
            var history = Member(Member(GameController(), "LeaderboardService"), "HistoricEventData");
            foreach (var pair in Enumerate(history))
            {
                if (!string.Equals(ObjectText(pair, "Key"), CurrentEventId(), StringComparison.Ordinal)) continue;
                return (int)Math.Round(DoubleMember(Member(pair, "Value"), "leaderboardRank"));
            }
            return 0;
        }

        private static bool IsEnrolledInEvent()
        {
            var value = Invoke(Member(GameController(), "EventService"), "HasPlayerPerformedEventReset", CurrentEventId());
            return value is bool && (bool)value;
        }

        private static double LeaderboardDisplayScore(double score)
        {
            var progression = ObjectText(GameState(), "progressionType");
            if (string.Equals(progression, "Missions", StringComparison.OrdinalIgnoreCase)) return score;
            if (!string.Equals(progression, "Angels", StringComparison.OrdinalIgnoreCase)) return score;
            var decoded = InvokeStatic(FindType("GameState"), "GetValueFromLeaderboardScore", (int)Math.Round(score));
            return decoded == null ? score : ConvertDouble(decoded);
        }

        private static string LeaderboardEntryInstruction()
        {
            var progression = ObjectText(GameState(), "progressionType");
            if (string.Equals(progression, "Missions", StringComparison.OrdinalIgnoreCase))
                return "Claim event goal points before the event ends to enter the leaderboard";
            if (string.Equals(progression, "Angels", StringComparison.OrdinalIgnoreCase))
                return "Claim Angels before the event ends to enter the leaderboard";
            return "Earn event score before the event ends to enter the leaderboard";
        }

        private static string MilestoneRewardText(object configuredMilestone, string milestoneState)
        {
            var rewards = Enumerate(Member(configuredMilestone, "Rewards"));
            if (rewards.Count == 0) return "details unavailable";
            var values = new List<string>();
            foreach (var reward in rewards)
            {
                if (BoolMember(reward, "isHidden") && string.Equals(milestoneState, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    values.Add("mystery reward");
                else values.Add(RewardDataText(reward));
            }
            return string.Join(", ", values.Where(value => !string.IsNullOrEmpty(value)).ToArray());
        }

        private static string RewardDataText(object reward)
        {
            var id = ObjectText(reward, "Id");
            var type = ObjectText(reward, "RewardType");
            var name = RewardName(id, type);
            var quantity = IntMember(reward, "Qty");
            return (quantity > 1 ? FormatNumber(quantity) + " " : "") + name;
        }

        private static string LeaderboardTierRewardText(object reward)
        {
            var id = ObjectText(reward, "rewardId");
            var quantity = IntMember(reward, "qty");
            var name = RewardName(id, "reward");
            return (quantity > 1 ? FormatNumber(quantity) + " " : "") + name;
        }

        private static string RewardName(string id, string fallback)
        {
            var inventory = Member(Member(GameController(), "GlobalPlayerData"), "inventory");
            var item = string.IsNullOrEmpty(id) ? null : Invoke(inventory, "GetItemById", id);
            var name = Clean(ObjectText(item, "ItemName"));
            if (!string.IsNullOrEmpty(name)) return name;
            name = Clean(id);
            if (!string.IsNullOrEmpty(name)) return name;
            name = Clean(fallback);
            return string.IsNullOrEmpty(name) ? "reward" : name;
        }

        private static string RankRange(int top, int bottom)
        {
            if (top <= 0 && bottom <= 0) return "an unspecified rank range";
            if (top == bottom || bottom <= 0) return "rank " + Math.Max(top, bottom);
            return "ranks " + top + " through " + bottom;
        }

        private static Type FindType(string name)
        {
            lock (TypeCacheLock)
            {
                Type cached;
                if (TypeCache.TryGetValue(name, out cached)) return cached;
            }
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try { type = assembly.GetType(name, false); } catch { }
                if (type != null)
                {
                    lock (TypeCacheLock) TypeCache[name] = type;
                    return type;
                }
                try { type = assembly.GetTypes().FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal)); } catch { }
                if (type != null)
                {
                    lock (TypeCacheLock) TypeCache[name] = type;
                    return type;
                }
            }
            return null;
        }

        private static object StaticMember(Type type, string name)
        {
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            try
            {
                var property = type.GetProperty(name, flags);
                if (property != null) return property.GetValue(null, null);
                var field = type.GetField(name, flags);
                return field == null ? null : field.GetValue(null);
            }
            catch { return null; }
        }

        private static object Member(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name)) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            var type = target.GetType();
            while (type != null)
            {
                try
                {
                    var property = type.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                    if (property != null) return property.GetValue(target, null);
                    var field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
                    if (field != null) return field.GetValue(target);
                }
                catch { return null; }
                type = type.BaseType;
            }
            return null;
        }

        private static object Invoke(object target, string name, params object[] arguments)
        {
            if (target == null) return null;
            return InvokeMethods(target.GetType(), target, name, arguments, false);
        }

        private static object InvokeStatic(Type type, string name, params object[] arguments)
        {
            return type == null ? null : InvokeMethods(type, null, name, arguments, true);
        }

        private static object InvokeMethods(Type type, object target, string name, object[] arguments, bool isStatic)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            var methods = type.GetMethods(flags).Where(method => string.Equals(method.Name, name, StringComparison.Ordinal) && !method.IsGenericMethodDefinition);
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (arguments.Length > parameters.Length || parameters.Take(arguments.Length).Where((parameter, index) => !ArgumentMatches(parameter.ParameterType, arguments[index])).Any()) continue;
                if (parameters.Skip(arguments.Length).Any(parameter => !parameter.IsOptional)) continue;
                var invokeArguments = new object[parameters.Length];
                for (var i = 0; i < parameters.Length; i++) invokeArguments[i] = i < arguments.Length ? arguments[i] : Type.Missing;
                try { return method.Invoke(target, invokeArguments); }
                catch (TargetInvocationException exception)
                {
                    if (exception.InnerException != null) throw exception.InnerException;
                    throw;
                }
            }
            return null;
        }

        private static bool ArgumentMatches(Type parameterType, object argument)
        {
            if (argument == null) return !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null;
            return parameterType.IsInstanceOfType(argument) || parameterType.IsAssignableFrom(argument.GetType());
        }

        private static List<object> Enumerate(object value)
        {
            var result = new List<object>();
            if (value == null || value is string) return result;
            var enumerable = value as IEnumerable;
            if (enumerable == null) return result;
            try { foreach (var item in enumerable) if (item != null) result.Add(item); } catch { }
            return result;
        }

        private static Component ActiveComponent(string typeName)
        {
            try
            {
                return Resources.FindObjectsOfTypeAll<Component>().FirstOrDefault(component => component != null &&
                    string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal) && IsPresent(component));
            }
            catch { return null; }
        }

        private static bool IsPresent(Component component)
        {
            if (component == null || component.gameObject == null || !component.gameObject.activeInHierarchy) return false;
            var canvasGroup = component.GetComponentInParent<CanvasGroup>();
            return canvasGroup == null || canvasGroup.alpha > 0.01f;
        }

        private static string ObjectText(object target, string name)
        {
            var value = Member(target, name);
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string ReactiveText(object value)
        {
            if (value == null) return "";
            var inner = Member(value, "Value");
            return Convert.ToString(inner ?? value, CultureInfo.InvariantCulture);
        }

        private static bool BoolMember(object target, string name)
        {
            var value = Member(target, name);
            var reactive = Member(value, "Value");
            if (reactive != null) value = reactive;
            try { return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture); } catch { return false; }
        }

        private static int IntMember(object target, string name)
        {
            var value = Member(target, name);
            var reactive = Member(value, "Value");
            if (reactive != null) value = reactive;
            try { return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        private static double DoubleMember(object target, string name)
        {
            return ConvertDouble(Member(target, name));
        }

        private static double DoubleReactive(object value)
        {
            if (value == null) return double.NaN;
            var inner = Member(value, "Value");
            return ConvertDouble(inner ?? value);
        }

        private static double ConvertDouble(object value)
        {
            try { return value == null ? double.NaN : Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return double.NaN; }
        }

        private static string FriendlyState(string state)
        {
            if (string.Equals(state, "COMPLETE", StringComparison.OrdinalIgnoreCase)) return "ready to claim";
            if (string.Equals(state, "CLAIMED", StringComparison.OrdinalIgnoreCase)) return "claimed";
            if (string.Equals(state, "EXPIRED", StringComparison.OrdinalIgnoreCase)) return "expired";
            if (string.Equals(state, "ACTIVE", StringComparison.OrdinalIgnoreCase)) return "in progress";
            return string.IsNullOrEmpty(state) ? "state unavailable" : Clean(state).ToLowerInvariant();
        }

        private static bool RowExists(EventItem item, int row)
        {
            if (item == null) return false;
            if (row == 0) return !string.IsNullOrEmpty(item.Name);
            if (row == 1) return !string.IsNullOrEmpty(item.Description);
            if (row == 2) return !string.IsNullOrEmpty(item.Progress);
            return row == 3 && item.Action != null && !string.IsNullOrEmpty(item.ActionName);
        }

        private static void ClampItemIndex(List<EventItem> items)
        {
            if (items == null || items.Count == 0) { ItemIndices[_tabIndex] = 0; RowIndices[_tabIndex] = 0; return; }
            ItemIndices[_tabIndex] = Math.Max(0, Math.Min(ItemIndices[_tabIndex], items.Count - 1));
        }

        private static string ItemDetails(EventItem item)
        {
            if (item == null) return "No item";
            var parts = new List<string> { item.Name, item.Description, item.Progress };
            if (item.Action != null) parts.Add(item.ActionName + " button");
            return string.Join(". ", parts.Where(value => !string.IsNullOrEmpty(value)).ToArray());
        }

        private static string Join(params string[] values)
        {
            return string.Join(". ", values.Select(Clean).Where(value => !string.IsNullOrEmpty(value)).Distinct().ToArray());
        }

        private static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "unavailable";
            var absolute = Math.Abs(value);
            if (absolute >= 1000000000000000d) return value.ToString("0.###E+0", CultureInfo.InvariantCulture);
            return value.ToString("#,0.##", CultureInfo.InvariantCulture);
        }

        private static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "time unavailable";
            seconds = Math.Max(0d, Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds));
            var span = TimeSpan.FromSeconds(Math.Floor(seconds));
            var parts = new List<string>();
            if (span.Days > 0) parts.Add(span.Days + (span.Days == 1 ? " day" : " days"));
            if (span.Hours > 0) parts.Add(span.Hours + (span.Hours == 1 ? " hour" : " hours"));
            if (span.Minutes > 0) parts.Add(span.Minutes + (span.Minutes == 1 ? " minute" : " minutes"));
            if (span.Seconds > 0 || parts.Count == 0) parts.Add(span.Seconds + (span.Seconds == 1 ? " second" : " seconds"));
            return string.Join(", ", parts.ToArray());
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = Regex.Replace(value, "<[^>]+>", "");
            value = value.Replace("_", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            value = Regex.Replace(value, "\\s+", " ");
            return Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
        }

        private static string Speak(string message, bool announce)
        {
            message = Clean(message);
            if (announce && !string.IsNullOrEmpty(message)) Speech.Write(message);
            return message;
        }
    }
}
