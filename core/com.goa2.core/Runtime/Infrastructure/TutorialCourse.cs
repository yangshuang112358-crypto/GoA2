#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;

namespace Goa2.Infrastructure
{
    // A local teaching adapter, not a second rules engine. Only fixture construction
    // uses debug intents; all learner and scripted actions use the ordinary boundary.
    public sealed class TutorialLine
    {
        public string Id, Title, Text, Badge, Card;
        public bool Reading;
        public TutorialLine(string id, string title, string text, bool reading=false, string badge="", string card="sabina-01-拔枪")
        { Id=id;Title=title;Text=text;Reading=reading;Badge=badge;Card=card; }
    }

    public sealed class TutorialCourse
    {
        public const int Version=1;
        public static readonly string[] Chapters={"镜头与移动","暗选与先攻","攻击英雄与水晶","防御与手牌条","攻击普通小兵","重型兵与推进","轮末小兵战斗","卡牌升级","八级终极技能","自己试一回"};
        public GameSession Session { get; private set; } = null!;
        public int Chapter { get; private set; }
        public int Step { get; private set; }
        public bool Complete => Step>=lines.Count;
        public TutorialLine Line => Complete ? new TutorialLine("chapter-complete","本章完成","可以重试本章，或继续下一章。",true) : lines[Step];
        public string TargetId { get; private set; }="";
        public string Error { get; private set; }="";
        private readonly ContentCatalog catalog;
        private List<TutorialLine> lines=new List<TutorialLine>();
        public TutorialCourse(ContentCatalog catalog,int chapter=0){this.catalog=catalog;Start(chapter);}
        private CommandResult Send(int actor,CommandKind kind,string value="",int target=-1,Hex destination=default,MoveMode mode=MoveMode.Secondary)
        {
            var v=Session.View(actor);
            return Session.Execute(actor,new Command{Id=Guid.NewGuid().ToString("N"),MatchId=v.MatchId,ExpectedRevision=v.Revision,ActorSeat=actor,Kind=kind,Value=value,TargetSeat=target,Destination=destination,MoveMode=mode});
        }
        private void Setup(int actor,CommandKind kind,string value="",int target=-1,Hex destination=default)
        {var r=Send(actor,kind,value,target,destination);if(!r.Accepted)throw new InvalidOperationException("Tutorial fixture: "+kind+" "+value+" "+r.Code+": "+r.Message);}
        public void Start(int chapter)
        {
            if(chapter<0 || chapter>=Chapters.Length)throw new ArgumentOutOfRangeException(nameof(chapter));
            Chapter=chapter;Step=0;Error="";TargetId="";lines=BuildLines(chapter);
            Session=LocalGameFactory.Create(catalog,"tutorial:"+Guid.NewGuid().ToString("N"),new[]{"你","陪练黄蜂","队友布罗根","陪练艾瑞恩"},42,true);
            Setup(0,CommandKind.DebugPrepare,"sabina,wasp,brogan,arien");Setup(0,CommandKind.SetQuickSelection,"off");
            if(chapter<=3 || chapter==9)
            {
                Setup(0,CommandKind.DebugTeleport,"hero:0",destination:new Hex(-2,5));
                Setup(0,CommandKind.DebugTeleport,"hero:1",destination:chapter==3?new Hex(-1,5):new Hex(0,5));
                if(chapter!=1)Reveal(chapter==3?"sabina-17-演练":"sabina-01-拔枪",chapter==3?"wasp-00-闪耀之刃":"wasp-07-抵挡屏障");
            }
            if(chapter==4 || chapter==5)
            {
                var enemies=Session.View(0).Units.Where(u=>u.Kind!="hero" && u.Team==Team.Red).ToArray();
                var heavy=enemies.Single(u=>u.Kind=="heavy");
                var ordinary=enemies.First(u=>u.Kind=="ranged");
                foreach(var u in enemies.Where(u=>u!=heavy && (chapter==5 || u!=ordinary)))Setup(0,CommandKind.DebugRemoveMinion,u.Id);
                TargetId=chapter==4?ordinary.Id:heavy.Id;var target=Session.View(0).Units.Single(u=>u.Id==TargetId);
                var v=Session.View(0);var pos=catalog.Cells.Where(c=>!c.Obstacle && c.Position.Distance(target.Position)==2 && !v.Units.Any(u=>u.Position==c.Position)).OrderBy(c=>c.Position.X).ThenBy(c=>c.Position.Y).First().Position;
                Setup(0,CommandKind.DebugTeleport,"hero:0",destination:pos);Reveal("sabina-01-拔枪","wasp-07-抵挡屏障");
            }
            if(chapter>=6 && chapter<=8)
            {
                if(chapter==6)
                    foreach(var u in Session.View(0).Units.Where(u=>u.Team==Team.Blue && u.Kind!="hero" && u.Kind!="heavy").Take(3).ToArray())Setup(0,CommandKind.DebugRemoveMinion,u.Id);
                if(chapter==7)Setup(0,CommandKind.DebugSetGold,"1",0);
                if(chapter==8)
                {
                    Setup(0,CommandKind.DebugSetGold,"21",0);Setup(0,CommandKind.DebugAdvance,"round");Setup(0,CommandKind.ResolveRoundEnd);
                    for(int i=0;i<6;i++)Setup(0,CommandKind.ChooseUpgrade,Session.View(0).UpgradeOptions.OrderBy(o=>o.CardId,StringComparer.Ordinal).First().CardId);
                    Setup(0,CommandKind.DebugSetGold,"7",0);
                    for(int i=1;i<4;i++)Setup(0,CommandKind.DebugSetGold,"0",i);
                }
                Setup(0,CommandKind.DebugAdvance,"round");
            }
        }
        private void Reveal(string own,string enemy)
        {
            string[] cards={own,enemy,"brogan-06-铜墙铁壁","arien-07-潮水"};
            for(int s=0;s<4;s++){Setup(s,CommandKind.SelectCard,cards[s]);Setup(s,CommandKind.ConfirmCard);}
        }
        public bool Acknowledge(){if(Complete || !Line.Reading)return false;Step++;return true;}
        public bool Signal(string signal)
        {
            if(Complete)return false;
            if(Line.Id=="read-card" && signal=="read-card" || Line.Id=="move-preview" && signal=="move-preview" || Line.Id=="move-cancel" && signal=="move-cancel") {Step++;return true;}
            return false;
        }
        public bool Allows(CommandKind kind,string value,MoveMode mode,out string why)
        {
            why="先完成当前练习："+Line.Title+"。可随时重试或返回目录。";
            if(Complete || Line.Reading)return false;
            switch(Line.Id)
            {
                case "move-commit": return kind==CommandKind.Move && mode==MoveMode.Secondary;
                case "select-first": case "select-change": return kind==CommandKind.SelectCard && (Line.Id!="select-change" || !Session.View(0).OwnCards.Any(c=>c.CardId==value && c.Zone==CardZone.Selected));
                case "select-cancel": return kind==CommandKind.CancelCardSelection;
                case "select-final": return kind==CommandKind.SelectCard && value=="sabina-01-拔枪";
                case "select-confirm": return kind==CommandKind.ConfirmCard;
                case "attack-hero": return (kind==CommandKind.CommitPrimaryAttack || kind==CommandKind.ChooseAttackTarget) && value=="hero:1" || kind==CommandKind.BeginPrimary;
                case "defend": return kind==CommandKind.Defend && value=="sabina-01-拔枪";
                case "attack-minion": case "attack-heavy": return (kind==CommandKind.CommitPrimaryAttack || kind==CommandKind.ChooseAttackTarget) && value==TargetId || kind==CommandKind.BeginPrimary;
                case "round-start": case "upgrade-start": case "ultimate-start": return kind==CommandKind.ResolveRoundEnd;
                case "round-remove": return kind==CommandKind.ChooseRoundMinionRemoval || kind==CommandKind.ChooseMinionSpawn;
                case "upgrade-pick": case "ultimate-pick": return kind==CommandKind.ChooseUpgrade;
                case "practice": return kind==CommandKind.Move || kind==CommandKind.CommitPrimaryAttack || kind==CommandKind.BeginPrimary || kind==CommandKind.ChooseAttackTarget;
                default:return false;
            }
        }
        public CommandResult Execute(CommandKind kind,string value="",int target=-1,Hex destination=default,MoveMode mode=MoveMode.Secondary)
        {
            if(!Allows(kind,value,mode,out var why))return new CommandResult{Code="tutorial_step",Message=why,View=Session.View(0)};
            var r=Send(0,kind,value,target,destination,mode);
            if(!r.Accepted)return r;
            ObserveAccepted(0,kind);return r;
        }
        private void ObserveAccepted(int actor,CommandKind kind)
        {
            if(Complete)return;var id=Line.Id;var v=Session.View(0);
            if(id=="select-confirm" && v.Phase!=Phase.Planning)Step++;
            else if(actor==0 && (id=="select-first" || id=="select-change" || id=="select-cancel" || id=="select-final" || id=="move-commit" || id=="defend" || id=="upgrade-pick" || id=="ultimate-pick"))Step++;
            else if(id=="round-start" || id=="upgrade-start" || id=="ultimate-start")Step++;
            else if(id=="round-remove" && v.RoundEndStage!="minion_battle")Step++;
            else if(id=="attack-minion" && !v.Units.Any(u=>u.Id==TargetId))Step++;
            else if(id=="attack-heavy" && v.BlueMarks>0)Step++;
            else if(id=="practice" && kind==CommandKind.Move || (id=="practice" || id=="attack-hero") && v.Players[0].Revealed.Any(c=>c.Zone==CardZone.PlayedResolved))Step++;
        }
        // One accepted opponent intent per tick. The caller provides visible pacing.
        public bool TickOpponent()
        {
            if(Complete || Line.Reading || Error!="")return false;
            var v=Session.View(0);int actor=-1;CommandKind kind=CommandKind.Pass;string value="";int target=-1;Hex destination=default;
            if(Chapter==1 && Line.Id=="select-confirm" && v.Phase==Phase.Planning)
            {
                actor=Enumerable.Range(1,3).FirstOrDefault(s=>!v.Players[s].Confirmed);
                if(actor==0)return false;
                var pv=Session.View(actor);var selected=pv.OwnCards.Any(c=>c.Zone==CardZone.Selected);
                kind=selected?CommandKind.ConfirmCard:CommandKind.SelectCard;
                value=actor==1?"wasp-07-抵挡屏障":actor==2?"brogan-06-铜墙铁壁":"arien-07-潮水";
            }
            else if(Chapter==1 && Line.Id=="initiative" && v.Phase==Phase.Planning)return false;
            else if(v.Pending!=null && v.Pending.ChooserSeat!=0)
            {
                actor=v.Pending.ChooserSeat;
                switch(v.Pending.Kind)
                {
                    case "defense":kind=CommandKind.Defend;value="wasp-00-闪耀之刃";break;
                    case "hero_respawn":kind=CommandKind.RespawnHero;destination=Session.View(actor).RespawnCells.First();break;
                    case "minion_spawn":kind=CommandKind.ChooseMinionSpawn;destination=v.Pending.CandidateCells.First();break;
                    case "initiative":kind=CommandKind.ChooseInitiative;target=v.Pending.CandidateSeats.First();break;
                    case "attack_target":kind=CommandKind.ChooseAttackTarget;value="hero:0";break;
                    default:Error="陪练遇到未覆盖的选择："+v.Pending.Kind+"。请重试本章。";return false;
                }
            }
            else if(Chapter==3 && v.Phase==Phase.Action && v.ActiveSeat==1){actor=1;kind=CommandKind.CommitPrimaryAttack;value="hero:0";}
            else return false;
            var r=Send(actor,kind,value,target,destination);
            if(!r.Accepted){Error="陪练操作失败："+r.Message;return false;}
            ObserveAccepted(actor,kind);return true;
        }
        public string Description
        {
            get
            {
                var v=Session.View(0);string body=Line.Text;
                if(Line.Id=="crystal-result")body+="\n本次：红方水晶剩 "+v.RedCrystal+"，被击败者为1级；不是扣掉攻击值4。";
                if(Line.Id=="lane-result" || Line.Id=="round-result" || Line.Id=="finish")body+="\n本局累计推进获胜要求："+v.VictoryMarksRequired+" 枚皇冠；也可从敌方近区继续推进到泉水获胜。";
                if(Line.Id=="defend" && v.Attack!=null)body+="\n本次攻击总值："+v.Attack.FinalAttack+"。红牌次要防御为6。";
                return body;
            }
        }
        private static List<TutorialLine> BuildLines(int chapter)
        {
            var result=new List<TutorialLine>();
            void L(string id,string title,string text,bool read=false,string badge="",string card="sabina-01-拔枪")=>result.Add(new TutorialLine(id,title,text,read,badge,card));
            switch(chapter)
            {
                case 0:
                    L("welcome","你是蓝方的萨彼娜","这是独立教学局面，你只操控萨彼娜，其他三人由陪练操作。滚轮缩放，中键拖动；空格或右下窗口回到当前行动。全课目标8—10分钟，可随时退出并从本章重来。",true);
                    L("read-card","先读一张牌","左键萨彼娜展开技能环，右键红色的「拔枪」查看牌文，鼠标移开收起。读完按空格回到当前行动。");
                    L("move-preview","① 移动值：本次能走多远","左上鞋子旁的4是次要移动最多4格。按空格返回行动环，选择「次要移动」，先看看地图上的合法落点。主要行动与次要移动是二选一，不是先走再攻击。",false,"movement");
                    L("move-cancel","预选可以撤回","可拖动镜头看远处，再点右侧深灰撤回按钮，回到行动环。预览与撤回不会消耗行动，已确认发生的效果不能撤回。");
                    L("move-commit","实际走一次","再次选次要移动，点击任意高亮落点，右侧绿色按钮或Enter完成移动。",false,"movement");break;
                case 1:
                    L("select-first","暗选一张牌","每个回合每人暗选一张；确认前对手看不到你选了什么。先点任意一个技能图标。");
                    L("select-change","换选另一张","尚未确认时，可以点另一个图标更换选择。");
                    L("select-cancel","再点一次取消","再次点击刚选中的图标，取消选择。");
                    L("select-final","这回合选「拔枪」","点红色拔枪。本课使用正式确认模式；四人都确认后才一起揭示。");
                    L("select-confirm","准备揭牌","用右侧绿色按钮或Enter锁定选择。陪练也会各自确认；锁定后不能换牌。");
                    L("initiative","② 先攻：决定行动先后","底部羽翼石标里的8是先攻，数值越大越早行动，与攻击强弱无关。左侧石板是已揭示行动顺序；同队并列由队长选，跨队并列由决策币先后处理。",true,"initiative");break;
                case 2:
                    L("attack-values","③ 主要行动值；④ 距离／范围","拔枪左下剑旁4是攻击基础值，右下远程标记2是攻击距离。技能牌的范围则限定其牌文目标，不是伤害；移动类主行动的数字是步数。0值技能不显示数字，也不代表无效果。",true,"primary");
                    L("attack-hero","攻击黄蜂","行动环选择主要行动，再选距离2格的黄蜂并提交。「拔枪」不能攻击相邻单位，牌文限制仍须满足。陪练会用防御2尝试抵挡攻击4。",false,"range");
                    L("crystal-result","击败英雄，损伤水晶","英雄不是逐点扣血：防御不足就被击败。其队伍水晶减少该英雄实际等级的生命，水晶归零则对方获胜；英雄到自己后续行动时选择复活。",true);break;
                case 3:
                    L("defense-intro","轮到敌人攻击你","新的教学局面：黄蜂将用「闪耀之刃」攻击你。不要切换角色，等待你的防御技能环展开。",true);
                    L("defend","⑤ 防御值：够不够抵挡","右上盾牌旁是次要防御值。选红色「拔枪」并提交防御：总防御≥总攻击就能挡住。环上方是公开算式；小兵和被动会修正结果，绿字表示增加、红字表示减少。",false,"defense");
                    L("hand-bar","头顶五色条不是生命条","现在再看萨彼娜：金、银、红、绿、蓝对应五张牌。已出的蓝牌变暗；刚用于防御的红牌进入弃牌区，条变灰、技能按钮翻为金属背面。已出和弃置都不能再暗选，轮末回收。等级圆与金币是成长信息。",true);break;
                case 4:
                    L("attack-minion","先攻击普通小兵","教学局面只留下一个普通敌兵与重型兵。用拔枪攻击高亮的普通小兵：普通小兵不出防御牌，合法攻击即可击败；重型兵仍被同队小兵保护。",false,"range");
                    L("minion-result","普通击杀不等于推进","你拿到了击杀金币，但战线还没推进。重型兵的保护现在消失；下一章练习击败它。英雄交战时，邻近的友方近战兵可减轻攻击压力，敌方近战/远程兵提供攻击修正，具体看公开算式。",true);break;
                case 5:
                    L("attack-heavy","击败失去保护的重型兵","新的教学局面：敌方只剩重型兵。用拔枪攻击它，观察交战区与皇冠币变化。");
                    L("lane-result","推进也是获胜路线","重型兵被移除后立即推进战线，产生新的兵线。不是随便击杀一个普通兵就推进。",true);break;
                case 6:
                    L("round-start","第四回合后：小兵战斗","教学局面已来到第四回合结束：蓝方3兵，红方6兵。启动轮末结算，先回收卡牌，再比较当前交战区兵数。少兵方按差额移除小兵。");
                    L("round-remove","你是蓝方队长","按地图高亮选择要移除的小兵并提交，共需移除3个。有普通兵时不能先移除重型兵。这里不是攻击，不发击杀金币。");
                    L("round-result","轮末也可能推进","本例最后移除了蓝方重型兵，红方因此推进。若兵数持平，或移除后重型兵仍在，则不会因为到了轮末就自动推进。",true);break;
                case 7:
                    L("upgrade-start","轮末才实际升级","教学局面：1级萨彼娜有1金币。启动轮末结算。每升一级消耗当前等级金币；头顶1→2只是本轮预告，结算后才真到2级并扣费。");
                    L("upgrade-pick","选一个升级方向","2—7级升级红绿蓝：先把三色各升2阶，才能升3阶。每色有两个方向；右键看前后对比。选中牌替换同色旧牌，未选路线贡献界面标注的永久被动。任选一个方向完成升级。");
                    L("upgrade-result","仍是五张手牌","升级不会让你多拿一张同色牌。下轮使用新的同色技能；金银两张保留。被动会反映为石标上的绿色数值。",true);break;
                case 8:
                    L("ultimate-start","满级前的独立教学局面","这不是刚才那次击杀的奖励：本章预置7级、六次普通卡升级已完成、持有7金币。启动轮末结算，了解8级的大招。");
                    L("ultimate-pick","确认获得紫色终极技能","满8级仍需要选择唯一紫卡。右键读「重型枪械」，再获得它；它会持续改变基础攻击和推动规则。");
                    L("ultimate-result","大招不是第六张暗选牌","紫卡是持续能力，不加入五张暗选手牌。不同英雄的大招会改变不同规则，实战前记得阅读；其具体联动以后再学。",true);break;
                case 9:
                    L("practice","自己完成一次行动","最后一个独立练习：用当前拔枪选择一次合法移动，或攻击黄蜂。可读牌、撤回预选、自由看战场；不提供定位光框。");
                    L("finish","你已掌握第一局的基本操作","一轮四回合；每回合暗选一张，再按先攻行动。防御消耗手牌，轮末回收并升级。两条胜利路线：击败英雄耗尽敌方水晶，或推进战线。教程可从目录重练；联机仍从联机启动器进入。",true);break;
            }
            return result;
        }
    }
}
