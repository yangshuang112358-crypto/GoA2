using System.IO;
using System.Linq;
using Goa2.Application;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;
namespace Goa2.Tests
{
    public sealed class SirenSongTests
    {
        internal const string Card="shargatha-06-海妖之歌";
        internal static GameSession Ready(ContentCatalog cat,bool purple=false)
        {
            var g=LocalGameFactory.Create(cat,"siren-song",new[]{"A","B","C","D"},42,true);Apply(g,0,CommandKind.DebugPrepare,"shargatha,wasp,brogan,sabina");
            if(purple)
            {
                Apply(g,0,CommandKind.DebugSetGold,"28",target:0);Apply(g,0,CommandKind.DebugAdvance,"round");Apply(g,0,CommandKind.ResolveRoundEnd);
                for(int i=0;i<7;i++)Apply(g,0,CommandKind.ChooseUpgrade,g.View(0).UpgradeOptions.First().CardId);
            }
            var positions=new[]{new Hex(6,-8),new Hex(6,-6),new Hex(8,-8),new Hex(4,-8)};
            for(int i=0;i<4;i++)Apply(g,0,CommandKind.DebugTeleport,"hero:"+i,cell:positions[i]);
            string[] cards={Card,"wasp-06-静电封锁","brogan-06-铜墙铁壁","sabina-07-指挥"};
            for(int i=0;i<4;i++)Apply(g,i,CommandKind.SelectCard,cards[i]);OpportuneMomentTests.AdvanceTo(g,0);return g;
        }
        [Test] public void ExactBindingAndOldEngineGate()
        {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(c),Is.True);Assert.That(CombatRules.HasPrimaryProgram(c,66),Is.False);c.Text+="三次";Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);}
        [Test] public void NearestTiedNonAdjacentEnemiesAreChosenAgainForRepeat()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);
            Assert.That(g.View(0).EffectTargets,Is.EquivalentTo(new[]{"hero:1","hero:3"}));g=ChargeTests.Restore(cat,g);
            Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");g=ChargeTests.Restore(cat,g);Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(6,-7));
            Assert.That(g.View(0).EffectTargets,Is.EqualTo(new[]{"hero:3"}));g=ChargeTests.Restore(cat,g);
            Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-8));ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="ActionRepeated" && e.CardId==Card),Is.EqualTo(1));
            Assert.That(g.View(0).Pending?.ResumeAt,Is.Not.EqualTo("approach_repeat"));
        }
        [Test] public void ZeroMoveCanRepeatAgainstSameStillEligibleTarget()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");
            Apply(g,0,CommandKind.ChooseEffectMove,"skip");Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,0,CommandKind.ChooseEffectMove,"skip");ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Units.Single(u=>u.Seat==1).Position,Is.EqualTo(new Hex(6,-6)));Assert.That(g.View(0).Events.Count(e=>e.Kind=="ActionRepeated"),Is.EqualTo(1));
        }
        [Test] public void WrongActorInvalidMoveAndDuplicateChoiceAreAtomic()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);string before=g.ExportSave();
            foreach(var cmd in new[]{Cmd(g,1,CommandKind.ChooseEffectTarget,"hero:1"),Cmd(g,0,CommandKind.ChooseEffectTarget,"hero:2"),Cmd(g,0,CommandKind.ChooseEffectTarget,"skip")})
            {Assert.That(g.Execute(cmd.ActorSeat,cmd).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));}
            Assert.That(g.View(1).EffectTargets,Is.Empty);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");before=g.ExportSave();
            Assert.That(g.Execute(0,Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(6,-5))).Accepted,Is.False);Assert.That(g.ExportSave(),Is.EqualTo(before));
            var move=Cmd(g,0,CommandKind.ChooseEffectMove,destination:new Hex(6,-7));Assert.That(g.Execute(0,move).Accepted,Is.True);before=g.ExportSave();
            Assert.That(g.Execute(0,move).Duplicate,Is.True);Assert.That(g.ExportSave(),Is.EqualTo(before));Apply(g,0,CommandKind.ChooseEffectTarget,"skip");ChargeTests.Restore(cat,g);
        }
        [Test] public void AdjacentOrOutOfRangeUnitsDoNotLeaveAChoice()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(6,-7));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(5,-8));
            Apply(g,0,CommandKind.BeginPrimary);Assert.That(g.View(0).Events.Any(e=>e.Kind=="CardEffectStopped" && e.Detail=="no_targets"),Is.True);ChargeTests.Restore(cat,g);
        }
        [Test] public void MinionReturnsBeforeRepeatedSkillAndResumesOnlyOnce()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(0,-5));
            Apply(g,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(6,-8));Apply(g,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(7,-8));
            Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"minion:-1,-3");Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(-1,-4));
            Assert.That(g.View(0).Pending?.Kind,Is.EqualTo("minion_return"));Assert.That(g.View(0).Pending.ChooserSeat,Is.EqualTo(1));g=ChargeTests.Restore(cat,g);
            Apply(g,1,CommandKind.ChooseMinionReturn,"minion:-1,-3",cell:new Hex(-1,-3));g=ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Pending?.ResumeAt,Is.EqualTo("approach_repeat"));Apply(g,0,CommandKind.ChooseEffectTarget,"skip");ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));
        }
        [Test] public void PhantasmRunsBeforeFirstAndRepeatedSkillButNotEachMovedStep()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat,true);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");
            Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(6,-7));Apply(g,0,CommandKind.ChooseEffectTarget,"hero:3");
            Assert.That(g.View(0).Pending?.ResumeAt,Is.EqualTo("before_action_target"));g=ChargeTests.Restore(cat,g);
            Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");Apply(g,1,CommandKind.ForcedDiscard,"wasp-00-闪耀之刃");g=ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Pending?.ResumeAt,Is.EqualTo("approach_move"));Apply(g,0,CommandKind.ChooseEffectMove,cell:new Hex(5,-8));ChargeTests.Restore(cat,g);
            Assert.That(g.View(0).Events.Count(e=>e.Kind=="UltimateTriggered" && e.CardId=="shargatha-12-幻化"),Is.EqualTo(2));
        }
        [Test] public void Old66PendingRespawnKeepsExactBytes()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine66-legendary-duelist-respawn.json"));
            Assert.That(LocalGameFactory.Restore(cat,save).ExportSave(),Is.EqualTo(save));Assert.That(CombatRules.HasPrimaryProgram(cat.Card(Card),66),Is.False);
        }
        [Test] public void ImmuneNearestEnemyIsExcludedBeforeChoosingTheNearestEligibleOne()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());
            s.Units.Single(u=>u.Seat==3).Position=new Hex(3,-8);
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:1"}));
            s.Effects.Add(new ActiveEffect{Kind=EffectKind.ImmunityAndUnitTraversal,SourceUnitId="hero:1",ControllerSeat=1,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:3"}));
        }
        [Test] public void ProtectedHeavyIsNotEligibleUntilOtherFriendlyMinionsAreGone()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());
            s.Units.Single(u=>u.Seat==1).Position=new Hex(0,-2);s.Units.Single(u=>u.Seat==3).Position=new Hex(0,-3);
            var heavy=s.Units.Single(u=>u.Kind=="heavy" && u.Team==Team.Red);heavy.Position=new Hex(6,-6);
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);
            s.Units.RemoveAll(u=>u.Kind!="hero" && u.Team==Team.Red && u.Id!=heavy.Id);
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{heavy.Id}));
        }
        [Test] public void ShortestValidPathMayInitiallyMoveGeometricallyFartherAway()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");
            var s=new JsonStateCodec().Read(g.ExportSave());var source=s.Units.Single(u=>u.Seat==0);
            foreach(var p in new[]{new Hex(6,-7),new Hex(5,-6),new Hex(7,-7)})s.Units.Add(new UnitState{Id="route-block:"+p,Kind="melee",Team=Team.Blue,Position=p});
            var moves=GameRules.LegalEffectMoves(cat,s,0);Assert.That(moves,Is.Not.Empty);
            Assert.That(moves.All(m=>m.Path.Count<=3 && m.Path[1].Distance(source.Position)>2),Is.True);
            Assert.That(moves.All(m=>m.Path.All(p=>cat.Cell(p)!=null && !cat.Cell(p).Obstacle)),Is.True);
        }
        [Test] public void NoValidRouteDoesNotAuthorizeAFartherTarget()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);var s=new JsonStateCodec().Read(g.ExportSave());var source=s.Units.Single(u=>u.Seat==0);
            s.Units.Single(u=>u.Seat==3).Position=new Hex(3,-8);
            foreach(var p in source.Position.Neighbors().Where(p=>cat.Cell(p)!=null && !cat.Cell(p).Obstacle && s.Units.All(u=>u.Position!=p)))
                s.Units.Add(new UnitState{Id="sealed:"+p,Kind="melee",Team=Team.Blue,Position=p});
            var rules=new GameRules();rules.Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.BeginPrimary});
            Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:1"}));
            rules.Apply(cat,s,new Command{ActorSeat=0,Kind=CommandKind.ChooseEffectTarget,Value="hero:1"});
            Assert.That(s.Pending.ResumeAt,Is.EqualTo("approach_repeat"));Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EqualTo(new[]{"hero:1"}));
            Assert.That(s.Events.Any(e=>e.Kind=="EffectMoveSkipped" && e.Detail=="no_valid_path"),Is.True);
        }
        [Test] public void StaticBoundaryAppliesToTheMovedEnemyAndBonusesDoNotExtendTwoSteps()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);Apply(g,0,CommandKind.ChooseEffectTarget,"hero:1");var s=new JsonStateCodec().Read(g.ExportSave());
            s.Players[0].MovementBonus=9;s.Players[1].MovementBonus=9;
            Assert.That(GameRules.LegalEffectMoves(cat,s,0).All(m=>m.Path.Count<=3),Is.True);
            s.Units.Single(u=>u.Seat==2).Position=new Hex(7,-5);
            s.Effects.Add(new ActiveEffect{Kind=EffectKind.MovementBoundary,SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:2",ControllerSeat=2,AreaKind=EffectAreaKind.SkillRange,Window=EffectTimeline.Create(s.Round,s.Turn,4,EffectDuration.ThisTurn)});
            Assert.That(GameRules.LegalEffectMoves(cat,s,0),Is.Empty);
        }
        [Test] public void OnlyRangedPassiveExtendsTheTargetDistance()
        {
            var cat=BattlefieldTests.Catalog();var g=Ready(cat);Apply(g,0,CommandKind.BeginPrimary);var s=new JsonStateCodec().Read(g.ExportSave());
            s.Units.Single(u=>u.Seat==1).Position=new Hex(8,-6);s.Units.Single(u=>u.Seat==3).Position=new Hex(2,-6);
            s.Players[0].RangeBonus=9;s.Players[0].MovementBonus=9;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.Empty);
            s.Players[0].RangedBonus=1;Assert.That(GameRules.LegalEffectTargets(cat,s,0),Is.EquivalentTo(new[]{"hero:1","hero:3"}));
        }
    }
}
