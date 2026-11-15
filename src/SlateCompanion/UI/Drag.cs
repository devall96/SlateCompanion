using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SlateCompanion.UI;

/// <summary>
/// Dragging things inside the app (an ingredient into the cooking pot, a weapon onto Link, an item onto a loadout
/// slot): press and move to drag, press and let go without moving to click. Drop targets light up while something
/// they take is over them.
/// </summary>
public static class Drag
{
    /// <summary>What's dragged: "kind:value" text, only inside this app.</summary>
    public static readonly DataFormat<string> Item = DataFormat.CreateInProcessFormat<string>("slatecompanion-item");

    /// <summary>Makes <paramref name="c"/> draggable with <paramref name="payload"/>; a click without moving calls <paramref name="click"/>.</summary>
    public static void Source(Control c, string payload, Action? click = null)
    {
        PointerPressedEventArgs? pressed = null;
        Point start = default;
        bool dragging = false;
        c.Cursor = new Cursor(StandardCursorType.Hand);
        c.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
            pressed = e; start = e.GetPosition(c); dragging = false;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        c.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
        {
            if (pressed == null || dragging) return;
            var p = e.GetPosition(c);
            if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) < 6) return;
            dragging = true;
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(Item, payload));
            var from = pressed;
            pressed = null;
            c.Opacity = 0.5;
            try { await DragDrop.DoDragDropAsync(from, data, DragDropEffects.Move | DragDropEffects.Copy); }
            catch (Exception ex) { Log.Warn("drag: " + ex.Message); }
            finally { c.Opacity = 1; dragging = false; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        c.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            // Only a source with a click of its own takes the release: marking it handled otherwise (this runs in the
            // tunnel phase, before the controls inside) kept buttons inside the card, like a pouch item's Equip, from
            // ever getting their click.
            if (pressed != null && !dragging && click != null) { pressed = null; click(); e.Handled = true; }
            pressed = null;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
    }

    /// <summary>Makes <paramref name="target"/> take drops it <paramref name="accepts"/>; <paramref name="drop"/> gets the payload.</summary>
    public static void Target(Border target, Func<string, bool> accepts, Action<string> drop)
    {
        DragDrop.SetAllowDrop(target, true);
        IBrush? before = null;
        bool lit = false;
        void Light(bool on)
        {
            if (on == lit) return;
            lit = on;
            if (on) { before = target.BorderBrush; target.BorderBrush = Ui.Brush("Orange"); }
            else target.BorderBrush = before;
        }
        static string? Payload(DragEventArgs e) => e.DataTransfer.TryGetValue(Item);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var p = Payload(e);
            bool ok = p != null && accepts(p);
            e.DragEffects = ok ? DragDropEffects.Move : DragDropEffects.None;
            Light(ok);
            e.Handled = true;
        });
        target.AddHandler(DragDrop.DragLeaveEvent, (_, _) => Light(false));
        target.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            Light(false);
            if (Payload(e) is { } p && accepts(p)) { drop(p); e.Handled = true; }
        });
    }
}
