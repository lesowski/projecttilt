import test from 'node:test';
import assert from 'node:assert/strict';
import {recordCareer,recentMatches,careerStats,RECENT_MATCH_LIMIT} from '../syndicateCareerCore.js';

const ev=(kind,extra={})=>({id:'{'+Math.random()+'}',username:'Player',kind,mode:'solo',place:0,arena:false,season:10,seconds:0,players:100,...extra});

test('a match records its kills, placement, lobby size and time alive',()=>{
 const a={};
 recordCareer(a,ev('kill'));recordCareer(a,ev('kill'));recordCareer(a,ev('survival'));
 recordCareer(a,ev('match',{place:7,seconds:845,season:6}));
 const [m]=recentMatches(a);
 assert.equal(m.kills,2);assert.equal(m.place,7);assert.equal(m.players,100);assert.equal(m.seconds,845);
 assert.equal(m.season,6);assert.equal(m.mode,'solo');assert.equal(m.won,false);assert.ok(!Number.isNaN(Date.parse(m.at)));
 assert.equal(a.syndicate_match_kills,0,'kills start again for the next match');
});

test('a win (place 1) is a won match, and the totals keep working',()=>{
 const a={};
 recordCareer(a,ev('kill',{mode:'duo'}));recordCareer(a,ev('win',{mode:'duo',place:1}));recordCareer(a,ev('match',{mode:'duo',place:1,seconds:1500}));
 const [m]=recentMatches(a);
 assert.equal(m.won,true);assert.equal(m.kills,1);assert.equal(m.mode,'duo');
 assert.equal(a.tilted_career.duo.wins,1);assert.equal(a.tilted_career.duo.kills,1);assert.equal(a.tilted_career.duo.matches,1);
 assert.equal(careerStats(a).br_placetop1_pc_m0_p10,1);
});

test('newest first, capped, and kills never leak into the previous match',()=>{
 const a={};
 for(let i=1;i<=RECENT_MATCH_LIMIT+5;i++){for(let k=0;k<i%4;k++)recordCareer(a,ev('kill'));recordCareer(a,ev('match',{place:i}));}
 const list=recentMatches(a);
 assert.equal(list.length,RECENT_MATCH_LIMIT);
 assert.equal(list[0].place,RECENT_MATCH_LIMIT+5);assert.equal(list[0].kills,(RECENT_MATCH_LIMIT+5)%4);
 assert.equal(list[1].kills,(RECENT_MATCH_LIMIT+4)%4);
});

test('bad saved data is cleaned up instead of reaching the launcher',()=>{
 const list=recentMatches({syndicate_recent_matches:[null,'x',{place:-3,kills:'9',mode:'trios',won:'yes',seconds:1e12}]});
 assert.equal(list.length,1);
 assert.deepEqual(list[0],{at:'',season:0,mode:'unknown',arena:false,place:0,players:0,kills:9,seconds:86400,won:false});
 assert.deepEqual(recentMatches({}),[]);
});
