using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace KartuBacaNgaju {
    // Keep fonts alive while controls can still reference them. WinForms may
    // retain its existing Font when an equal-valued Font is assigned.
    public sealed class ButtonFontCache : IDisposable {
        readonly Dictionary<int,Font> fonts=new Dictionary<int,Font>();
        public Font Get(float pixels) {
            int key=Math.Max(4,(int)Math.Round(pixels*4)); Font font;
            if(!fonts.TryGetValue(key,out font)) {
                font=new Font("Segoe UI",key/4f,FontStyle.Bold,GraphicsUnit.Pixel); fonts.Add(key,font);
            }
            return font;
        }
        public void Dispose() { foreach(Font font in fonts.Values) font.Dispose(); fonts.Clear(); }
    }
    public sealed class PictureButton : Button {
        public Color TopColor=Color.FromArgb(85,139,221), BottomColor=Color.FromArgb(43,83,161);
        bool hover, pressed;
        public PictureButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor,true);
            BackColor=Color.Transparent; FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0;
            Cursor=Cursors.Hand; UseVisualStyleBackColor=false;
        }
        static GraphicsPath Rounded(RectangleF r,float radius) {
            GraphicsPath p=new GraphicsPath(); float d=radius*2;
            p.AddArc(r.Left,r.Top,d,d,180,90); p.AddArc(r.Right-d,r.Top,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.Left,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; pressed=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            float s=Math.Max(.5f,Height/88f), pad=4*s;
            RectangleF face=new RectangleF(pad,pad+(pressed?2*s:0),Width-2*pad-3*s,Height-2*pad-5*s);
            RectangleF shadow=face; shadow.Offset(2*s,4*s);
            using(GraphicsPath path=Rounded(shadow,11*s)) using(SolidBrush brush=new SolidBrush(Color.FromArgb(70,44,33,20))) g.FillPath(brush,path);
            Color top=hover?ControlPaint.Light(TopColor):TopColor;
            using(GraphicsPath path=Rounded(face,11*s))
            using(LinearGradientBrush brush=new LinearGradientBrush(face,pressed?BottomColor:top,BottomColor,90f))
            using(Pen pen=new Pen(Color.White,3*s)) { g.FillPath(brush,path); g.DrawPath(pen,path); }
            using(StringFormat format=new StringFormat()) using(SolidBrush brush=new SolidBrush(Color.White)) {
                format.Alignment=StringAlignment.Center; format.LineAlignment=StringAlignment.Center;
                g.DrawString(Text,Font,brush,face,format);
            }
            if(Focused && ShowFocusCues) {
                Rectangle focus=Rectangle.Round(face); focus.Inflate(-(int)(7*s),-(int)(7*s));
                ControlPaint.DrawFocusRectangle(g,focus,Color.White,BottomColor);
            }
        }
    }
    public sealed class Card {
        public string Letter, Word, Spelling, Syllables, Meaning, ImagePath;
        public static List<Card> Load(string root) {
            XmlDocument doc = new XmlDocument(); doc.XmlResolver = null;
            doc.Load(Path.Combine(root, "data/kartu.xml"));
            List<Card> cards = new List<Card>();
            foreach (XmlNode n in doc.SelectNodes("/cards/card")) {
                Card c = new Card();
                c.Letter=n.Attributes["letter"].Value; c.Word=n["word"].InnerText;
                c.Spelling=n["spelling"].InnerText; c.Syllables=n["syllables"].InnerText;
                c.Meaning=n["meaning"].InnerText;
                c.ImagePath=Path.Combine(root,n["image"].InnerText.Replace('/',Path.DirectorySeparatorChar));
                if (!File.Exists(c.ImagePath)) throw new IOException("Gambar tidak ditemukan: " + c.ImagePath);
                cards.Add(c);
            }
            if(cards.Count!=26) throw new InvalidDataException("Data harus berisi 26 kartu.");
            for(int i=0;i<26;i++) if(cards[i].Letter!=((char)('A'+i)).ToString())
                throw new InvalidDataException("Urutan kartu harus A sampai Z.");
            return cards;
        }
    }
    public sealed class Deck {
        public int[] Order; public int Position; public bool RandomMode;
        readonly Random rng = new Random();
        public void Start(bool random, int first) {
            RandomMode=random; Order=new int[26];
            for(int i=0;i<26;i++) Order[i]=i;
            if(random) for(int i=25;i>0;i--) { int j=rng.Next(i+1); int t=Order[i]; Order[i]=Order[j]; Order[j]=t; }
            Position=random?0:first;
        }
        public int Current { get { return Order[Position]; } }
        public bool Move(int delta) {
            int next=Position+delta; if(next<0 || next>=26) return false;
            Position=next; return true;
        }
    }
    public sealed class AudioPlayer : IDisposable {
        [DllImport("winmm.dll", CharSet=CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder result, int length, IntPtr callback);
        [DllImport("winmm.dll", CharSet=CharSet.Unicode)]
        static extern bool mciGetErrorString(int error, StringBuilder text, int length);
        bool opened;
        public void Stop() { if(opened) { mciSendString("close ngaju_audio",null,0,IntPtr.Zero); opened=false; } }
        void Command(string command) {
            int result=mciSendString(command,null,0,IntPtr.Zero);
            if(result!=0) { StringBuilder text=new StringBuilder(512); mciGetErrorString(result,text,512); throw new IOException(text.ToString()); }
        }
        public void Play(string path) {
            Stop(); if(!File.Exists(path)) throw new FileNotFoundException("Audio belum tersedia",path);
            try { Command("open \""+path+"\" type mpegvideo alias ngaju_audio"); opened=true; Command("play ngaju_audio from 0"); }
            catch { Stop(); throw; }
        }
        public bool IsPlaying {
            get { if(!opened) return false; StringBuilder b=new StringBuilder(64);
                return mciSendString("status ngaju_audio mode",b,64,IntPtr.Zero)==0 && b.ToString()=="playing"; }
        }
        public void Dispose() { Stop(); }
    }
    public sealed class ReaderForm : Form {
        readonly string root;
        readonly List<Card> cards;
        readonly Deck deck=new Deck();
        readonly AudioPlayer audio=new AudioPlayer();
        readonly Dictionary<Control,RectangleF> layout=new Dictionary<Control,RectangleF>();
        readonly ButtonFontCache buttonFonts=new ButtonFontCache();
        readonly List<Image> pictures=new List<Image>();
        readonly Timer timer=new Timer();
        Image frame; bool menu=true, full=false, wasPlaying=false;
        bool arranging, changingWindow, resourcesDisposed;
        Rectangle restoreBounds; FormWindowState restoreState;
        float scale=1, ox=0, oy=0; string message="Pilih huruf untuk mulai belajar.";
        Button previous,next,listen,stop,home,fullscreen;
        Color ink=Color.FromArgb(43,57,47), green=Color.FromArgb(31,89,68), cream=Color.FromArgb(255,249,234);
        public ReaderForm(string folder) {
            root=folder; cards=Card.Load(root);
            foreach(Card c in cards) pictures.Add(Image.FromFile(c.ImagePath));
            frame=Image.FromFile(Path.Combine(root,"gambar/bingkai.jpeg"));
            Text="Kartu Baca Basa Dayak Ngaju"; ClientSize=new Size(1200,800);
            MinimumSize=new Size(800,570); StartPosition=FormStartPosition.CenterScreen;
            AutoScaleMode=AutoScaleMode.None;
            DoubleBuffered=true; KeyPreview=true; BackColor=Color.FromArgb(230,224,210);
            ShowMenu(); Resize+=delegate { if(!changingWindow) { Arrange(); Invalidate(); } };
            KeyDown+=HandleKeys;
            timer.Interval=200; timer.Tick+=delegate {
                if(wasPlaying && !audio.IsPlaying) { wasPlaying=false; message="Audio selesai. Klik Dengarkan Ejaan untuk mengulang."; Invalidate(); }
            }; timer.Start();
            FormClosing+=delegate { timer.Stop(); audio.Stop(); };
        }
        protected override void Dispose(bool disposing) {
            if(disposing && !resourcesDisposed) { timer.Stop(); audio.Stop(); }
            base.Dispose(disposing);
            if(disposing && !resourcesDisposed) {
                resourcesDisposed=true; timer.Dispose(); audio.Dispose();
                foreach(Image i in pictures) i.Dispose(); if(frame!=null) frame.Dispose();
                buttonFonts.Dispose();
            }
        }
        Button AddButton(string text, float x,float y,float w,float h, EventHandler action, bool primary) {
            Button b=new Button(); b.Text=text; b.FlatStyle=FlatStyle.Flat; b.FlatAppearance.BorderSize=1;
            b.FlatAppearance.BorderColor=primary?green:Color.FromArgb(213,219,201);
            b.BackColor=primary?green:Color.White; b.ForeColor=primary?Color.White:ink;
            b.Cursor=Cursors.Hand; b.UseVisualStyleBackColor=false; b.Click+=action;
            b.AccessibleName=text.Replace("\n"," "); b.Tag=18f;
            layout.Add(b,new RectangleF(x,y,w,h)); Controls.Add(b); return b;
        }
        void ClearButtons() {
            Control[] old=new Control[Controls.Count]; Controls.CopyTo(old,0);
            Controls.Clear(); foreach(Control c in old) c.Dispose(); layout.Clear();
        }
        void HeaderButtons() {
            home=AddButton("Menu Utama",800,22,170,48,delegate { ShowMenu(); },false);
            home.Visible=!menu;
            fullscreen=AddButton(full?"Keluar Layar Penuh":"Layar Penuh (F11)",984,22,194,48,delegate { ToggleFull(); },true);
            fullscreen.Tag=15f;
        }
        Button AddPictureButton(string text,float x,float y,float w,float h,EventHandler action,bool warm) {
            PictureButton b=new PictureButton(); b.Text=text; b.AccessibleName=text; b.Tag=36f; b.Click+=action;
            if(warm) { b.TopColor=Color.FromArgb(53,133,96); b.BottomColor=Color.FromArgb(23,82,58); }
            layout.Add(b,new RectangleF(x,y,w,h)); Controls.Add(b); return b;
        }
        void ShowMenu() {
            StopAudio(); menu=true; message="Pilih huruf untuk mulai belajar."; ClearButtons();
            fullscreen=AddPictureButton(full?"Keluar Layar Penuh":"Layar Penuh (F11)",1006,12,182,43,delegate { ToggleFull(); },true);
            fullscreen.Tag=14f;
            int offset=0;
            for(int row=0;row<4;row++) {
                int count=8-row; float start=(1200-(count*88+(count-1)*12))/2f;
                for(int col=0;col<count;col++) {
                    int index=offset+col;
                    Button b=AddPictureButton(cards[index].Letter,start+col*100,252+row*96,88,88,
                        delegate { deck.Start(false,index); ShowCard(); },false);
                    b.AccessibleName="Huruf "+cards[index].Letter+", "+cards[index].Word;
                }
                offset+=count;
            }
            AddPictureButton("Mulai dari A",365,640,225,48,delegate { deck.Start(false,0); ShowCard(); },true).Tag=19f;
            AddPictureButton("Belajar Acak",610,640,225,48,delegate { deck.Start(true,0); ShowCard(); },true).Tag=19f;
            Arrange(); Invalidate();
        }
        void ShowCard() {
            StopAudio(); menu=false; ClearButtons(); HeaderButtons();
            previous=AddButton("Sebelumnya",74,668,208,56,delegate { Move(-1); },false);
            next=AddButton(deck.Position==25?"Selesai — Menu":"Berikutnya",918,668,208,56,
                delegate { if(deck.Position==25) ShowMenu(); else Move(1); },true);
            listen=AddButton("Dengarkan Ejaan",380,668,275,56,delegate { PlayAudio(); },true);
            stop=AddButton("Hentikan",669,668,174,56,delegate { StopAudio(); message="Audio dihentikan."; Invalidate(); },false);
            previous.Enabled=deck.Position>0;
            message=File.Exists(AudioPath())?"Klik Dengarkan Ejaan atau tekan Spasi.":"Audio belum tersedia.";
            Arrange(); Invalidate();
        }
        string AudioPath() { return Path.Combine(root,"audio/"+cards[deck.Current].Letter+".mp3"); }
        void Move(int delta) { if(deck.Move(delta)) ShowCard(); }
        void StopAudio() { audio.Stop(); wasPlaying=false; }
        void PlayAudio() {
            StopAudio();
            try { audio.Play(AudioPath()); wasPlaying=true; message="Memutar ejaan "+cards[deck.Current].Word+"…"; }
            catch(FileNotFoundException) { message="Audio belum tersedia."; }
            catch(Exception ex) { message="Audio belum dapat diputar."; Program.Log(root,ex.ToString()); }
            Invalidate();
        }
        void ToggleFull() {
            if(changingWindow || IsDisposed) return;
            changingWindow=true; SuspendLayout();
            try {
                if(!full) {
                    Rectangle monitor=Screen.FromControl(this).Bounds;
                    restoreState=WindowState;
                    restoreBounds=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;
                    full=true; WindowState=FormWindowState.Normal;
                    FormBorderStyle=FormBorderStyle.None; Bounds=monitor;
                } else {
                    full=false; WindowState=FormWindowState.Normal;
                    FormBorderStyle=FormBorderStyle.Sizable; Bounds=restoreBounds; WindowState=restoreState;
                }
                fullscreen.Text=full?"Keluar Layar Penuh":"Layar Penuh (F11)";
            } finally { ResumeLayout(false); changingWindow=false; }
            Arrange(); Invalidate(true);
        }
        void HandleKeys(object sender,KeyEventArgs e) {
            bool handled=true;
            if(e.KeyCode==Keys.F11) ToggleFull();
            else if(e.KeyCode==Keys.Escape) { if(full) ToggleFull(); else if(!menu) ShowMenu(); }
            else if(!menu && e.KeyCode==Keys.Right) { if(deck.Position==25) ShowMenu(); else Move(1); }
            else if(!menu && e.KeyCode==Keys.Left) Move(-1);
            else if(!menu && e.KeyCode==Keys.Space) PlayAudio();
            else if(e.KeyCode==Keys.Home) ShowMenu();
            else handled=false;
            if(handled) { e.Handled=true; e.SuppressKeyPress=true; }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            // Buttons normally consume arrow keys for focus movement. Route reading shortcuts first.
            Keys k=keyData & Keys.KeyCode;
            if(k==Keys.F11 || k==Keys.Escape || k==Keys.Home || (!menu && (k==Keys.Left || k==Keys.Right || k==Keys.Space))) {
                HandleKeys(this,new KeyEventArgs(keyData)); return true;
            }
            return base.ProcessCmdKey(ref msg,keyData);
        }
        void Arrange() {
            if(arranging || changingWindow || Disposing || IsDisposed || ClientSize.Width<=0 || ClientSize.Height<=0) return;
            arranging=true; SuspendLayout();
            try {
                scale=Math.Min(ClientSize.Width/1200f,ClientSize.Height/800f);
                ox=(ClientSize.Width-1200*scale)/2; oy=(ClientSize.Height-800*scale)/2;
                foreach(KeyValuePair<Control,RectangleF> entry in layout) {
                    if(entry.Key.IsDisposed) continue;
                    RectangleF r=entry.Value; entry.Key.SetBounds((int)(ox+r.X*scale),(int)(oy+r.Y*scale),(int)(r.Width*scale),(int)(r.Height*scale));
                    Font font=buttonFonts.Get((float)entry.Key.Tag*scale);
                    if(!Object.ReferenceEquals(entry.Key.Font,font)) entry.Key.Font=font;
                }
            } finally { ResumeLayout(false); arranging=false; }
        }
        void TextIn(Graphics g,string text,float size,FontStyle style,Color color,RectangleF bounds,bool centered) {
            using(Font f=new Font("Segoe UI",size,style,GraphicsUnit.Pixel))
            using(SolidBrush brush=new SolidBrush(color))
            using(StringFormat format=new StringFormat()) {
                format.Alignment=centered?StringAlignment.Center:StringAlignment.Near; format.LineAlignment=StringAlignment.Center;
                g.DrawString(text,f,brush,bounds,format);
            }
        }
        void Fill(Graphics g,Color color,float x,float y,float w,float h) { using(SolidBrush b=new SolidBrush(color)) g.FillRectangle(b,x,y,w,h); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); Graphics g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.TranslateTransform(ox,oy); g.ScaleTransform(scale,scale);
            if(menu) {
                g.DrawImage(frame,new RectangleF(0,0,1200,800));
                using(SolidBrush shadow=new SolidBrush(Color.FromArgb(65,61,33,19))) g.FillRectangle(shadow,258,190,690,54);
                using(LinearGradientBrush banner=new LinearGradientBrush(new Rectangle(255,182,690,54),Color.FromArgb(237,64,42),Color.FromArgb(182,30,24),90f))
                    g.FillRectangle(banner,255,182,690,54);
                using(Pen border=new Pen(Color.White,3)) g.DrawRectangle(border,255,182,690,54);
                TextIn(g,"KARTU BACA BASA DAYAK NGAJU",30,FontStyle.Bold,Color.White,new RectangleF(263,184,674,50),true);
                return;
            }
            Fill(g,cream,0,0,1200,800);
            g.DrawImage(frame,new RectangleF(0,85,1200,650));
            Fill(g,cream,55,106,1090,630);
            Fill(g,green,0,0,1200,5);
            TextIn(g,"KARTU BACA",25,FontStyle.Bold,green,new RectangleF(28,15,500,33),false);
            TextIn(g,"Basa Dayak Ngaju",18,FontStyle.Regular,ink,new RectangleF(28,48,500,26),false);
            if(menu) {
                TextIn(g,"Ayo, pilih hurufmu",35,FontStyle.Bold,green,new RectangleF(74,115,700,55),false);
                TextIn(g,"Membaca permulaan • Kelas 1 SD",19,FontStyle.Regular,ink,new RectangleF(74,165,700,32),false);
                TextIn(g,"26 kartu",19,FontStyle.Bold,green,new RectangleF(946,125,180,40),true);
            } else {
                Card c=cards[deck.Current];
                TextIn(g,(deck.RandomMode?"BELAJAR ACAK":"BELAJAR A–Z")+"   /   "+(deck.Position+1)+" dari 26",17,FontStyle.Bold,green,new RectangleF(78,111,600,30),false);
                Fill(g,Color.White,74,160,440,468);
                Image image=pictures[deck.Current]; float ratio=Math.Min(390f/image.Width,385f/image.Height);
                float iw=image.Width*ratio, ih=image.Height*ratio;
                g.DrawImage(image,new RectangleF(294-iw/2,385-ih/2,iw,ih));
                Fill(g,green,534,160,76,76);
                TextIn(g,c.Letter,48,FontStyle.Bold,Color.White,new RectangleF(534,160,76,76),true);
                TextIn(g,c.Word,c.Word.Length>10?37:46,FontStyle.Bold,green,new RectangleF(627,155,500,90),false);
                TextIn(g,"ARTI BAHASA INDONESIA",15,FontStyle.Bold,green,new RectangleF(544,265,560,28),false);
                TextIn(g,c.Meaning,29,FontStyle.Bold,ink,new RectangleF(544,295,560,83),false);
                TextIn(g,"EJAAN HURUF",15,FontStyle.Bold,green,new RectangleF(544,399,560,28),false);
                TextIn(g,c.Spelling,c.Spelling.Length>32?24:29,FontStyle.Regular,ink,new RectangleF(544,430,560,68),false);
                Fill(g,Color.FromArgb(232,240,220),534,529,592,99);
                TextIn(g,"SUKU KATA",15,FontStyle.Bold,green,new RectangleF(552,537,550,25),false);
                TextIn(g,c.Syllables,36,FontStyle.Bold,green,new RectangleF(552,565,550,53),false);
            }
            TextIn(g,message,17,FontStyle.Regular,ink,new RectangleF(65,740,1070,30),true);
            TextIn(g,"F11: layar penuh     Esc: kembali     ← / →: pindah kartu     Spasi: audio     Home: menu",14,FontStyle.Regular,Color.DimGray,new RectangleF(60,775,1080,20),true);
        }
    }
    static class Program {
        public static void Log(string root,string text) {
            try { Directory.CreateDirectory(Path.Combine(root,"logs")); File.AppendAllText(Path.Combine(root,"logs/aplikasi.log"),DateTime.Now.ToString("s")+" "+text+Environment.NewLine); } catch { }
        }
        static void Assert(bool condition,string name) { if(!condition) throw new Exception("Uji gagal: "+name); }
        static void SelfTest(string root) {
            List<Card> cards=Card.Load(root); Deck deck=new Deck();
            using(ButtonFontCache fonts=new ButtonFontCache())
            using(Button button=new Button())
            using(Bitmap bitmap=new Bitmap(80,80))
            using(Graphics graphics=Graphics.FromImage(bitmap)) {
                float[] sizes={18f,18f,28.75f,28.75f,18f,12f,18f};
                for(int pass=0;pass<100;pass++) foreach(float size in sizes) {
                    button.Font=fonts.Get(size);
                    Assert(button.Font.GetHeight(graphics)>0,"font tetap valid saat resize berulang");
                    Assert(Object.ReferenceEquals(fonts.Get(size),fonts.Get(size)),"font dipakai ulang");
                }
            }
            for(int n=0;n<1000;n++) { deck.Start(true,0); HashSet<int> seen=new HashSet<int>();
                for(int i=0;i<26;i++) { Assert(seen.Add(deck.Current),"kartu acak unik"); if(i<25) Assert(deck.Move(1),"navigasi maju"); }
                Assert(!deck.Move(1),"batas akhir");
                for(int i=0;i<25;i++) Assert(deck.Move(-1),"navigasi kembali"); Assert(!deck.Move(-1),"batas awal"); }
            for(int i=0;i<26;i++) { deck.Start(false,i); Assert(deck.Current==i,"pilihan huruf"); using(Image im=Image.FromFile(cards[i].ImagePath)) Assert(im.Width>0 && im.Height>0,"gambar valid"); }
            using(Image im=Image.FromFile(Path.Combine(root,"gambar/bingkai.jpeg"))) Assert(im.Width>0,"bingkai valid");
            using(AudioPlayer player=new AudioPlayer()) {
                bool missing=false;
                try { player.Play(Path.Combine(root,Guid.NewGuid().ToString()+".mp3")); }
                catch(FileNotFoundException) { missing=true; }
                Assert(missing,"audio belum tersedia"); player.Stop();
            }
            Directory.CreateDirectory(Path.Combine(root,"logs"));
            File.WriteAllText(Path.Combine(root,"logs/uji-otomatis.txt"),"LULUS: validitas font pada 700 pergantian ukuran, 26 kartu dan gambar, 1000 putaran acak tanpa duplikat, batas navigasi, dan pilihan huruf.\r\nPemutaran MP3, tampilan, dan layar penuh perlu uji manual di Windows.\r\n");
        }
        [STAThread] public static int Main(string[] args) {
            string root=AppDomain.CurrentDomain.BaseDirectory;
            try {
                if(args.Length>0 && args[0]=="--self-test") { SelfTest(root); return 0; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ReaderForm(root)); return 0;
            } catch(Exception ex) {
                Log(root,ex.ToString());
                MessageBox.Show("Aplikasi belum dapat dibuka.\n\n"+ex.Message+"\n\nPastikan ZIP sudah diekstrak dan folder data serta gambar tetap berada di samping aplikasi.","Kartu Baca Basa Dayak Ngaju",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
