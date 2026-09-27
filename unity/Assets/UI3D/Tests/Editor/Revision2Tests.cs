using Goa2.Domain;
using Goa2.Presentation;
using Goa2.Presentation.UI3D;
using NUnit.Framework;

namespace Goa2.UI3D.Tests
{
    public sealed class Revision2Tests
    {
        [Test]
        public void RotationHasIntermediateFramesAndWrapsWithoutJump()
        {
            var state=new Board3DViewport();state.Rotate(-1);
            Assert.That(state.Step,Is.EqualTo(11));Assert.That(state.Yaw,Is.Zero);
            state.Advance(.12f);Assert.That(state.Yaw,Is.EqualTo(-15).Within(.01));
            state.Advance(.12f);Assert.That(state.Yaw,Is.EqualTo(-30).Within(.01));
            state.Rotate(1);state.Advance(.24f);Assert.That(state.Yaw,Is.EqualTo(0).Within(.01));
            Assert.That(state.Rotating,Is.False);
        }
        [Test]
        public void RapidKeysRetargetWithoutSnappingOrDroppingSteps()
        {
            var state=new Board3DViewport();state.Rotate(1);state.Advance(.08f);float angle=state.Yaw;
            state.Rotate(1);Assert.That(state.Yaw,Is.EqualTo(angle));state.Advance(.24f);
            Assert.That(state.Yaw,Is.EqualTo(60).Within(.01));Assert.That(state.Step,Is.EqualTo(2));
        }
        [Test]
        public void NewHeightsMatchRequestedModelRatios()
        {Assert.That(Board3DScene.WallHeight,Is.EqualTo(.35f*3).Within(.001));Assert.That(Board3DScene.HeroHeight,Is.EqualTo(Board3DScene.WallHeight*2));}
        [TestCase("技能",0,"技能")]
        [TestCase("终极技能",0,"终极技能")]
        [TestCase("基础技能",0,"基础技能")]
        [TestCase("基础攻击",0,"基础攻击 0")]
        [TestCase("技能",2,"技能 2")]
        public void PrimaryFormattingOnlySuppressesSkillZero(string category,int value,string expected)
        {Assert.That(CardDisplay.Primary(new CardDefinition {PrimaryCategory=category,PrimaryValue=value}),Is.EqualTo(expected));}
        [Test]
        public void DefenseExclamationDisplaysInfinityWithoutChangingSource()
        {
            var card=new CardDefinition {PrimaryCategory="防御",PrimaryFamily="defense",Exclamation=true};
            Assert.That(CardDisplay.Primary(card),Is.EqualTo("防御 ∞"));Assert.That(card.Exclamation,Is.True);
            Assert.That(CardDisplay.WarnDefense(card,new DefenseAssessment {FinalDefense=0,AttackCompared=9}),Is.False);
            card.PrimaryFamily="attack";Assert.That(CardDisplay.Primary(card),Is.EqualTo("防御 !"));
        }
        [TestCase(3,5,true)] [TestCase(5,5,false)] [TestCase(6,5,false)]
        public void PinkWarningIsOnlyNumericEvenWhenCardBlocks(int defense,int attack,bool warning)
        {Assert.That(CardDisplay.WarnDefense(new CardDefinition(),new DefenseAssessment {FinalDefense=defense,AttackCompared=attack,Blocked=true,Successful=true}),Is.EqualTo(warning));}
        [Test]
        public void WholeDiscardQuantityTurnsRedWithoutChangingWords()
        {
            const string text="丢弃一张卡牌，然后移动2格。";
            string formatted=CardTextMarkup.Format(text);
            Assert.That(formatted,Does.Contain("<color="+CardTextMarkup.DefeatColor+"><b>丢弃一张卡牌</b>"));
            Assert.That(CardTextMarkup.PlainText(formatted),Is.EqualTo(text));
        }
    }
}
