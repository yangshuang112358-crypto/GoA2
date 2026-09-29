const fs=require('fs'),path=require('path');
let playwright;try{playwright=require('playwright');}catch{playwright=require(path.join(process.env.USERPROFILE,'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'));}
const {chromium}=playwright;
const out=path.join(__dirname,'output');fs.mkdirSync(out,{recursive:true});
const record=process.argv.includes('--record');
(async()=>{
 const browser=await chromium.launch({executablePath:process.env.GOA_DEMO_BROWSER||path.join(process.env['ProgramFiles(x86)'],'Microsoft/Edge/Application/msedge.exe'),headless:true,args:['--disable-background-timer-throttling','--disable-renderer-backgrounding']});
 const context=await browser.newContext({viewport:{width:1600,height:1000},deviceScaleFactor:1,...record?{recordVideo:{dir:out,size:{width:1600,height:1000}}}:{}});
 const page=await context.newPage();const errors=[];page.on('pageerror',e=>{errors.push(e.message);console.error('PAGE ERROR:',e.message);});
 await page.goto('file:///'+path.join(__dirname,'index.html').replace(/\\/g,'/')+(record?'?record=1':''));
 await page.waitForFunction(()=>window.demo);
 if(record){
   await page.waitForFunction(()=>window.demo.state.finished,{},{timeout:220000});
   await page.waitForTimeout(1200);
   fs.writeFileSync(path.join(out,'record-events.json'),JSON.stringify(await page.evaluate(()=>window.demoAudit),null,2));
   const video=page.video();await context.close();await video.saveAs(path.join(out,'stone-sequence-continuous.webm'));
   console.log(JSON.stringify({recorded:true,errors}));
 }else{
   const checks=[];function check(name,ok){checks.push({name,passed:!!ok});if(!ok)throw Error(name);}
   async function at(t){await page.evaluate(t=>{demo.seek(t);demo.pause();},t);await page.waitForTimeout(900);return page.evaluate(()=>demo.state);}
   let s=await at(5);check('four revealed roots only',s.nodes.length===4&&s.nodes.every(n=>n.level===0));check('two red then blue preview coins',s.coins.length===2&&s.coins[0].color==='red'&&s.coins[1].color==='blue');
   await page.screenshot({path:path.join(out,'01-reveal.png')});
   s=await at(24);check('no tentative discarded card',!s.nodes.some(n=>n.id==='pay'));
   s=await at(27);check('defense below attacker',s.nodes.find(n=>n.id==='r').parent==='atk');check('discard below defense',s.nodes.find(n=>n.id==='pay').parent==='r'&&s.nodes.find(n=>n.id==='pay').level===2);
   await page.screenshot({path:path.join(out,'02-response.png')});
   s=await at(41);check('counterattack new root',s.nodes.find(n=>n.id==='riposte').level===0);check('new root focuses and ends browsing',s.focusId==='riposte'&&!s.freeBrowse&&s.scrollGoal>0);check('response does not create initiative coin',s.coins.length===0);
   await page.screenshot({path:path.join(out,'03-counterattack.png')});
   s=await at(51);await page.mouse.move(230,193);await page.mouse.click(230,193);s=await page.evaluate(()=>demo.state);check('real pointer selects captain candidate',!s.selection&&s.nodes[0].id==='s-bro'&&s.nodes[1].id==='s-sh'&&s.nodes[2].id==='s-ar');
   s=await at(65);check('four-way tie ordered without losing cards',s.nodes.length===4&&s.nodes[0].id==='s-bro'&&s.nodes[1].id==='s-sh');
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
