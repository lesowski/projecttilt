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

