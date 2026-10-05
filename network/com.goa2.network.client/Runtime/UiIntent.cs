#nullable enable
using System;
using Goa2.Domain;

namespace Goa2.Network.Client
{
    // Omit unused legacy UI arguments; the service rejects surplus fields.
    public static class UiIntent
    {
        public static PlayerIntent Create(CommandKind kind, string value, int target, Hex destination, MoveMode mode)
        {
            switch (kind)
            {
                case CommandKind.ReportCoinToss: case CommandKind.MarkCoinStuck: case CommandKind.VoteCoinReroll:
                case CommandKind.CommitPrimaryAttack:
                case CommandKind.ChooseHero: case CommandKind.SelectCard: case CommandKind.ChooseAttackTarget:
                case CommandKind.Defend: case CommandKind.ChooseRoundMinionRemoval: case CommandKind.ChooseUpgrade:
                case CommandKind.ForcedDiscard: case CommandKind.ChooseOptionalDiscard: case CommandKind.ChooseRecoveredCard:
                case CommandKind.ChooseEffectTarget: case CommandKind.ChooseCardSwap: case CommandKind.ChoosePrimaryOption:
                case CommandKind.ChooseMinionProtection: case CommandKind.ChooseDiscardAttack:
                    return new PlayerIntent(kind, value);
                case CommandKind.DeployHero: return new PlayerIntent(kind, targetSeat:target, destination:destination);
                case CommandKind.ChooseInitiative: return new PlayerIntent(kind, targetSeat:target);
                case CommandKind.Move: return new PlayerIntent(kind, value=="begin" ? value : null, destination:value=="begin" ? (Hex?)null : destination, moveMode:mode);
                case CommandKind.CommitPrimaryMove:
                case CommandKind.ChooseEffectMove: return new PlayerIntent(kind, value=="skip" ? value : null, destination:value=="skip" ? (Hex?)null : destination);
                case CommandKind.ChooseMinionSpawn: case CommandKind.ChooseMinionReturn: return new PlayerIntent(kind,value,destination:destination);
                case CommandKind.CommitPrimaryPlacement:
                case CommandKind.RespawnHero: case CommandKind.ChoosePlacement: return new PlayerIntent(kind,destination:destination);
                case CommandKind.ChooseGoldTransfer: return new PlayerIntent(kind,value,targetSeat:target);
                case CommandKind.ConfirmCard: case CommandKind.CancelCardSelection: case CommandKind.Pass:
                case CommandKind.BeginPrimary: case CommandKind.DeclineDefense: case CommandKind.ResolveRoundEnd:
                case CommandKind.DeclineRetaliationDiscard: return new PlayerIntent(kind);
                default: throw new InvalidOperationException("This operation is unavailable in network play.");
            }
        }
    }
}
