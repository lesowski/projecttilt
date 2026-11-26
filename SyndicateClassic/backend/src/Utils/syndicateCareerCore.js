// The launcher's Statistics page keeps this many of the player's latest matches.
export const RECENT_MATCH_LIMIT = 50;
const whole = (v, max = Number.MAX_SAFE_INTEGER) => Math.max(0, Math.min(max, Math.floor(Number(v) || 0)));

// Stored inside the Athena profile so account travel carries career totals.
export function recordCareer(attributes, event) {
  if (!event || !['kill', 'win', 'match'].includes(event.kind)) return;
  const mode = ['solo', 'duo', 'squad'].includes(event.mode) ? event.mode : 'unknown';
  const career = attributes.tilted_career ??= {};
  const row = career[mode] ??= {kills: 0, wins: 0};
  // Match history: a player's kill events come before their one "match" event (death or win), and the Erbium
  // seasons send no match id, so kills are counted here until that match is recorded.
  if (event.kind === 'kill') attributes.syndicate_match_kills = whole(attributes.syndicate_match_kills) + 1;
  if (event.kind === 'match') {
    const place = whole(event.place);
    const recent = Array.isArray(attributes.syndicate_recent_matches) ? attributes.syndicate_recent_matches : [];
    recent.unshift({
      at: new Date().toISOString(),
      season: whole(event.season, 99),
      mode,
      arena: event.arena === true,
      place,
      players: whole(event.players, 1000),
      kills: whole(attributes.syndicate_match_kills, 1000),
      seconds: whole(event.seconds, 24 * 3600),
      won: place === 1,
    });
    attributes.syndicate_recent_matches = recent.slice(0, RECENT_MATCH_LIMIT);
    attributes.syndicate_match_kills = 0;
  }
  if(event.kind==='match'){
    if(Array.isArray(event.matchStats)&&event.matchStats.length===20){row.stats??=Array(20).fill(0);for(let i=0;i<20;i++)if(Number.isSafeInteger(event.matchStats[i])&&event.matchStats[i]>=0)row.stats[i]=(row.stats[i]||0)+event.matchStats[i];}
    row.score=(row.score||0)+Math.max(0,Math.floor(Number(event.score)||0));
    row.matches=Math.max(0,Number(row.matches)||0)+1;
    row.seconds=Math.max(0,Number(row.seconds)||0)+Math.max(0,Math.floor(Number(event.seconds)||0));
    const place=Math.max(1,Math.floor(Number(event.place)||100));
    if(place<=({solo:10,duo:5,squad:3}[mode]||1))row.high=(row.high||0)+1;
    if(place<=({solo:25,duo:12,squad:6}[mode]||1))row.low=(row.low||0)+1;
  } else {const key=event.kind==='kill'?'kills':'wins';row[key]=Math.max(0,Number(row[key])||0)+1;}
  attributes.lifetime_kills=Object.values(career).reduce((sum,r)=>sum+(Number(r.kills)||0),0);
  attributes.lifetime_wins=Object.values(career).reduce((sum,r)=>sum+(Number(r.wins)||0),0);
}

/** Career totals over every mode, for the launcher's Home (kills, wins, matches and hours in matches). */
export function careerTotals(attributes = {}) {
  const rows = Object.values(attributes.tilted_career || {}).filter(r => r && typeof r === 'object');
  const sum = key => rows.reduce((t, r) => t + whole(r[key]), 0);
  return {kills: sum('kills'), wins: sum('wins'), matches: sum('matches'), hours: Math.floor(sum('seconds') / 360) / 10};
}

/** The player's latest matches, newest first, as the launcher's Statistics page shows them. */
export function recentMatches(attributes = {}) {
  const list = Array.isArray(attributes.syndicate_recent_matches) ? attributes.syndicate_recent_matches : [];
  return list.slice(0, RECENT_MATCH_LIMIT).filter(m => m && typeof m === 'object').map(m => ({
    at: typeof m.at === 'string' ? m.at : '',
    season: whole(m.season, 99),
    mode: ['solo', 'duo', 'squad'].includes(m.mode) ? m.mode : 'unknown',
    arena: m.arena === true,
    place: whole(m.place),
    players: whole(m.players, 1000),
    kills: whole(m.kills, 1000),
    seconds: whole(m.seconds, 24 * 3600),
    won: m.won === true,
  }));
}

export function careerStats(attributes = {}) {
  const stats = {};
  for (const [mode, playlist] of [['solo', 'p2'], ['duo', 'p10'], ['squad', 'p9']]) {
    const row = attributes.tilted_career?.[mode] || {};
    for (const [field, name] of [['kills','kills'],['wins','placetop1'],['matches','matchesplayed'],['seconds','minutesplayed'],['score','score'],['high',({solo:'placetop10',duo:'placetop5',squad:'placetop3'})[mode]],['low',({solo:'placetop25',duo:'placetop12',squad:'placetop6'})[mode]]]) {
      const value = Math.max(0, Math.floor((Number(row[field]) || 0)/(field==='seconds'?60:1)));
      stats[`br_${name}_pc_m0_${playlist}`] = value;
    }
  }
  return stats;
}

export function legacyCareer(accountId, attributes) {
  return Object.entries(careerStats(attributes)).map(([name, value]) => ({
    name, value, window: 'alltime', ownerType: 1, accountId
  }));
}

// Fortnite 4.00 uses an MCP athena_stats profile in addition to the bulk stats API.
// Match stat slots follow the launch client's GameplayStat.Profile.Match table.
export function legacyAthenaStats(accountId,athena){
 const attrs=athena?.stats?.attributes||{},now=new Date().toISOString();
 const profile={created:athena?.created||now,updated:athena?.updated||now,rvn:athena?.rvn||0,commandRevision:athena?.commandRevision||0,wipeNumber:1,accountId,profileId:'athena_stats',version:'no_version',items:{},stats:{attributes:{recent_match_ids:[]}}};
 const total=Array(20).fill(0);
 for(const [mode,id] of [['solo',2],['duo',10],['squad',9]]){
  const r=attrs.tilted_career?.[mode]||{},s=Array.from({length:20},(_,i)=>r.stats?.[i]||0);
  s[0]=r.score||0;s[2]=r.seconds||0;s[3]=r.kills||0;s[16]=r.wins||0;s[17]=r.high||0;s[18]=r.low||0;s[19]=r.matches||0;
  for(let i=0;i<20;i++){s[i]=Math.max(0,Math.floor(Number(s[i])||0));total[i]+=s[i];}
  profile.items['lifetime_'+mode]={templateId:'ProfileStats:LifetimeStats',quantity:1,attributes:{bucket_name:'pc_m0_p'+id,stats:s}};
  profile.items['season4_'+mode]={templateId:'ProfileStats:SeasonStats',quantity:1,attributes:{bucket_name:'pc_m0_p'+id,season_number:4,stats:[...s]}};
 }
 profile.items.lifetime={templateId:'ProfileStats:LifetimeStats',quantity:1,attributes:{bucket_name:'',stats:total}};
 profile.items.season4={templateId:'ProfileStats:SeasonStats',quantity:1,attributes:{bucket_name:'',season_number:4,stats:[...total]}};
 return profile;
}
