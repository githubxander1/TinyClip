using System;
using System.Runtime.InteropServices;

namespace TinyClip {
  internal static class Native {
    public const int ClipboardUpdate=0x031D,HotKey=0x0312;
    [DllImport("user32.dll",SetLastError=true)]internal static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)]internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")]internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll",SetLastError=true)]internal static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]internal static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll",SetLastError=true)]internal static extern bool PostMessage(IntPtr hwnd,uint msg,IntPtr wparam,IntPtr lparam);
    [DllImport("user32.dll")]internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]internal static extern uint GetDpiForWindow(IntPtr hwnd);
  }
}
