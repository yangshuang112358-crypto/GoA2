#nullable enable
using Goa2.Domain;
namespace Goa2.Presentation
{
    // Display only: canonical values and rule assessments are never rewritten.
    public static class CardDisplay
    {
        public static string Primary(CardDefinition card)
        {
            if(card.Exclamation) return card.PrimaryCategory+" "+(card.PrimaryFamily=="defense" ? "∞" : "!");
            if(card.PrimaryValue==0 && card.PrimaryCategory.Contains("技能")) return card.PrimaryCategory;
            return card.PrimaryCategory+" "+card.PrimaryValue;
        }
        public static bool WarnDefense(CardDefinition card,DefenseAssessment assessment) =>
            !(card.Exclamation && card.PrimaryFamily=="defense") && assessment.FinalDefense < assessment.AttackCompared;
    }
}
