using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AdCapUnityMCP;
using UnityEngine;

namespace UnityEngine
{
    public class Transform { public Transform parent; public Component component; }
    public class Component { public string name; public Transform transform = new Transform(); public Component() { transform.component = this; } }
}
class Reactive<T> { public T Value; public Reactive(T value) { Value = value; } }
class Venture { public string Name; public Reactive<double> TotalOwned; public Venture(string name, double n) { Name = name; TotalOwned = new Reactive<double>(n); } }
class State { public List<Venture> VentureModels = new List<Venture>(); }
class SingleVentureUnlock { public string ventureName; public int amountToEarn; public string reward; public string Bonus(State s) => reward; }
class EveryVentureUnlock { public int amountToEarn; public string Bonus(State s) => "Profits of everything x2"; }
class Row { public string Id; public List<object> Elements = new List<object>(); }
class Grid { public List<Row> Rows = new List<Row>(); }
class UnlocksPanel : Component { public State gameState = new State(); public Grid unlocksReactiveGrid = new Grid(); }
class UnlocksGridRowView : Component { public Component[] ElementViews; }
class UnlockItemView : Component { }

namespace AdCapUnityMCP
{
    internal static partial class AccessibilityNavigator
    {
        private class PanelItem { public string Name; public string Description; }
        private static Component ComponentAncestor(Transform t, string name) { while(t != null) { if(t.component != null && t.component.GetType().Name == name) return t.component; t=t.parent; } return null; }
        private static object ObjectMember(object x, string name) { if(x == null) return null; for(var t=x.GetType();t != null;t=t.BaseType) { var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly); if(f != null) return f.GetValue(x); var p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly); if(p != null) return p.GetValue(x); } return null; }
        private static string ObjectTextMember(object x,string n) => Clean(Convert.ToString(ObjectMember(x,n)));
        private static double ObjectDoubleMember(object x,string n) { var v=ObjectMember(x,n); if(v == null) return double.NaN; return Convert.ToDouble(ObjectMember(v,"Value") ?? v); }
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("_", " ").Replace("Btn ", "").Replace("btn ", "").Replace("\n", " ").Trim();
            s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ");
            return System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");
        }
        private static string TextNamed(Transform t,string n) => "MISLEADING REWARD";
        private static string InvokeTextMethod(object x,string n) => Convert.ToString(x.GetType().GetMethod(n)?.Invoke(x,null));
        public static string Read(Transform t) { var item=UnlockViewItem(t); return item.Name + "; " + item.Description; }
    }
}
class Program
{
    static int checks;
    static void Check(bool value,string name) { if(!value) throw new Exception(name); checks++; }
    static void Main()
    {
        var panel=new UnlocksPanel();
        panel.gameState.VentureModels.AddRange(new[] {new Venture("Pimp Thy Steed",416),new Venture("Tapestry Selfie Booth",295),new Venture("Serf Surfing",274),new Venture("Wyvernry",167),new Venture("Unicorn Jousting",173)});
        var view0=new UnlockItemView(); var view1=new UnlockItemView();
        var gridView=new UnlocksGridRowView {name="UnlocksGridRowView3",ElementViews=new Component[]{view0,view1}};
        gridView.transform.parent=panel.transform; view0.transform.parent=gridView.transform; view1.transform.parent=gridView.transform;
        panel.unlocksReactiveGrid.Rows.Add(new Row {Id="UnlocksGridRowView0",Elements=new List<object>{new SingleVentureUnlock{ventureName="Pimp Thy Steed",amountToEarn=1,reward="wrong row"}}});
        var row=new Row {Id=gridView.name}; panel.unlocksReactiveGrid.Rows.Add(row);
        row.Elements.Add(new SingleVentureUnlock {ventureName="Tapestry Selfie Booth",amountToEarn=400,reward="Pimp Thy Steed profits x6666"});
        row.Elements.Add(new SingleVentureUnlock {ventureName="Wyvernry",amountToEarn=250,reward="Serf Surfing profits x2222"});
        var text=AccessibilityNavigator.Read(view0.transform);
        Check(text.StartsWith("Tapestry Selfie Booth automatic unlock"),"Required business label");
        Check(text.Contains("Current 295 of 400 owned. 105 remaining"),"416 of 400 regression");
        Check(text.Contains("Reward: Pimp Thy Steed profits x6666"),"Cross-business reward retained");
        Check(AccessibilityNavigator.Read(view1.transform).Contains("Current 167 of 250 owned. 83 remaining"),"Correct element slot");
        row.Elements[0]=new SingleVentureUnlock{ventureName="Pimp Thy Steed",amountToEarn=777,reward="Profits of everything x2"};
        Check(AccessibilityNavigator.Read(view0.transform).Contains("Current 416 of 777 owned. 361 remaining"),"Recycled slot and all-business reward");
        row.Elements[0]=new EveryVentureUnlock {amountToEarn=333};
        text=AccessibilityNavigator.Read(view0.transform);
        Check(text.Contains("Own 333 of every business. 1 of 5 businesses meet the requirement. Lowest owned 167 of 333"),"Every-business requirement");
        panel.gameState.VentureModels.Add(new Venture("Unowned",0));
        Check(AccessibilityNavigator.Read(view0.transform).Contains("Lowest owned 0 of 333"),"Unowned businesses included");
        row.Elements[0]=new SingleVentureUnlock{ventureName="Unowned",amountToEarn=10,reward="Something else"};
        Check(AccessibilityNavigator.Read(view0.transform).Contains("Current 0 of 10 owned. 10 remaining"),"Zero ownership");
        panel.gameState.VentureModels.Add(new Venture("camelCase_Business",7));
        row.Elements[0]=new SingleVentureUnlock{ventureName="camelCase_Business",amountToEarn=10,reward="Everything"};
        Check(AccessibilityNavigator.Read(view0.transform).Contains("Current 7 of 10 owned. 3 remaining"),"Raw business identity survives speech formatting");
        row.Elements[0]=new SingleVentureUnlock{ventureName="Missing",amountToEarn=400,reward="Pimp Thy Steed"};
        Check(AccessibilityNavigator.Read(view0.transform).Contains("Requirement information unavailable"),"Missing business never guessed from reward");
        gridView.name="RecycledButNotBound";
        Check(AccessibilityNavigator.Read(view0.transform).Contains("Requirement information unavailable"),"Unbound row fails closed");
        gridView.name=row.Id; row.Elements.Clear();
        Check(AccessibilityNavigator.Read(view1.transform).Contains("Requirement information unavailable"),"Empty element slot");
        Console.WriteLine("PASS: " + checks + " unlock mapping and progress checks using the production helper");
    }
}
