using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace PortraitPetPlus {
    public class PetWindow : Window {
        public readonly AppHost Host;
        public readonly int Index;
        public PetProfile Profile { get { return Host.Settings.Pets[Index]; } }
        public Atlas ActiveAtlas;
        public string CurrentAction="";
        readonly Canvas canvas=new Canvas();
        readonly Image body=new Image();
        readonly Border balloon=new Border();
        readonly TextBlock words=new TextBlock(),effect=new TextBlock(),badge=new TextBlock();
        readonly ScaleTransform scaleBody=new ScaleTransform(1,1);
        readonly RotateTransform rotate=new RotateTransform();
        readonly TranslateTransform shift=new TranslateTransform();
        readonly ContextMenu menu=new ContextMenu();
        readonly MediaPlayer voice=new MediaPlayer();
        readonly Random random=new Random(Guid.NewGuid().GetHashCode());
        Sprite sprite;
        bool pressed,dragged,moving,sleeping,manualSleep;
        double actionUntil,bubbleUntil,effectUntil,nextWander,nextIdle,nextBlink,blinkUntil,lastPat=-20,lastTap=-20,lastRoutine,manualOverride,sleepSince,dir=-1;
        int taps;
        Point pressCursor,pressPosition;
        IntPtr attachment;
        public bool Attached { get { return attachment!=IntPtr.Zero; } }
        FoodBox foodBox;
        public bool Busy { get { return pressed || menu.IsOpen || sleeping || Host.Now<actionUntil || Host.FocusActive || attachment!=IntPtr.Zero; } }
        public string Mood { get { return sleeping ? "休息中" : Profile.Satiety<25 ? "有点饿" : Profile.Energy<25 ? "有点困" : Host.FocusActive ? "专注陪伴" : Profile.Affinity>=120 ? "亲密" : Profile.Affinity>=40 ? "熟悉" : "自在"; } }
        public PetWindow(AppHost host,int index) {
            Host=host; Index=index;
            Title="真人桌宠增强版 · "+Profile.Name;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
            ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; ShowActivated=false; Topmost=Profile.Topmost;
            Content=canvas; UseLayoutRounding=true; AllowDrop=true;
            body.Stretch=Stretch.Uniform; body.RenderTransformOrigin=new Point(.5,1);
            var transform=new TransformGroup(); transform.Children.Add(scaleBody); transform.Children.Add(rotate); transform.Children.Add(shift); body.RenderTransform=transform;
            RenderOptions.SetBitmapScalingMode(body,BitmapScalingMode.HighQuality); canvas.Children.Add(body);
            balloon.Background=new SolidColorBrush(Color.FromArgb(246,255,253,248)); balloon.BorderBrush=new SolidColorBrush(Color.FromRgb(218,209,193)); balloon.BorderThickness=new Thickness(1); balloon.CornerRadius=new CornerRadius(14); balloon.Padding=new Thickness(12,9,12,9); balloon.IsHitTestVisible=false;
            words.FontFamily=new FontFamily("Microsoft YaHei UI"); words.FontSize=12; words.TextAlignment=TextAlignment.Center; words.TextWrapping=TextWrapping.Wrap; words.Foreground=new SolidColorBrush(Color.FromRgb(63,61,57)); balloon.Child=words;
            canvas.Children.Add(balloon); balloon.Visibility=Visibility.Collapsed;
            effect.FontSize=28; effect.Foreground=new SolidColorBrush(Color.FromRgb(210,100,125)); effect.IsHitTestVisible=false; canvas.Children.Add(effect);
            badge.FontSize=11; badge.Foreground=new SolidColorBrush(Color.FromRgb(90,83,74)); badge.Background=new SolidColorBrush(Color.FromArgb(220,255,253,248)); badge.Padding=new Thickness(7,3,7,3); badge.IsHitTestVisible=false; canvas.Children.Add(badge);
            BuildMenu(); ContextMenu=menu;
            SourceInitialized+=delegate { HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(WindowHook); };
            Loaded+=delegate { ReloadAtlas(); ResizePet(); if(Double.IsNaN(Profile.Left) || Double.IsNaN(Profile.Top)) GoHome(); else { Left=Profile.Left; Top=Profile.Top; Clamp(); } nextWander=Host.Now+10; nextIdle=Host.Now+15; nextBlink=Host.Now+4; };
            MouseLeftButtonDown+=MouseDownHandler; MouseLeftButtonUp+=MouseUpHandler; MouseMove+=MouseMoveHandler;
            LostMouseCapture+=delegate { if(pressed) { pressed=false; Clamp(); } };
            MouseWheel+=delegate(object s,MouseWheelEventArgs e) { Profile.Size=Math.Max(170,Math.Min(440,Profile.Size+(e.Delta>0 ? 20 : -20))); ResizePet(); Host.Save(); e.Handled=true; };
            PreviewKeyDown+=delegate(object s,KeyEventArgs e) { if(e.Key==Key.Escape) Host.ExitAll(); if(e.Key==Key.Space) { Profile.Wander=!Profile.Wander; moving=false; Host.Save(); } };
            DragOver+=delegate(object s,DragEventArgs e) { e.Effects=e.Data.GetDataPresent("PortraitPetFood") ? DragDropEffects.Copy : DragDropEffects.None; e.Handled=true; };
            Drop+=delegate(object s,DragEventArgs e) { if(e.Data.GetDataPresent("PortraitPetFood")) { string food=e.Data.GetData("PortraitPetFood") as string; Feed(food); e.Handled=true; } };
            voice.MediaFailed+=delegate { Host.SetStatus("声音播放失败，请换用 WAV 或 MP3 文件。"); };
            Closed+=delegate { voice.Close(); if(foodBox!=null) foodBox.Close(); };
        }
        public void ReloadAtlas() {
            try { ActiveAtlas=String.IsNullOrEmpty(Profile.CustomAtlas) ? Atlas.LoadBuiltin(Profile.Outfit) : Atlas.LoadFile(Profile.CustomAtlas,Profile.AtlasColumns,Profile.AtlasRows); }
            catch(Exception ex) { ActiveAtlas=Atlas.LoadBuiltin(Profile.Outfit); Say("自定义素材未能加载，先使用内置形象。",5); Host.SetStatus(ex.Message); }
            sprite=null; SetSprite(ActiveAtlas.Get(0));
        }
        MenuItem Menu(string text,Action action) { var m=new MenuItem { Header=text }; m.Click+=delegate { action(); }; menu.Items.Add(m); return m; }
        void BuildMenu() {
            menu.FontFamily=new FontFamily("Microsoft YaHei UI"); menu.FontSize=13;
            Menu("打开陪伴控制台",delegate { Host.OpenPanel(); }); menu.Items.Add(new Separator());
            Menu("摸摸头",Pat); Menu("零食盒（可拖动投喂）",OpenFoodBox);
            Menu("喝过了",Host.AcknowledgeWater); Menu("喝水稍后提醒",Host.SnoozeWater);
            var actions=new MenuItem { Header="小动作" }; menu.Items.Add(actions);
            string[] labels={"挥手","整理眼镜","伸懒腰","坐一会儿","喝一口水","吃饼干","探头","打哈欠（原外套）","趴一会儿（原外套）","比心（好感 40）","开心摇摆（好感 120）"};
            string[] codes={"wave","glasses","stretch","sit","drink","snack","peek","yawn","lie","heart","happy"};
            for(int i=0;i<labels.Length;i++) { string action=codes[i]; var a=new MenuItem { Header=labels[i] }; a.Click+=delegate { Action(action,action=="sit" ? 20 : 4); }; actions.Items.Add(a); }
            Menu("休息 / 唤醒",ToggleSleep);
            Menu("跟随鼠标 开 / 关",delegate { Profile.FollowMouse=!Profile.FollowMouse; Say(Profile.FollowMouse ? "我跟着你的光标走。" : "我自己逛一会儿。",3); Host.Save(); });
            Menu("暂停 / 继续走动",delegate { Profile.Wander=!Profile.Wander; moving=false; Host.Save(); Say(Profile.Wander ? "起来走走。" : "我就在这里。",3); });
            menu.Items.Add(new Separator());
            Menu("坐到刚才的窗口上",AttachToWindow); Menu("趴到任务栏旁",delegate { CancelAttachment(); GoHome(); Action("lie",30); });
            Menu("开始番茄钟",Host.StartFocus); Menu("双人互动 / 添加分身",Host.Social);
            Menu("回到屏幕右下角",GoHome); menu.Items.Add(new Separator()); Menu("退出全部桌宠",Host.ExitAll);
        }
        public void ResizePet() {
            double feet=Double.IsNaN(Top) || Double.IsNaN(Height) ? Double.NaN : Top+Height;
            Width=CurrentAction=="lie" ? Math.Max(320,Profile.Size*1.15) : Math.Max(250,Profile.Size*.88); Height=Profile.Size+109;
            body.Width=Width-18; body.Height=Profile.Size; Canvas.SetLeft(body,9); Canvas.SetTop(body,91);
            balloon.Width=Width-36; Canvas.SetLeft(balloon,18); Canvas.SetTop(balloon,2);
            badge.Text=Profile.Name; Canvas.SetLeft(badge,(Width-100)/2); Canvas.SetTop(badge,Height-18);
            if(!Double.IsNaN(feet)) Top=feet-Height;
            if(IsLoaded) Clamp();
        }
        Matrix ToDevice { get { var s=PresentationSource.FromVisual(this); return s==null ? Matrix.Identity : s.CompositionTarget.TransformToDevice; } }
        Matrix FromDevice { get { var s=PresentationSource.FromVisual(this); return s==null ? Matrix.Identity : s.CompositionTarget.TransformFromDevice; } }
        public Rect WorkArea() {
            var c=ToDevice.Transform(new Point(Double.IsNaN(Left) ? 0 : Left+Width/2,Double.IsNaN(Top) ? 0 : Top+Height/2));
            var area=Forms.Screen.FromPoint(new System.Drawing.Point((int)c.X,(int)c.Y)).WorkingArea;
            return new Rect(FromDevice.Transform(new Point(area.Left,area.Top)),FromDevice.Transform(new Point(area.Right,area.Bottom)));
        }
        public void Clamp() { var a=WorkArea(); Left=Math.Max(a.Left,Math.Min(Left,Math.Max(a.Left,a.Right-Width))); Top=Math.Max(a.Top,Math.Min(Top,Math.Max(a.Top,a.Bottom-Height))); }
        public void GoHome() { CancelAttachment(); var a=WorkArea(); Left=Math.Max(a.Left,a.Right-Width-35-(Index*155)); Top=Math.Max(a.Top,a.Bottom-Height-3); moving=false; nextWander=Host.Now+10; }
        public void RememberPosition() { if(IsLoaded) { Profile.Left=Left; Profile.Top=Top; } }
        public void Say(string text,double seconds) { words.Text=text; balloon.Visibility=Visibility.Visible; bubbleUntil=Host.Now+seconds; }
        public void ShowEffect(string text,double seconds) { effect.Text=text; effect.Visibility=Visibility.Visible; effectUntil=Host.Now+seconds; }
        public void Action(string code,double seconds) {
            if((code=="lie" || code=="yawn") && (Profile.Outfit!="coat" || !String.IsNullOrEmpty(Profile.CustomAtlas))) { Say("这个动作使用原外套形象，可在控制台切换。",4); return; }
            if(code=="heart" && Profile.Affinity<40) { Say("好感达到 40，就能解锁比心。",3); return; }
            if(code=="happy" && Profile.Affinity<120) { Say("好感达到 120，解锁开心摇摆。",3); return; }
            sleeping=false; manualSleep=false; moving=false; bool wasLying=CurrentAction=="lie"; CurrentAction=code; actionUntil=Host.Now+seconds;
            if(wasLying || code=="lie") ResizePet();
            if(code=="heart") ShowEffect("♥",seconds);
            if(code=="happy") ShowEffect("♪",seconds);
            if(code=="peek") Say("看看你在忙什么。",3);
        }
        public void ClearAction() { bool wasLying=CurrentAction=="lie"; CurrentAction=""; actionUntil=0; if(wasLying) ResizePet(); }
        public void Pat() {
            if(Host.Now-lastPat<2) { Action("shy",2); Say("慢一点，会害羞的。",2); return; }
            lastPat=Host.Now; Profile.HeadPets++; Profile.Affinity=Math.Min(999,Profile.Affinity+3); Profile.Energy=Math.Min(100,Profile.Energy+1);
            Action("pet",3); ShowEffect("♥",2.5); Say(Profile.Affinity>=40 ? "已经认得你的摸摸啦。" : "收到摸摸，心情变好了。",3); PlayVoice(); Host.RefreshPanel();
        }
        public void Feed(string food) {
            if(food!="cookie" && food!="tea" && food!="water") return;
            Profile.Snacks++; Profile.Satiety=Math.Min(100,Profile.Satiety+(food=="water" ? 3 : 18)); Profile.Energy=Math.Min(100,Profile.Energy+3);
            // Affection gains have a small interaction cooldown; feeding still works.
            if(Host.Now-lastTap>2) { Profile.Affinity=Math.Min(999,Profile.Affinity+5); lastTap=Host.Now; }
            Action(food=="cookie" ? "snack" : "drink",5);
            ShowEffect(food=="cookie" ? "🍪" : food=="tea" ? "🧋" : "💧",2.5);
            Say(food=="cookie" ? "谢谢，饼干很好吃。" : food=="tea" ? "收到奶茶，开心加一。" : "一起喝水。",4); PlayVoice(); Host.RefreshPanel();
        }
        public void OpenFoodBox() {
            if(foodBox==null) { foodBox=new FoodBox(this); foodBox.Closed+=delegate { foodBox=null; }; }
            foodBox.Show(); foodBox.Left=Math.Max(WorkArea().Left,Left-220); foodBox.Top=Math.Max(WorkArea().Top,Top+120); foodBox.Activate();
        }
        public void ToggleSleep() {
            sleeping=!sleeping; sleepSince=Host.Now; manualSleep=sleeping; manualOverride=Host.Now+7200; moving=false; ClearAction();
            Say(sleeping ? "休息中 · 双击可以叫醒我" : "醒啦，继续陪你。",5); nextWander=Host.Now+10;
        }
        public void PlayVoice() {
            if(!Profile.SoundEnabled || String.IsNullOrEmpty(Profile.VoiceFile) || !File.Exists(Profile.VoiceFile) || Host.Testing) return;
            try { voice.Stop(); voice.Open(new Uri(Profile.VoiceFile,UriKind.Absolute)); voice.Volume=.65; voice.Play(); }
            catch(Exception ex) { Host.SetStatus("声音未能播放："+ex.Message); }
        }
        public void SpeakCustomLine() {
            var lines=Profile.Lines; Say(lines.Length==0 ? "我在这里。" : lines[random.Next(lines.Length)],5); PlayVoice();
        }
        public void CancelAttachment() { attachment=IntPtr.Zero; }
        public void AttachToWindow() {
            IntPtr target=Host.LastExternalWindow; Desktop.NativeRect r;
            if(target==IntPtr.Zero || !Desktop.IsWindowVisible(target) || Desktop.IsIconic(target) || !Desktop.GetWindowRect(target,out r) || r.Right-r.Left<250 || r.Bottom-r.Top<150) { Say("先点一下想停靠的窗口，再右键选择这里。",5); return; }
            Point edge=FromDevice.Transform(new Point(r.Left,r.Top));
            if(edge.Y<WorkArea().Top+Profile.Size*.7) { Say("窗口上方空间不足，先在任务栏旁坐着。",5); GoHome(); Action("sit",20); return; }
            attachment=target; moving=false; Action("sit",86400); FollowAttached(); Say("坐在这里陪你。",4);
        }
        void FollowAttached() {
            Desktop.NativeRect r;
            if(!Desktop.IsWindowVisible(attachment) || Desktop.IsIconic(attachment) || !Desktop.GetWindowRect(attachment,out r)) { CancelAttachment(); ClearAction(); GoHome(); return; }
            Point top=FromDevice.Transform(new Point(r.Left,r.Top)),right=FromDevice.Transform(new Point(r.Right,r.Top));
            if(top.Y<WorkArea().Top+Profile.Size*.6) { CancelAttachment(); ClearAction(); GoHome(); return; }
            Sprite sit=ActiveAtlas.Get(6); double factor=Math.Min(body.Width/sit.Width,body.Height/sit.Height);
            double drawY=91+(body.Height-sit.Height*factor)/2;
            double seatY=drawY+(sit.Bounds.Top+sit.Bounds.Height*.57)*factor;
            Left=Math.Max(top.X,Math.Min(Left,right.X-Width)); Top=top.Y-seatY; Clamp();
        }
        void SetSprite(Sprite s) { if(sprite==s) return; sprite=s; body.Source=s.Image; }
        Point CursorPoint() { var p=Forms.Cursor.Position; return FromDevice.Transform(new Point(p.X,p.Y)); }
        void MouseDownHandler(object sender,MouseButtonEventArgs e) {
            if(e.ClickCount==2) { pressed=false; ReleaseMouseCapture(); ToggleSleep(); e.Handled=true; return; }
            pressed=true; dragged=false; moving=false; pressCursor=CursorPoint(); pressPosition=new Point(Left,Top); CaptureMouse(); e.Handled=true;
        }
        void MouseMoveHandler(object sender,MouseEventArgs e) {
            if(!pressed || e.LeftButton!=MouseButtonState.Pressed) return;
            Vector d=CursorPoint()-pressCursor; if(d.Length>5) dragged=true;
            if(dragged) { CancelAttachment(); Left=pressPosition.X+d.X; Top=pressPosition.Y+d.Y; }
        }
        void MouseUpHandler(object sender,MouseButtonEventArgs e) {
            if(!pressed) return; pressed=false; ReleaseMouseCapture(); Clamp();
            if(dragged) { RememberPosition(); Say("就在这里陪你。",2); }
            else {
                Point p=e.GetPosition(body); Sprite s=sprite;
                double f=Math.Min(body.Width/s.Width,body.Height/s.Height);
                double bodyTop=(body.Height-s.Height*f)/2+s.Bounds.Y*f;
                if(p.Y<bodyTop+s.Bounds.Height*f*.25) Pat();
                else {
                    if(Host.Now-lastTap<1.5) taps++; else taps=1; lastTap=Host.Now;
                    if(taps>=3) { taps=0; Action("shy",3); shift.X=dir*10; Say("被你戳得有点害羞。",3); }
                    else { Action("wave",3); SpeakCustomLine(); }
                }
            }
            nextWander=Host.Now+8;
        }
        public void Step(double dt) {
            double now=Host.Now;
            if(now>bubbleUntil) balloon.Visibility=Visibility.Collapsed;
            if(now>effectUntil) effect.Visibility=Visibility.Collapsed;
            else { Canvas.SetLeft(effect,Width*.62+Math.Sin(now*3)*8); Canvas.SetTop(effect,90+(effectUntil-now)*7); }
            Profile.Satiety=Math.Max(0,Profile.Satiety-dt/80);
            Profile.Energy=Math.Max(0,Math.Min(100,Profile.Energy+dt*(sleeping ? .04 : -.004)));
            if(now-lastRoutine>2) {
                lastRoutine=now;
                if(Profile.AutoRoutine && now>manualOverride && !Host.FocusActive && !pressed) {
                    bool night=DateTime.Now.Hour>=22 || DateTime.Now.Hour<8;
                    bool rest=night || Desktop.IdleSeconds()>300;
                    if(rest!=sleeping && !manualSleep) { sleeping=rest; sleepSince=now; moving=false; }
                }
                badge.Text=Profile.Name+(sleeping ? " · Zz" : Host.FocusActive ? " · 专注" : "");
            }
            if(attachment!=IntPtr.Zero && !pressed) FollowAttached();
            if(now>actionUntil && CurrentAction!="") ClearAction();
            bool free=!pressed && !menu.IsOpen && !sleeping && !Host.FocusActive && CurrentAction=="" && attachment==IntPtr.Zero;
            Point mouse=CursorPoint(); double dx=mouse.X-(Left+Width/2);
            if(free && Profile.Wander) {
                if(Profile.FollowMouse) { moving=Math.Abs(dx)>100; dir=dx<0 ? -1 : 1; }
                else if(now>nextWander) { moving=!moving; dir=random.Next(2)==0 ? -1 : 1; nextWander=now+(moving ? 3+random.NextDouble()*3 : 8+random.NextDouble()*10); }
            } else moving=false;
            if(moving) {
                Left+=dir*30*dt; Rect a=WorkArea();
                if(Left<a.Left || Left+Width>a.Right) { Clamp(); dir*=-1; }
            }
            if(free && !moving && now>nextIdle) {
                nextIdle=now+20+random.NextDouble()*25;
                if(Profile.Satiety<25) { Action("snack",3); Say("有一点饿，零食盒在哪里？",4); }
                else { string[] codes={"glasses","stretch","peek"}; Action(codes[random.Next(codes.Length)],3); }
                if(random.Next(3)==0) SpeakCustomLine();
            }
            if(now>nextBlink) { blinkUntil=now+.14; nextBlink=now+3+random.NextDouble()*4; }
            int pose=0; string action=CurrentAction;
            if(sleeping || (Host.FocusActive && action=="") || (attachment!=IntPtr.Zero && action=="")) pose=6;
            else if(action=="wave" || action=="heart" || action=="happy" || action=="pet") pose=3;
            else if(action=="glasses") pose=4;
            else if(action=="stretch") pose=5;
            else if(action=="sit") pose=6;
            else if(action=="drink") pose=7;
            else if(action=="snack") pose=8;
            else if(moving) pose=((int)(now*3.2)%2)+1;
            bool extra=Profile.Outfit=="coat" && String.IsNullOrEmpty(Profile.CustomAtlas);
            Sprite chosen=ActiveAtlas.Get(pose);
            if(extra && action=="pet") chosen=Atlas.LoadBuiltin("extra").Frames[1];
            if(extra && action=="shy") chosen=Atlas.LoadBuiltin("extra").Frames[2];
            if(extra && action=="peek") chosen=Atlas.LoadBuiltin("extra").Frames[3];
            if(extra && free && !moving && action=="" && now<blinkUntil) chosen=Atlas.LoadBuiltin("extra").Frames[0];
            if(extra && action=="yawn") chosen=Atlas.LoadBuiltin("rest").Frames[0];
            if(extra && action=="lie") chosen=Atlas.LoadBuiltin("rest").Frames[1];
            if(extra && sleeping) chosen=Atlas.LoadBuiltin("rest").Frames[now-sleepSince<3 ? 0 : 2];
            SetSprite(chosen);
            scaleBody.ScaleY=1+Math.Sin(now*(sleeping ? 1.3 : 2.1))*(sleeping ? .004 : .002);
            rotate.Angle=action=="happy" ? Math.Sin(now*8)*4 : action=="shy" ? Math.Sin(now*4)*2 : moving ? Math.Sin(now*5)*.6 : Profile.FollowMouse && free ? Math.Max(-1.2,Math.Min(1.2,dx/400)) : 0;
            shift.Y=moving ? -Math.Abs(Math.Sin(now*10))*2 : 0;
            shift.X=action=="shy" ? Math.Sin(now*3)*7 : 0;
            body.Opacity=sleeping ? .9 : 1;
        }
        IntPtr WindowHook(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
            if(msg!=0x84 || pressed || menu.IsOpen || sprite==null) return IntPtr.Zero;
            long value=lp.ToInt64(); Point local=PointFromScreen(new Point((short)(value&0xffff),(short)((value>>16)&0xffff)));
            double f=Math.Min(body.Width/sprite.Width,body.Height/sprite.Height);
            double ox=9+(body.Width-sprite.Width*f)/2,oy=91+(body.Height-sprite.Height*f)/2;
            int x=(int)Math.Floor((local.X-ox)/f),y=(int)Math.Floor((local.Y-oy)/f);
            if(!sprite.Opaque(x,y)) { handled=true; return new IntPtr(-1); } return IntPtr.Zero;
        }
        public bool PassesThroughPadding() {
            Point s=PointToScreen(new Point(1,1)); long lp=((long)(ushort)(short)s.Y<<16)|(ushort)(short)s.X; bool handled=false;
            return WindowHook(IntPtr.Zero,0x84,IntPtr.Zero,new IntPtr(lp),ref handled)==new IntPtr(-1) && handled;
        }
        public void SavePreview(string path) {
            UpdateLayout(); var r=new RenderTargetBitmap((int)Width,(int)Height,96,96,PixelFormats.Pbgra32); r.Render(canvas);
            var enc=new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(r)); using(var f=File.Create(path)) enc.Save(f);
        }
    }
    public class FoodBox : Window {
        public FoodBox(PetWindow pet) {
            Title="零食盒"; Width=235; Height=240; ResizeMode=ResizeMode.NoResize; Topmost=true; ShowInTaskbar=false;
            Background=new SolidColorBrush(Color.FromRgb(252,249,242));
            var stack=new StackPanel { Margin=new Thickness(16) }; Content=stack;
            stack.Children.Add(new TextBlock { Text="拖到人物身上，或单击投喂",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),FontSize=12 });
            string[] names={"🍪  饼干","🧋  奶茶","💧  清水"},ids={"cookie","tea","water"};
            for(int i=0;i<names.Length;i++) {
                string id=ids[i]; var b=new Button { Content=names[i],Height=38,Margin=new Thickness(0,0,0,8),FontSize=14 }; stack.Children.Add(b);
                Point start=new Point(); bool drag=false;
                b.PreviewMouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) { start=e.GetPosition(b); drag=false; };
                b.PreviewMouseMove+=delegate(object s,MouseEventArgs e) {
                    if(e.LeftButton==MouseButtonState.Pressed && !drag && (e.GetPosition(b)-start).Length>5) { drag=true; DragDrop.DoDragDrop(b,new DataObject("PortraitPetFood",id),DragDropEffects.Copy); }
                };
                b.Click+=delegate { if(!drag) pet.Feed(id); };
            }
        }
    }
}
