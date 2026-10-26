using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Reflection;
using System.Runtime.Versioning;

[assembly:AssemblyTitle("轻剪贴")]
[assembly:AssemblyDescription("独立、本地的悬浮剪贴板")]
[assembly:AssemblyVersion("0.1.0.0")]
[assembly:TargetFramework(".NETFramework,Version=v4.8")]
namespace TinyClip {
  internal static class Program {
    internal static string Argument(string[] args,string name){int n=Array.IndexOf(args,name);return n>=0&&n+1<args.Length?args[n+1]:null;}
    [STAThread]public static int Main(string[] args){
      string data=Argument(args,"--data-dir")??Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data");
      bool created;string identity="Local\\TinyClip."+StorageIdentity(data);
      using(Mutex mutex=new Mutex(true,identity,out created)){
        uint showMessage=Native.RegisterWindowMessage(identity+".Show");
        if(!created){Native.PostMessage(new IntPtr(0xffff),showMessage,IntPtr.Zero,IntPtr.Zero);return 0;}
        try {
          Application app=new Application();app.ShutdownMode=ShutdownMode.OnMainWindowClose;
          MainWindow window=new MainWindow(new Storage(data),showMessage);
          app.DispatcherUnhandledException+=(s,e)=>{e.Handled=true;window.ReportError("操作未完成，请重试");};
          app.Run(window);return 0;
        }catch(Exception ex){MessageBox.Show("无法启动轻剪贴："+ex.Message,"轻剪贴",MessageBoxButton.OK,MessageBoxImage.Information);return 1;}
        finally{mutex.ReleaseMutex();}
      }
    }
    private static string StorageIdentity(string path){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()))).Replace("-","").Substring(0,20);}
  }
}
