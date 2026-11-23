using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace PortraitPetPlus {
    public class ControlPanel : Window {
        readonly AppHost host;
        readonly TabControl tabs=new TabControl();
        readonly ComboBox selected=new ComboBox();
        readonly TextBlock state=new TextBlock(),summary=new TextBlock(),focusLabel=new TextBlock(),waterLabel=new TextBlock(),recordLabel=new TextBlock();
        readonly Image preview=new Image();
        StackPanel appearance,voicePanel;
        TextBox lines;
        int profileIndex;
        bool recording;
        string recordAlias,recordFile;
        readonly DispatcherTimer recordTimer=new DispatcherTimer();
        DateTime recordStart;
        readonly MediaPlayer audioPreview=new MediaPlayer();
        static Brush Ink=new SolidColorBrush(Color.FromRgb(54,62,57));
        static Brush Muted=new SolidColorBrush(Color.FromRgb(105,113,105));
        static Brush Accent=new SolidColorBrush(Color.FromRgb(57,102,80));
        [DllImport("winmm.dll",CharSet=CharSet.Unicode)] static extern int mciSendString(string command,StringBuilder result,int size,IntPtr callback);
        public ControlPanel(AppHost app) {
            host=app; Title="陪伴控制台 · 真人桌宠增强版"; Width=810; Height=760; MinWidth=710; MinHeight=600;
            WindowStartupLocation=WindowStartupLocation.CenterScreen; Background=new SolidColorBrush(Color.FromRgb(246,247,242));
            FontFamily=new FontFamily("Microsoft YaHei UI"); FontSize=13; Foreground=Ink;
            var root=new DockPanel { Margin=new Thickness(24,20,24,14) }; Content=root;
            var header=new StackPanel { Margin=new Thickness(0,0,0,16) }; DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
            header.Children.Add(new TextBlock { Text="把陪伴，放在桌面上。",FontSize=24,FontWeight=FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text="动作、专注、提醒与日常互动，都在这里。",Foreground=Muted,Margin=new Thickness(0,5,0,12) });
            var choose=new StackPanel { Orientation=Orientation.Horizontal };
            choose.Children.Add(new TextBlock { Text="当前角色",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0) });
            selected.Width=170; selected.Height=30; selected.Items.Add(new ComboBoxItem { Content="宋雨杨" }); selected.Items.Add(new ComboBoxItem { Content="song雨杨" }); selected.SelectedIndex=0;
            selected.SelectionChanged+=delegate { if(recording) StopRecording(); profileIndex=selected.SelectedIndex; RebuildAppearance(); RebuildVoice(); RefreshStatus(); };
            choose.Children.Add(selected); header.Children.Add(choose);
            state.Foreground=Muted; state.FontSize=11; state.Margin=new Thickness(0,10,0,0); state.TextWrapping=TextWrapping.Wrap; DockPanel.SetDock(state,Dock.Bottom); root.Children.Add(state);
            tabs.BorderThickness=new Thickness(0); tabs.Background=Brushes.Transparent; root.Children.Add(tabs);
            AddTab("陪伴",BuildCompanion()); AddTab("专注与提醒",BuildFocus());
            appearance=new StackPanel(); AddTab("形象与互动",appearance);
            voicePanel=new StackPanel(); AddTab("台词与声音",voicePanel);
            RebuildAppearance(); RebuildVoice();
            recordTimer.Interval=TimeSpan.FromSeconds(1); recordTimer.Tick+=delegate { recordLabel.Text="录音中 · "+(int)(DateTime.Now-recordStart).TotalSeconds+" / 30 秒"; if((DateTime.Now-recordStart).TotalSeconds>=30) StopRecording(); };
            Closed+=delegate { if(recording) StopRecording(); audioPreview.Close(); };
            audioPreview.MediaFailed+=delegate { host.SetStatus("试听失败，请使用系统支持的 WAV、MP3 或 M4A 文件。"); };
            RefreshStatus();
        }
        PetProfile Profile { get { return host.Settings.Pets[profileIndex]; } }
        PetWindow Actor { get { return host.FindPet(profileIndex); } }
        static TextBlock Text(string text,int size=13,Brush color=null) { return new TextBlock { Text=text,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,9) }; }
        static StackPanel Card(StackPanel parent,string title,string description) {
            var card=new Border { Background=Brushes.White,CornerRadius=new CornerRadius(13),Padding=new Thickness(18,16,18,14),Margin=new Thickness(0,13,0,0),BorderBrush=new SolidColorBrush(Color.FromRgb(226,231,222)),BorderThickness=new Thickness(1) };
            var inner=new StackPanel(); card.Child=inner; parent.Children.Add(card);
            inner.Children.Add(Text(title,17)); if(!String.IsNullOrEmpty(description)) inner.Children.Add(Text(description,12,Muted)); return inner;
        }
        static Button Button(string title,Action action,bool primary=false) {
            var b=new Button { Content=title,Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,4,8,4),Background=primary ? Accent : new SolidColorBrush(Color.FromRgb(243,246,239)),Foreground=primary ? Brushes.White : Ink,BorderBrush=new SolidColorBrush(Color.FromRgb(214,224,212)),BorderThickness=new Thickness(1),Cursor=System.Windows.Input.Cursors.Hand };
            b.Click+=delegate { action(); }; return b;
        }
        static void Buttons(StackPanel parent,params Button[] buttons) { var row=new WrapPanel(); foreach(var b in buttons) row.Children.Add(b); parent.Children.Add(row); }
        static CheckBox Check(string title,bool value,Action<bool> change) { var b=new CheckBox { Content=title,IsChecked=value,Margin=new Thickness(0,7,0,7) }; b.Click+=delegate { change(b.IsChecked==true); }; return b; }
        static TextBox Field(string value,int width=180) { return new TextBox { Text=value,Width=width,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,0,0,10) }; }
        void AddTab(string label,StackPanel content) {
            tabs.Items.Add(new TabItem { Header=label,Padding=new Thickness(13,9,13,9),Content=new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,12,10) } });
        }
        StackPanel BuildCompanion() {
            var p=new StackPanel(); var card=Card(p,"今天，也一起待一会儿。","摸头会增加好感；投喂和休息会影响状态。桌宠的操作都在本机完成。");
            var row=new DockPanel(); preview.Width=150; preview.Height=190; preview.Margin=new Thickness(0,0,22,0); DockPanel.SetDock(preview,Dock.Left); row.Children.Add(preview);
            summary.TextWrapping=TextWrapping.Wrap; summary.FontSize=14; summary.LineHeight=26; summary.VerticalAlignment=VerticalAlignment.Center; row.Children.Add(summary); card.Children.Add(row);
            Buttons(card,Button("摸摸头",delegate { WithActor(a=>a.Pat()); },true),Button("打开零食盒",delegate { WithActor(a=>a.OpenFoodBox()); }),Button("休息 / 唤醒",delegate { WithActor(a=>a.ToggleSleep()); }));
            var c=Card(p,"日常小动作","内置四套衣服都包含挥手、整理眼镜、伸懒腰、坐下、喝水、吃饼干和两步走动。原外套还带眨眼、摸头表情、害羞、探头、打哈欠和趴下。");
            string[] labels={"挥手","整理眼镜","伸懒腰","坐下","喝水","吃饼干","探头","打哈欠 · 原外套","趴下 · 原外套","比心 · 40 好感","摇摆 · 120 好感"};
            string[] codes={"wave","glasses","stretch","sit","drink","snack","peek","yawn","lie","heart","happy"};
            var wrap=new WrapPanel(); c.Children.Add(wrap);
            for(int i=0;i<labels.Length;i++) { string code=codes[i]; wrap.Children.Add(Button(labels[i],delegate { WithActor(a=>a.Action(code,code=="sit" ? 20 : 4)); })); }
            var buddy=Card(p,"两个人，一起陪伴","第二角色默认是同一人物的分身，可在「形象与互动」中导入另一个角色的透明 PNG。");
            buddy.Children.Add(Check("开启双人陪伴",host.Settings.BuddyEnabled,delegate(bool v) { host.ToggleBuddy(v); }));
            Buttons(buddy,Button("递饼干，一起坐坐",host.Social),Button("找回全部桌宠",delegate { foreach(var a in host.Pets) a.GoHome(); }));
            return p;
        }
        StackPanel BuildFocus() {
            var p=new StackPanel(); var c=Card(p,"安静陪你，做完一件事。","专注时桌宠会坐下并停止走动。计时结束进入休息；暂停计时后，剩余时间保持不变。");
            focusLabel.FontSize=27; focusLabel.FontWeight=FontWeights.SemiBold; focusLabel.Foreground=Accent; focusLabel.Margin=new Thickness(0,8,0,14); c.Children.Add(focusLabel);
            Buttons(c,Button("开始",host.StartFocus,true),Button("暂停 / 继续",host.PauseFocus),Button("结束本次",host.StopFocus));
            var water=Card(p,"喝水，也别忘记。","提醒会显示在桌宠气泡和系统托盘中。关闭程序后不再提醒。");
            waterLabel.Foreground=Accent; waterLabel.Margin=new Thickness(0,6,0,8); water.Children.Add(waterLabel);
            water.Children.Add(Check("开启喝水提醒",host.Settings.WaterEnabled,delegate(bool v) { host.Settings.WaterEnabled=v; host.ResetWaterSchedule(); host.Save(); }));
            water.Children.Add(Check("专注期间也允许喝水提醒",host.Settings.WaterDuringFocus,delegate(bool v) { host.Settings.WaterDuringFocus=v; host.Save(); }));
            Buttons(water,Button("喝过了",host.AcknowledgeWater,true),Button("10 分钟后提醒",host.SnoozeWater));
            var time=Card(p,"按自己的节奏来","单位：分钟。时间设置应用于下一段计时；保存后重新安排下一次喝水提醒。");
            var row=new WrapPanel(); time.Children.Add(row);
            TextBox f=NumberInput(row,"专注时长",host.Settings.FocusMinutes),b=NumberInput(row,"休息时长",host.Settings.BreakMinutes),w=NumberInput(row,"喝水间隔",host.Settings.WaterMinutes);
            Buttons(time,Button("保存时间设置",delegate {
                int fv,bv,wv;
                if(!Int32.TryParse(f.Text,out fv) || fv<1 || fv>180 || !Int32.TryParse(b.Text,out bv) || bv<1 || bv>60 || !Int32.TryParse(w.Text,out wv) || wv<5 || wv>240) { host.SetStatus("专注填 1～180，休息填 1～60，喝水间隔填 5～240。"); return; }
                host.Settings.FocusMinutes=fv; host.Settings.BreakMinutes=bv; host.Settings.WaterMinutes=wv; host.ResetWaterSchedule(); host.Save(); RefreshStatus();
            })); return p;
        }
        TextBox NumberInput(WrapPanel row,string label,int value) { var s=new StackPanel { Margin=new Thickness(0,0,18,0) }; s.Children.Add(Text(label,12,Muted)); var t=Field(value.ToString(),125); s.Children.Add(t); row.Children.Add(s); return t; }
        void WithActor(Action<PetWindow> action) { if(Actor==null) { host.ToggleBuddy(true); } if(Actor!=null) action(Actor); RefreshStatus(); }
        void RebuildAppearance() {
            if(appearance==null) return; appearance.Children.Clear();
            var identity=Card(appearance,"这个角色的样子","上方切换角色，分别设置名称、服装、大小和行为。");
            var name=Field(Profile.Name,230); identity.Children.Add(Text("显示名称（最多 16 字）",12,Muted)); identity.Children.Add(name);
            Buttons(identity,Button("保存名称",delegate { string n=name.Text.Trim(); if(n.Length<1 || n.Length>16) { host.SetStatus("名称请填写 1～16 字。"); return; } Profile.Name=n; if(Actor!=null) Actor.Title="真人桌宠增强版 · "+n; host.Save(); RefreshStatus(); }));
            identity.Children.Add(Text("服装",12,Muted)); var wardrobe=new ComboBox { Width=230,Height=32,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,12) };
            string[] ids={"coat","home","scarf","festival"}; foreach(string s in new[]{"原照片 · 深色外套","家居 · 奶油色卫衣","冬日 · 围巾针织帽","节日 · 酒红开衫"}) wardrobe.Items.Add(s);
            wardrobe.SelectedIndex=Array.IndexOf(ids,Profile.Outfit);
            wardrobe.SelectionChanged+=delegate { if(wardrobe.SelectedIndex<0) return; Profile.Outfit=ids[wardrobe.SelectedIndex]; Profile.CustomAtlas=""; host.ReloadPet(profileIndex); RefreshStatus(); };
            identity.Children.Add(wardrobe);
            identity.Children.Add(Text("桌宠大小",12,Muted)); var size=new Slider { Minimum=170,Maximum=440,Value=Profile.Size,TickFrequency=10,IsSnapToTickEnabled=true,Width=330,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,10) };
            size.ValueChanged+=delegate { Profile.Size=size.Value; if(Actor!=null) Actor.ResizePet(); }; size.PreviewMouseLeftButtonUp+=delegate { host.Save(); }; identity.Children.Add(size);
            var behavior=Card(appearance,"互动与作息","跟随鼠标会沿水平方向追随光标，并轻轻转身。自动作息：22:00～08:00 或电脑闲置 5 分钟时休息，手动唤醒后两小时内优先听你的。");
            behavior.Children.Add(Check("允许走动",Profile.Wander,delegate(bool v) { Profile.Wander=v; host.Save(); }));
            behavior.Children.Add(Check("跟随鼠标",Profile.FollowMouse,delegate(bool v) { Profile.FollowMouse=v; host.Save(); }));
            behavior.Children.Add(Check("自动作息",Profile.AutoRoutine,delegate(bool v) { Profile.AutoRoutine=v; host.Save(); }));
            behavior.Children.Add(Check("显示在其他窗口前面",Profile.Topmost,delegate(bool v) { Profile.Topmost=v; if(Actor!=null) Actor.Topmost=v; host.Save(); }));
            Buttons(behavior,Button("坐到刚才的窗口上",delegate { WithActor(a=>a.AttachToWindow()); }),Button("趴到任务栏旁",delegate { WithActor(a=>{ a.GoHome(); a.Action("lie",30); }); }));
            var custom=Card(appearance,"导入第二人物 / 自定义形象","支持透明 PNG 单张，或等分 2×2 / 3×3 动作表。普通照片需要先抠图。3×3 顺序：站立、迈步一、迈步二、挥手、整理眼镜、伸展、坐下、喝水、吃饼干。");
            var layout=new ComboBox { Width=230,Height=32,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8) };
            layout.Items.Add("单张透明 PNG"); layout.Items.Add("2 × 2 动作表（原版兼容）"); layout.Items.Add("3 × 3 动作表"); layout.SelectedIndex=0; custom.Children.Add(layout);
            Buttons(custom,Button("选择透明 PNG",delegate {
                var dialog=new Forms.OpenFileDialog { Filter="透明 PNG 图片|*.png",Title="选择角色素材" };
                if(dialog.ShowDialog()!=Forms.DialogResult.OK) return;
                try { int cells=layout.SelectedIndex==0 ? 1 : layout.SelectedIndex==1 ? 2 : 3; host.ImportAtlas(profileIndex,dialog.FileName,cells,cells); host.SetStatus("自定义角色已导入，素材会保存在本机。 "); RefreshStatus(); }
                catch(Exception ex) { host.SetStatus("导入失败："+ex.Message); }
            }),Button("恢复内置形象",delegate { Profile.CustomAtlas=""; host.ReloadPet(profileIndex); RefreshStatus(); }));
        }
        void RebuildVoice() {
            if(voicePanel==null) return; voicePanel.Children.Clear();
            var c=Card(voicePanel,"把口头禅留在这里","每行一句，最多 20 句，每句最多 80 字。单击桌宠身体或点击「说一句」，会随机显示一句。");
            lines=new TextBox { Text=String.Join(Environment.NewLine,Profile.Lines),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=140,Padding=new Thickness(10),Margin=new Thickness(0,5,0,10) }; c.Children.Add(lines);
            Buttons(c,Button("保存台词",delegate {
                string[] list=lines.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();
                if(list.Length<1 || list.Length>20 || list.Any(x=>x.Length>80)) { host.SetStatus("请填 1～20 句台词，每句不超过 80 字。"); return; }
                Profile.Lines=list; host.Save(); RefreshStatus();
            },true),Button("说一句",delegate { WithActor(a=>a.SpeakCustomLine()); }));
            var sound=Card(voicePanel,"熟悉的声音","可以导入声音文件，或使用麦克风录一段最多 30 秒的声音。互动和提醒时播放这段录音。声音文件不会被上传；不会模仿或克隆声音。");
            sound.Children.Add(Check("启用互动与提醒声音",Profile.SoundEnabled,delegate(bool v) { Profile.SoundEnabled=v; host.Save(); }));
            recordLabel.Text=String.IsNullOrEmpty(Profile.VoiceFile) ? "尚未设置声音" : "已设置声音："+Path.GetFileName(Profile.VoiceFile);
            recordLabel.TextWrapping=TextWrapping.Wrap; recordLabel.Foreground=Muted; recordLabel.Margin=new Thickness(0,6,0,8);
            // A UIElement can only have one parent when changing the selected pet.
            if(recordLabel.Parent is Panel) ((Panel)recordLabel.Parent).Children.Remove(recordLabel);
            sound.Children.Add(recordLabel);
            Buttons(sound,Button("导入声音",delegate {
                var d=new Forms.OpenFileDialog { Filter="声音文件|*.wav;*.mp3;*.m4a" };
                if(d.ShowDialog()!=Forms.DialogResult.OK) return;
                try { Profile.VoiceFile=host.ImportVoice(d.FileName); host.Save(); RebuildVoice(); }
                catch(Exception ex) { host.SetStatus(ex.Message); }
            }),Button("试听",delegate {
                if(!File.Exists(Profile.VoiceFile)) { host.SetStatus("请先导入声音或完成录音。"); return; }
                try { audioPreview.Open(new Uri(Profile.VoiceFile)); audioPreview.Play(); } catch(Exception ex) { host.SetStatus(ex.Message); }
            }),Button("停止播放",delegate { audioPreview.Stop(); }));
            Buttons(sound,Button("开始录音",StartRecording),Button("结束并保存",StopRecording),Button("清除声音设置",delegate { if(recording) StopRecording(); Profile.VoiceFile=""; Profile.SoundEnabled=false; host.Save(); RebuildVoice(); }));
        }
        void StartRecording() {
            if(recording) return;
            string dir=Path.Combine(host.DataRoot,"media"); Directory.CreateDirectory(dir);
            recordAlias="petrecord"+Guid.NewGuid().ToString("N"); recordFile=Path.Combine(dir,"recording-"+Guid.NewGuid().ToString("N")+".wav");
            int result=mciSendString("open new type waveaudio alias "+recordAlias,null,0,IntPtr.Zero);
            if(result==0) result=mciSendString("record "+recordAlias,null,0,IntPtr.Zero);
            if(result!=0) { mciSendString("close "+recordAlias,null,0,IntPtr.Zero); host.SetStatus("无法开始录音，请检查麦克风是否可用，以及系统是否允许桌面应用使用麦克风。"); return; }
            recording=true; recordStart=DateTime.Now; recordLabel.Text="录音中 · 0 / 30 秒"; recordTimer.Start();
        }
        void StopRecording() {
            if(!recording) return; recording=false; recordTimer.Stop();
            mciSendString("stop "+recordAlias,null,0,IntPtr.Zero);
            int code=mciSendString("save "+recordAlias+" \""+recordFile+"\"",null,0,IntPtr.Zero); mciSendString("close "+recordAlias,null,0,IntPtr.Zero);
            if(code!=0 || !File.Exists(recordFile)) { host.SetStatus("录音保存失败，请重试或导入 WAV 文件。"); return; }
            Profile.VoiceFile=recordFile; Profile.SoundEnabled=true; host.Save(); recordLabel.Text="录音已保存，已启用声音。";
        }
        public void RefreshStatus() {
            if(!IsInitialized) return;
            var p=Profile; var a=Actor;
            ((ComboBoxItem)selected.Items[0]).Content=host.Settings.Pets[0].Name;
            ((ComboBoxItem)selected.Items[1]).Content=host.Settings.Pets[1].Name;
            summary.Text=p.Name+"  ·  "+(a==null ? "未显示" : a.Mood)+"\n好感 "+p.Affinity+" / 999   饱腹 "+(int)p.Satiety+" / 100\n精力 "+(int)p.Energy+" / 100\n摸头 "+p.HeadPets+" 次 · 投喂 "+p.Snacks+" 次\n"+(p.Affinity>=120 ? "已解锁：比心、开心摇摆" : p.Affinity>=40 ? "已解锁：比心；120 好感解锁开心摇摆" : "40 好感解锁比心；120 好感解锁开心摇摆");
            try { preview.Source=(a!=null ? a.ActiveAtlas : Atlas.LoadBuiltin(p.Outfit)).Get(0).Image; } catch {}
            focusLabel.Text=host.Focus.Label(DateTime.UtcNow);
            double minutes=Math.Max(0,(host.NextWaterUtc-DateTime.UtcNow).TotalMinutes);
            waterLabel.Text="今日喝水打卡 "+host.Settings.WaterToday+" 次  ·  完成专注 "+host.Settings.FocusCompleted+" 段\n"+(!host.Settings.WaterEnabled ? "喝水提醒已关闭" : host.WaterPending ? "喝水提醒待确认" : "下次提醒约 "+Math.Ceiling(minutes)+" 分钟后");
            state.Text=host.Status+"  ·  设置自动保存在本机";
        }
        public void SelectTab(int index) { tabs.SelectedIndex=index; }
        public void ExerciseProfileSwitch() { selected.SelectedIndex=1; selected.SelectedIndex=0; }
        public void SavePreview(string path) {
            UpdateLayout(); var b=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32); b.Render(this);
            var enc=new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(b)); using(var f=File.Create(path)) enc.Save(f);
        }
    }
}
