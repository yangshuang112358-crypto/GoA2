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
    public sealed class LegendaryDuelistTests
    {
        internal const string Card="arien-16-传奇决斗家";
        [Test] public void ExactTextAndPreviousEngineGate()
        {var c=BattlefieldTests.Catalog().Card(Card);Assert.That(CombatRules.HasDefenseProgram(c),Is.True);Assert.That(CombatRules.HasDefenseProgram(c,65),Is.False);Assert.That(CombatRules.HasPrimaryProgram(c),Is.False);c.Text+="成功";Assert.That(CombatRules.HasDefenseProgram(c),Is.False);}
        [TestCase(5,false)] [TestCase(8,true)] public void SuccessfulOrFailedDefenseCreatesAllActionImmunity(int power,bool defeated)
        {
            var cat=BattlefieldTests.Catalog();var game=DuelistTests.Ready(cat,Card);DuelistTests.Defend(game,Card,power);ChargeTests.Restore(cat,game);
            Assert.That(game.View(1).Effects.Single(e=>e.SourceCardId==Card).Kind,Is.EqualTo(EffectKind.OtherEnemyActionImmunity));
            Assert.That(game.View(1).Players[1].AwaitingRespawn,Is.EqualTo(defeated));Assert.That(game.View(null).Events.Any(e=>e.CardId==Card),Is.False);
            if(!defeated)
            {
                var s=new JsonStateCodec().Read(game.ExportSave());var target=s.Units.Single(u=>u.Seat==1);
                foreach(int seat in new[]{0,1,3})Assert.That(EffectRules.CanAffect(s,seat,target),Is.True);
                Assert.That(EffectRules.CanAffect(s,2,target),Is.False);Assert.That(EffectRules.CanDisplace(cat,s,2,target),Is.False);
            }
        }
        [Test] public void RealSameTurnRespawnPreservesProtectionUntilTurnEnd()
        {
            var cat=BattlefieldTests.Catalog();var game=DuelistTests.Ready(cat,Card);DuelistTests.Defend(game,Card,8);
            string[] cards={"brogan-00-猛攻","arien-00-华丽刀锋","wasp-13-控物","sabina-07-指挥"};
            for(int s=0;s<4;s++)Apply(game,s,CommandKind.SelectCard,cards[s]);Apply(game,0,CommandKind.Pass);game=ChargeTests.Restore(cat,game);
            Apply(game,1,CommandKind.RespawnHero,cell:game.View(1).RespawnCells.First());var state=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(EffectRules.CanAffect(state,2,state.Units.Single(u=>u.Seat==1)),Is.False);ChargeTests.Restore(cat,game);
            Apply(game,0,CommandKind.DebugAdvance,"turn");Assert.That(game.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,game);
        }
        [TestCase(DuelistTests.Card,false)] [TestCase(Card,true)]
        public void OtherEnemyDefenseRetaliationDistinguishesAttackAndAllActionImmunity(string defense,bool blocked)
        {
            var cat=BattlefieldTests.Catalog();var game=LocalGameFactory.Create(cat,"duelist-retaliation",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"wasp,arien,tigerclaw,brogan");Apply(game,0,CommandKind.DebugEquipCard,defense,target:1);
            Apply(game,0,CommandKind.DebugEquipCard,"tigerclaw-14-近身格挡",target:2);
            var positions=new[]{new Hex(8,-8),new Hex(6,-8),new Hex(7,-8),new Hex(4,-8)};
            for(int s=0;s<4;s++)Apply(game,0,CommandKind.DebugTeleport,"hero:"+s,cell:positions[s]);DuelistTests.Defend(game,defense);
            string[] cards={"wasp-13-控物","arien-00-华丽刀锋","tigerclaw-07-伺机待发","brogan-06-铜墙铁壁"};
            for(int s=0;s<4;s++)Apply(game,s,CommandKind.SelectCard,cards[s]);OpportuneMomentTests.AdvanceTo(game,1);
            Apply(game,1,CommandKind.BeginPrimary);Apply(game,1,CommandKind.ChooseAttackTarget,"hero:2");
            if(game.View(1).Pending?.Kind=="effect_target")Apply(game,1,CommandKind.ChooseEffectTarget,"skip");
            Apply(game,2,CommandKind.Defend,"tigerclaw-14-近身格挡");game=ChargeTests.Restore(cat,game);
            Assert.That(game.View(1).Pending?.Kind=="forced_discard",Is.EqualTo(!blocked));
            if(!blocked)Apply(game,1,CommandKind.ForcedDiscard,"arien-07-潮水");
            else Assert.That(game.View(1).Events.Any(e=>e.Kind=="ForcedDiscardSkipped" && e.Detail=="immune"),Is.True);
            ChargeTests.Restore(cat,game);
        }
        [Test] public void FriendlyRecoveryStillCancelsSource()
        {
            var cat=BattlefieldTests.Catalog();var game=DuelistTests.Ready(cat,Card,bard:true);DuelistTests.Defend(game,Card);
            string[] cards={"sabina-00-近身射击","arien-07-潮水","wasp-13-控物","brogan-10-吟游诗人"};
            for(int s=0;s<4;s++)Apply(game,s,CommandKind.SelectCard,cards[s]);OpportuneMomentTests.AdvanceTo(game,3);
            Apply(game,3,CommandKind.BeginPrimary);Apply(game,3,CommandKind.ChooseEffectTarget,"hero:1");Apply(game,1,CommandKind.ChooseRecoveredCard,Card);
            Assert.That(game.View(1).Effects.Any(e=>e.SourceCardId==Card),Is.False);ChargeTests.Restore(cat,game);
        }
        [Test] public void Old65RespawnWindowRetainsItsBytesAndAttackOnlyImmunity()
        {
            var cat=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine65-duelist-respawn.json"));
            var game=LocalGameFactory.Restore(cat,save);Assert.That(game.ExportSave(),Is.EqualTo(save));
            Apply(game,1,CommandKind.RespawnHero,cell:game.View(1).RespawnCells.First());var s=new JsonStateCodec().Read(game.ExportSave());
            Assert.That(EffectRules.CanAffect(s,2,s.Units.Single(u=>u.Seat==1)),Is.True);ChargeTests.Restore(cat,game);
        }
    }
}
