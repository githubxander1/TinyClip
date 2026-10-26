using System;
using System.IO;
using System.Linq;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TinyClip {
  internal static class ClipboardService {
    private static bool IsZeroFlag(object value){
      byte[] bytes=value as byte[];if(bytes!=null)return bytes.Length>=4&&BitConverter.ToInt32(bytes,0)==0;
      MemoryStream stream=value as MemoryStream;if(stream!=null){long at=stream.Position;try{byte[] data=new byte[4];stream.Position=0;return stream.Read(data,0,4)==4&&BitConverter.ToInt32(data,0)==0;}finally{stream.Position=at;}}
      return value is int&&(int)value==0;
    }
    public static Clip Read(){
      IDataObject data=Clipboard.GetDataObject();if(data==null)return null;
      if(data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing",false))return null;
      if(data.GetDataPresent("CanIncludeInClipboardHistory",false)&&IsZeroFlag(data.GetData("CanIncludeInClipboardHistory",false)))return null;
      BitmapSource bitmap=null;
      if(data.GetDataPresent(DataFormats.Bitmap,false))bitmap=data.GetData(DataFormats.Bitmap,false) as BitmapSource;
      if(data.GetDataPresent("PNG",false)){
        object png=data.GetData("PNG",false);MemoryStream stream=png as MemoryStream;byte[] bytes=png as byte[];
        if(stream!=null)bytes=stream.ToArray();if(bytes!=null&&bytes.Length<=6*1024*1024)try{using(MemoryStream read=new MemoryStream(bytes)){BitmapDecoder decoder=BitmapDecoder.Create(read,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);bitmap=decoder.Frames[0];bitmap.Freeze();}}catch{}
      }
      if(bitmap==null&&Clipboard.ContainsImage())bitmap=Clipboard.GetImage();
      if(bitmap!=null){if((long)bitmap.PixelWidth*bitmap.PixelHeight>24000000)return null;PngBitmapEncoder encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(MemoryStream output=new MemoryStream()){encoder.Save(output);if(output.Length<=6*1024*1024)return new Clip{Kind="image",Png=output.ToArray()};}}
      if(data.GetDataPresent(DataFormats.FileDrop)){
        string[] files=data.GetData(DataFormats.FileDrop) as string[];
        if(files!=null&&files.Length>0&&files.Length<=1000){Clip image=ReadImageFile(files);return image??new Clip{Kind="files",Files=files};}
      }
      if(data.GetDataPresent(DataFormats.UnicodeText)){
        string text=data.GetData(DataFormats.UnicodeText) as string;if(!String.IsNullOrWhiteSpace(text)&&text.Length<=1024*1024){
          string html=data.GetDataPresent(DataFormats.Html)?data.GetData(DataFormats.Html) as string:null;
          string rtf=data.GetDataPresent(DataFormats.Rtf)?data.GetData(DataFormats.Rtf) as string:null;
          return new Clip{Kind="text",Text=text,Html=html!=null&&html.Length<1024*1024?html:null,Rtf=rtf!=null&&rtf.Length<1024*1024?rtf:null};
        }
      }
      return null;
    }
    private static Clip ReadImageFile(string[] files){
      if(files.Length!=1)return null;
      string extension=Path.GetExtension(files[0]).ToLowerInvariant();
      if(!new[]{".png",".jpg",".jpeg",".bmp",".gif",".tif",".tiff"}.Contains(extension))return null;
      try{using(FileStream input=File.OpenRead(files[0])){BitmapDecoder decoder=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);BitmapSource bitmap=decoder.Frames[0];bitmap.Freeze();if((long)bitmap.PixelWidth*bitmap.PixelHeight>24000000)return null;PngBitmapEncoder encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(MemoryStream output=new MemoryStream()){encoder.Save(output);if(output.Length>6*1024*1024)return null;return new Clip{Kind="image",Png=output.ToArray(),Files=files};}}}catch{return null;}
    }
    public static void Copy(Clip clip,bool plain){
      DataObject data=new DataObject();
      if(clip.Kind=="files"){
        if(clip.Files==null||clip.Files.Length==0)throw new InvalidOperationException("文件列表为空");
        if(clip.Files.Any(f=>!File.Exists(f)&&!Directory.Exists(f)))throw new InvalidOperationException("部分文件已移动或删除");
        data.SetData(DataFormats.FileDrop,clip.Files);
        data.SetData("Preferred DropEffect",new MemoryStream(BitConverter.GetBytes(1)));
      } else if(clip.Kind=="image"){
        if(clip.Png==null)throw new InvalidOperationException("图片为空");
        BitmapImage bitmap=new BitmapImage();using(MemoryStream input=new MemoryStream(clip.Png)){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();}
        data.SetData(DataFormats.Bitmap,bitmap);data.SetData("PNG",new MemoryStream(clip.Png));
        if(clip.Files!=null&&clip.Files.Length>0&&clip.Files.All(f=>File.Exists(f)||Directory.Exists(f))){data.SetData(DataFormats.FileDrop,clip.Files);data.SetData("Preferred DropEffect",new MemoryStream(BitConverter.GetBytes(1)));}
      } else {
        data.SetData(DataFormats.UnicodeText,clip.Text??"");
        if(!plain&&!String.IsNullOrEmpty(clip.Html))data.SetData(DataFormats.Html,clip.Html);
        if(!plain&&!String.IsNullOrEmpty(clip.Rtf))data.SetData(DataFormats.Rtf,clip.Rtf);
      }
      Clipboard.SetDataObject(data,true);
    }
  }
}
