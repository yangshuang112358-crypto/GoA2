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
    public sealed class CrossfireTests
    {
        internal const string Crossfire="sabina-02-交叉火力",First="minion:-1,-3";
        internal static string Extra(GameSession game)=>game.View(0).Units.Single(u=>u.Kind=="ranged" && u.Team==Team.Red).Id;
        internal static GameSession Setup(ContentCatalog catalog)
        {
            var game=LocalGameFactory.Create(catalog,"crossfire",new[]{"A","B","C","D"},42,true);
            Apply(game,0,CommandKind.DebugPrepare,"sabina,tigerclaw,brogan,arien");Apply(game,0,CommandKind.DebugEquipCard,Crossfire,target:0);Apply(game,0,CommandKind.DebugEquipCard,"tigerclaw-18-躲闪",target:1);
            Apply(game,0,CommandKind.DebugTeleport,"hero:0",cell:new Hex(5,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(8,-8));
            Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(4,-8));Apply(game,0,CommandKind.DebugTeleport,"hero:3",cell:new Hex(8,-7));
            Apply(game,0,CommandKind.DebugTeleport,First,cell:new Hex(6,-8));Apply(game,0,CommandKind.DebugTeleport,Extra(game),cell:new Hex(5,-7));
            string[] cards={Crossfire,"tigerclaw-07-伺机待发","brogan-06-铜墙铁壁","arien-07-潮水"};for(int seat=0;seat<4;seat++)Apply(game,seat,CommandKind.SelectCard,cards[seat]);
            Assert.That(game.View(0).ActiveSeat,Is.EqualTo(0));return game;
        }
        private static GameSession Restore(ContentCatalog catalog,GameSession game)
        {string save=game.ExportSave();var result=LocalGameFactory.Restore(catalog,save);Assert.That(result.ExportSave(),Is.EqualTo(save));return result;}
        private static void Attack(GameSession game,string target=First){Apply(game,0,CommandKind.BeginPrimary);Apply(game,0,CommandKind.ChooseAttackTarget,target);}
        [Test]
        public void ExactContractAndEngineGate()
        {
            var card=BattlefieldTests.Catalog().Card(Crossfire);Assert.That(card.PrimaryValue,Is.EqualTo(3));Assert.That(card.Initiative,Is.EqualTo(9));Assert.That(card.SubtypeValue,Is.EqualTo(2));
            Assert.That(card.SecondaryMovement,Is.EqualTo(4));Assert.That(card.SecondaryDefense,Is.EqualTo(4));Assert.That(CombatRules.HasPrimaryProgram(card),Is.True);Assert.That(CombatRules.HasPrimaryProgram(card,19),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void ExtraRemovalOrSkipHasNoAdditionalDefeatOrGold(bool skip)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);string extra=Extra(game);Attack(game);game=Restore(catalog,game);var view=game.View(0);
            Assert.That(view.Pending!.Kind,Is.EqualTo("effect_minion"));Assert.That(view.Pending.Optional,Is.True);Assert.That(view.Pending.Source,Is.EqualTo(Crossfire));Assert.That(view.EffectTargets,Is.EqualTo(new[]{extra}));
            Assert.That(game.View(1).EffectTargets,Is.Empty);Assert.That(game.View(null).EffectTargets,Is.Empty);Assert.That(view.CanPass,Is.False);Assert.That(view.FastMoves,Is.Empty);
            var choose=Cmd(game,0,CommandKind.ChooseEffectTarget,skip?"skip":extra);Assert.That(game.Execute(0,choose).Accepted,Is.True);game=Restore(catalog,game);string saved=game.ExportSave();
            Assert.That(game.Execute(0,choose).Duplicate,Is.True);Assert.That(game.ExportSave(),Is.EqualTo(saved));view=game.View(0);
            Assert.That(view.Units.Any(u=>u.Id==extra),Is.EqualTo(skip));Assert.That(view.Players[0].Gold,Is.EqualTo(2));Assert.That(view.Players[2].Gold,Is.Zero);Assert.That(view.ActiveSeat,Is.EqualTo(2));
            Assert.That(view.Events.Count(e=>e.Kind=="MinionDefeated"),Is.EqualTo(1));Assert.That(view.Events.Count(e=>e.Kind=="AttackDeclared"),Is.EqualTo(1));Assert.That(view.Events.Count(e=>e.Kind=="MinionRemoved"),Is.EqualTo(skip?0:1));
            if(!skip)Assert.That(view.Events.Single(e=>e.Kind=="EffectMinionRemoved").CardId,Is.EqualTo(Crossfire));
        }
        [Test]
        public void RejectsForeignSeatHeavyFriendlyHeroAndDistantMinionsAtomically()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);string extra=Extra(game);var heavy=game.View(0).Units.Single(u=>u.Team==Team.Red && u.Kind=="heavy");
            Apply(game,0,CommandKind.DebugTeleport,heavy.Id,cell:new Hex(4,-7));string far=game.View(0).Units.First(u=>u.Team==Team.Red && u.Kind=="melee" && u.Id!=First).Id;
            Apply(game,0,CommandKind.DebugTeleport,far,cell:new Hex(6,-7));Attack(game);string before=game.ExportSave();
            foreach(var command in new[]{Cmd(game,1,CommandKind.ChooseEffectTarget,extra),Cmd(game,1,CommandKind.ChooseEffectTarget,"skip"),Cmd(game,0,CommandKind.ChooseEffectTarget,heavy.Id),Cmd(game,0,CommandKind.ChooseEffectTarget,far),Cmd(game,0,CommandKind.ChooseEffectTarget,"hero:2"),Cmd(game,0,CommandKind.ChooseEffectTarget,"hero:1")})
            {Assert.That(game.Execute(command.ActorSeat,command).Accepted,Is.False);Assert.That(game.ExportSave(),Is.EqualTo(before));}
            Apply(game,0,CommandKind.ChooseEffectTarget,extra);Restore(catalog,game);
        }
        [TestCase(6,-9,false)] [TestCase(7,-8,false)] [TestCase(8,-8,true)]
        public void EnemyHeroInsideAttackRangePreventsExtraRemoval(int x,int y,bool allowed)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);if(x!=8||y!=-8)Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(x,y));Attack(game);game=Restore(catalog,game);
            Assert.That(game.View(0).Pending?.Kind=="effect_minion",Is.EqualTo(allowed));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));
        }
        [Test]
        public void RangeBonusBlocksExtraAndImmuneHeroesStillCountAsPresent()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());
            state.Players[0].RangeBonus=2;Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Is.Not.Empty);
            state.Players[0].RangedBonus=1;Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Is.Empty);
            state.Players[0].RangedBonus=0;state.Units.Single(u=>u.Seat==1).Position=new Hex(7,-8);
            state.Effects.Add(new ActiveEffect{Id="test-ranged-immunity",Kind=EffectKind.NonAdjacentRangedImmunity,ProtectedUnitId="hero:1",Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!});
            Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Is.Empty);
        }
        [Test]
        public void ExtraRemovalRemainsAdjacentWhenAttackRangeIsExtended()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Attack(game);var state=new JsonStateCodec().Read(game.ExportSave());string extra=Extra(game);
            state.Units.Single(u=>u.Seat==1).Position=new Hex(8,-5);state.Players[0].RangedBonus=1;
            Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Does.Contain(extra));state.Units.Single(u=>u.Id==extra).Position=new Hex(6,-7);
            Assert.That(GameRules.LegalEffectTargets(catalog,state,0),Does.Not.Contain(extra));
        }
        [TestCase(false)] [TestCase(true)]
        public void HeroAttackNeverAllowsTheExtraRemovalWhetherDefendedOrDefeated(bool defend)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,"hero:1",cell:new Hex(7,-8));Attack(game,"hero:1");
            if(defend)Apply(game,1,CommandKind.Defend,"tigerclaw-18-躲闪");else Apply(game,1,CommandKind.DeclineDefense);
            game=Restore(catalog,game);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));Assert.That(game.View(0).Pending,Is.Null);
            Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(defend?0:1));
        }
        [Test]
        public void NoRemainingAdjacentMinionEndsWithoutAChoice()
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);Apply(game,0,CommandKind.DebugTeleport,Extra(game),cell:new Hex(6,-7));Attack(game);game=Restore(catalog,game);
            Assert.That(game.View(0).Pending,Is.Null);Assert.That(game.View(0).ActiveSeat,Is.EqualTo(2));Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(2));
        }
        [TestCase(false)] [TestCase(true)]
        public void HeavyDefeatResumesAfterFrontlineAndOffersCurrentRegionMinionsOnce(bool blockedSpawn)
        {
            var catalog=BattlefieldTests.Catalog();var game=Setup(catalog);
            foreach(var unit in game.View(0).Units.Where(u=>u.Team==Team.Red && u.Kind!="hero" && u.Kind!="heavy").ToList())Apply(game,0,CommandKind.DebugRemoveMinion,unit.Id);
            var heavy=game.View(0).Units.Single(u=>u.Team==Team.Red && u.Kind=="heavy");Apply(game,0,CommandKind.DebugTeleport,heavy.Id,cell:new Hex(6,-8));
            if(blockedSpawn)Apply(game,0,CommandKind.DebugTeleport,"hero:2",cell:new Hex(2,-7));Attack(game,heavy.Id);
            if(blockedSpawn)Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("minion_spawn"));
            while(game.View(0).Pending?.Kind=="minion_spawn")
            {
                game=Restore(catalog,game);int seat=game.View(0).Pending!.ChooserSeat;var option=game.View(seat).SpawnChoices.First(p=>p.Value.Count>0);
                Apply(game,seat,CommandKind.ChooseMinionSpawn,option.Key,cell:option.Value.First());
            }
            game=Restore(catalog,game);Assert.That(game.View(0).Pending!.Kind,Is.EqualTo("effect_minion"));Assert.That(game.View(0).EffectTargets.All(id=>id.StartsWith("minion:1:")),Is.True);
            Assert.That(game.View(0).Events.Count(e=>e.Kind=="EffectMinionChoiceRequired"),Is.EqualTo(1));Assert.That(game.View(0).Events.Count(e=>e.Kind=="AttackResolved"),Is.EqualTo(1));
            Apply(game,0,CommandKind.ChooseEffectTarget,game.View(0).EffectTargets.First());game=Restore(catalog,game);Assert.That(game.View(0).Players[0].Gold,Is.EqualTo(4));Assert.That(game.View(0).BlueMarks,Is.EqualTo(1));
        }
        [Test]
        public void FrozenThunderStillAllowsStopWithoutIntroducingNewCards()
        {
            var catalog=BattlefieldTests.Catalog();string save=File.ReadAllText(Path.Combine(ContentTests.Root(),"tests/fixtures/engine19-thunder-repeat.json"));var game=LocalGameFactory.Restore(catalog,save);
            Assert.That(game.ExportSave(),Is.EqualTo(save));Assert.That(game.View(0).SupportedPrimaryCards,Does.Not.Contain(Crossfire));Apply(game,0,CommandKind.ChooseAttackTarget,"skip");Restore(catalog,game);
        }
    }
}
