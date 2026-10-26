using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using WF=System.Windows.Forms;
using Drawing=System.Drawing;

namespace TinyClip {
  public sealed class MainWindow : Window {
    private readonly Storage storage;private readonly Preferences prefs;internal readonly History History=new History();
    private readonly uint showMessage;private bool applyingState,anchorAtRight=true,menuActive;private HwndSource source;private IntPtr hwnd;
    private Border outer;private Grid root,expanded;private Button mini,pinButton,pauseButton;private StackPanel list,tabs;
    private TextBox search;private TextBlock footer;private ScrollViewer scroll;private WF.NotifyIcon tray;
    private readonly DispatcherTimer captureTimer=new DispatcherTimer(),saveTimer=new DispatcherTimer(),searchTimer=new DispatcherTimer(),noticeTimer=new DispatcherTimer();
    private string kind="all",writeHash="";private bool favorites=false,closing=false,loading=true,historyWritable=true;
    private bool movingMini=false,miniMoved=false;private Point miniDown,miniStart;
    private uint lastSequence=0,ownSequence=0;private int readAttempt=0;private bool hotkey=false,listening=false;private string persistError="";
    private static Brush Ink=Brush("#263831"),Muted=Brush("#8C9790"),Accent=Brush("#175A49"),Line=Brush("#E8ECE7"),Soft=Brush("#F1F5F0");
    public MainWindow(Storage value,uint message){
      storage=value;showMessage=message;prefs=storage.ReadPreferences();Normalize();History.Limit=prefs.Limit;
      if(prefs.SaveHistory)try{History.Items.AddRange(storage.ReadHistory());History.Trim();}catch{historyWritable=false;persistError="历史无法解密，本次会话暂不写盘";}
      Title="轻剪贴";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;
      ShowInTaskbar=false;Topmost=prefs.Pinned;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=12;UseLayoutRounding=true;SnapsToDevicePixels=true;
      Width=prefs.Width;Height=prefs.Height;MinWidth=24;MinHeight=24;
      Build();SourceInitialized+=Initialize;Closed+=Cleanup;LocationChanged+=(s,e)=>{if(!loading&&!applyingState&&!prefs.Collapsed)RememberAnchor();};
      Deactivated+=(s,e)=>{if(prefs.AutoCollapse&&!prefs.Collapsed)Dispatcher.BeginInvoke(new Action(()=>{if(!IsActive&&!prefs.Collapsed&&!menuActive&&OwnedWindows.Count==0)Collapse();}),DispatcherPriority.Background);};
      captureTimer.Interval=TimeSpan.FromMilliseconds(70);captureTimer.Tick+=CaptureTick;
      saveTimer.Interval=TimeSpan.FromMilliseconds(600);saveTimer.Tick+=(s,e)=>{saveTimer.Stop();Save();};
      searchTimer.Interval=TimeSpan.FromMilliseconds(100);searchTimer.Tick+=(s,e)=>{searchTimer.Stop();RefreshList(false);};
      noticeTimer.Interval=TimeSpan.FromSeconds(2);noticeTimer.Tick+=(s,e)=>{noticeTimer.Stop();UpdateFooter();};
      PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape){Collapse();e.Handled=true;}else if(e.Key==Key.F&&Keyboard.Modifiers==ModifierKeys.Control){search.Focus();e.Handled=true;}};
      Loaded+=(s,e)=>{PlaceInitial();loading=false;ApplyState();RefreshList(false);};
      SystemEvents.DisplaySettingsChanged+=DisplayChanged;
    }
    internal bool IsMini {get{return prefs.Collapsed;}}
    internal int MiniSize {get{return prefs.MiniSize;}}
    internal string DataDirectory {get{return storage.DirectoryPath;}}
    private static Brush Brush(string hex){SolidColorBrush b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);b.Freeze();return b;}
    private void Normalize(){prefs.Width=Number(prefs.Width,280,650,328);prefs.Height=Number(prefs.Height,260,850,470);prefs.AnchorX=Number(prefs.AnchorX,-100000,100000,-1);prefs.AnchorY=Number(prefs.AnchorY,-100000,100000,180);prefs.MiniSize=new[]{24,32,40}.Contains(prefs.MiniSize)?prefs.MiniSize:32;prefs.Limit=new[]{50,100,200}.Contains(prefs.Limit)?prefs.Limit:100;}
    private static double Number(double n,double low,double high,double fallback){return Double.IsNaN(n)||Double.IsInfinity(n)||n<low||n>high?fallback:n;}
    private static Button Glyph(string value,string tooltip,Action action,int size){Button b=Button(value,action);b.FontFamily=new FontFamily("Segoe MDL2 Assets");b.FontSize=13;b.Width=size;b.Height=size;b.Padding=new Thickness(0);b.ToolTip=tooltip;return b;}
    private static Button Button(string text,Action action){
      Button b=new Button{Content=text,Foreground=Muted,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(8,5,8,5),Cursor=Cursors.Hand,Focusable=false,HorizontalContentAlignment=HorizontalAlignment.Center};
      ControlTemplate template=new ControlTemplate(typeof(Button));FrameworkElementFactory border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});border.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});FrameworkElementFactory presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty,new System.Windows.Data.Binding("HorizontalContentAlignment"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});presenter.SetBinding(ContentPresenter.VerticalAlignmentProperty,new System.Windows.Data.Binding("VerticalContentAlignment"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});border.AppendChild(presenter);template.VisualTree=border;
      Trigger hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BackgroundProperty,Soft));template.Triggers.Add(hover);b.Template=template;
      b.Click+=(s,e)=>{e.Handled=true;if(action!=null)action();};return b;
    }
    private void Build(){
      root=new Grid();Content=root;
      outer=new Border{Background=Brush("#FCFDFC"),BorderBrush=Brush("#DCE4DC"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(12,8,12,7)};root.Children.Add(outer);
      expanded=new Grid();for(int i=0;i<5;i++)expanded.RowDefinitions.Add(new RowDefinition{Height=i==3?new GridLength(1,GridUnitType.Star):GridLength.Auto});outer.Child=expanded;
       Grid title=new Grid{Height=34};title.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});for(int i=0;i<5;i++)title.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
      TextBlock name=new TextBlock{Text="剪贴板",Foreground=Ink,FontSize=14,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Padding=new Thickness(1,0,0,0)};title.Children.Add(name);
      title.MouseLeftButtonDown+=(s,e)=>{if(e.OriginalSource==title||e.OriginalSource==name)DragExpanded();};
       Button add=Button("添加",ShowAddText);add.FontSize=11;add.Padding=new Thickness(7,3,7,3);add.Foreground=Accent;add.ToolTip="添加多行文本记录";Grid.SetColumn(add,1);title.Children.Add(add);
       pinButton=Glyph("\uE718","置顶窗口",()=>{prefs.Pinned=!prefs.Pinned;Topmost=prefs.Pinned;UpdateButtons();QueueSave();},27);Grid.SetColumn(pinButton,2);title.Children.Add(pinButton);
       pauseButton=Glyph("\uE769","暂停记录",()=>{prefs.Paused=!prefs.Paused;UpdateButtons();QueueSave();UpdateFooter();},27);Grid.SetColumn(pauseButton,3);title.Children.Add(pauseButton);
       Button collapse=Glyph("\uE70D","收起",Collapse,27);Grid.SetColumn(collapse,4);title.Children.Add(collapse);
       Button menu=Glyph("\uE712","更多",()=>ShowMenu(title),27);Grid.SetColumn(menu,5);title.Children.Add(menu);
      expanded.Children.Add(title);
      tabs=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,6)};Grid.SetRow(tabs,1);expanded.Children.Add(tabs);BuildTabs();
      Border searchBorder=new Border{Background=Brush("#F0F3EE"),CornerRadius=new CornerRadius(7),Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,0,0,8)};
      Grid searchGrid=new Grid();searchGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});searchGrid.ColumnDefinitions.Add(new ColumnDefinition());
      TextBlock magnify=new TextBlock{Text="\uE721",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=12,Foreground=Muted,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)};searchGrid.Children.Add(magnify);
      search=new TextBox{Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=Ink,FontSize=12,VerticalContentAlignment=VerticalAlignment.Center,Padding=new Thickness(0),ToolTip="搜索内容或文件名"};Grid.SetColumn(search,1);searchGrid.Children.Add(search);searchBorder.Child=searchGrid;Grid.SetRow(searchBorder,2);expanded.Children.Add(searchBorder);search.TextChanged+=(s,e)=>{searchTimer.Stop();searchTimer.Start();};
      scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0)};list=new StackPanel();scroll.Content=list;Grid.SetRow(scroll,3);expanded.Children.Add(scroll);
      Grid bottom=new Grid{Margin=new Thickness(0,7,0,0),Height=22};bottom.ColumnDefinitions.Add(new ColumnDefinition());bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});bottom.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
      footer=new TextBlock{Foreground=Muted,FontSize=10,VerticalAlignment=VerticalAlignment.Center};bottom.Children.Add(footer);
      Button clear=Button("清空",ClearRegular);clear.FontSize=10;clear.Padding=new Thickness(6,2,6,2);Grid.SetColumn(clear,1);bottom.Children.Add(clear);
      Thumb resize=new Thumb{Width=13,Height=15,Cursor=Cursors.SizeNWSE,Margin=new Thickness(4,0,0,0)};ControlTemplate grip=new ControlTemplate(typeof(Thumb));FrameworkElementFactory label=new FrameworkElementFactory(typeof(TextBlock));label.SetValue(TextBlock.TextProperty,"◢");label.SetValue(TextBlock.ForegroundProperty,Brush("#BBC5BC"));label.SetValue(TextBlock.FontSizeProperty,11.0);grip.VisualTree=label;resize.Template=grip;resize.DragDelta+=(s,e)=>{if(prefs.Collapsed)return;Width=Math.Max(280,Math.Min(650,Width+e.HorizontalChange));Height=Math.Max(260,Math.Min(850,Height+e.VerticalChange));prefs.Width=Width;prefs.Height=Height;RememberAnchor();QueueSave();};Grid.SetColumn(resize,2);bottom.Children.Add(resize);Grid.SetRow(bottom,4);expanded.Children.Add(bottom);
      mini=Glyph("\uE70E","展开剪贴板 · Ctrl+Alt+V",Expand,prefs.MiniSize);mini.Background=Accent;mini.Foreground=Brush("#E6F5DB");mini.FontSize=13;mini.Padding=new Thickness(0);mini.HorizontalAlignment=HorizontalAlignment.Stretch;mini.VerticalAlignment=VerticalAlignment.Stretch;mini.Width=Double.NaN;mini.Height=Double.NaN;
      mini.MouseEnter+=(s,e)=>mini.Foreground=Accent;mini.MouseLeave+=(s,e)=>mini.Foreground=Brush("#E6F5DB");
      mini.PreviewMouseLeftButtonDown+=(s,e)=>{movingMini=true;miniMoved=false;miniDown=PointToScreen(e.GetPosition(this));miniStart=new Point(Left,Top);mini.CaptureMouse();e.Handled=true;};
      mini.PreviewMouseMove+=(s,e)=>{if(!movingMini||e.LeftButton!=MouseButtonState.Pressed)return;Point p=PointToScreen(e.GetPosition(this));double factor=DpiScale();double dx=(p.X-miniDown.X)/factor,dy=(p.Y-miniDown.Y)/factor;if(Math.Abs(dx)+Math.Abs(dy)>4)miniMoved=true;if(miniMoved){Left=miniStart.X+dx;Top=miniStart.Y+dy;prefs.AnchorX=Left;prefs.AnchorY=Top;e.Handled=true;}};
      mini.PreviewMouseLeftButtonUp+=(s,e)=>{movingMini=false;mini.ReleaseMouseCapture();if(miniMoved){ClampMini();prefs.AnchorX=Left;prefs.AnchorY=Top;QueueSave();}else Expand();e.Handled=true;};
      mini.MouseRightButtonUp+=(s,e)=>{ShowMenu(mini);e.Handled=true;};root.Children.Add(mini);UpdateButtons();
    }
    private void BuildTabs(){tabs.Children.Clear();foreach(string[] spec in new[]{new[]{"all","全部"},new[]{"text","文本"},new[]{"image","图片"},new[]{"files","文件"},new[]{"favorite","收藏"}}){string key=spec[0];Button b=Button(spec[1],()=>{favorites=key=="favorite";kind=favorites?"all":key;BuildTabs();RefreshList(false);});b.FontSize=11;b.Padding=new Thickness(9,5,9,5);b.Margin=new Thickness(0,0,3,0);if((favorites&&key=="favorite")||(!favorites&&kind==key)){b.Background=Soft;b.Foreground=Accent;}tabs.Children.Add(b);}}
    private void Initialize(object sender,EventArgs args){
      hwnd=new WindowInteropHelper(this).Handle;source=HwndSource.FromHwnd(hwnd);source.AddHook(Hook);
      listening=Native.AddClipboardFormatListener(hwnd);hotkey=Native.RegisterHotKey(hwnd,1,0x0001|0x0002|0x4000,0x56);BuildTray();if(!listening)ReportError("剪贴板监听未启动");
    }
    private IntPtr Hook(IntPtr window,int msg,IntPtr wp,IntPtr lp,ref bool handled){
      if(showMessage!=0&&(uint)msg==showMessage){Expand();handled=true;}
      if(msg==Native.HotKey){if(prefs.Collapsed)Expand();else Collapse();handled=true;}
      if(msg==Native.ClipboardUpdate&&!prefs.Paused){readAttempt=0;captureTimer.Stop();captureTimer.Start();}
      return IntPtr.Zero;
    }
    private void CaptureTick(object sender,EventArgs args){captureTimer.Stop();if(prefs.Paused)return;uint sequence=Native.GetClipboardSequenceNumber();if(sequence==lastSequence)return;
      try {Clip clip=ClipboardService.Read();if(sequence!=Native.GetClipboardSequenceNumber()){captureTimer.Start();return;}lastSequence=sequence;if(clip==null)return;clip.ComputeHash();if(sequence==ownSequence&&clip.Hash==writeHash)return;
        if(History.Add(clip)!=null){if(!prefs.Collapsed)RefreshList(false);UpdateFooter();QueueSave();}
      }catch(System.Runtime.InteropServices.ExternalException){if(++readAttempt<8){captureTimer.Interval=TimeSpan.FromMilliseconds(75+readAttempt*25);captureTimer.Start();}}
      catch{lastSequence=sequence;}
    }
    private void RefreshList(bool keepOffset){double offset=keepOffset?scroll.VerticalOffset:0;list.Children.Clear();List<Clip> visible=History.Filter(kind,favorites,search==null?"":search.Text).ToList();
      if(visible.Count==0){list.Children.Add(new TextBlock{Text=String.IsNullOrWhiteSpace(search.Text)?"暂无记录":"没有匹配内容",Foreground=Brush("#B4BEB4"),FontSize=13,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,75,0,0)});}
      foreach(Clip clip in visible){list.Children.Add(Card(clip));}scroll.ScrollToVerticalOffset(offset);UpdateFooter();
    }
    private Border Card(Clip clip){
      Border card=new Border{Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(9,7,7,8),Margin=new Thickness(0,0,0,6)};
      Grid content=new Grid();content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});content.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});card.Child=content;
       Grid meta=new Grid();meta.ColumnDefinitions.Add(new ColumnDefinition());for(int i=0;i<3;i++)meta.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});content.Children.Add(meta);
      TextBlock stamp=new TextBlock{Text=(clip.Kind=="text"?"文本":clip.Kind=="image"?"图片":"文件")+"  ·  "+(clip.Created.Date==DateTime.Today?clip.Created.ToString("HH:mm"):clip.Created.ToString("MM-dd HH:mm")),Foreground=Muted,FontSize=9,VerticalAlignment=VerticalAlignment.Center};meta.Children.Add(stamp);
       Button pin=Glyph("\uE718",clip.Pinned?"取消置顶":"置顶此条",()=>TogglePinned(clip),20);pin.Foreground=clip.Pinned?Accent:Muted;Grid.SetColumn(pin,1);meta.Children.Add(pin);
       Button favorite=Glyph(clip.Favorite?"\uE735":"\uE734",clip.Favorite?"取消收藏":"收藏",()=>ToggleFavorite(clip),20);favorite.Foreground=clip.Favorite?Accent:Muted;Grid.SetColumn(favorite,2);meta.Children.Add(favorite);
       Button remove=Glyph("\uE711","删除",()=>{History.Items.Remove(clip);RefreshList(true);QueueSave();},20);Grid.SetColumn(remove,3);meta.Children.Add(remove);
      Button body=Button("",()=>Copy(clip,false));body.HorizontalContentAlignment=HorizontalAlignment.Stretch;body.Padding=new Thickness(0,4,0,0);body.Foreground=Ink;
      if(clip.Kind=="image"){
        try{BitmapImage bitmap=new BitmapImage();using(MemoryStream input=new MemoryStream(clip.Png)){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=260;bitmap.DecodePixelHeight=100;bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();}body.Content=new Image{Source=bitmap,MaxHeight=95,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};}catch{body.Content=new TextBlock{Text="图片无法读取",Foreground=Muted};}
      }else {string text=clip.Summary.Replace("\r","");body.Content=new TextBlock{Text=text.Length>300?text.Substring(0,300)+"…":text,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,MaxHeight=64,LineHeight=19,FontSize=12,Foreground=Ink};body.ToolTip=clip.Summary.Length>600?clip.Summary.Substring(0,600)+"…":clip.Summary;}
       Grid.SetRow(body,1);content.Children.Add(body);ContextMenu menu=new ContextMenu();AddMenu(menu,"复制",()=>Copy(clip,false),false,false);if(clip.Kind=="text"){AddMenu(menu,"编辑",()=>ShowEditText(clip),false,false);AddMenu(menu,"复制纯文本",()=>Copy(clip,true),false,false);AddMenu(menu,"查看全文",()=>ShowDetail(clip),false,false);}AddMenu(menu,clip.Pinned?"取消置顶":"置顶此条",()=>TogglePinned(clip),false,false);AddMenu(menu,clip.Favorite?"取消收藏":"收藏",()=>ToggleFavorite(clip),false,false);AddMenu(menu,"删除",()=>{History.Items.Remove(clip);RefreshList(true);QueueSave();},false,false);TrackMenu(menu);card.ContextMenu=menu;
      return card;
    }
    private void Copy(Clip clip,bool plain){
      try{ClipboardService.Copy(clip,plain);Clip expected=plain&&clip.Kind=="text"?new Clip{Kind="text",Text=clip.Text}:clip;expected.ComputeHash();writeHash=expected.Hash;ownSequence=Native.GetClipboardSequenceNumber();lastSequence=ownSequence;ReportError("已复制");if(prefs.AutoCollapse)Collapse();}
      catch(System.Runtime.InteropServices.ExternalException){ReportError("剪贴板正忙，请再点一次");}catch(Exception ex){ReportError(ex.Message);}
    }
    private void ToggleFavorite(Clip clip){if(!History.SetFavorite(clip,!clip.Favorite)){ReportError("收藏已达空间或条数上限");return;}RefreshList(true);QueueSave();}
     private void TogglePinned(Clip clip){History.SetPinned(clip,!clip.Pinned);RefreshList(true);QueueSave();}
     private void ShowAddText(){
       Window dialog=new Window{Title="添加记录",Width=420,Height=330,MinWidth=320,MinHeight=240,Owner=this,Topmost=Topmost,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White};
       Grid g=new Grid{Margin=new Thickness(14)};g.RowDefinitions.Add(new RowDefinition());g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
       TextBox input=new TextBox{AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(9),FontSize=13,Foreground=Ink,BorderBrush=Line,ToolTip="支持多行文本"};g.Children.Add(input);
       Button save=Button("添加",()=>{string text=input.Text;if(String.IsNullOrWhiteSpace(text)){input.Focus();return;}if(History.Add(new Clip{Kind="text",Text=text})==null){MessageBox.Show(dialog,"内容超过单条记录大小限制。","无法添加",MessageBoxButton.OK,MessageBoxImage.Warning);return;}favorites=false;kind="all";if(search!=null)search.Text="";searchTimer.Stop();BuildTabs();RefreshList(false);QueueSave();dialog.DialogResult=true;});save.Background=Soft;save.Foreground=Accent;save.Margin=new Thickness(0,10,0,0);save.Padding=new Thickness(12,6,12,6);Grid.SetRow(save,1);g.Children.Add(save);dialog.Content=g;dialog.Loaded+=(s,e)=>input.Focus();dialog.ShowDialog();
     }
     private void ShowEditText(Clip clip){
       Window dialog=new Window{Title="编辑文本",Width=420,Height=330,MinWidth=320,MinHeight=240,Owner=this,Topmost=Topmost,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White};
       Grid g=new Grid{Margin=new Thickness(14)};g.RowDefinitions.Add(new RowDefinition());g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
       TextBox input=new TextBox{Text=clip.Text??"",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(9),FontSize=13,Foreground=Ink,BorderBrush=Line};g.Children.Add(input);
       Button save=Button("保存",()=>{string text=input.Text;if(String.IsNullOrWhiteSpace(text)){input.Focus();return;}if(System.Text.Encoding.UTF8.GetByteCount(text)>6*1024*1024){MessageBox.Show(dialog,"内容超过单条记录大小限制。","无法保存",MessageBoxButton.OK,MessageBoxImage.Warning);return;}clip.Text=text;clip.Html=null;clip.Rtf=null;clip.ComputeHash();History.Trim();RefreshList(true);QueueSave();dialog.DialogResult=true;});save.Background=Soft;save.Foreground=Accent;save.Margin=new Thickness(0,10,0,0);save.Padding=new Thickness(12,6,12,6);Grid.SetRow(save,1);g.Children.Add(save);dialog.Content=g;dialog.Loaded+=(s,e)=>{input.Focus();input.CaretIndex=input.Text.Length;};dialog.ShowDialog();
     }
    private void ShowDetail(Clip clip){
      Window detail=new Window{Title="文本",Width=500,Height=390,Owner=this,Topmost=Topmost,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.White};
      Grid g=new Grid{Margin=new Thickness(15)};g.RowDefinitions.Add(new RowDefinition());g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});TextBox text=new TextBox{Text=clip.Text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),Foreground=Ink};g.Children.Add(text);Button copy=Button("复制",()=>Copy(clip,false));copy.Background=Soft;copy.Margin=new Thickness(0,10,0,0);Grid.SetRow(copy,1);g.Children.Add(copy);detail.Content=g;detail.Show();
    }
    internal void Collapse(){if(prefs.Collapsed)return;RememberAnchor();prefs.Width=Width;prefs.Height=Height;prefs.Collapsed=true;ApplyState();QueueSave();}
    internal void Expand(){prefs.Collapsed=false;ApplyState();RefreshList(false);Activate();search.Focus();QueueSave();}
    private void ApplyState(){
      applyingState=true;try {
      if(prefs.Collapsed){outer.Visibility=Visibility.Collapsed;mini.Visibility=Visibility.Visible;Width=prefs.MiniSize;Height=prefs.MiniSize;Left=prefs.AnchorX;Top=prefs.AnchorY;ClampMini();}
      else {mini.Visibility=Visibility.Collapsed;outer.Visibility=Visibility.Visible;Width=prefs.Width;Height=prefs.Height;Rect area=WorkArea(prefs.AnchorX,prefs.AnchorY);anchorAtRight=prefs.AnchorX>area.Left+area.Width/2;Left=anchorAtRight?prefs.AnchorX+prefs.MiniSize-Width:prefs.AnchorX;Top=prefs.AnchorY;Left=Math.Max(area.Left,Math.Min(Left,area.Right-Width));Top=Math.Max(area.Top,Math.Min(Top,area.Bottom-Height));}
      Topmost=prefs.Pinned;
      } finally {applyingState=false;}
    }
    private void PlaceInitial(){Rect area=SystemParameters.WorkArea;if(prefs.AnchorX<0){prefs.AnchorX=area.Right-prefs.MiniSize-14;prefs.AnchorY=area.Top+Math.Min(180,area.Height/3);} }
    private double DpiScale(){try{return hwnd==IntPtr.Zero?1:Math.Max(1,Native.GetDpiForWindow(hwnd)/96.0);}catch{return 1;}}
    private Rect WorkArea(double x,double y){double factor=DpiScale();WF.Screen screen=WF.Screen.FromPoint(new Drawing.Point((int)(x*factor),(int)(y*factor)));Drawing.Rectangle a=screen.WorkingArea;return new Rect(a.Left/factor,a.Top/factor,a.Width/factor,a.Height/factor);}
    private void ClampMini(){Rect area=WorkArea(Left,Top);Left=Math.Max(area.Left,Math.Min(Left,area.Right-prefs.MiniSize));Top=Math.Max(area.Top,Math.Min(Top,area.Bottom-prefs.MiniSize));prefs.AnchorX=Left;prefs.AnchorY=Top;}
    private void RememberAnchor(){prefs.AnchorX=anchorAtRight?Left+Width-prefs.MiniSize:Left;prefs.AnchorY=Top;}
    private void DragExpanded(){try{DragMove();Rect area=WorkArea(Left,Top);anchorAtRight=Left+Width/2>area.Left+area.Width/2;RememberAnchor();QueueSave();}catch{}}
    private void DisplayChanged(object sender,EventArgs args){Dispatcher.BeginInvoke(new Action(()=>{ApplyState();QueueSave();}));}
    private void UpdateButtons(){pinButton.Foreground=prefs.Pinned?Accent:Muted;pinButton.ToolTip=prefs.Pinned?"取消置顶":"置顶";pauseButton.Content=prefs.Paused?"\uE768":"\uE769";pauseButton.ToolTip=prefs.Paused?"继续记录":"暂停记录";pauseButton.Foreground=prefs.Paused?Brush("#B68035"):Muted;}
    private void UpdateFooter(){if(footer==null)return;footer.Text=prefs.Paused?"已暂停":History.Items.Count+" 条";footer.ToolTip=String.IsNullOrEmpty(persistError)?(hotkey?"Ctrl+Alt+V 展开 / 收起":"快捷键未注册，可使用悬浮按钮"):persistError;}
    internal void ReportError(string message){if(footer!=null){footer.Text=message;footer.ToolTip=message;noticeTimer.Stop();noticeTimer.Start();}if(tray!=null)tray.Text="轻剪贴 · "+(message.Length>45?message.Substring(0,45):message);}
    private void QueueSave(){saveTimer.Stop();saveTimer.Start();}
    private void Save(){try{storage.SavePreferences(prefs);if(prefs.SaveHistory&&historyWritable)storage.SaveHistory(History.Items);}catch{persistError="本次历史尚未保存，请检查目录权限";ReportError("历史未保存");}}
    private static MenuItem AddMenu(ContextMenu menu,string title,Action action,bool checkable,bool selected){MenuItem item=new MenuItem{Header=title,IsCheckable=checkable,IsChecked=selected};item.Click+=(s,e)=>{if(action!=null)action();};menu.Items.Add(item);return item;}
    private void ShowMenu(FrameworkElement target){
      ContextMenu menu=new ContextMenu();AddMenu(menu,prefs.Paused?"继续记录":"暂停记录",()=>{prefs.Paused=!prefs.Paused;UpdateButtons();UpdateFooter();QueueSave();},false,false);
      AddMenu(menu,"置顶",()=>{prefs.Pinned=!prefs.Pinned;Topmost=prefs.Pinned;UpdateButtons();QueueSave();},true,prefs.Pinned);
      AddMenu(menu,"失焦自动收起",()=>{prefs.AutoCollapse=!prefs.AutoCollapse;QueueSave();},true,prefs.AutoCollapse);
      MenuItem sizes=new MenuItem{Header="收起按钮大小"};foreach(int n in new[]{24,32,40}){int pixels=n;MenuItem item=new MenuItem{Header=n+" 像素",IsCheckable=true,IsChecked=prefs.MiniSize==n};item.Click+=(s,e)=>{prefs.MiniSize=pixels;if(prefs.Collapsed)ApplyState();QueueSave();};sizes.Items.Add(item);}menu.Items.Add(sizes);
      MenuItem limits=new MenuItem{Header="历史条数"};foreach(int n in new[]{50,100,200}){int count=n;MenuItem item=new MenuItem{Header=n.ToString(),IsCheckable=true,IsChecked=prefs.Limit==n};item.Click+=(s,e)=>{prefs.Limit=count;History.Limit=count;History.Trim();RefreshList(true);QueueSave();};limits.Items.Add(item);}menu.Items.Add(limits);
      AddMenu(menu,"保存本地历史",()=>{bool next=!prefs.SaveHistory;if(!next&&MessageBox.Show(this,"关闭后将删除磁盘历史，仅保留本次会话。","轻剪贴",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;prefs.SaveHistory=next;try{if(!next)storage.ForgetHistory();else historyWritable=true;storage.SavePreferences(prefs);if(next)Save();}catch{ReportError("设置未保存");}},true,prefs.SaveHistory);
      AddMenu(menu,"开机启动",ToggleStartup,true,StartupEnabled());menu.Items.Add(new Separator());
      AddMenu(menu,"清空未收藏",ClearRegular,false,false);AddMenu(menu,"清空全部",ClearAll,false,false);
      if(!historyWritable)AddMenu(menu,"重置无法读取的历史",()=>{if(MessageBox.Show(this,"重置磁盘历史并保存当前会话？","轻剪贴",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;try{storage.ForgetHistory();historyWritable=true;persistError="";Save();}catch{ReportError("无法重置历史");}},false,false);
      AddMenu(menu,"关于",()=>MessageBox.Show(this,"轻剪贴 0.1.0\nCtrl+Alt+V 展开 / 收起\n点击记录复制，右键更多操作\n本地存储，不联网","轻剪贴"),false,false);menu.Items.Add(new Separator());AddMenu(menu,"退出",Exit,false,false);
      TrackMenu(menu);menu.PlacementTarget=target;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;
    }
    private void TrackMenu(ContextMenu menu){menu.Opened+=(s,e)=>menuActive=true;menu.Closed+=(s,e)=>menuActive=false;}
    private void ClearRegular(){int n=History.Items.Count(c=>!c.Favorite);if(n==0)return;if(MessageBox.Show(this,"清空 "+n+" 条未收藏记录？","轻剪贴",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;History.Items.RemoveAll(c=>!c.Favorite);RefreshList(false);QueueSave();}
    private void ClearAll(){if(MessageBox.Show(this,"清空全部记录，包括收藏？","轻剪贴",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;History.Items.Clear();RefreshList(false);try{storage.ForgetHistory();historyWritable=true;Save();}catch{ReportError("历史未清空");}}
    private bool StartupEnabled(){try{using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return key!=null&&key.GetValue("TinyClip")!=null;}catch{return false;}}
    private void ToggleStartup(){try{using(RegistryKey key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(StartupEnabled())key.DeleteValue("TinyClip",false);else key.SetValue("TinyClip","\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\" --data-dir \""+storage.DirectoryPath+"\"");}}catch{ReportError("无法修改开机启动");}}
    private void BuildTray(){
      tray=new WF.NotifyIcon();using(Drawing.Bitmap bitmap=new Drawing.Bitmap(32,32)){using(Drawing.Graphics g=Drawing.Graphics.FromImage(bitmap)){g.Clear(Drawing.Color.Transparent);g.SmoothingMode=Drawing.Drawing2D.SmoothingMode.AntiAlias;using(Drawing.SolidBrush b=new Drawing.SolidBrush(Drawing.Color.FromArgb(23,90,73)))g.FillEllipse(b,1,1,30,30);using(Drawing.Pen pen=new Drawing.Pen(Drawing.Color.FromArgb(231,245,220),2)){g.DrawRectangle(pen,10,8,13,18);g.DrawLine(pen,13,13,20,13);g.DrawLine(pen,13,18,20,18);}}IntPtr handle=bitmap.GetHicon();try{using(Drawing.Icon icon=Drawing.Icon.FromHandle(handle))tray.Icon=(Drawing.Icon)icon.Clone();}finally{Native.DestroyIcon(handle);}}
      tray.Text="轻剪贴";tray.Visible=true;tray.DoubleClick+=(s,e)=>Dispatcher.BeginInvoke(new Action(Expand));WF.ContextMenuStrip menu=new WF.ContextMenuStrip();menu.Items.Add("展开",null,(s,e)=>Dispatcher.BeginInvoke(new Action(Expand)));menu.Items.Add("收起",null,(s,e)=>Dispatcher.BeginInvoke(new Action(Collapse)));menu.Items.Add("暂停 / 继续",null,(s,e)=>Dispatcher.BeginInvoke(new Action(()=>{prefs.Paused=!prefs.Paused;UpdateButtons();UpdateFooter();QueueSave();})));menu.Items.Add(new WF.ToolStripSeparator());menu.Items.Add("退出",null,(s,e)=>Dispatcher.BeginInvoke(new Action(Exit)));tray.ContextMenuStrip=menu;
    }
    internal void Exit(){closing=true;Close();}
    protected override void OnClosing(System.ComponentModel.CancelEventArgs args){if(!closing){args.Cancel=true;Collapse();}base.OnClosing(args);}
    private void Cleanup(object sender,EventArgs args){SystemEvents.DisplaySettingsChanged-=DisplayChanged;captureTimer.Stop();searchTimer.Stop();noticeTimer.Stop();saveTimer.Stop();Save();if(listening)Native.RemoveClipboardFormatListener(hwnd);if(hotkey)Native.UnregisterHotKey(hwnd,1);if(source!=null)source.RemoveHook(Hook);if(tray!=null){tray.Visible=false;tray.Dispose();}}
  }
}
