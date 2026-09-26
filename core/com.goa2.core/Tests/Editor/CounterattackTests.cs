using System;
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
    public sealed class CounterattackTests
    {
        internal const string Card="shargatha-00-反击", Slash="shargatha-01-劈砍", Sneak="tigerclaw-02-偷袭";
        internal static GameSession Ready(ContentCatalog catalog, bool phantasm=false, string red=Slash)
        {
            var game=LocalGameFactory.Create(catalog,"counterattack",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"shargatha,tigerclaw,brogan,wasp");
            if(phantasm)
            {
                Apply(game,0,CommandKind.DebugSetGold,"28",target:0);Apply(game,0,CommandKind.DebugAdvance,"round");Apply(game,0,CommandKind.ResolveRoundEnd);
                for(int i=0;i<7;i++)Apply(game,0,CommandKind.ChooseUpgrade,game.View(0).UpgradeOptions.First().CardId);
                Apply(game,0,CommandKind.DebugEquipCard,Slash,target:0);
            }
            if(red!=Slash)Apply(game,0,CommandKind.DebugEquipCard,red,target:0);
            for(int i=0;i<4;i++)Apply(game,0,CommandKind.DebugTeleport,"hero:"+i,cell:new[]{new Hex(6,-8),new Hex(7,-8),new Hex(4,-8),new Hex(6,-9)}[i]);
            var minion=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee");
            Apply(game,0,CommandKind.DebugTeleport,minion.Id,cell:new Hex(5,-8));
            string[] cards={Card,Sneak,"brogan-06-铜墙铁壁","wasp-01-电击"};
            for(int i=0;i<4;i++)Apply(game,i,CommandKind.SelectCard,cards[i]);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(0));return game;
        }
        internal static void Arm(GameSession game)
        {Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,game.View(0).Units.Single(u=>u.Position==new Hex(5,-8)).Id);}
        internal static void EnemyAttack(GameSession game)
        {Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(game,0,CommandKind.Defend,Slash);}
        private static void Choose(GameSession game,string card=Slash)
        {Apply(game,0,(CommandKind)Enum.Parse(typeof(CommandKind),"ChooseDiscardAttack"),card);}

        [Test] public void GoldAttackRegistersExactTextAndOldEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);
            Assert.That(CombatRules.HasPrimaryProgram(card,62),Is.False);card.Text+="可选";Assert.That(CombatRules.HasPrimaryProgram(card),Is.False);
        }
        [Test] public void DefenseDiscardWaitsForEnemyAfterAttackMovementThenUsesActualDiscardedCard()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Arm(game);EnemyAttack(game);
            Assert.That(game.View(1).Pending.Kind,Is.EqualTo("effect_move"));
            Assert.That(game.View(1).Pending.Source,Is.EqualTo(Sneak));game=ChargeTests.Restore(cat,game);
            Apply(game,1,CommandKind.ChooseEffectMove,"skip");
            Assert.That(game.View(0).Pending.Kind,Is.EqualTo("discard_attack"));game=ChargeTests.Restore(cat,game);
            Choose(game);Assert.That(game.View(0).Pending.Source,Is.EqualTo(Slash));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==Slash).Zone,Is.EqualTo(CardZone.Discarded));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Card),Is.EqualTo(1));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Slash),Is.Zero);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Sneak),Is.EqualTo(1));
            ChargeTests.Restore(cat,game);
        }
        [Test] public void AfterAttackMovementChangesTargetsBeforeCounterSelection()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Arm(game);EnemyAttack(game);
            Apply(game,1,CommandKind.ChooseEffectMove,cell:new Hex(8,-8));Choose(game);
            Assert.That(game.View(0).AttackTargets,Does.Not.Contain("hero:1"));Assert.That(game.View(0).AttackTargets,Does.Contain("hero:3"));
            ChargeTests.Restore(cat,game);
        }
        [Test] public void DiscardedNonAttackCannotBeUsedAndNoCandidateAutomaticallyResumes()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Arm(game);
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");
            Apply(game,0,CommandKind.Defend,"shargatha-13-石化");Apply(game,1,CommandKind.ChooseEffectMove,"skip");
            Assert.That(game.View(0).Pending?.Kind,Is.Not.EqualTo("discard_attack"));
            Assert.That(game.View(0).Events.Any(e=>e.Kind=="DiscardReactionSkipped"),Is.True);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Sneak),Is.EqualTo(1));ChargeTests.Restore(cat,game);
        }
        [Test] public void InvalidChooserSkipAndNonDiscardedGoldAreAtomic()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Arm(game);EnemyAttack(game);Apply(game,1,CommandKind.ChooseEffectMove,"skip");
            var kind=(CommandKind)Enum.Parse(typeof(CommandKind),"ChooseDiscardAttack");
            foreach(var command in new[]{Cmd(game,1,kind,Slash),Cmd(game,0,kind,"skip"),Cmd(game,0,kind,Card),Cmd(game,0,CommandKind.Pass)})
            {string before=game.ExportSave();Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            var choose=Cmd(game,0,kind,Slash);Assert.That(game.Execute(0,choose).Accepted,Is.True);string after=game.ExportSave();
            Assert.That(game.Execute(0,choose).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(after));ChargeTests.Restore(cat,game);
        }
        [Test] public void Frozen62PaymentKeepsExactBytes()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine62-defensive-counter-payment.json"));
            var game=LocalGameFactory.Restore(cat,save);Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Card));
        }
        [Test] public void PrivateCandidatesDoNotLeakAndDebugCannotReplaceSuspendedParent()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Arm(game);EnemyAttack(game);Apply(game,1,CommandKind.ChooseEffectMove,"skip");
            Assert.That(game.View(0).DiscardAttackCards,Is.EqualTo(new[]{Slash}));
            foreach(int? viewer in new int?[]{null,1,2,3})Assert.That(game.View(viewer).DiscardAttackCards,Is.Empty);
            foreach(var command in new[]{Cmd(game,0,CommandKind.DebugRecover,Slash,target:0),Cmd(game,0,CommandKind.DebugPrepare,"wasp,brogan,arien,sabina")})
            {string before=game.ExportSave();Assert.That(game.Execute(0,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
        }
        [Test] public void ReflectionDuringCounterQueuesAChildAndRestoresBothParentsOnce()
        {
            var cat=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(cat,"nested-counter",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"shargatha,tigerclaw,brogan,wasp");
            const string thrust="shargatha-02-快速突刺",reflection="wasp-08-偏转屏障";
            Apply(game,0,CommandKind.DebugEquipCard,thrust,target:0);Apply(game,0,CommandKind.DebugEquipCard,reflection,target:3);
            for(int i=0;i<4;i++)Apply(game,0,CommandKind.DebugTeleport,"hero:"+i,cell:new[]{new Hex(6,-8),new Hex(7,-8),new Hex(4,-8),new Hex(6,-6)}[i]);
            var minion=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee");Apply(game,0,CommandKind.DebugTeleport,minion.Id,cell:new Hex(5,-8));
            string[] cards={Card,Sneak,"brogan-06-铜墙铁壁","wasp-01-电击"};for(int i=0;i<4;i++)Apply(game,i,CommandKind.SelectCard,cards[i]);Arm(game);
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(game,0,CommandKind.Defend,thrust);
            Apply(game,1,CommandKind.ChooseEffectMove,"skip");Choose(game,thrust);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:3");
            Apply(game,3,CommandKind.Defend,reflection);Assert.That(game.View(0).Pending.Kind,Is.EqualTo("forced_discard"));game=ChargeTests.Restore(cat,game);
            Apply(game,0,CommandKind.ForcedDiscard,"shargatha-13-石化");Assert.That(game.View(0).Pending.Kind,Is.EqualTo("discard_attack"));game=ChargeTests.Restore(cat,game);
            Choose(game,thrust);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:3");Apply(game,3,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="DiscardAttackCompleted"),Is.EqualTo(2));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Sneak),Is.EqualTo(1));
            var state=new JsonStateCodec().Read(game.ExportSave());Assert.That(state.DiscardReactionFrames,Is.Null);Assert.That(state.DiscardReactions,Is.Null);
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==thrust).Zone,Is.EqualTo(CardZone.Discarded));ChargeTests.Restore(cat,game);
        }
        [TestCase(false)] [TestCase(true)] public void SourceDefeatOrTurnBoundaryCannotLeaveAnExecutableReaction(bool defeat)
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Arm(game);
            if(defeat)
            {
                var minion=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee");
                Apply(game,0,CommandKind.DebugTeleport,minion.Id,cell:new Hex(5,-7));
                Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");
                Apply(game,0,CommandKind.Defend,"shargatha-07-魅惑");
                Assert.That(game.View(0).Units.Any(u=>u.Seat==0),Is.False);
                Apply(game,1,CommandKind.ChooseEffectMove,"skip");
            }
            else Apply(game,0,CommandKind.DebugAdvance,"turn");
            Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).DiscardReactions,Is.Null);ChargeTests.Restore(cat,game);
        }
        [Test] public void SecondaryMoveDoesNotArmCounter()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);var move=game.View(0).SecondaryMoves.First();
            Apply(game,0,CommandKind.Move,cell:move.Destination);
            Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,game);
        }
        [Test] public void PhantasmRunsBeforeGoldDefenseAndDiscardAttackWithoutLosingParent()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat,true);
            void Prelude()
            {
                Assert.That(game.View(0).Pending.Source,Is.EqualTo(PhantasmTests.Card));
                Apply(game,0,CommandKind.ChooseEffectTarget,"hero:1");
                if(game.View(1).Pending?.Kind=="forced_discard")Apply(game,1,CommandKind.ForcedDiscard,game.View(1).ForcedDiscardCards.First());
            }
            Apply(game,0,CommandKind.BeginPrimary);Prelude();
            Apply(game,0,CommandKind.ChooseAttackTarget,game.View(0).Units.Single(u=>u.Position==new Hex(5,-8)).Id);
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");
            Apply(game,0,CommandKind.Defend,Slash);Prelude();Apply(game,1,CommandKind.ChooseEffectMove,"skip");
            Choose(game);game=ChargeTests.Restore(cat,game);Prelude();
            Assert.That(game.View(0).Pending.Source,Is.EqualTo(Slash));Assert.That(game.View(0).Pending.Kind,Is.EqualTo("attack_target"));
            Apply(game,0,CommandKind.ChooseAttackTarget,"hero:3");Apply(game,3,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="UltimateTriggered" && e.CardId==PhantasmTests.Card),Is.EqualTo(3));
            Assert.That(new JsonStateCodec().Read(game.ExportSave()).DiscardReactionFrames,Is.Null);ChargeTests.Restore(cat,game);
        }
        [Test] public void HeavyWeaponryProducesTwoIndependentCountersAndSameCardMayBeReused()
        {
            var cat=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(cat,"double-counter",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"shargatha,sabina,brogan,wasp");
            Apply(game,0,CommandKind.DebugSetGold,"15",target:0);
            Apply(game,0,CommandKind.DebugSetGold,"28",target:1);Apply(game,0,CommandKind.DebugAdvance,"round");Apply(game,0,CommandKind.ResolveRoundEnd);
            foreach(string id in new[]{"shargatha-03-致命横扫","shargatha-08-统治领域","shargatha-14-石化之眼","shargatha-05-死亡回旋","shargatha-10-独霸一方"})
                Apply(game,0,CommandKind.ChooseUpgrade,id);
            foreach(string id in new[]{"sabina-02-交叉火力","sabina-08-带头冲锋","sabina-13-近身支援","sabina-04-枪林弹雨","sabina-10-武装密谋","sabina-15-火力掩护",HeavyWeaponryTests.Card})
                Apply(game,1,CommandKind.ChooseUpgrade,id);
            for(int i=0;i<4;i++)Apply(game,0,CommandKind.DebugTeleport,"hero:"+i,cell:new[]{new Hex(6,-8),new Hex(7,-8),new Hex(5,-8),new Hex(6,-6)}[i]);
            var minion=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee");Apply(game,0,CommandKind.DebugTeleport,minion.Id,cell:new Hex(6,-9));
            Apply(game,0,CommandKind.DebugSetCoin,"blue");
            Apply(game,0,CommandKind.DebugSetCrystal,"20",target:1);
            string[] cards={Card,HeavyWeaponryTests.Shot,"brogan-06-铜墙铁壁","wasp-07-抵挡屏障"};for(int i=0;i<4;i++)Apply(game,i,CommandKind.SelectCard,cards[i]);
            OpportuneMomentTests.AdvanceTo(game,0);Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,minion.Id);
            OpportuneMomentTests.AdvanceTo(game,1);int finished=game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==HeavyWeaponryTests.Shot);
            const string red="shargatha-05-死亡回旋";
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(game,0,CommandKind.Defend,red);
            Assert.That(game.View(0).Pending.Source,Is.EqualTo(HeavyWeaponryTests.Card));
            Apply(game,0,CommandKind.ForcedDiscard,"shargatha-14-石化之眼");Assert.That(game.View(0).Pending.Kind,Is.EqualTo("discard_attack"));game=ChargeTests.Restore(cat,game);
            Choose(game,red);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.Defend,"sabina-04-枪林弹雨");
            Assert.That(game.View(0).Pending.Kind,Is.EqualTo("discard_attack"));game=ChargeTests.Restore(cat,game);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==HeavyWeaponryTests.Shot),Is.EqualTo(finished));
            Choose(game,red);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="DiscardAttackCompleted"),Is.EqualTo(2));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==HeavyWeaponryTests.Shot),Is.EqualTo(finished+1));ChargeTests.Restore(cat,game);
        }
        [Test] public void VictoryInCounterClearsQueueAndNeverResumesEnemyTurn()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Apply(game,0,CommandKind.DebugSetCrystal,"1",target:1);Arm(game);EnemyAttack(game);
            Apply(game,1,CommandKind.ChooseEffectMove,"skip");Choose(game);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");Apply(game,1,CommandKind.DeclineDefense);
            var state=new JsonStateCodec().Read(game.ExportSave());Assert.That(state.Phase,Is.EqualTo(Phase.Finished));
            Assert.That(state.Execution,Is.Null);Assert.That(state.DiscardReactionFrames,Is.Null);Assert.That(state.DiscardReactions,Is.Null);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Sneak),Is.Zero);ChargeTests.Restore(cat,game);
        }
        [Test] public void GoldWithoutTargetDoesNotArmCounter()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);
            var location=cat.Cells.First(c=>!c.Obstacle && game.View(0).Units.All(u=>u.Position!=c.Position && (u.Team==Team.Blue || u.Position.Distance(c.Position)>1))).Position;
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:location);Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(0).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,game);
        }
        [Test] public void DebugDiscardUsesAnImmediateBoundaryAndReturnsToTheOriginalSeat()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Arm(game);int active=game.View(0).ActiveSeat.Value;
            Apply(game,0,CommandKind.DebugDiscard,Slash,target:0);Assert.That(game.View(0).Pending.Kind,Is.EqualTo("discard_attack"));game=ChargeTests.Restore(cat,game);
            Choose(game);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:3");Apply(game,3,CommandKind.DeclineDefense);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(active));Assert.That(game.View(0).CanPass,Is.False);
            Assert.That(game.View(active).CanPass,Is.True);ChargeTests.Restore(cat,game);
        }
        [Test] public void CounterDefeatsHeavyWaitsForSpawnAndResumesOriginalAttackOnce()
        {
            var cat=BattlefieldTests.Catalog();var game=Ready(cat);Arm(game);
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-6));
            foreach(var minion in game.View(0).Units.Where(u=>u.Team==Team.Red && (u.Kind=="melee" || u.Kind=="ranged")).ToArray())
                Apply(game,0,CommandKind.DebugRemoveMinion,minion.Id);
            var heavy=game.View(0).Units.Single(u=>u.Team==Team.Red && u.Kind=="heavy");
            Apply(game,0,CommandKind.DebugTeleport,heavy.Id,cell:new Hex(6,-9));
            var spawn=cat.Cells.First(c=>c.Region=="redNear" && c.Spawn.EndsWith("Spawn") && !c.Spawn.Contains("Hero") && game.View(0).Units.All(u=>u.Position!=c.Position));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:spawn.Position);
            EnemyAttack(game);Apply(game,1,CommandKind.ChooseEffectMove,"skip");Choose(game);Apply(game,0,CommandKind.ChooseAttackTarget,heavy.Id);
            Assert.That(game.View(0).Pending.Kind,Is.EqualTo("minion_spawn"));game=ChargeTests.Restore(cat,game);
            for(int step=0;game.View(0).Pending?.Kind=="minion_spawn" && step<20;step++)
            {
                int chooser=game.View(0).Pending.ChooserSeat;var option=game.View(chooser).SpawnChoices.First();
                Apply(game,chooser,CommandKind.ChooseMinionSpawn,option.Key,cell:option.Value.First());
            }
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="DiscardAttackCompleted"),Is.EqualTo(1));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="CardResolved" && e.CardId==Sneak),Is.EqualTo(1));ChargeTests.Restore(cat,game);
        }
        [Test] public void DiscardedHorsemanExecutesItsDifferentTargetRepeatWithoutConsumingAnotherCard()
        {
            const string horse="shargatha-04-横枪跃马";var cat=BattlefieldTests.Catalog();var game=Ready(cat,red:horse);Arm(game);
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-6));
            var minion=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee");Apply(game,0,CommandKind.DebugTeleport,minion.Id,cell:new Hex(5,-6));
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:0");Apply(game,0,CommandKind.Defend,horse);
            Apply(game,1,CommandKind.ChooseEffectMove,"skip");Choose(game,horse);Apply(game,0,CommandKind.ChooseAttackTarget,"hero:3");Apply(game,3,CommandKind.Defend,"wasp-07-抵挡屏障");
            Assert.That(game.View(0).Pending.ResumeAt,Is.EqualTo("repeat_once_different"));Assert.That(game.View(0).AttackTargets,Does.Not.Contain("hero:3"));game=ChargeTests.Restore(cat,game);
            Apply(game,0,CommandKind.ChooseAttackTarget,minion.Id);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackRepeated" && e.CardId==horse),Is.EqualTo(1));
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="DiscardAttackCompleted"),Is.EqualTo(1));
            Assert.That(game.View(0).OwnCards.Single(c=>c.CardId==horse).Zone,Is.EqualTo(CardZone.Discarded));ChargeTests.Restore(cat,game);
        }
    }
}
