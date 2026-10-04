using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AdCapUnityMCP
{
    internal static partial class AccessibilityNavigator
    {
        // Resolve the same grid row and element slot that UnlocksPanel wires to this view.
        // Reward text is deliberately never used to select a requirement business.
        private static PanelItem UnlockViewItem(Transform view)
        {
            var gridView = ComponentAncestor(view, "UnlocksGridRowView");
            var panel = ComponentAncestor(view, "UnlocksPanel");
            var state = ObjectMember(panel, "gameState");
            var views = ObjectMember(gridView, "ElementViews") as Array;
            var grid = ObjectMember(panel, "unlocksReactiveGrid");
            var rows = ObjectMember(grid, "Rows") as IEnumerable;
            object unlock = null;
            if (gridView != null && views != null && rows != null)
            {
                int slot = -1;
                for (int i = 0; i < views.Length; i++)
                {
                    var element = views.GetValue(i) as Component;
                    if (element != null && element.transform == view) { slot = i; break; }
                }
                foreach (var row in rows)
                {
                    // Machine identifiers must bypass speech formatting (which splits camel case).
                    if (!string.Equals(Convert.ToString(ObjectMember(row, "Id")), gridView.name, StringComparison.Ordinal)) continue;
                    var elements = ObjectMember(row, "Elements") as IEnumerable;
                    if (elements != null && slot >= 0) unlock = elements.Cast<object>().ElementAtOrDefault(slot);
                    break;
                }
            }
            if (unlock == null)
                return new PanelItem { Name = "Automatic unlock", Description = "Requirement information unavailable. Reward: " + TextNamed(view, "Txt_Description") };

            var amount = ObjectDoubleMember(unlock, "amountToEarn");
            var ventures = ObjectMember(state, "VentureModels") as IEnumerable;
            var models = ventures == null ? new List<object>() : ventures.Cast<object>().Where(v => v != null).ToList();
            var requiredName = Convert.ToString(ObjectMember(unlock, "ventureName"));
            string name;
            string progress;
            if (unlock.GetType().Name == "EveryVentureUnlock")
            {
                name = "Every business automatic unlock";
                var owned = models.Select(v => ObjectDoubleMember(v, "TotalOwned")).ToList();
                progress = owned.Count == 0 || owned.Any(v => !ValidUnlockNumber(v)) || !ValidUnlockNumber(amount)
                    ? "Requirement information unavailable"
                    : "Own " + UnlockNumber(amount) + " of every business. " + owned.Count(v => v >= amount) + " of " + owned.Count + " businesses meet the requirement. Lowest owned " + UnlockNumber(owned.Min()) + " of " + UnlockNumber(amount);
            }
            else if (unlock.GetType().Name == "SingleVentureUnlock")
            {
                name = (string.IsNullOrEmpty(requiredName) ? "Business" : Clean(requiredName)) + " automatic unlock";
                var venture = models.FirstOrDefault(v => string.Equals(Convert.ToString(ObjectMember(v, "Name")), requiredName, StringComparison.Ordinal));
                var owned = ObjectDoubleMember(venture, "TotalOwned");
                progress = !ValidUnlockNumber(owned) || !ValidUnlockNumber(amount)
                    ? "Requirement information unavailable"
                    : "Current " + UnlockNumber(owned) + " of " + UnlockNumber(amount) + " owned. " + UnlockNumber(Math.Max(0, amount - owned)) + " remaining";
            }
            else
            {
                name = ObjectTextMember(unlock, "name") + " automatic unlock";
                progress = InvokeTextMethod(unlock, "GetDescription");
                if (string.IsNullOrEmpty(progress)) progress = "Requirement information unavailable";
            }
            var bonusMethod = unlock.GetType().GetMethod("Bonus");
            var reward = bonusMethod == null || state == null ? "" : Clean(Convert.ToString(bonusMethod.Invoke(unlock, new[] { state })));
            if (string.IsNullOrEmpty(reward)) reward = TextNamed(view, "Txt_Description");
            return new PanelItem
            {
                Name = name,
                Description = progress + ". Reward: " + reward + ". Applies automatically when reached"
            };
        }

        private static bool ValidUnlockNumber(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        }

        private static string UnlockNumber(double value)
        {
            return value.ToString("#,0.##", CultureInfo.InvariantCulture);
        }
    }
}
