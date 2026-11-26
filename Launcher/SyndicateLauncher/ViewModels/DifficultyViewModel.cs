using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using SyndicateLauncher.Models;
using SyndicateLauncher.Services;
namespace SyndicateLauncher.ViewModels;
public sealed class SkillSlider : ViewModelBase
{
    private int _value;
    private readonly Action _changed;
    public SkillSlider(SkillDefinition definition,int value,Action changed){Definition=definition;_value=value;_changed=changed;}
    public SkillDefinition Definition {get;}
    public int Value {get=>_value;set{if(Set(ref _value,Math.Clamp(value,0,100)))_changed();}}
    /// <summary>Section heading in Settings &gt; Match &amp; bots &gt; Fine-tune bot skills.</summary>
    public string Group => GroupOf(Definition.Key);
    public static string GroupOf(string key) => key switch
    {
        "aimAccuracy" or "aimTracking" or "reactionSpeed" or "prediction" or "firingFrequency" => "AIM",
        "aggression" or "pursuit" or "strafing" or "jumping" or "awareness" => "FIGHTING",
        "buildFrequency" or "buildSpeed" or "heightSeeking" or "defensiveBuilding" or "editFrequency" or "editSpeed" => "BUILDING",
        _ => "SURVIVAL"
    };
}
public sealed partial class MainViewModel
{
    private readonly PresetStore _presets;
    private BotPreset _currentPreset=BotPreset.Builtin(2);
    private BotPreset? _selectedPreset;
    private bool _applyingPreset;
    private int _difficultyIndex=2;
    private string _presetName="My preset",_presetStatus="";
    public ObservableCollection<BotPreset> DifficultyPresets {get;}=new();
    public ObservableCollection<SkillSlider> SkillSliders {get;}=new();
    public ICommand SavePresetCommand {get;private set;}=null!;
    public ICommand ImportPresetCommand {get;private set;}=null!;
    public ICommand ExportPresetCommand {get;private set;}=null!;
    public string PresetName {get=>_presetName;set=>Set(ref _presetName,value??"");}
    public string PresetStatus {get=>_presetStatus;private set=>Set(ref _presetStatus,value);}
    public BotPreset? SelectedDifficultyPreset
    {
        get=>_selectedPreset;
        set {if(value is null){Set(ref _selectedPreset,null);return;}ApplyPreset(value);}
    }
    private void InitializeDifficulty()
    {
        for(int i=0;i<6;i++)DifficultyPresets.Add(BotPreset.Builtin(i));
        foreach(var p in _presets.Load())DifficultyPresets.Add(p);
        foreach(var d in BotPreset.Definitions)SkillSliders.Add(new(d,50,SliderChanged));
        SavePresetCommand=new RelayCommand(()=>PresetAction(()=>{
            var p=_currentPreset.Copy();p.Name=PresetName.Trim();p.BuiltinIndex=-1;
            var saved=_presets.Save(p);DifficultyPresets.Add(saved);ApplyPreset(saved);PresetStatus="Preset saved. Use Export .fnp to share it.";
        }));
        ImportPresetCommand=new RelayCommand(()=>PresetAction(()=>{
            var dialog=new OpenFileDialog {Filter="Fortnite bot presets (*.fnp)|*.fnp",Title="Import a bot preset",CheckFileExists=true};
            if(dialog.ShowDialog()!=true)return;
            var saved=_presets.Save(PresetStore.Read(dialog.FileName));DifficultyPresets.Add(saved);ApplyPreset(saved);PresetStatus="Preset imported and selected.";
        }));
        ExportPresetCommand=new RelayCommand(()=>PresetAction(()=>{
            var p=_currentPreset.Copy();p.Name=PresetName.Trim();p.Validate();
            string safe=new string(p.Name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray());
            var dialog=new SaveFileDialog {Filter="Fortnite bot presets (*.fnp)|*.fnp",DefaultExt=".fnp",AddExtension=true,FileName=safe+".fnp",Title="Export a bot preset"};
            if(dialog.ShowDialog()!=true)return;PresetStore.Write(dialog.FileName,p);PresetStatus="Preset exported.";
        }));
        SetAdaptiveCommand=new RelayCommand(ApplyAdaptiveLevel);
        GameLauncher.DifficultyForNextMatch=AdaptivePresetForNextMatchAsync;
        bool adaptive=_settings.AdaptiveDifficulty;
        var initial=_settings.BotPreset;
        try{initial?.Validate();}catch{initial=null;}
        ApplyPreset(initial??BotPreset.Builtin((int)_settings.Difficulty+1));
        // ApplyPreset turns Adaptive off for anything but its own preset; a saved "Adaptive" preset keeps it.
        if(adaptive&&!_settings.AdaptiveDifficulty&&initial?.Name==AdaptiveDifficulty.PresetName)SetAdaptive(true);
    }
    /// <summary>
    /// The Bot skill slider under the difficulty chips: the average of the 20 skills. Setting it gives every skill that
    /// value; on a built-in level (Bot 5, Easy 25, Medium 50, Hard 75, Sweat 100) it selects that level, otherwise it
    /// makes a Custom difficulty, the same as moving a slider under Fine-tune bot skills.
    /// </summary>
    public int OverallSkill
    {
        get=>SkillSliders.Count==0?50:(int)Math.Round(SkillSliders.Average(s=>s.Value));
        set
        {
            value=Math.Clamp(value,0,100);
            int level=Array.IndexOf(new[]{5,25,50,75,100},value);
            if(level>=0){ApplyPreset(DifficultyPresets[level]);return;}
            _applyingPreset=true;
            try{foreach(var slider in SkillSliders)slider.Value=value;}finally{_applyingPreset=false;}
            SliderChanged();
        }
    }
    private void PresetAction(Action action)
    {
        try{action();}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException){PresetStatus=e.Message;}
    }
    // Adaptive difficulty (Services/AdaptiveDifficulty.cs) ----------------------------------------------------
    public ICommand SetAdaptiveCommand {get;private set;}=null!;
    /// <summary>The Adaptive chip: the level follows the player's results. Picking anything else turns it off.</summary>
    public bool IsAdaptive => _settings.AdaptiveDifficulty;
    private AdaptiveState AdaptiveFor(string profile)
    {
        string key=profile.Trim().ToLowerInvariant();
        if(!_settings.Adaptive.TryGetValue(key,out var state))_settings.Adaptive[key]=state=new AdaptiveState();
        return state;
    }
    /// <summary>The line under the difficulty chips while Adaptive is on.</summary>
    public string AdaptiveStatus
    {
        get
        {
            const string how="Bots match how you play: tougher after wins and deep runs, easier after early eliminations.";
            if(SelectedProfile is null)return how+" Choose a player to start.";
            var s=AdaptiveFor(SelectedProfile.Name);
            string level=$"Bots are at {s.Level}/100 for {SelectedProfile.Name} ({AdaptiveDifficulty.TierName(s.Level)} in Pre-season to Season 9).";
            if(s.MatchesCounted==0)return how+" "+level;
            string last=s.LastChange>0?$" Last match: +{s.LastChange}.":s.LastChange<0?$" Last match: {s.LastChange}.":" Last match: no change.";
            return level+" Adjusted over "+s.MatchesCounted+(s.MatchesCounted==1?" match.":" matches.")+last;
        }
    }
    private void SetAdaptive(bool on)
    {
        if(_settings.AdaptiveDifficulty==on)return;
        _settings.AdaptiveDifficulty=on;Save();
        OnPropertyChanged(nameof(IsAdaptive));OnPropertyChanged(nameof(AdaptiveStatus));
    }
    /// <summary>Shows the selected player's adaptive level on the sliders (Adaptive on, or the player changed).</summary>
    private void ApplyAdaptiveLevel()
    {
        int level=SelectedProfile is null?AdaptiveDifficulty.StartLevel:AdaptiveFor(SelectedProfile.Name).Level;
        ApplyPreset(AdaptiveDifficulty.Preset(level));
        SetAdaptive(true);
        OnPropertyChanged(nameof(AdaptiveStatus));NotifyDifficulty();
    }
    /// <summary>
    /// Before a match server starts: counts the player's newest results (backend launcher/stats, or the Statistics
    /// page's cached reply when the backend is not up yet) and returns the preset for the next match. Null when
    /// Adaptive is off, so the session keeps its difficulty.
    /// </summary>
    public async Task<BotPreset?> AdaptivePresetForNextMatchAsync()
    {
        var app=System.Windows.Application.Current;
        if(app is null)return null;
        var (on,profile)=await app.Dispatcher.InvokeAsync(()=>(IsAdaptive,SelectedProfile?.Name));
        if(!on||profile is null)return null;
        string? json=null;
        try{if(await SyndicateBackend.IsAvailableAsync())json=await SyndicateBackend.PostAsPlayerAsync(profile,"launcher/stats",new{});}
        catch(Exception){json=null;}
        if(json is null)try{var cache=StatsViewModel.CacheFileFor(profile);if(File.Exists(cache))json=File.ReadAllText(cache);}catch(Exception){}
        var results=json is null?new System.Collections.Generic.List<MatchResult>():AdaptiveDifficulty.ParseRecent(json);
        return await app.Dispatcher.InvokeAsync<BotPreset?>(()=>
        {
            if(!IsAdaptive||!string.Equals(SelectedProfile?.Name,profile,StringComparison.OrdinalIgnoreCase))return null;
            var state=AdaptiveFor(profile);
            AdaptiveDifficulty.Update(state,results);
            Save();ApplyAdaptiveLevel();
            return _currentPreset.Copy();
        });
    }
    private void ApplyPreset(BotPreset preset)
    {
        if(preset.Name!=AdaptiveDifficulty.PresetName||preset.BuiltinIndex>=0)SetAdaptive(false);
        preset.Validate();_applyingPreset=true;
        try {
            _currentPreset=preset.Copy();
            if(preset.BuiltinIndex>=0)_difficultyIndex=preset.BuiltinIndex;
            _selectedPreset=DifficultyPresets.FirstOrDefault(p=>ReferenceEquals(p,preset))??DifficultyPresets.FirstOrDefault(p=>p.Name==preset.Name&&p.Mixed==preset.Mixed&&p.Argument()==preset.Argument());
            if(_selectedPreset?.BuiltinIndex>=0)_difficultyIndex=_selectedPreset.BuiltinIndex;
            foreach(var slider in SkillSliders)slider.Value=preset.Values[slider.Definition.Key];
            PresetName=preset.Name;_settings.BotPreset=_currentPreset.Copy();Save();
        }finally{_applyingPreset=false;}
        NotifyDifficulty();
    }
    private void SliderChanged()
    {
        if(_applyingPreset)return;
        SetAdaptive(false);
        if(_currentPreset.BuiltinIndex>=0)PresetName="My preset";
        _currentPreset=new(){Name="Custom",Values=SkillSliders.ToDictionary(s=>s.Definition.Key,s=>s.Value)};
        _selectedPreset=null;_settings.BotPreset=_currentPreset.Copy();Save();NotifyDifficulty();
    }
    private void NotifyDifficulty()
    {
        OnPropertyChanged(nameof(DifficultyIndex));OnPropertyChanged(nameof(DifficultyName));
        OnPropertyChanged(nameof(DifficultyDescription));OnPropertyChanged(nameof(SelectedDifficultyPreset));
        OnPropertyChanged(nameof(DifficultyMeta));OnPropertyChanged(nameof(IsAdaptive));OnPropertyChanged(nameof(OverallSkill));
    }
}
