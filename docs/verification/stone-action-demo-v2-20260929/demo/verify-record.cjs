const fs=require('fs'),path=require('path');
let playwright;try{playwright=require('playwright');}catch{playwright=require(path.join(process.env.USERPROFILE,'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'));}
const {chromium}=playwright;
const out=path.join(__dirname,'output');fs.mkdirSync(out,{recursive:true});
const record=process.argv.includes('--record');
(async()=>{
 const browser=await chromium.launch({executablePath:process.env.GOA_DEMO_BROWSER||path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),headless:true,args:['--disable-background-timer-throttling','--disable-renderer-backgrounding']});
 const context=await browser.newContext({viewport:{width:1600,height:1000},deviceScaleFactor:1});
 const page=await context.newPage();const errors=[];page.on('pageerror',e=>{errors.push(e.message);console.error('PAGE ERROR:',e.message);});
 await page.goto('file:///'+path.join(__dirname,'index.html').replace(/\\/g,'/')+(record?'?record=1':''));
 await page.waitForFunction(()=>window.demo);
 if(record){
   await page.evaluate(()=>demo.pause());
   await page.evaluate(()=>document.querySelector('#controls').classList.remove('hide'));
   await page.click('#sound');
   await page.waitForFunction(()=>demo.state.audio.state==='running');
   await page.evaluate(()=>{
     document.querySelector('#controls').classList.add('hide');
     demo.seek(0);demo.pause();
     demoAudit.events=[];demoAudit.interactions=[];
     const stream=canvas.captureStream(25);
     sound.destination.stream.getAudioTracks().forEach(t=>stream.addTrack(t));
     const recorder=new MediaRecorder(stream,{mimeType:'video/webm;codecs=vp8,opus',videoBitsPerSecond:4000000,audioBitsPerSecond:128000});
     const chunks=[];window.captureDone=new Promise(resolve=>{recorder.ondataavailable=e=>{if(e.data.size)chunks.push(e.data);};recorder.onstop=async()=>{const blob=new Blob(chunks,{type:recorder.mimeType});const reader=new FileReader();reader.onload=()=>resolve(reader.result.slice(reader.result.indexOf(';base64,')+8));reader.readAsDataURL(blob);};});
     window.demoRecorder=recorder;recorder.start(1000);demo.resume();sound.play('drop');
   });
   const smoke=process.argv.includes('--smoke');
   if(smoke)await page.waitForTimeout(5000);
   else await page.waitForFunction(()=>window.demo.state.finished,{},{timeout:220000});
   await page.waitForTimeout(1200);
   fs.writeFileSync(path.join(out,smoke?'smoke-events.json':'record-events.json'),JSON.stringify(await page.evaluate(()=>window.demoAudit),null,2));
   const recording=await page.evaluate(async()=>{window.demoRecorder.stop();return await window.captureDone;});
   const bytes=Buffer.from(recording,'base64');
   if(bytes.length<10000||bytes.readUInt32BE(0)!==0x1a45dfa3)throw Error('Invalid WebM container from recorder');
   fs.writeFileSync(path.join(out,smoke?'record-smoke.webm':'stone-sequence-continuous.webm'),bytes);
   await context.close();
   console.log(JSON.stringify({recorded:true,errors}));
 }else{
   const checks=[];function check(name,ok){checks.push({name,passed:!!ok});if(!ok)throw Error(name);}
   async function at(t){await page.evaluate(t=>{demo.seek(t);demo.pause();},t);await page.waitForTimeout(900);return page.evaluate(()=>demo.state);}
   let s=await at(3);check('four revealed roots only',s.nodes.length===4&&s.nodes.every(n=>n.level===0&&n.notched));check('neutral coins on both tied groups',s.coins.length===2&&s.coins.every(c=>!('color' in c)));
   check('different initiative wider than tied spacing',s.nodes[2].ty-s.nodes[1].ty-s.nodes[1].h===54&&s.nodes[1].ty-s.nodes[0].ty-s.nodes[0].h===14);
   await page.click('#sound');await page.waitForFunction(()=>demo.state.audio.state==='running');
   let coin=s.coins[0];await page.mouse.move(coin.position.x,coin.position.y);await page.waitForTimeout(100);s=await page.evaluate(()=>demo.state);
   check('hover actually spins coin on a random axis',Math.abs(s.coins[0].velocity)>1&&s.coins[0].axis>=0&&s.coins[0].axis<Math.PI*2);
   const before=s.nodes.map(n=>n.id).join(',');await page.mouse.click(coin.position.x,coin.position.y);await page.waitForTimeout(50);s=await page.evaluate(()=>demo.state);
   check('coin click leaves rule queue unchanged',before===s.nodes.map(n=>n.id).join(','));
   check('audio unlocked and procedural sound emitted',s.audio.enabled&&s.audio.played>0);
   await page.screenshot({path:path.join(out,'01-reveal.png')});
   s=await at(11);check('acting card leaves group and loses its coin',!s.coins.some(c=>c.after==='a'||c.before==='a')&&s.nodes[1].ty-s.nodes[0].ty-s.nodes[0].h===54);
   check('other pending tie remains intact',s.coins.length===1&&s.coins[0].after==='d');
   await page.screenshot({path:path.join(out,'06-acting-detaches.png')});
   s=await at(24);check('no tentative discarded card',!s.nodes.some(n=>n.id==='pay'));
   s=await at(27);check('defense below attacker',s.nodes.find(n=>n.id==='r').parent==='atk');check('discard below defense',s.nodes.find(n=>n.id==='pay').parent==='r'&&s.nodes.find(n=>n.id==='pay').level===2);
   check('all nested response cards have straight edges',s.nodes.filter(n=>n.level>0).every(n=>!n.notched));
   await page.screenshot({path:path.join(out,'02-response.png')});
   s=await at(41);check('counterattack new root',s.nodes.find(n=>n.id==='riposte').level===0);check('new root focuses and ends browsing',s.focusId==='riposte'&&!s.freeBrowse&&s.scrollGoal>0);check('response does not create initiative coin',s.coins.length===0);
   check('extra counterattack root has no notches',!s.nodes.find(n=>n.id==='riposte').notched);
   await page.screenshot({path:path.join(out,'03-counterattack.png')});
   s=await at(51);await page.mouse.move(230,193);await page.mouse.click(230,193);s=await page.evaluate(()=>demo.state);check('real pointer selects captain candidate',!s.selection&&s.nodes[0].id==='s-bro'&&s.nodes[1].id==='s-sh'&&s.nodes[2].id==='s-ar');
   s=await at(65);check('four-way tie ordered without losing cards',s.nodes.length===4&&s.nodes[0].id==='s-bro'&&s.nodes[1].id==='s-sh');
   check('four-way active root separated but pending three linked',s.coins.length===2&&s.coins.every(c=>c.after!=='s-bro'&&c.before!=='s-bro'));
   s=await at(75);check('poison sorts only pending nodes',s.nodes.map(n=>n.id).join(',')==='p-done,p-current,p-s,p-w'&&s.nodes.at(-1).init===6&&s.nodes[0].status==='done');
   await page.screenshot({path:path.join(out,'04-poison.png')});
   s=await at(83);let z=s.zoomGoal;await page.mouse.move(260,420);await page.mouse.wheel(0,440);s=await page.evaluate(()=>demo.state);check('real wheel inside scrolls list only',s.freeBrowse&&s.scrollGoal>0&&s.zoomGoal===z);
   const offset=s.scrollGoal;await page.mouse.move(1000,500);await page.mouse.wheel(0,-180);s=await page.evaluate(()=>demo.state);check('real wheel outside zooms board only',s.scrollGoal===offset&&s.zoomGoal>z);
   s=await at(98);check('next root resets free browse',!s.freeBrowse&&s.focusId==='future'&&Math.abs(s.scrollGoal-(s.nodes.at(-1).ty-126))<1);
   check('no visible DOM scrollbars',await page.evaluate(()=>document.documentElement.scrollHeight===innerHeight&&document.querySelectorAll('[role="scrollbar"]').length===0));
   await page.setViewportSize({width:1280,height:800});await at(27);await page.screenshot({path:path.join(out,'05-response-1280.png')});
   check('no browser runtime errors',errors.length===0);fs.writeFileSync(path.join(out,'verification.json'),JSON.stringify({passed:true,checks,errors,method:'Independent browser canvas UI; fixture assertions and Playwright pointer/wheel events, not Unity or game-rule verification'},null,2));
   console.log(JSON.stringify({passed:true,checks:checks.length,errors}));await context.close();
 }
 await browser.close();
})().catch(e=>{console.error(e);process.exit(1);});
