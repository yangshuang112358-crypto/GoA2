using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;
using static Goa2.Tests.SessionTests;
using static Goa2.Tests.TurnFlowTests;

namespace Goa2.Tests
{
    public sealed class MarksmanTests
    {
        internal const string Marksman="sabina-03-神枪手", Headshot="sabina-05-一枪爆头";
        [TestCase(Marksman)]
        [TestCase(Headshot)]
        public void RevealedUnresolvedAttackAddsTwoAndChangesTheFiveDefenseOutcome(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attackCard:card);
            Assert.That(game.View(0).PrimarySupported, Is.True);
            Assert.That(game.View(null).Players[1].Revealed.Single().CardId, Is.EqualTo("wasp-01-电击"));
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            var attack=game.View(1).Attack!;
            Assert.That(attack.BaseAttack, Is.EqualTo(4)); Assert.That(attack.AttackBonus, Is.EqualTo(2)); Assert.That(attack.FinalAttack, Is.EqualTo(6));
            Assert.That(game.View(1).DefenseOptions.Single(o => o.CardId=="wasp-13-控物").Assessment.Successful, Is.False);
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,1,CommandKind.Defend,"wasp-13-控物");
            Assert.That(game.View(null).Players[1].AwaitingRespawn, Is.True); Assert.That(game.View(null).RedCrystal, Is.EqualTo(6));
            Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Marksman,false)]
        [TestCase(Headshot,true)]
        public void OnlyHeadshotAllowsAnAdjacentTarget(string card,bool adjacent)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attackCard:card);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-10));
            Assert.That(game.View(0).AttackTargets.Contains("hero:1"), Is.EqualTo(adjacent));
            Apply(game,0,CommandKind.BeginPrimary);
            if(adjacent)
            {
                Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
                Assert.That(game.View(1).Attack!.Ranged, Is.True); Assert.That(game.View(1).Attack!.FinalAttack, Is.EqualTo(6));
                Assert.That(game.View(1).DefenseOptions.Any(o => o.CardId=="wasp-07-抵挡屏障"), Is.False);
            }
            else Assert.That(game.View(null).Events.Any(e => e.Kind=="CardEffectStopped" && e.Detail=="no_targets"), Is.True);
        }
        [TestCase(Marksman)]
        [TestCase(Headshot)]
        public void ARevealedSkillDoesNotGrantTheAttackBonus(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attackCard:card,defender:"arien");
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(1).Attack!.FinalAttack, Is.EqualTo(4));
            Apply(game,1,CommandKind.Defend,"arien-13-挑战者");
            Assert.That(game.View(null).Players[1].AwaitingRespawn, Is.False); Assert.That(game.View(null).RedCrystal, Is.EqualTo(7));
        }
        [TestCase("wasp-01-电击",CardZone.PlayedUnresolved,1,1,2)]
        [TestCase("wasp-01-电击",CardZone.PlayedResolved,1,1,2)]
        [TestCase("wasp-00-闪耀之刃",CardZone.PlayedResolved,1,1,2)]
        [TestCase("wasp-00-闪耀之刃",CardZone.InHand,1,1,2)]
        [TestCase("wasp-01-电击",CardZone.Selected,0,0,0)]
        [TestCase("wasp-01-电击",CardZone.Discarded,0,0,0)]
        [TestCase("wasp-01-电击",CardZone.PlayedResolved,1,2,0)]
        [TestCase("wasp-01-电击",CardZone.PlayedResolved,2,1,0)]
        [TestCase("wasp-13-控物",CardZone.PlayedUnresolved,1,1,0)]
        public void BonusUsesTheActualRevealStampAndAttackFamilyNotItsZoneOrRangeIcon(string used,CardZone zone,int round,int turn,int expected)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog);
            // A query fixture isolates card recall/old-turn cases; it is not a fabricated replay save.
            var state=new JsonStateCodec().Read(game.ExportSave());
            foreach(var instance in state.Players[1].Cards) { instance.Zone=CardZone.InHand; instance.PlayedRound=null; instance.PlayedTurn=null; }
            var card=state.Players[1].Cards.Single(c => c.CardId==used); card.Zone=zone;
            card.PlayedRound=round==0 ? (int?)null : round; card.PlayedTurn=turn==0 ? (int?)null : turn;
            var target=state.Units.Single(u => u.Seat==1);
            foreach(string attacker in new[] {Marksman,Headshot}) Assert.That(CombatRules.CardTextAttackBonus(catalog,state,catalog.Card(attacker),target), Is.EqualTo(expected));
            Assert.That(CombatRules.CardTextAttackBonus(catalog,state,catalog.Card("sabina-01-拔枪"),target), Is.Zero);
            target.Kind="melee"; target.Seat=null;
            Assert.That(CombatRules.CardTextAttackBonus(catalog,state,catalog.Card(Marksman),target), Is.Zero);
        }
        [TestCase(Marksman)]
        [TestCase(Headshot)]
        public void RangeBonusAndPermanentAttackRemainSeparateAndTheChallengerDoesNotIgnoreCardText(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attackCard:card);
            var state=new JsonStateCodec().Read(game.ExportSave()); var target=state.Units.Single(u => u.Seat==1); var source=state.Units.Single(u => u.Seat==0);
            target.Position=catalog.Cells.First(c => !c.Obstacle && c.Position.Distance(source.Position)==3 && !state.Units.Any(u => u.Position==c.Position)).Position;
            state.Players[0].RangeBonus=10;
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Does.Not.Contain(target.Id));
            state.Players[0].RangedBonus=1;
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Does.Contain(target.Id));
            state.Players[0].AttackBonus=1;
            var attack=CombatMath.Attack(state,catalog.Card(card),0,target.Id,CombatRules.CardTextAttackBonus(catalog,state,catalog.Card(card),target));
            Assert.That(attack.CardTextBonus, Is.EqualTo(2)); Assert.That(attack.AttackBonus, Is.EqualTo(3));
            attack.EnemySupport=3; attack.FriendlyGuard=4; attack.FinalAttack=6;
            Assert.That(CombatMath.Defense(attack,6,0,ignoreMinions:true).Successful, Is.False);
            Assert.That(CombatMath.Defense(attack,7,0,ignoreMinions:true).AttackCompared, Is.EqualTo(7));
        }
        [TestCase(Marksman)]
        [TestCase(Headshot)]
        public void LegacyEngineAndChangedTextRemainExplicitlyUnsupported(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attackCard:card,engineVersion:4);
            Assert.That(game.View(0).PrimarySupported, Is.False); string before=game.ExportSave();
            Assert.That(game.Execute(0,Cmd(game,0,CommandKind.BeginPrimary)).Code, Is.EqualTo("primary_not_implemented"));
            Assert.That(game.ExportSave(), Is.EqualTo(before)); Assert.That(LocalGameFactory.Restore(catalog,before).ExportSave(), Is.EqualTo(before));
            catalog.Card(card).Text+="（新条件）"; Assert.That(CombatRules.HasPrimaryProgram(catalog.Card(card)), Is.False);
        }
        [TestCase(Marksman)]
        [TestCase(Headshot)]
        public void BarrierStillBlocksSixAttackAndRunsItsOwnCounterOnce(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attackCard:card,equipment:BarrierResponseTests.Reflection);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Apply(game,1,CommandKind.Defend,BarrierResponseTests.Reflection);
            Assert.That(game.View(null).Pending!.Kind, Is.EqualTo("forced_discard"));
            var discard=Cmd(game,0,CommandKind.ForcedDiscard,"sabina-00-近身射击"); Assert.That(game.Execute(0,discard).Accepted, Is.True);
            Assert.That(game.Execute(0,discard).Duplicate, Is.True);
            Assert.That(game.View(null).RedCrystal, Is.EqualTo(7)); Assert.That(game.View(null).Effects.Count, Is.EqualTo(1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Marksman)]
        [TestCase(Headshot)]
        public void PassingAnAlreadyRevealedBasicAttackDoesNotRemoveItsThisTurnUse(string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"used-attack",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"sabina,wasp,brogan,arien"); Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(7,-10)); Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-9));
            foreach(var choice in new[] {(0,card),(1,"wasp-00-闪耀之刃"),(2,"brogan-06-铜墙铁壁"),(3,"arien-07-潮水")}) Apply(game,choice.Item1,CommandKind.SelectCard,choice.Item2);
            Apply(game,1,CommandKind.Pass); Assert.That(game.View(null).ActiveSeat, Is.EqualTo(0));
            Assert.That(game.View(null).Players[1].Revealed.Single().Zone, Is.EqualTo(CardZone.PlayedResolved));
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(1).Attack!.CardTextBonus, Is.EqualTo(2)); Assert.That(game.View(1).Attack!.FinalAttack, Is.EqualTo(6));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Marksman,6)]
        [TestCase(Headshot,7)]
        public void SecondaryDefenseDoesNotApplyTheCardsAttackText(string card,int defense)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attacker:"shargatha",attackCard:"shargatha-02-快速突刺",defender:"sabina",equipment:card);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            var option=game.View(1).DefenseOptions.Single(o => o.CardId==card);
            Assert.That(option.Primary, Is.False); Assert.That(option.Assessment.FinalDefense, Is.EqualTo(defense));
            Apply(game,1,CommandKind.Defend,card);
            Assert.That(game.View(null).RedCrystal, Is.EqualTo(7)); Assert.That(game.View(null).Effects, Is.Empty);
            Assert.That(game.View(null).Events.Count(e => e.Kind=="AttackCalculated"), Is.EqualTo(1));
        }
        [TestCase(Marksman,MoveMode.Secondary)]
        [TestCase(Headshot,MoveMode.Fast)]
        public void SecondaryAndFastMovesDoNotExecuteTheAttackProgram(string card,MoveMode mode)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attackCard:card);
            if(mode==MoveMode.Fast) Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-8,8));
            var moves=mode==MoveMode.Fast ? game.View(0).FastMoves : game.View(0).SecondaryMoves;
            Assert.That(moves, Is.Not.Empty); Apply(game,0,CommandKind.Move,cell:moves.First().Destination,mode:mode);
            Assert.That(game.View(null).Events.Any(e => e.Kind=="AttackCalculated"), Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Marksman,"先攻")]
        [TestCase("sabina-02-交叉火力","防御")]
        [TestCase(Headshot,"范围")]
        [TestCase("sabina-04-枪林弹雨","移动")]
        public void UpgradesGrantTheRejectedCandidateIcon(string selected,string bonus)
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"marksman-upgrade",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"sabina,wasp,brogan,arien");
            Apply(game,0,CommandKind.DebugSetGold,"28",target:0); Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            if(catalog.Card(selected).Level==3)
                foreach(string previous in new[] {Marksman,"sabina-08-带头冲锋","sabina-14-战斗演练"}) Apply(game,0,CommandKind.ChooseUpgrade,previous);
            var prior=game.View(0).Players[0].PermanentBonuses;
            Apply(game,0,CommandKind.ChooseUpgrade,selected);
            var history=game.View(0).OwnUpgradeHistory.Last(); Assert.That(history.SelectedCardId, Is.EqualTo(selected)); Assert.That(history.Bonus, Is.EqualTo(bonus));
            Assert.That(game.View(0).Players[0].PermanentBonuses[bonus], Is.EqualTo((prior.TryGetValue(bonus,out int count) ? count : 0)+1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase(Marksman,false)]
        [TestCase(Headshot,true)]
        public void ReflectionImmunityExcludesDistantTargetsButNotAdjacentHeadshot(string card,bool adjacentAllowed)
        {
            var catalog=BattlefieldTests.Catalog(); var game=CombatFlowTests.Duel(catalog,attackCard:card);
            var state=new JsonStateCodec().Read(game.ExportSave());
            state.Effects.Add(new ActiveEffect {Id="fixture-protection",SourceCardId=BarrierResponseTests.Reflection,SourceUnitId="hero:1",ProtectedUnitId="hero:1",ControllerSeat=1,
                Kind=EffectKind.NonAdjacentRangedImmunity,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Does.Not.Contain("hero:1"));
            var source=state.Units.Single(u => u.Seat==0); state.Units.Single(u => u.Seat==1).Position=source.Position.Neighbors().First();
            Assert.That(CombatRules.AttackTargets(catalog,state,0).Contains("hero:1"), Is.EqualTo(adjacentAllowed));
        }
    }
}
