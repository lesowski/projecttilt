using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using SyndicateLauncher;
using SyndicateLauncher.Models;
using SyndicateLauncher.Services;
using SyndicateLauncher.ViewModels;
internal static class Program
{
 [STAThread] static int Main(string[] args)
 {
  int checks=0;void Check(bool b,string text){if(!b)throw new Exception(text);checks++;}
  string temp=Path.Combine(Path.GetTempPath(),"SyndicateDifficultyUi-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
  var errors=new StringWriter();PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(errors));PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Error;
  var app=new App();app.InitializeComponent();
  try {
   var store=new SettingsService(temp);var library=new PresetStore(Path.Combine(temp,"Presets"));
   var vm=new MainViewModel(store,library);Check(vm.SkillSliders.Count==20&&vm.DifficultyPresets.Count==6,"Six built-ins and twenty controls.");
   for(int level=0;level<6;level++){vm.SetDifficultyCommand.Execute(level.ToString());Check(vm.DifficultyName==BotPreset.Levels[level]&&vm.DifficultyIndex==level,"Difficulty slider selects the corresponding built-in.");}
   Check(store.Load().BotPreset!.Mixed,"Mixed is saved.");
   vm.SkillSliders[0].Value=17;Check(vm.DifficultyName=="Custom"&&!store.Load().BotPreset!.Mixed,"Mixed slider edit becomes a fixed custom preset.");
   Check(vm.SkillSliders[0].Value==17&&vm.SkillSliders[1].Value==50,"Independent slider change.");
   vm.PresetName="Shared profile";vm.SavePresetCommand.Execute(null);Check(vm.DifficultyName=="Shared profile"&&library.Load().Single().Values["aimAccuracy"]==17,"Save preset command.");
   var restored=new MainViewModel(store,library);Check(restored.SkillSliders[0].Value==17&&restored.DifficultyName=="Shared profile","Custom settings survive restart.");
   restored.SetDifficultyCommand.Execute("4");restored=new MainViewModel(store,library);Check(restored.DifficultyIndex==4&&restored.DifficultyName=="Sweat","Built-in slider position survives restart.");
   restored.DifficultyIndex=5;Check(store.Load().BotPreset!.Mixed,"Slider chooses Mixed.");
   // Adaptive difficulty: how a match moves the level, seeding, clamping, and what each server is sent.
   var t0=new DateTime(2026,11,1,12,0,0,DateTimeKind.Utc);
   MatchResult M(int i,int place,int kills,bool won=false,bool arena=false,int players=100)=>new(t0.AddHours(i),place,players,kills,won,arena);
   Check(AdaptiveDifficulty.Change(M(0,1,7,true))==8&&AdaptiveDifficulty.Change(M(0,90,0))==-5&&AdaptiveDifficulty.Change(M(0,40,2))==0
       &&AdaptiveDifficulty.Change(M(0,8,3))==4&&AdaptiveDifficulty.Change(M(0,60,1))==-2&&AdaptiveDifficulty.Change(M(0,4,0,players:30))==0,"One match's change: wins and deep runs up, early eliminations down, lobby size counts.");
   var adaptive=new AdaptiveState();
   var history=Enumerable.Range(0,30).Select(i=>M(i,1,8,true)).ToList();
   Check(AdaptiveDifficulty.Update(adaptive,history)==10&&adaptive.Level==100&&adaptive.MatchesCounted==10&&adaptive.LastMatchUtc==t0.AddHours(29),"First run seeds from the last 10 matches only, capped at 100.");
   Check(AdaptiveDifficulty.Update(adaptive,history)==0&&adaptive.Level==100,"A match is never counted twice.");
   Check(AdaptiveDifficulty.Update(adaptive,new[]{M(30,95,0),M(31,99,0),M(32,1,5,true,arena:true)})==2&&adaptive.Level==90&&adaptive.LastChange==-5,"New early eliminations lower the level; Arena matches don't count.");
   var floor=new AdaptiveState{Level=12,LastMatchUtc=t0};AdaptiveDifficulty.Update(floor,new[]{M(1,99,0),M(2,99,0)});Check(floor.Level==AdaptiveDifficulty.MinLevel,"The level never drops below 10.");
   var fresh=new AdaptiveState();AdaptiveDifficulty.Update(fresh,Array.Empty<MatchResult>());Check(fresh.Level==50&&fresh.LastMatchUtc!=null,"No history: start at 50 and only count matches from now on.");
   Check(AdaptiveDifficulty.ParseRecent("{\"recent\":[{\"at\":\"2026-11-27T13:05:00Z\",\"place\":3,\"players\":100,\"kills\":4,\"won\":false},{\"at\":\"x\"},{\"place\":1}]}").Single().Kills==4,"Results are read from launcher/stats; undated entries are skipped.");
   Check(GameLauncher.SyndicateSkillName(AdaptiveDifficulty.Preset(10))=="bot"&&GameLauncher.SyndicateSkillName(AdaptiveDifficulty.Preset(30))=="easy"
       &&GameLauncher.SyndicateSkillName(AdaptiveDifficulty.Preset(50))=="medium"&&GameLauncher.SyndicateSkillName(AdaptiveDifficulty.Preset(80))=="hard","Pre-season to Season 9 get the nearest of their four tiers.");
   Check(GameLauncher.SyndicateSkillName(BotPreset.Builtin(1))=="easy"&&GameLauncher.SyndicateSkillName(BotPreset.Builtin(5))=="variety"&&GameLauncher.SyndicateSkillName(BotPreset.Builtin(3))=="hard","Built-in difficulties still map one-to-one.");
   restored.SetAdaptiveCommand.Execute(null);
   Check(restored.IsAdaptive&&restored.DifficultyName=="Adaptive"&&restored.DifficultyIndex==-1&&restored.SkillSliders.All(s=>s.Value==50)&&restored.DifficultyDescription.Contains("tougher"),"Adaptive chip: level on the sliders, no other chip selected.");
   Check(new MainViewModel(store,library).IsAdaptive,"Adaptive survives a restart.");
   restored.SkillSliders[3].Value=70;Check(!restored.IsAdaptive&&restored.DifficultyName=="Custom","Moving a slider turns Adaptive off.");
   // Bot skill slider (MainViewModel.OverallSkill): one value for all 20 skills.
   restored.OverallSkill=75;Check(restored.DifficultyName=="Hard"&&restored.DifficultyIndex==3&&restored.SkillSliders.All(s=>s.Value==75),"Bot skill slider on 75 selects Hard.");
   restored.OverallSkill=40;Check(restored.DifficultyName=="Custom"&&restored.SkillSliders.All(s=>s.Value==40)&&store.Load().BotPreset!.Values.Values.All(v=>v==40),"Bot skill slider between levels: every skill 40, saved as Custom.");
   restored.SkillSliders[0].Value=100;Check(restored.OverallSkill==(int)Math.Round((100+40*19)/20.0),"Bot skill follows the average of the fine-tune sliders.");
   restored.SetAdaptiveCommand.Execute(null);restored.OverallSkill=25;Check(!restored.IsAdaptive&&restored.DifficultyName=="Easy","Bot skill slider turns Adaptive off.");
   restored.SetAdaptiveCommand.Execute(null);restored.SetDifficultyCommand.Execute("2");Check(!restored.IsAdaptive&&restored.DifficultyIndex==2,"Picking a fixed difficulty turns Adaptive off.");
   restored.DifficultyIndex=2;restored.IsSettingsOpen=true;
   var window=new MainWindow(restored);var root=(FrameworkElement)window.Content;
   root.Measure(new Size(996,576));root.Arrange(new Rect(0,0,996,576));root.UpdateLayout();
   foreach(var e in Descendants(root).OfType<FrameworkElement>()){e.BeginAnimation(UIElement.OpacityProperty,null);if(e.RenderTransform is TranslateTransform t)t.BeginAnimation(TranslateTransform.XProperty,null);}
   // Settings > Match & bots: the "Difficulty" label sits in the settings ScrollViewer (Bot/Easy/Medium/Hard/Sweat chips).
   var label=Descendants(root).OfType<TextBlock>().First(t=>t.Text=="Difficulty");
   var scroll=Ancestors(label).OfType<ScrollViewer>().First();
   var point=label.TransformToAncestor((Visual)scroll.Content).Transform(new Point());scroll.ScrollToVerticalOffset(point.Y);root.UpdateLayout();
   Check(Descendants(root).OfType<RadioButton>().Count(r=>r.GroupName=="DifficultyChip")==6,"Six difficulty chips in Settings (Bot, Easy, Medium, Hard, Variety, Adaptive).");
   Directory.CreateDirectory(args[0]);Render(root,Path.Combine(args[0],"ui-difficulty.png"));
   var chips=(FrameworkElement)VisualTreeHelper.GetParent(Descendants(root).OfType<RadioButton>().Single(r=>AutomationProperties.GetName(r)=="Adaptive difficulty"));
   foreach(var e in Descendants(chips).OfType<FrameworkElement>())e.BeginAnimation(UIElement.OpacityProperty,null);
   Render(chips,Path.Combine(args[0],"ui-difficulty-chips.png"));
   // Fine-tune bot skills: all 20 sliders render in four groups, and moving one on screen makes a saved Custom preset.
   var expander=Descendants(root).OfType<Expander>().Single(x=>AutomationProperties.GetName(x)=="Fine-tune bot skills");
   expander.IsExpanded=true;root.UpdateLayout();
   var items=Descendants(expander).OfType<ItemsControl>().Single(i=>AutomationProperties.GetName(i)=="Bot skill sliders");
   items.Measure(new Size(450,double.PositiveInfinity));items.Arrange(new Rect(0,0,450,items.DesiredSize.Height));items.UpdateLayout();
   var sliders=Descendants(items).OfType<Slider>().ToList();
   Check(sliders.Count==20,"All twenty visible sliders have WPF templates.");
   Check(items.Items.Groups!.Count==4&&string.Join(",",items.Items.Groups.Cast<CollectionViewGroup>().Select(g=>g.Name))=="AIM,FIGHTING,BUILDING,SURVIVAL","Sliders are grouped Aim, Fighting, Building, Survival.");
   var building=sliders.Single(s=>AutomationProperties.GetName(s)=="Build frequency");
   building.Value=83;Check(restored.DifficultyName=="Custom"&&store.Load().BotPreset!.Values["buildFrequency"]==83,"Moving an on-screen slider saves a Custom preset.");
   items.UpdateLayout();
   Render(items,Path.Combine(args[0],"ui-all-skills.png"));

   // Statistics: a launcher/stats reply (backend syndicateCareerCore.js) and the launcher's session log.
   var stats=restored.Stats;
   stats.ApplyJson("{}");Check(stats.ShowEmpty&&stats.Matches=="0"&&stats.WinRate=="0%"&&stats.KillsPerMatch=="0.00"&&!stats.HasRecent,"No matches: empty state, zeros.");
   const string reply="{\"username\":\"Player\",\"modes\":{\"solo\":{\"matches\":40,\"wins\":6,\"kills\":95,\"seconds\":36000,\"score\":0,\"top\":18,\"topWide\":25},\"duo\":{\"matches\":8,\"wins\":2,\"kills\":11,\"seconds\":6000,\"top\":5},\"squad\":{\"matches\":0}},"
    +"\"total\":{},\"recent\":[{\"at\":\"2026-11-27T13:05:00.000Z\",\"season\":10,\"mode\":\"solo\",\"arena\":false,\"place\":1,\"players\":100,\"kills\":9,\"seconds\":1490,\"won\":true},"
    +"{\"at\":\"2026-11-27T12:30:00.000Z\",\"season\":6,\"mode\":\"duo\",\"place\":14,\"players\":100,\"kills\":2,\"seconds\":610,\"won\":false},{\"at\":\"bad\",\"season\":11,\"mode\":\"solo\",\"arena\":true,\"place\":3,\"players\":32,\"kills\":1,\"seconds\":3700,\"won\":false},7]}";
   stats.ApplyJson(reply);
   Check(stats.Matches=="48"&&stats.Wins=="8"&&stats.Kills=="106"&&stats.TopFinishes=="23","Totals add up solo, duo and squad.");
   Check(stats.WinRate=="16.7%"&&stats.KillsPerMatch=="2.21","Win rate and eliminations per match.");
   Check(stats.Modes.Count==3&&stats.Modes[0].WinRate=="15%"&&stats.Modes[1].KillsPerMatch=="1.38"&&!stats.Modes[2].Played,"Mode rows.");
   Check(stats.Recent.Count==3&&stats.Recent[0].PlaceText=="#1"&&stats.Recent[0].ResultText=="VICTORY ROYALE"&&stats.Recent[0].SeasonText=="Season X"&&stats.Recent[0].TimeText=="24m 50s","Victory row.");
   Check(stats.Recent[1].PlaceText=="#14"&&stats.Recent[1].PlayersText=="of 100"&&stats.Recent[1].KillsText=="2 elims"&&stats.Recent[1].ModeText=="Duo","Eliminated row.");
   Check(stats.Recent[2].At==null&&stats.Recent[2].ModeText=="Arena"&&stats.Recent[2].SeasonText=="Chapter 2 Season 1"&&stats.Recent[2].TimeText=="1h 1m","Bad date and arena row.");
   Check(stats.AveragePlace=="#6"&&stats.BestStreak=="1"&&!stats.ShowEmpty,"Average placement and streak over the history.");
   var log=new PlayerStats();
   log.Sessions.Add(new GameSession{StartedUtc=DateTime.UtcNow.AddDays(-1),Minutes=30,Season=10});log.Sessions.Add(new GameSession{StartedUtc=DateTime.UtcNow,Minutes=90,Season=10});
   log.Sessions.Add(new GameSession{StartedUtc=DateTime.UtcNow,Minutes=20,Season=0});
   stats.ApplyLocal(log);
   Check(stats.Seasons.Count==2&&stats.Seasons[0].Season=="Season X"&&stats.Seasons[0].SessionsText=="2 sessions"&&stats.Seasons[0].MinutesText=="2h 0m"&&stats.Seasons[1].Season=="Pre-season","Playtime by season, most played first.");
   Check(stats.TimePlayed=="11h 40m","Time played is the larger of match time (700 min) and launcher time (140 min).");
   // Editions: the developer build compiles in the Studio and the in-game menu toggle; the public build has neither.
   bool developer=Type.GetType("SyndicateLauncher.Studio.StudioWindow, SyndicateLauncher")!=null;
   var menuToggle=Descendants(root).OfType<CheckBox>().SingleOrDefault(c=>AutomationProperties.GetName(c)=="Inject the in-game menu (developer build)");
   restored.EnableMenu=true;
   if(developer)
   {
    Check(restored.EnableMenu&&menuToggle!=null,"Developer build: the in-game menu toggle is there and works.");
    restored.EnableMenu=false;
   }
   else
   {
    Check(!restored.EnableMenu&&menuToggle==null,"Public build: no menu toggle, and EnableMenu stays off when set.");
    Check(SyndicateLauncher.Studio.StudioSession.Current()==null,"Public build: a Studio session file never turns on Studio or dev live data.");
    Check(Type.GetType("SyndicateLauncher.Studio.StudioPipeClient, SyndicateLauncher")==null&&Type.GetType("SyndicateLauncher.Studio.NativeEmbed, SyndicateLauncher")==null,"Public build: the Studio tools are not compiled in.");
   }
   Console.WriteLine(developer?"Edition: developer":"Edition: public");

   // Home: launcher/home (V-Bucks, menu flag, career totals, latest matches). The achievements system is gone.
   var home=restored.Home;
   home.Apply("{\"vbucks\":12500,\"menuActive\":false,\"totals\":{\"kills\":106,\"wins\":8,\"matches\":48,\"hours\":11.6},\"recent\":"+reply.Substring(reply.IndexOf("[{"))[..^1]+"}");
   Check(home.VBucksText=="12,500"&&home.Wins=="8"&&home.Kills=="106"&&home.Matches=="48"&&home.WinRate=="16.7%","Home totals and V-Bucks.");
   Check(home.Recent.Count==3&&home.Recent[0].Won&&!home.ShowRecentPlaceholder,"Home shows the latest matches.");
   Check(typeof(HomeViewModel).GetProperty("Achievements")==null&&typeof(HomeViewModel).GetProperty("ClaimCommand")==null&&Type.GetType("SyndicateLauncher.ViewModels.AchievementItem, SyndicateLauncher")==null,"No achievements left in the launcher.");
   // Update check (Services/UpdateChecker.cs): Tilt's repo, and date tags that compare as dates.
   var updater=typeof(SyndicateLauncher.Services.UpdateChecker);
   const System.Reflection.BindingFlags hidden=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
   Func<string,string,bool> newer=(a,b)=>(bool)updater.GetMethod("IsNewer",hidden)!.Invoke(null,new object[]{a,b})!;
   Check(((string[])updater.GetField("Repos",hidden)!.GetValue(null)!).SequenceEqual(new[]{"lesowski/projecttilt"}),"Update check reads Tilt's release repo.");
   Check(newer("v2026-11-28","v2026-11-27")&&newer("v2026.11.27.1","v2026-11-27")&&newer("v2026-11-27","v2026-11-15"),"Update check: a later date tag is an update.");
   Check(!newer("v2026-11-27","v2026-11-27")&&!newer("v2026-11-15","v2026-11-27")&&!newer("v1.4.1","v2026-11-27"),"Update check: the same or an older tag is not.");
   // Party chat certificate for Pre-season / Season 1 / Season 2 (Services/LegacyXmppTrust.cs).
   var trust=typeof(SyndicateLauncher.Services.UpdateChecker).Assembly.GetType("SyndicateLauncher.Services.LegacyXmppTrust")!;
   const System.Reflection.BindingFlags open=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
   var der=(byte[])trust.GetMethod("EnsureCertificate",open)!.Invoke(null,null)!;
   string tlsDir=(string)trust.GetProperty("TlsDirectory",open)!.GetValue(null)!;
   using(var made=new System.Security.Cryptography.X509Certificates.X509Certificate2(der))
    Check(der.Length==1144&&made.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName,false)=="prod.ol.epicgames.com"&&File.Exists(Path.Combine(tlsDir,"key.pem")),"Chat certificate: 1144 bytes, for prod.ol.epicgames.com, with its key saved.");
   Check(((byte[])trust.GetMethod("EnsureCertificate",open)!.Invoke(null,null)!).SequenceEqual(der),"Chat certificate is kept between runs.");
   Action<string,string> patch=(file,version)=>trust.GetMethod("Ensure",open)!.Invoke(null,new object?[]{file,version,null});
   string fakeDir=Path.Combine(Path.GetTempPath(),"tilt-trust-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fakeDir);
   try
   {
    var expired=Convert.FromBase64String((string)trust.GetField("ExpiredRoot",open)!.GetValue(null)!);
    var filler=new byte[5000];new Random(7).NextBytes(filler);
    string fake=Path.Combine(fakeDir,"FortniteClient-Win64-Shipping.exe");
    File.WriteAllBytes(fake,filler.Concat(expired).Concat(filler).ToArray());
    patch(fake,"4.5");Check(File.ReadAllBytes(fake).AsSpan(5000,1144).SequenceEqual(expired),"Chat certificate: other seasons' games are left alone.");
    patch(fake,"1.7.2");var after=File.ReadAllBytes(fake);
    Check(after.Length==11144&&after.AsSpan(5000,1144).SequenceEqual(der)&&after.AsSpan(0,5000).SequenceEqual(filler)&&File.Exists(fake+".tilt-original"),"Chat certificate replaces the expired root in 1.7.2 and keeps the original.");
    patch(fake,"1.7.2");Check(File.ReadAllBytes(fake).SequenceEqual(after),"Chat certificate: a patched game is not patched again.");
    // A game patched by the old Project Tilted launcher carries its certificate (marker in an OU) instead.
    var old=new byte[1144];new Random(9).NextBytes(old);new byte[]{0x30,0x82,0x04,0x74,0x30,0x82,0x03,0x5C}.CopyTo(old,0);System.Text.Encoding.ASCII.GetBytes("Tilted local XMPP").CopyTo(old,180);
    string fake2=Path.Combine(fakeDir,"second.exe");File.WriteAllBytes(fake2,filler.Concat(old).Concat(filler).ToArray());
    patch(fake2,"1.8.2");Check(File.ReadAllBytes(fake2).AsSpan(5000,1144).SequenceEqual(der),"Chat certificate replaces an earlier launcher's certificate.");
   }
   finally{Directory.Delete(fakeDir,true);}
   // MongoDB: a damaged database is recognised from its log, and the clean shutdown command is a valid OP_MSG.
   var backendType=typeof(SyndicateLauncher.Services.SyndicateBackend);
   Func<string,bool> damaged=log=>(bool)backendType.GetMethod("DamagedDatabase",open)!.Invoke(null,new object[]{log})!;
   Check(damaged("{\"s\":\"E\",\"msg\":\"__wt_txn_recover:1239:Recovery failed\",\"error_str\":\"WT_ERROR: non-specific WiredTiger error\"}")&&damaged("{\"s\":\"F\",\"msg\":\"Fatal assertion\",\"attr\":{\"msgid\":50944}}"),"MongoDB: a journal that cannot be recovered counts as a damaged database.");
   Check(!damaged("")&&!damaged("{\"s\":\"E\",\"msg\":\"Failed to set up listener\",\"error\":\"Address already in use\"}"),"MongoDB: a busy port or an empty log is not treated as damage (the database is never moved aside for that).");
   var shutdown=(byte[])backendType.GetMethod("MongoShutdownMessage",open)!.Invoke(null,null)!;
   Check(BitConverter.ToInt32(shutdown,0)==shutdown.Length&&BitConverter.ToInt32(shutdown,12)==2013&&shutdown[20]==0&&BitConverter.ToInt32(shutdown,21)==shutdown.Length-21&&System.Text.Encoding.ASCII.GetString(shutdown).Contains("shutdown")&&System.Text.Encoding.ASCII.GetString(shutdown).Contains("admin"),"MongoDB shutdown command is a well-formed OP_MSG.");
   // Chapter 2 Season 1 (11.00) is playable in both editions; the backend gets season 11.
   var c2=SyndicateLauncher.Models.SeasonCatalog.GetSeasons(2);
   Check(c2[0].IsReleased&&c2[0].BuildVersion=="11.00"&&c2[0].SeasonNumber==11&&SyndicateLauncher.Services.GameLauncher.IsSupportedBuild("11.00"),"Chapter 2 Season 1 is playable on 11.00 as season 11.");
   Check(SyndicateLauncher.Models.SeasonCatalog.GetSeasons(1).Single(s=>s.Code=="X").SeasonNumber==10&&SyndicateLauncher.Models.SeasonCatalog.GetSeasons(1).Single(s=>s.Code=="0").SeasonNumber==0,"Chapter 1 season numbers are unchanged.");
   if(!developer)Check(c2.Skip(1).All(s=>!s.IsReleased),"Public build: later Chapter 2 seasons stay locked.");
   Check(home.LatestMatch==home.Recent[0],"Home's Recent Match tile shows the newest match.");
   var recentTile=Descendants(root).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Recent match");
   var strip=(FrameworkElement)VisualTreeHelper.GetParent(recentTile);
   strip.Measure(new Size(420,140));strip.Arrange(new Rect(0,0,420,140));strip.UpdateLayout();
   Check(Descendants(recentTile).OfType<TextBlock>().Any(t=>t.Text==home.Recent[0].PlaceText),"Recent Match tile renders the place.");
   Render(strip,Path.Combine(args[0],"ui-home-recent.png"));
   var page=Descendants(root).OfType<Grid>().Single(g=>AutomationProperties.GetName(g)=="Statistics");
   page.Visibility=Visibility.Visible;root.UpdateLayout();
   Check(Descendants(page).OfType<ItemsControl>().Single(i=>AutomationProperties.GetName(i)=="Match history").Items.Count==3,"Match history renders.");
   Check(Descendants(page).OfType<TextBlock>().Any(t=>t.Text=="16.7%"),"Win rate tile renders.");
   var statsScroll=Descendants(page).OfType<ScrollViewer>().First();var body=(FrameworkElement)statsScroll.Content;
   body.Measure(new Size(980,double.PositiveInfinity));body.Arrange(new Rect(0,0,980,body.DesiredSize.Height));body.UpdateLayout();
   Render(body,Path.Combine(args[0],"ui-statistics.png"));
   Check(errors.ToString().Length==0,"WPF binding errors: "+errors);
   Console.WriteLine($"PASS: {checks} WPF difficulty, persistence, preset-command and offscreen rendering checks; no window shown or game launched.");
   window.Close();return 0;
  }finally{app.Shutdown();Directory.Delete(temp,true);}
 }
 static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
 {
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var next in Descendants(child))yield return next;}
 }
 static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
 {
  for(var p=VisualTreeHelper.GetParent(child);p!=null;p=VisualTreeHelper.GetParent(p))yield return p;
 }
 static void Render(FrameworkElement e,string path)
 {
  int width=(int)Math.Ceiling(e.ActualWidth),height=(int)Math.Ceiling(e.ActualHeight);
  // Draw on the launcher's dark backdrop so light text is readable in the PNG.
  var backdrop=new DrawingVisual();
  using(var dc=backdrop.RenderOpen()){dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10,0x12,0x16)),null,new Rect(0,0,width,height));dc.DrawRectangle(new VisualBrush(e){Stretch=Stretch.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top},null,new Rect(0,0,width,height));}
  var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(backdrop);
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);
 }
}

