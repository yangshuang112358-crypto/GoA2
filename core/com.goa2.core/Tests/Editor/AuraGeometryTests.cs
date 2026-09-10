using System.Collections.Generic;
using System.Linq;
using Goa2.Domain;
using Goa2.Rules;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class AuraGeometryTests
    {
        // Deliberately small geometry fixture: these tests exercise movement policy,
        // while AuraFlowTests verifies journaled setup, activation and restoration.
        private static (ContentCatalog catalog, GameState state) Arena(Hex start)
        {
            var catalog=BattlefieldTests.Catalog(); catalog.Cells.Clear();
            for(int x=-5;x<=5;x++) for(int y=-5;y<=5;y++)
                if (new Hex(x,y).Distance(new Hex(0,0))<=5) catalog.Cells.Add(new CellDefinition { Position=new Hex(x,y),Region=x==0 && y==0 ? "source" : "arena" });
            var state=new GameRules().Create(catalog,"aura-geometry",new[] {"A","B","C","D"},42);
            state.Phase=Phase.Action; state.ActiveSeat=1;
            state.Players[0].HeroId="wasp"; state.Players[1].HeroId="sabina";
            state.Players[1].Cards.Add(new CardInstance { CardId="sabina-01-拔枪",Zone=CardZone.PlayedUnresolved });
            state.Players[1].MovementBonus=2;
            state.Units.Add(new UnitState { Id="hero:0",Kind="hero",Seat=0,Team=Team.Blue,Position=new Hex(0,0) });
            state.Units.Add(new UnitState { Id="hero:1",Kind="hero",Seat=1,Team=Team.Red,Position=start });
            state.Effects.Add(new ActiveEffect { Id="effect:1",SourceCardId="wasp-06-静电封锁",SourceUnitId="hero:0",ControllerSeat=0,CreationOrder=1,
                Kind=EffectKind.MovementBoundary,Duration=EffectDuration.ThisTurn,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)! });
            return (catalog,state);
        }
        [Test]
        public void OutsideRouteMayGoAroundTheCircleButCannotCrossItToAnotherOutsideCell()
        {
            var (catalog,state)=Arena(new Hex(3,0));
            state.Players[1].MovementBonus=3; // Seven steps can go around the occupied caster, but not around its radius-two aura.
            var moves=MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary);
            Assert.That(moves.Any(m => m.Destination==new Hex(-3,0)), Is.False);
            Assert.That(moves.Single(m => m.Destination==new Hex(0,3)).Path.All(h => h.Distance(new Hex(0,0))>2), Is.True);
            var effects=state.Effects; state.Effects=new List<ActiveEffect>();
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary).Any(m => m.Destination==new Hex(-3,0)), Is.True);
            state.Effects=effects;
        }
        [Test]
        public void InsideMovementUsesTheSkillRangeBonusAndNotTheRangedBonus()
        {
            var (catalog,state)=Arena(new Hex(1,0)); state.Players[0].RangedBonus=20;
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary).Any(m => m.Destination==new Hex(2,0)), Is.True);
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary).Any(m => m.Destination==new Hex(3,0)), Is.False);
            state.Players[0].RangeBonus=1;
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary).Any(m => m.Destination==new Hex(3,0)), Is.True);
        }
        [Test]
        public void FastMovementChecksItsEndpointsAndItsOwnRegionRules()
        {
            var (catalog,state)=Arena(new Hex(3,0));
            var moves=MovementRules.LegalMoves(catalog,state,1,MoveMode.Fast);
            Assert.That(moves.Any(m => m.Destination==new Hex(1,0)), Is.False);
            Assert.That(moves.Any(m => m.Destination==new Hex(-3,0)), Is.True);
            state.Units.Single(u => u.Seat==0).Position=new Hex(0,1);
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Fast), Is.Empty, "Enemy in the starting region independently prevents fast movement");
        }
        [Test]
        public void FriendlyUnitsAndAnAbsentSourceAreNotFrozenByTheAura()
        {
            var (catalog,state)=Arena(new Hex(1,0)); var target=state.Units.Single(u => u.Seat==1);
            target.Team=Team.Blue;
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary).Any(m => m.Destination==new Hex(3,0)), Is.True);
            target.Team=Team.Red; state.Units.RemoveAll(u => u.Seat==0);
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary).Any(m => m.Destination==new Hex(3,0)), Is.True);
            Assert.That(state.Effects.Count, Is.EqualTo(1));
        }
        [Test]
        public void ScheduledOrExpiredEffectsAreInactiveEvenBeforePhysicalCleanup()
        {
            var (catalog,state)=Arena(new Hex(1,0)); state.Effects[0].Window=EffectTimeline.Create(1,1,4,EffectDuration.NextTurn)!;
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary).Any(m => m.Destination==new Hex(3,0)), Is.True);
            state.Turn=2;
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary).Any(m => m.Destination==new Hex(3,0)), Is.False);
            state.Turn=3;
            Assert.That(MovementRules.LegalMoves(catalog,state,1,MoveMode.Secondary).Any(m => m.Destination==new Hex(3,0)), Is.True);
        }
        [Test]
        public void DifferentSourcesKeepMovementAndSkillRestrictionsIndependent()
        {
            var (catalog,state)=Arena(new Hex(1,0));
            state.Players[2].HeroId="arien";
            state.Units.Add(new UnitState { Id="hero:2",Seat=2,Kind="hero",Team=Team.Blue,Position=new Hex(1,-1) });
            state.Effects.Add(new ActiveEffect { Id="effect:2",Kind=EffectKind.SkillSuppression,SourceCardId="arien-06-打断施法",SourceUnitId="hero:2",ControllerSeat=2,
                Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)! });
            Assert.That(EffectRules.SkillRestriction(catalog,state,1,catalog.Card("wasp-06-静电封锁")), Is.EqualTo("arien-06-打断施法"));
            Assert.That(EffectRules.SkillRestriction(catalog,state,1,catalog.Card("sabina-01-拔枪")), Is.Empty);
            state.Effects.RemoveAt(0);
            Assert.That(EffectRules.CanMoveAcross(catalog,state,state.Units[1],new Hex(2,0),new Hex(3,0)), Is.True);
            Assert.That(EffectRules.SkillRestriction(catalog,state,1,catalog.Card("wasp-06-静电封锁")), Is.EqualTo("arien-06-打断施法"));
        }
    }
}
