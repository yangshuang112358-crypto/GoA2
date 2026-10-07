using System.Linq;
using Goa2.Domain;

namespace Goa2.Ai
{
    public static class ObservationProjector
    {
        // Explicit allowlist. Never serialize the application view wholesale.
        public static Observation Project(GameView own, int seat, StableIds ids, PublicRuleProfile rules)
        {
            ObservedCard Card(CardInstance c) => new ObservedCard { Id=ids.Card(c.CardId), Zone=c.Zone.ToString(), PlayedRound=c.PlayedRound, PlayedTurn=c.PlayedTurn };
            return new Observation
            {
                Rules=rules.CopyFor(own.VictoryMarksRequired),
                Seat=seat, Round=own.Round, Turn=own.Turn, Phase=own.Phase.ToString(), Decision=own.Pending?.Kind ?? own.Phase.ToString(),
                ActiveSeat=own.ActiveSeat, CombatRegion=own.CombatRegion, Coin=own.DecisionCoin.ToString(),
                BlueCrystal=own.BlueCrystal, RedCrystal=own.RedCrystal, BlueMarks=own.BlueMarks, RedMarks=own.RedMarks,
                OwnCards=own.OwnCards.Select(Card).ToList(),
                Players=own.Players.Select(p => new ObservedPlayer
                {
                    Seat=p.Seat, Team=p.Team.ToString(), Hero=p.HeroId??"", Purple=ids.Card(p.PurpleCardId), Level=p.Level, Gold=p.Gold,
                    HandCount=p.HandCount, Confirmed=p.Confirmed, AwaitingRespawn=p.AwaitingRespawn, Poisoned=p.IsPoisoned,
                    PoisonDefense=p.PoisonIncludesDefense, Petrified=p.IsPetrified, Cards=p.PublicCards.Select(Card).ToList()
                }).ToList(),
                Units=own.Units.Select(u => new ObservedUnit { Id=u.Id, Kind=u.Kind, Team=u.Team.ToString(), Seat=u.Seat, Position=u.Position }).ToList(),
                PublicHistory=own.Events.Where(e=>e.PrivateTo==null && !e.Kind.StartsWith("Debug",System.StringComparison.Ordinal)).Select(e=>new ObservedEvent { Kind=e.Kind, Card=ids.Card(e.CardId), Seat=e.Seat, From=e.From, To=e.To }).ToList(),
                Effects=own.Effects.Select(e=>new ObservedEffect { Kind=e.Kind.ToString(), Card=ids.Card(e.SourceCardId), SourceUnit=e.SourceUnitId,
                    ProtectedUnit=e.ProtectedUnitId, Controller=e.ControllerSeat, StartRound=e.Window.StartRound, StartTurn=e.Window.StartTurn, EndRound=e.Window.EndRound, EndTurn=e.Window.EndTurn }).ToList()
            };
        }
    }
}
