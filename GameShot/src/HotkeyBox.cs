using System;
using System.Windows.Forms;

namespace GameShot
{
    internal sealed class HotkeyBox : TextBox
    {
        internal Hotkey Value;
        internal HotkeyBox(Hotkey value)
        {
            Value = new Hotkey((Keys)value.Key, value.Modifiers);
            Text = Value.ToString();
            ReadOnly = true;
            ShortcutsEnabled = false;
            AccessibleDescription = "聚焦后按下希望使用的快捷键，支持 Ctrl、Alt、Shift 组合。";
        }
        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) != Keys.Tab || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab) { base.OnKeyDown(e); return; }
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return;
            if (e.KeyCode == Keys.F12) { Text = "F12 是系统保留键，请换一个"; return; }
            if (e.KeyCode == Keys.Escape) { Text = Value.ToString(); return; }
            uint modifiers = (e.Control ? 2u : 0) | (e.Alt ? 1u : 0) | (e.Shift ? 4u : 0);
            Value = new Hotkey(e.KeyCode, modifiers);
            Text = Value.ToString();
        }
    }
}
