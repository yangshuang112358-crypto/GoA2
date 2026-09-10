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
    public sealed class AdjacentAttackTests
    {
        internal static GameSession Ready(ContentCatalog catalog,string hero,string card,int engineVersion=GameState.CurrentEngineVersion)
        {
            var game=LocalGameFactory.Create(catalog,"adjacent-attack",new[] {"A","B","C","D"},42,true,engineVersion);
            Apply(game,0,CommandKind.DebugPrepare,hero+",arien,brogan,wasp");
            if(!game.View(0).OwnCards.Any(c => c.CardId==card)) Apply(game,0,CommandKind.DebugEquipCard,card,target:0);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(6,-8));
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));
            foreach(var choice in new[] {(0,card),(1,"arien-07-潮水"),(2,"brogan-06-铜墙铁壁"),(3,"wasp-07-抵挡屏障")}) Apply(game,choice.Item1,CommandKind.SelectCard,choice.Item2);
            Assert.That(game.View(null).ActiveSeat, Is.EqualTo(0));
            return game;
        }
        [TestCase("shargatha-01-劈砍",4,1)]
        [TestCase("shargatha-03-致命横扫",3,2)]
        [TestCase("shargatha-05-死亡回旋",2,3)]
        public void OneAdjacentEnemyIncludesTheAttackTargetAndProducesFiveAttack(string card,int basis,int factor)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,"shargatha",card);
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            var attack=game.View(1).Attack!;
            Assert.That(attack.Ranged, Is.False); Assert.That(attack.BaseAttack, Is.EqualTo(basis)); Assert.That(attack.CardTextBonus, Is.EqualTo(factor));
            Assert.That(attack.FinalAttack, Is.EqualTo(5));
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); Apply(game,1,CommandKind.Defend,"arien-13-挑战者");
            Assert.That(game.View(null).RedCrystal, Is.EqualTo(7));
        }
        [TestCase("shargatha-01-劈砍",4,1)]
        [TestCase("shargatha-03-致命横扫",3,2)]
        [TestCase("shargatha-05-死亡回旋",2,3)]
        public void AllEnemyUnitKindsCountAtTheAttackerWhileMinionSupportUsesTheDefender(string card,int basis,int factor)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,"shargatha",card);
            Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(6,-9));
            foreach(var pair in new[] {("melee",new Hex(5,-8)),("ranged",new Hex(5,-7)),("heavy",new Hex(6,-7))})
                Apply(game,0,CommandKind.DebugTeleport,game.View(null).Units.First(u => u.Team==Team.Red && u.Kind==pair.Item1).Id,cell:pair.Item2);
            Apply(game,0,CommandKind.DebugTeleport,game.View(null).Units.First(u => u.Team==Team.Blue && u.Kind=="melee").Id,cell:new Hex(7,-9));
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            var attack=game.View(1).Attack!;
            Assert.That(attack.CardTextBonus, Is.EqualTo(5*factor)); Assert.That(attack.EnemySupport, Is.EqualTo(1)); Assert.That(attack.FriendlyGuard, Is.Zero);
            Assert.That(attack.CardTextReason, Is.EqualTo("source_adjacent_enemies"));
            Assert.That(attack.CardTextSourceUnits, Does.Contain("hero:1").And.Contain("hero:3"));
            Assert.That(attack.CardTextSourceUnits.Count, Is.EqualTo(5));
            Assert.That(attack.CardTextSourceUnits, Is.Ordered.Using<string>(System.StringComparer.Ordinal));
            Assert.That(attack.CardTextSourceUnits.Intersect(attack.EnemySupportSources), Is.Empty);
            Assert.That(attack.FinalAttack, Is.EqualTo(basis+5*factor+1));
            Assert.That(game.View(1).DefenseOptions.Single(o => o.CardId=="arien-13-挑战者").Assessment.AttackCompared, Is.EqualTo(basis+5*factor));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [Test]
        public void BackstabExcludesTheAttackerItself()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,"tigerclaw","tigerclaw-03-背刺");
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(1).Attack!.FinalAttack, Is.EqualTo(4)); Assert.That(game.View(1).Attack!.CardTextBonus, Is.Zero);
        }
        [Test]
        public void BackstabAddsTwoOnceEvenWithSeveralOtherFriendlySupporters()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,"tigerclaw","tigerclaw-03-背刺");
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(8,-8));
            Apply(game,0,CommandKind.DebugTeleport,game.View(null).Units.First(u => u.Team==Team.Blue && u.Kind=="melee").Id,cell:new Hex(7,-9));
            Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            Assert.That(game.View(1).Attack!.CardTextBonus, Is.EqualTo(2)); Assert.That(game.View(1).Attack!.FinalAttack, Is.EqualTo(7));
            Assert.That(game.View(1).Attack!.CardTextReason, Is.EqualTo("target_adjacent_other_allies"));
            Assert.That(game.View(1).Attack!.CardTextSourceUnits.Count, Is.EqualTo(2));
            Assert.That(game.View(1).DefenseOptions.Single(o => o.CardId=="arien-13-挑战者").Assessment.AttackCompared, Is.EqualTo(6));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase("hero",Team.Blue,1,2)]
        [TestCase("melee",Team.Blue,1,2)]
        [TestCase("ranged",Team.Blue,1,2)]
        [TestCase("heavy",Team.Blue,1,2)]
        [TestCase("hero",Team.Red,1,0)]
        [TestCase("ranged",Team.Red,1,0)]
        [TestCase("marker",Team.Blue,1,0)]
        [TestCase("hero",Team.Blue,2,0)]
        public void BackstabSupportRequiresAnotherFriendlyCombatUnitAdjacentToTheTarget(string kind,Team team,int distance,int expected)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,"tigerclaw","tigerclaw-03-背刺");
            // Query-only fixture isolates supporter taxonomy, including future non-unit markers.
            var state=new JsonStateCodec().Read(game.ExportSave()); var source=state.Units.Single(u => u.Seat==0); var target=state.Units.Single(u => u.Seat==1);
            state.Units.RemoveAll(u => u.Id!=source.Id && u.Id!=target.Id);
            state.Units.Add(new UnitState {Id="supporter",Team=team,Kind=kind,Position=distance==1 ? new Hex(8,-8) : new Hex(5,-7)});
            var modifier=CombatRules.CardTextModifier(catalog,state,catalog.Card("tigerclaw-03-背刺"),source,target);
            Assert.That(modifier.Amount, Is.EqualTo(expected)); Assert.That(modifier.UnitSources, Is.EqualTo(expected==0 ? new string[0] : new[] {"supporter"}));
        }
        [TestCase("shargatha","shargatha-01-劈砍")]
        [TestCase("shargatha","shargatha-03-致命横扫")]
        [TestCase("shargatha","shargatha-05-死亡回旋")]
        [TestCase("tigerclaw","tigerclaw-03-背刺")]
        public void OnlyAdjacentEnemyLegalUnitsCanBeAttackedDespiteRangeBonuses(string hero,string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,hero,card); var state=new JsonStateCodec().Read(game.ExportSave());
            state.Players[0].RangeBonus=20; state.Players[0].RangedBonus=20;
            state.Units.Single(u => u.Seat==2).Position=new Hex(5,-8); state.Units.Single(u => u.Seat==3).Position=new Hex(8,-8);
            var heavy=state.Units.First(u => u.Team==Team.Red && u.Kind=="heavy"); heavy.Position=new Hex(6,-7);
            var melee=state.Units.First(u => u.Team==Team.Red && u.Kind=="melee"); melee.Position=new Hex(6,-9);
            var ranged=state.Units.First(u => u.Team==Team.Red && u.Kind=="ranged"); ranged.Position=new Hex(5,-7);
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Is.EquivalentTo(new[] {"hero:1",melee.Id,ranged.Id}));
            state.Effects.Add(new ActiveEffect {Id="ranged-protection",Kind=EffectKind.NonAdjacentRangedImmunity,ProtectedUnitId="hero:1",Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            Assert.That(CombatRules.AttackTargets(catalog,state,0), Does.Contain("hero:1"));
        }
        [Test]
        public void EnemyCountExcludesAlliesDistantEnemiesAndNonUnitMarkers()
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,"shargatha","shargatha-01-劈砍"); var state=new JsonStateCodec().Read(game.ExportSave());
            var source=state.Units.Single(u => u.Seat==0); var target=state.Units.Single(u => u.Seat==1);
            state.Units.Single(u => u.Seat==2).Position=new Hex(6,-9); state.Units.Single(u => u.Seat==3).Position=new Hex(8,-8);
            state.Units.Add(new UnitState {Id="enemy-marker",Team=Team.Red,Kind="marker",Position=new Hex(6,-7)});
            var modifier=CombatRules.CardTextModifier(catalog,state,catalog.Card("shargatha-01-劈砍"),source,target);
            Assert.That(modifier.Amount, Is.EqualTo(1)); Assert.That(modifier.UnitSources, Is.EqualTo(new[] {"hero:1"}));
            state.Players[0].AttackBonus=3;
            var attack=CombatMath.Attack(state,catalog.Card("shargatha-01-劈砍"),0,target.Id,modifier.Amount);
            Assert.That(attack.CardTextBonus, Is.EqualTo(1)); Assert.That(attack.AttackBonus, Is.EqualTo(4)); Assert.That(attack.FinalAttack, Is.EqualTo(8));
        }
        [TestCase("shargatha","shargatha-01-劈砍")]
        [TestCase("tigerclaw","tigerclaw-03-背刺")]
        public void NoTargetsStopsTheCardWithoutAnAttackAndLegalMinionTargetIsDefeatedDirectly(string hero,string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,hero,card);
            Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8)); Apply(game,0,CommandKind.BeginPrimary);
            Assert.That(game.View(null).Events.Any(e => e.Kind=="AttackCalculated"), Is.False);
            Assert.That(game.View(null).Events.Last(e => e.Kind=="CardEffectStopped").Detail, Is.EqualTo("no_targets"));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
            game=Ready(catalog,hero,card); string minion=game.View(null).Units.First(u => u.Team==Team.Red && u.Kind=="melee").Id;
            Apply(game,0,CommandKind.DebugTeleport,minion,cell:new Hex(6,-9)); Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,minion);
            Assert.That(game.View(null).Units.Any(u => u.Id==minion), Is.False); Assert.That(game.View(null).Pending, Is.Null);
            Assert.That(game.View(null).Players[0].Gold, Is.EqualTo(2)); Assert.That(game.View(null).Events.Any(e => e.Kind=="DefenseRequired"), Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase("shargatha","shargatha-01-劈砍")]
        [TestCase("tigerclaw","tigerclaw-03-背刺")]
        public void PendingAttackRestoresRejectsIllegalChoicesAndDoesNotDuplicateResolution(string hero,string card)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,hero,card); Apply(game,0,CommandKind.BeginPrimary);
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); string before=game.ExportSave();
            foreach(var choice in new[] {(0,"hero:0"),(0,"hero:2"),(0,"hero:3"),(1,"hero:1")})
            {
                Assert.That(game.Execute(choice.Item1,Cmd(game,choice.Item1,CommandKind.ChooseAttackTarget,choice.Item2)).Accepted, Is.False);
                Assert.That(game.ExportSave(), Is.EqualTo(before));
            }
            var choose=Cmd(game,0,CommandKind.ChooseAttackTarget,"hero:1"); Assert.That(game.Execute(0,choose).Accepted, Is.True);
            Assert.That(game.Execute(0,choose).Duplicate, Is.True);
            var view=game.View(1); view.Attack!.CardTextSourceUnits.Clear();
            if(hero=="shargatha") Assert.That(game.View(null).Attack!.CardTextSourceUnits, Is.EqualTo(new[] {"hero:1"}));
            game=LocalGameFactory.Restore(catalog,game.ExportSave()); var defend=Cmd(game,1,CommandKind.Defend,"arien-13-挑战者");
            Assert.That(game.Execute(1,defend).Accepted, Is.True); string saved=game.ExportSave();
            Assert.That(game.Execute(1,defend).Duplicate, Is.True); Assert.That(game.ExportSave(), Is.EqualTo(saved));
            Assert.That(game.View(null).Events.Count(e => e.Kind=="AttackResolved"), Is.EqualTo(1));
            Assert.That(game.View(null).Events.Count(e => e.Kind=="CardResolved" && e.CardId==card), Is.EqualTo(1));
        }
        [TestCase("shargatha","shargatha-01-劈砍",6)]
        [TestCase("shargatha","shargatha-03-致命横扫",7)]
        [TestCase("shargatha","shargatha-05-死亡回旋",7)]
        [TestCase("tigerclaw","tigerclaw-03-背刺",6)]
        public void LegacyEngineStaysUnsupportedAndSecondaryDefenseDoesNotApplyAttackText(string hero,string card,int defense)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,hero,card,5); string before=game.ExportSave();
            Assert.That(game.View(0).PrimarySupported, Is.False); Assert.That(game.Execute(0,Cmd(game,0,CommandKind.BeginPrimary)).Code, Is.EqualTo("primary_not_implemented"));
            Assert.That(game.ExportSave(), Is.EqualTo(before)); Assert.That(LocalGameFactory.Restore(catalog,before).ExportSave(), Is.EqualTo(before));
            game=CombatFlowTests.Duel(catalog,defender:hero,equipment:card); Apply(game,0,CommandKind.BeginPrimary); Apply(game,0,CommandKind.ChooseAttackTarget,"hero:1");
            var option=game.View(1).DefenseOptions.Single(o => o.CardId==card);
            Assert.That(option.Primary, Is.False); Assert.That(option.Assessment.FinalDefense, Is.EqualTo(defense)); Apply(game,1,CommandKind.Defend,card);
            Assert.That(game.View(null).RedCrystal, Is.EqualTo(7)); Assert.That(game.View(null).Events.Count(e => e.Kind=="AttackCalculated"), Is.EqualTo(1));
            catalog.Card(card).Text+="（不同文本）"; Assert.That(CombatRules.HasPrimaryProgram(catalog.Card(card)), Is.False);
        }
        [TestCase("shargatha","shargatha-01-劈砍",MoveMode.Secondary)]
        [TestCase("shargatha","shargatha-03-致命横扫",MoveMode.Fast)]
        [TestCase("shargatha","shargatha-05-死亡回旋",MoveMode.Secondary)]
        [TestCase("tigerclaw","tigerclaw-03-背刺",MoveMode.Fast)]
        public void MoveUseDoesNotRunPrimaryAttackText(string hero,string card,MoveMode mode)
        {
            var catalog=BattlefieldTests.Catalog(); var game=Ready(catalog,hero,card);
            if(mode==MoveMode.Fast) Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(-8,8));
            var moves=mode==MoveMode.Fast ? game.View(0).FastMoves : game.View(0).SecondaryMoves;
            Assert.That(moves, Is.Not.Empty); Apply(game,0,CommandKind.Move,cell:moves.First().Destination,mode:mode);
            Assert.That(game.View(null).Events.Any(e => e.Kind=="AttackCalculated"), Is.False);
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
        [TestCase("shargatha","shargatha-03-致命横扫","先攻")]
        [TestCase("shargatha","shargatha-02-快速突刺","防御")]
        [TestCase("shargatha","shargatha-05-死亡回旋","范围")]
        [TestCase("shargatha","shargatha-04-横枪跃马","移动")]
        [TestCase("tigerclaw","tigerclaw-03-背刺","防御")]
        [TestCase("tigerclaw","tigerclaw-04-影袭","先攻")]
        public void UpgradeReceivesTheRejectedCandidatePassive(string hero,string selected,string bonus)
        {
            var catalog=BattlefieldTests.Catalog(); var game=LocalGameFactory.Create(catalog,"adjacent-upgrade",new[] {"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,hero+",arien,brogan,wasp"); Apply(game,0,CommandKind.DebugSetGold,"28",target:0);
            Apply(game,0,CommandKind.DebugAdvance,"round"); Apply(game,0,CommandKind.ResolveRoundEnd);
            if(catalog.Card(selected).Level==3)
                foreach(string color in new[] {"red","green","blue"})
                    Apply(game,0,CommandKind.ChooseUpgrade,catalog.Cards.First(c => c.HeroId==hero && c.Color==color && c.Level==2).Id);
            var prior=game.View(0).Players[0].PermanentBonuses; Apply(game,0,CommandKind.ChooseUpgrade,selected);
            Assert.That(game.View(0).OwnUpgradeHistory.Last().Bonus, Is.EqualTo(bonus));
            Assert.That(game.View(0).Players[0].PermanentBonuses[bonus], Is.EqualTo((prior.TryGetValue(bonus,out int count) ? count : 0)+1));
            Assert.That(LocalGameFactory.Restore(catalog,game.ExportSave()).ExportSave(), Is.EqualTo(game.ExportSave()));
        }
    }
}
