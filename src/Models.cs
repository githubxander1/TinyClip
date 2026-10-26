using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TinyClip {
  public sealed class Clip {
    public string Id {get;set;} public string Kind {get;set;} public string Text {get;set;}
    public string Html {get;set;} public string Rtf {get;set;} public string[] Files {get;set;}
    [ScriptIgnore]public byte[] Png {get;set;} public bool Favorite {get;set;} public bool Pinned {get;set;} public DateTime Created {get;set;}
    public string ImageData {get{return Png==null?null:Convert.ToBase64String(Png);}set{Png=String.IsNullOrEmpty(value)?null:Convert.FromBase64String(value);}}
    public string Hash {get;set;}
    [ScriptIgnore]public long Bytes { get {return (Text==null?0:Encoding.UTF8.GetByteCount(Text))+(Html==null?0:Encoding.UTF8.GetByteCount(Html))+(Rtf==null?0:Encoding.UTF8.GetByteCount(Rtf))+(Png==null?0:Png.Length)+(Files==null?0:Files.Sum(f=>(long)Encoding.UTF8.GetByteCount(f)));} }
    [ScriptIgnore]public string Summary {get {if(Kind=="image")return "图片";if(Kind=="files")return Files==null?"文件":String.Join("、",Files.Take(3).Select(Path.GetFileName))+(Files.Length>3?" 等 "+Files.Length+" 项":"");return Text??"";} }
    public void ComputeHash(){using(SHA256 sha=SHA256.Create()){byte[] data=Kind=="image"?(Png??new byte[0]):Encoding.UTF8.GetBytes(Kind+"\0"+(Kind=="files"?String.Join("\0",Files??new string[0]):(Text??"")+"\0"+(Html??"")+"\0"+(Rtf??"")));if(Kind=="image"&&Files!=null&&Files.Length>0)data=data.Concat(Encoding.UTF8.GetBytes("\0"+String.Join("\0",Files))).ToArray();Hash=BitConverter.ToString(sha.ComputeHash(data)).Replace("-","");}}
  }
  public sealed class Preferences {
    public double AnchorX {get;set;} public double AnchorY {get;set;} public double Width {get;set;} public double Height {get;set;}
    public bool Collapsed {get;set;} public bool Pinned {get;set;} public bool AutoCollapse {get;set;} public bool Paused {get;set;}
    public bool SaveHistory {get;set;} public int Limit {get;set;} public int MiniSize {get;set;}
    public Preferences(){AnchorX=-1;AnchorY=180;Width=328;Height=470;Pinned=true;SaveHistory=true;Limit=100;MiniSize=32;}
  }
  public sealed class HistoryFile { public int Version {get;set;} public List<Clip> Items {get;set;} }
  public sealed class History {
    public readonly List<Clip> Items=new List<Clip>();
    public int Limit=100;public const long Budget=24*1024*1024;
    public Clip Add(Clip clip){
      if(clip==null||clip.Bytes>6*1024*1024)return null;
      if(String.IsNullOrEmpty(clip.Id))clip.Id=Guid.NewGuid().ToString("N");if(clip.Created==default(DateTime))clip.Created=DateTime.Now;
      clip.ComputeHash();Clip previous=Items.FirstOrDefault(c=>c.Hash==clip.Hash);
      if(previous!=null){Items.Remove(previous);previous.Created=clip.Created;Items.Insert(0,previous);return previous;}
      Items.Insert(0,clip);Trim();return Items.Contains(clip)?clip:null;
    }
    public bool SetFavorite(Clip clip,bool value){if(value&&!clip.Favorite&&(Items.Count(c=>c.Favorite)>=100||Items.Where(c=>c.Favorite).Sum(c=>c.Bytes)+clip.Bytes>Budget))return false;clip.Favorite=value;Trim();return true;}
    public void SetPinned(Clip clip,bool value){clip.Pinned=value;}
    public void Trim(){
      while(Items.Count(c=>!c.Favorite)>Math.Max(10,Limit)){Clip last=Items.LastOrDefault(c=>!c.Favorite);if(last==null)break;Items.Remove(last);}
      while(Items.Sum(c=>c.Bytes)>Budget){Clip last=Items.LastOrDefault(c=>!c.Favorite);if(last==null)break;Items.Remove(last);}
    }
    public IEnumerable<Clip> Filter(string kind,bool favorites,string query){return Items.Where(c=>(kind=="all"||c.Kind==kind)&&(!favorites||c.Favorite)&&(String.IsNullOrWhiteSpace(query)||c.Summary.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||(c.Files!=null&&c.Files.Any(f=>f.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)))).OrderByDescending(c=>c.Pinned);}
  }
  public sealed class Storage {
    public readonly string DirectoryPath;private readonly string historyPath;private readonly string preferencesPath;
    private static readonly byte[] Entropy=Encoding.UTF8.GetBytes("TinyClip.history.v1");
    public Storage(string dir){DirectoryPath=Path.GetFullPath(dir);historyPath=Path.Combine(DirectoryPath,"history.dpapi");preferencesPath=Path.Combine(DirectoryPath,"preferences.json");}
    public static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=64*1024*1024,RecursionLimit=50};}
    public Preferences ReadPreferences(){try{if(File.Exists(preferencesPath))return Serializer().Deserialize<Preferences>(File.ReadAllText(preferencesPath,Encoding.UTF8))??new Preferences();}catch{}return new Preferences();}
    public void SavePreferences(Preferences value){Directory.CreateDirectory(DirectoryPath);AtomicWrite(preferencesPath,Encoding.UTF8.GetBytes(Serializer().Serialize(value)));}
    public List<Clip> ReadHistory(){
      if(!File.Exists(historyPath))return new List<Clip>();
      try{return Decode(File.ReadAllBytes(historyPath));}catch{if(File.Exists(historyPath+".bak"))return Decode(File.ReadAllBytes(historyPath+".bak"));throw;}
    }
    private static List<Clip> Decode(byte[] encrypted){byte[] clear=ProtectedData.Unprotect(encrypted,Entropy,DataProtectionScope.CurrentUser);try{HistoryFile value=Serializer().Deserialize<HistoryFile>(Encoding.UTF8.GetString(clear));if(value==null||value.Version!=1||value.Items==null||value.Items.Count>1000)throw new InvalidDataException("历史格式不受支持");return value.Items.Where(c=>c!=null&&(c.Kind=="text"||c.Kind=="image"||c.Kind=="files")&&c.Bytes<=6*1024*1024).ToList();}finally{Array.Clear(clear,0,clear.Length);}}
    public void SaveHistory(IEnumerable<Clip> clips){Directory.CreateDirectory(DirectoryPath);byte[] clear=Encoding.UTF8.GetBytes(Serializer().Serialize(new HistoryFile{Version=1,Items=clips.ToList()}));try{AtomicWrite(historyPath,ProtectedData.Protect(clear,Entropy,DataProtectionScope.CurrentUser));}finally{Array.Clear(clear,0,clear.Length);}}
    public void ForgetHistory(){if(File.Exists(historyPath))File.Delete(historyPath);if(File.Exists(historyPath+".bak"))File.Delete(historyPath+".bak");}
    public static void AtomicWrite(string path,byte[] bytes){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);if(File.Exists(path))File.Replace(temp,path,path+".bak",true);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
  }
}
