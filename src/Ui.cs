// ----------------------------------------------------------------------------
//  Utilidades de UI
// ----------------------------------------------------------------------------
static class Ui
{
    public static Button Btn(string text, string tip, Action action)
    {
        var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2) };
        b.Click += (s, e) => action();
        return b;
    }
}
