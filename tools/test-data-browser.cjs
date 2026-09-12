/* Real browser checks for the generated offline reference; use --visual to watch. */
const fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url'),crypto=require('node:crypto');
let playwright;try{playwright=require('playwright');}catch{playwright=require(path.join(process.env.USERPROFILE||'','.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'));}
const root=path.resolve(__dirname,'..'),input=path.join(root,'artifacts/reference/Goa2V1-资料浏览器.html'),output=path.join(root,'artifacts/reference-tests',crypto.randomUUID().replaceAll('-',''));
const report={startedUtc:new Date().toISOString(),finishedUtc:null,visual:process.argv.includes('--visual'),inputSha256:crypto.createHash('sha256').update(fs.readFileSync(input)).digest('hex'),passed:false,checks:[],errors:[]};
fs.mkdirSync(output,{recursive:true});
function check(value,name){report.checks.push({check:name,passed:!!value});if(!value)throw new Error(name);process.stdout.write('PASS '+name+'\n');}
(async()=>{
 let browser;
 try{
  browser=await playwright.chromium.launch({headless:!report.visual,channel:'msedge',slowMo:report.visual?180:0});
  const page=await browser.newPage({viewport:{width:1440,height:1000}});
  page.on('pageerror',error=>report.errors.push(error.message));
  const remote=[];page.on('request',request=>{if(/^https?:/.test(request.url()))remote.push(request.url());});
  await page.goto(pathToFileURL(input).href);await page.waitForSelector('.card');
  check(await page.locator('.card').count()===108,'The offline page lists all 108 cards');
  await page.selectOption('#hero-filter','wasp');check(await page.locator('.card').count()===18,'Hero filtering preserves all 18 formal cards');
  await page.selectOption('#hero-filter','');await page.selectOption('#status-filter','implemented');check(await page.locator('.card').count()===23,'Implementation status is separate from the full catalog');
  await page.selectOption('#status-filter','');await page.selectOption('#color-filter','purple');check(await page.locator('.card').count()===6,'All six purple cards remain searchable');
  await page.locator('.card').first().click();check((await page.locator('#card-detail').innerText()).includes('英雄8级'),'Purple card rank and hero unlock level are not confused');await page.keyboard.press('Escape');
  await page.selectOption('#color-filter','');await page.fill('#card-search','投掷飞斧');check(await page.locator('.card').count()===1,'Chinese name search finds the exact throwing axe');
  await page.locator('.card').click();const detail=await page.locator('#card-detail').innerText();
  check(detail.includes('你可以丢弃一张卡牌')&&detail.includes('次要移动')&&detail.includes('次要防御'),'Card details keep complete official text and separate action values');
  await page.locator('#card-detail summary').click();check((await page.locator('#card-detail').innerText()).includes('行为合同'),'Implemented cards expose their actual behavior contract');
  await page.screenshot({path:path.join(output,'card-detail.png')});await page.keyboard.press('Escape');
  await page.fill('#card-search','<script>missing</script>');check(await page.locator('#no-cards').isVisible(),'Empty searches show a clear result without interpreting input as HTML');await page.fill('#card-search','');
  await page.screenshot({path:path.join(output,'catalog.png')});
  await page.click('[data-page="map"]');await page.waitForFunction(()=>document.getElementById('map-svg').getAttribute('viewBox'));
  check(await page.locator('.hex').count()===254,'The map renders every formal hex');
  await page.fill('#coordinate-search','6,-8');await page.click('#find-coordinate');check((await page.locator('#cell-detail').innerText()).includes('6, -8'),'Coordinate lookup selects the actual formal cell');
  await page.fill('#coordinate-search','900,900');await page.click('#find-coordinate');check((await page.locator('#map-status').innerText()).includes('不在正式地图'),'Coordinates outside the map are rejected');
  await page.click('#map-fit');const before=await page.locator('#map-svg').getAttribute('viewBox'),box=await page.locator('#map-svg').boundingBox();
  await page.evaluate(()=>document.getElementById('map-svg').addEventListener('wheel',e=>{window.testWheelPoint={x:e.clientX,y:e.clientY};},{once:true}));
  await page.mouse.move(box.x+box.width*.62,box.y+box.height*.45);await page.mouse.wheel(0,-350);await page.waitForTimeout(80);
  const after=await page.locator('#map-svg').getAttribute('viewBox');check(Number(after.split(' ')[2])<Number(before.split(' ')[2]),'Mouse wheel zooms the map');
  const b=before.split(' ').map(Number),a=after.split(' ').map(Number),point=await page.evaluate(()=>window.testWheelPoint),px=(point.x-box.x)/box.width,py=(point.y-box.y)/box.height;
  report.wheel={before:b,after:a,point,box,errorX:b[0]+b[2]*px-a[0]-a[2]*px,errorY:b[1]+b[3]*py-a[1]-a[3]*py};
  check(Math.abs(report.wheel.errorX)<.1&&Math.abs(report.wheel.errorY)<.1,'Wheel zoom keeps the coordinate under the cursor stable');
  await page.mouse.down();await page.mouse.move(box.x+box.width*.62+70,box.y+box.height*.45+40,{steps:8});await page.mouse.up();check(await page.locator('#map-svg').getAttribute('viewBox')!==after,'Dragging pans the map without changing source coordinates');
  await page.setViewportSize({width:1152,height:768});await page.waitForTimeout(80);
  const resized=await page.locator('#map-svg').boundingBox(),anchor={x:Math.floor(resized.x+resized.width*.7),y:Math.floor(resized.y+resized.height*.4)};
  const world=()=>page.evaluate(p=>{const svg=document.getElementById('map-svg'),point=svg.createSVGPoint();point.x=p.x;point.y=p.y;const result=point.matrixTransform(svg.getScreenCTM().inverse());return {x:result.x,y:result.y};},anchor);
  const priorWorld=await world();await page.mouse.move(anchor.x,anchor.y);await page.mouse.wheel(0,-200);await page.waitForTimeout(80);const nextWorld=await world();
  check(Math.abs(priorWorld.x-nextWorld.x)<.1&&Math.abs(priorWorld.y-nextWorld.y)<.1,'After a window resize, zoom still anchors to the actual map coordinate');
  await page.setViewportSize({width:1440,height:1000});await page.waitForTimeout(80);
  await page.click('#map-fit');await page.check('#show-coordinates');check(await page.locator('.map-label').filter({hasText:'6,-8'}).count()===1,'Coordinate labels can be enabled');
  await page.selectOption('#region-filter','redNear');check(await page.locator('.hex.faded').count()>0&&(await page.locator('#map-status').innerText()).includes('红方近区'),'Region filtering highlights the formal region');
  await page.screenshot({path:path.join(output,'map.png')});
  await page.click('[data-page="rules"]');await page.fill('#doc-search','快速移动');check((await page.locator('#document-body').innerText()).includes('不执行被替代主要行动的文字'),'The rule handbook can be searched offline');
  await page.click('[data-page="questions"]');check((await page.locator('#document-body').innerText()).includes('U-016'),'Unanswered rulings remain visible');
  await page.click('[data-page="sources"]');check(await page.locator('#source-list strong').count()>=30,'The artifact records each contributing source hash');
  await page.click('[data-page="cards"]');await page.setViewportSize({width:1152,height:768});await page.screenshot({path:path.join(output,'catalog-1152.png')});
  check(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),'The compact viewport has no horizontal overflow');
  check(remote.length===0&&report.errors.length===0,'The artifact runs without network requests or JavaScript errors');
  report.passed=true;
 }catch(error){report.errors.push(error.message);process.exitCode=1;}
 finally{if(browser)await browser.close();report.finishedUtc=new Date().toISOString();fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(report,null,2));process.stdout.write('Reference browser evidence: '+output+'\n');if(!report.passed)process.stderr.write(report.errors.join('\n')+'\n');}
})();
