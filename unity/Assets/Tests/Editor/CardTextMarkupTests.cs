using System.Linq;
using NUnit.Framework;

namespace Goa2.Presentation.Tests
{
    public sealed class CardTextMarkupTests
    {
        [Test]
        public void ActionsTimingsRestrictionsAndDefeatHaveDistinctEmphasis()
        {
            string formatted=CardTextMarkup.Format("攻击后：你可以移动2格，否则被击败。");
            Assert.That(formatted,Does.Contain("<color="+CardTextMarkup.TimingColor+"><b>攻击后</b></color>"));
            Assert.That(formatted,Does.Contain("<color="+CardTextMarkup.ActionColor+"><b>移动</b></color>"));
            Assert.That(formatted,Does.Contain("<color="+CardTextMarkup.RestrictionColor+"><b>否则</b></color>"));
            Assert.That(formatted,Does.Contain("<color="+CardTextMarkup.DefeatColor+"><b>被击败</b></color>"));
            Assert.That(formatted,Does.Contain("<nobr>").And.Contain("2格"));
        }
        [TestCase("下一回合：抵挡一次非远程攻击。")]
        [TestCase("攻击前：你可以丢弃一张卡牌。若如此做，则+2攻击距离。")]
        [TestCase("“打断施法”（不能攻击）\n最多2格；卡牌+1，保持 空格。")]
        [TestCase("<size=200>攻击</size> & </noparse> <b>不删原文</b>")]
        [TestCase("")]
        public void FormattingRoundTripsExactSourceWithoutAddingPunctuation(string text)
        {
            Assert.That(CardTextMarkup.PlainText(CardTextMarkup.Format(text)),Is.EqualTo(text));
        }
        [TestCase("非远程攻击")]
        [TestCase("不是远程攻击")]
        public void NegationAndNextTurnAreNotSplitIntoMisleadingShorterKeywords(string negation)
        {
            string formatted=CardTextMarkup.Format("下一回合："+negation+"。");
            Assert.That(formatted,Does.Contain("<b>下一回合</b>"));
            Assert.That(formatted,Does.Contain("<color="+CardTextMarkup.RestrictionColor+"><b>"+negation+"</b>"));
            Assert.That(formatted,Does.Not.Contain("<b>远程攻击</b>"));
        }
        [Test]
        public void SourceRichTextCannotInjectFormatting()
        {
            string formatted=CardTextMarkup.Format("<size=200>攻击</size>");
            Assert.That(formatted,Does.Not.Contain("<size=200>"));
            Assert.That(formatted,Does.Contain("<noparse><</noparse>size=200>"));
        }
        [Test]
        public void PreviewMeasuresRichTextAndKeepsTwoWholeLinesWithEllipsis()
        {
            int formattedMeasurements=0;
            string value=CardTextMarkup.Preview("攻击后：你可以移动2格，然后丢弃一张卡牌，否则被击败。",12, candidate=>
            { if(candidate.Contains("<b>")) formattedMeasurements++;return CardTextMarkup.PlainText(candidate).Length; });
            var lines=CardTextMarkup.PlainText(value).Split('\n');
            Assert.That(formattedMeasurements,Is.GreaterThan(0));
            Assert.That(lines.Length,Is.EqualTo(2));
            Assert.That(lines.All(line=>line.Length<=12),Is.True);
            Assert.That(lines.Last(),Does.EndWith("…"));
        }
    }
}
