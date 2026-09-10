using System.Linq;
using Goa2.Domain;
using Goa2.Infrastructure;
using Goa2.Rules;
using NUnit.Framework;

namespace Goa2.Tests
{
    public sealed class EffectCancellationTests
    {
        // Synthetic source records isolate category/time filtering. ShiningBladeTests
        // separately verifies actual card creation, cancellation and journal restoration.
        [Test]
        public void OnlyActiveAdjacentEnemySkillSourcesAreCancelledInCreationOrder()
        {
            var catalog=BattlefieldTests.Catalog(); var game=ShiningBladeTests.Setup(catalog,false); var state=new JsonStateCodec().Read(game.ExportSave());
            state.Turn=2;
            void Add(string id,int seat,string card,int order,EffectWindow window) => state.Effects.Add(new ActiveEffect
            { Id=id,ControllerSeat=seat,SourceUnitId="hero:"+seat,SourceCardId=card,CreationOrder=order,Window=window });
            var active=EffectTimeline.Create(1,2,4,EffectDuration.ThisTurn)!;
            Add("enemy-second",1,"arien-06-打断施法",3,active);
            Add("enemy-first",1,"arien-06-打断施法",1,active);
            Add("friendly",2,"brogan-06-铜墙铁壁",2,active);
            Add("far-enemy",3,"sabina-07-指挥",4,active);
            Add("defense",1,"arien-13-挑战者",5,active);
            Add("attack",1,"wasp-00-闪耀之刃",6,active);
            Add("movement",1,"shargatha-07-魅惑",7,active);
            Add("scheduled",1,"arien-06-打断施法",8,EffectTimeline.Create(1,2,4,EffectDuration.NextTurn)!);
            Add("expired",1,"arien-06-打断施法",9,EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)!);
            state.Units.Single(u => u.Seat==2).Position=new Hex(7,-9);
            Assert.That(EffectRules.CancellableAdjacentSkills(catalog,state,0), Is.EqualTo(new[] {"enemy-first","enemy-second"}));
            Assert.That(state.Effects.Count, Is.EqualTo(9), "The query does not remove anything");
            state.Units.Single(u => u.Seat==1).Position=new Hex(8,-8);
            Assert.That(EffectRules.CancellableAdjacentSkills(catalog,state,0), Is.Empty, "Cancellation uses the current position, not where the skill was cast");
            state.Units.RemoveAll(u => u.Seat==0);
            Assert.That(EffectRules.CancellableAdjacentSkills(catalog,state,0), Is.Empty);
        }
        [Test]
        public void AdjacentSilenceDoesNotBecomeSkillRangeOrRangedRange()
        {
            var catalog=BattlefieldTests.Catalog(); var game=ShiningBladeTests.Setup(catalog,false); var state=new JsonStateCodec().Read(game.ExportSave());
            state.Players[0].RangeBonus=20; state.Players[0].RangedBonus=20;
            var effect=new ActiveEffect { Id="gold-aura",SourceCardId="wasp-00-闪耀之刃",SourceUnitId="hero:0",ControllerSeat=0,
                Kind=EffectKind.SkillSuppression,AreaKind=EffectAreaKind.Adjacent,Window=EffectTimeline.Create(1,1,4,EffectDuration.ThisTurn)! };
            state.Effects.Add(effect); var center=state.Units.Single(u => u.Seat==0).Position;
            Assert.That(EffectRules.Area(catalog,state,effect), Is.EquivalentTo(catalog.Cells.Where(c => c.Position.Distance(center)<=1).Select(c => c.Position)));
            Assert.That(EffectRules.SkillRestriction(catalog,state,1,catalog.Card("arien-06-打断施法")), Is.EqualTo("wasp-00-闪耀之刃"));
            state.Units.Single(u => u.Seat==1).Position=new Hex(8,-9);
            Assert.That(EffectRules.SkillRestriction(catalog,state,1,catalog.Card("arien-06-打断施法")), Is.Empty);
        }
    }
}
